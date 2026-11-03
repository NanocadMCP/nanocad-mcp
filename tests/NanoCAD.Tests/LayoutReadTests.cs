using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class LayoutReadTests
{
    private static readonly EntityRef Owner = new("11111111-1111-4111-8111-111111111111", "22222222-2222-4222-8222-222222222222", "AB");
    private static void Check(bool ok, string text) { if (!ok) throw new Exception(text); }
    private static void Domain(string code, Action action)
    { try { action(); throw new Exception("Expected " + code); } catch (DomainException e) { Check(e.Error.code == code, e.Error.code); } }
    public static LayoutProperties Layout(EntityRef owner) => new(2, false, owner with { handle = "AC" }, false,
        new[] { -10d, -5 }, new[] { 420d, 297 }, new("None", "Sheet A3", "", "", "", 1, 1, 5,
        new[] { 420d, 297 }, new[] { 2d, 3 }, new[] { 5d, 6 }, new[] { 7d, 8 }, new[] { 0d, 0 }, new[] { 100d, 200 },
        false, 0, .02, 1, 50, false, true, false, true));
    public static ViewportProperties Viewport(EntityRef owner) => new(owner with { handle = "AD" }, 2, new[] { 100d, 80, 0 }, 80, 50, new[] { 10d, 20 },
        new[] { 5d, 6, 7 }, new[] { 1d, 2, 3 }, 250, -.3, true, true, false, .2, 5, 50, true, true, false, 10, -30, true,
        new("entity", "resolved", owner with { handle = "AE" }, "Polyline", null),
        new[] { new StructureDependency("layer", "resolved", owner with { handle = "AF" }, "Frozen", null) });
    public static StructurePage ProtocolData(string section, string bridge, string document)
    {
        var owner = Owner with { bridge_instance_id = bridge, document_id = document };
        object value = section == "layouts" ? LayoutMetadata.Validate(owner, Layout(owner)) : LayoutMetadata.Validate(owner, Viewport(owner));
        return new(section, new[] { new StructureRecord(owner, section, "Sheet:2", value) }, 1, 1, true, null);
    }
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("layout metadata retains raw scales, units and inactive window parameters", () =>
        {
            var layout = Layout(Owner); var viewport = Viewport(Owner);
            Check(ReferenceEquals(layout, LayoutMetadata.Validate(Owner, layout)), "No normalization"); LayoutMetadata.Validate(Owner, viewport);
            var raw = layout with { plot = layout.plot with { standard_scale = -1, custom_scale_denominator = 0, paper_units_native_value = int.MaxValue, window_min = new[] { 100d, 90 }, window_max = new[] { -5d, -10 } } };
            LayoutMetadata.Validate(Owner, raw);
            var unresolved = viewport with { clip_boundary = new("entity", "null", null, null, null), frozen_layers = new[] { new StructureDependency("layer", "unavailable", null, null, "erased") } };
            LayoutMetadata.Validate(Owner, unresolved);
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!, "layout-read-examples.json"), JsonSerializer.Serialize(new {
                layout_schema = LayoutReadSchema.Layout(), viewport_schema = LayoutReadSchema.Viewport(), layouts = new[] { layout, raw }, viewports = new[] { viewport, unresolved }, native_executed = false }, Wire.Json));
            return Task.CompletedTask;
        });
        await test("viewport dependencies reject foreign lifetimes and false resolved states", () =>
        {
            var value = Viewport(Owner); var dependency = value.clip_boundary;
            foreach (var bad in new[] { dependency with { @ref = Owner with { document_id = Guid.NewGuid().ToString("D") } }, dependency with { @ref = Owner with { handle = "00AF" } },
                dependency with { @ref = null }, dependency with { label = null }, dependency with { reason = "read_failed" }, dependency with { target_kind = "layer" },
                new StructureDependency("entity", "null", Owner, null, null), new("entity", "unavailable", null, null, "guessed"), new("entity", "unknown", null, null, null) })
                Domain("OBJECT_READ_FAILED", () => LayoutMetadata.Validate(Owner, value with { clip_boundary = bad }));
            Domain("OBJECT_READ_FAILED", () => LayoutMetadata.Validate(Owner, Layout(Owner) with { block_record = Owner with { bridge_instance_id = Guid.NewGuid().ToString("D") } }));
            return Task.CompletedTask;
        });
        await test("frozen layers retain byte-bounded complete lists and reject foreign duplicated data", () =>
        {
            var v = Viewport(Owner); var layers = Enumerable.Range(1, 600).Select(i => v.frozen_layers[0] with { @ref = Owner with { handle = i.ToString("X") },label="L" }).ToArray();
            LayoutMetadata.Validate(Owner, v with { frozen_layers = layers }); LayoutMetadata.Validate(Owner, v with { frozen_layers = Array.Empty<StructureDependency>() });
            Check(LayoutMetadata.FrozenCount(600)==600,"Native count acquired a hidden512-layer ceiling.");
            Domain("OBJECT_READ_FAILED", () => LayoutMetadata.FrozenCount(-1));
            var byteLarge=Enumerable.Range(1,1024).Select(i=>layers[0] with{@ref=Owner with{handle=i.ToString("X")},label=new string('L',255)}).ToArray();
            Domain("RESPONSE_LIMIT_EXCEEDED",()=>LayoutMetadata.Validate(Owner,v with{frozen_layers=byteLarge}));
            int checks=0;
            try{LayoutMetadata.Validate(Owner,v with{frozen_layers=layers},()=>{if(++checks==501)throw new OperationCanceledException();});throw new Exception("Late frozen-layer cancellation was ignored.");}
            catch(OperationCanceledException){}
            foreach (var invalid in new[] { null!, new[] { v.frozen_layers[0], v.frozen_layers[0] }, new[] { new StructureDependency("layer", "null", null, null, null) } })
                Domain("OBJECT_READ_FAILED", () => LayoutMetadata.Validate(Owner, v with { frozen_layers = invalid }));
            return Task.CompletedTask;
        });
        await test("layout and viewport reject nonfinite coordinates and malformed Unicode", () =>
        {
            foreach (double n in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity })
            {
                var layout = Layout(Owner); var view = Viewport(Owner);
                Domain("OBJECT_READ_FAILED", () => LayoutMetadata.Validate(Owner, layout with { plot = layout.plot with { custom_scale_denominator = n } }));
                Domain("OBJECT_READ_FAILED", () => LayoutMetadata.Validate(Owner, layout with { limits_min = new[] { 0d, n } }));
                Domain("OBJECT_READ_FAILED", () => LayoutMetadata.Validate(Owner, view with { custom_scale = n }));
                Domain("OBJECT_READ_FAILED", () => LayoutMetadata.Validate(Owner, view with { front_clip_distance = n }));
                Domain("OBJECT_READ_FAILED", () => LayoutMetadata.Validate(Owner, view with { view_direction = new[] { 0d, 0, n } }));
            }
            Domain("OBJECT_READ_FAILED", () => LayoutMetadata.Validate(Owner, Viewport(Owner) with { view_center = new[] { 0d, 0, 0 } }));
            Check(LayoutMetadata.Text("😀") == "😀", "Unicode roundtrip");
            Domain("ENTITY_TOO_LARGE", () => LayoutMetadata.Text(new string('a', 4097))); Domain("OBJECT_READ_FAILED", () => LayoutMetadata.Text("\ud800"));
            Domain("OBJECT_READ_FAILED", () => LayoutMetadata.Text(null)); return Task.CompletedTask;
        });
    }
}
