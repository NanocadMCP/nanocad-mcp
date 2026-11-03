using System.Text.Json;
using NanoCAD.Contracts;

internal static class ViewportCreateTests
{
    private static readonly EntityRef Owner = new("11111111-1111-4111-8111-111111111111", "22222222-2222-4222-8222-222222222222", "1E");
    private static void Check(bool value, string why) { if (!value) throw new Exception(why); }
    private static void Domain(string code, Action action)
    { try { action(); throw new Exception("Expected " + code); } catch (DomainException e) { Check(e.Error.code == code, e.Error.code); } }
    public static Dictionary<string, object?> Input() => new() { ["operation"] = "create_viewport", ["layout_handle"] = "001e", ["layer_handle"] = "02f",
        ["center"] = new[] { 150d, 100 }, ["width"] = 120d, ["height"] = 80d, ["view_center"] = new[] { 20d, 30 },
        ["view_height"] = 800d, ["twist_angle"] = Math.PI / 6, ["is_on"] = true, ["is_locked"] = true };
    public static Dictionary<string, object?> ProtocolInput(string bridge, string document) => new() {
        ["bridge_instance_id"] = bridge, ["document_id"] = document, ["expected_revision"] = "3", ["operation_id"] = Guid.NewGuid().ToString("D"),
        ["units"] = "Millimeters", ["operations"] = new[] { Input() } };
    public static ModifiedStructure ProtocolData(StructureWriteArguments args) => new(args.OperationId, "committed", false,
        new[] { new StructureChange(0, "create_viewport", new(args.BridgeId, args.DocumentId, "90"), "viewport", "Sheet A3:90", true) }, "single_command");
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("viewport creation public route canonicalizes identities and protects replay intent", () =>
        {
            var input = ProtocolInput(Owner.bridge_instance_id, Owner.document_id); var parsed = StructureWriteValidation.Parse(Wire.Element(input));
            var edit = (CreateViewportEdit)parsed.Operations.Single(); Check(edit.LayoutHandle == "1E" && edit.LayerHandle == "2F", "normalized handles");
            var operation = Input(); operation["layout_handle"] = "1E"; operation["layer_handle"] = "2F"; input["operations"] = new[] { operation };
            Check(StructureWriteValidation.Parse(Wire.Element(input)).Fingerprint() == parsed.Fingerprint(), "same canonical request");
            foreach (var field in new[] { "is_on", "is_locked" })
            { var altered = Input(); altered[field] = false; input["operations"] = new[] { altered }; Check(StructureWriteValidation.Parse(Wire.Element(input)).Fingerprint() != parsed.Fingerprint(), field + " bound to intent"); }
            operation["twist_angle"] = 0d;
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!, "viewport-create-examples.json"),
                JsonSerializer.Serialize(new { inputs = new[] { Input(), operation }, native_executed = false }, Wire.Json));
            return Task.CompletedTask;
        });
        await test("viewport creation rejects missing fields hidden actions malformed numbers and aliases", () =>
        {
            foreach (string field in Input().Keys)
            { var input = Input(); input.Remove(field); Domain("INVALID_ARGUMENT", () => ViewportCreateValidation.Parse(Wire.Element(input))); }
            foreach (string field in new[] { "activate", "save", "command", "initialize_layout", "view_target", "perspective" })
            { var input = Input(); input[field] = true; Domain("INVALID_ARGUMENT", () => ViewportCreateValidation.Parse(Wire.Element(input))); }
            foreach (var pair in new (string, object?)[] { ("width", 0d), ("height", -1d), ("view_height", 1e-7), ("twist_angle", 2 * Math.PI),
                ("center", new[] { 1d }), ("view_center", new[] { 1d, 2, 3 }), ("is_on", 1), ("is_locked", "true"), ("layer_handle", "0"), ("layer_handle", "1E") })
            { var input = Input(); input[pair.Item1] = pair.Item2; Domain(pair.Item1 == "layer_handle" && Equals(pair.Item2, "0") ? "INVALID_HANDLE" : "INVALID_ARGUMENT", () => ViewportCreateValidation.Parse(Wire.Element(input))); }
            string json = JsonSerializer.Serialize(Input());
            using var underflow = JsonDocument.Parse(json.Replace("\"width\":120", "\"width\":1e-9999"));
            Domain("INVALID_ARGUMENT", () => ViewportCreateValidation.Parse(underflow.RootElement));
            using var duplicate = JsonDocument.Parse(json.Replace("\"width\":120", "\"width\":120,\"width\":200"));
            Domain("INVALID_ARGUMENT", () => ViewportCreateValidation.Parse(duplicate.RootElement));
            return Task.CompletedTask;
        });
        await test("viewport creation bounds batch paper frame and derived model scale", () =>
        {
            var edit = ViewportCreateValidation.Parse(Wire.Element(Input()));
            Check(ViewportCreateValidation.ValidateBatch(Enumerable.Repeat(edit, 8)) == 8, "eight native objects");
            Check(ViewportCreateValidation.ValidateBatch(Enumerable.Repeat(edit,9))==9,"no arbitrary new viewport ceiling");
            Domain("INVALID_OPERATION_ORDER", () => ViewportCreateValidation.ValidateBatch(new StructureEdit[] { edit, new CreateLayoutEdit("A", "1E") }));
            foreach (var invalid in new[] { edit with { Center = new[] { 1e9, 0d } }, edit with { Height = 1e-6, ViewHeight = 1e9 },
                edit with { Height = double.NaN }, edit with { ViewCenter = new[] { double.PositiveInfinity, 0d } } })
                Domain("INVALID_ARGUMENT", () => ViewportCreateValidation.Validate(invalid));
            return Task.CompletedTask;
        });
        await test("viewport creation refuses uninitialized imperial model and oversized layouts", () =>
        {
            var layout = LayoutReadTests.Layout(Owner) with { is_model = false }; layout = layout with { plot = layout.plot with { paper_units_native_value = 1 } };
            ViewportCreateValidation.RequireLayout(layout, new[] { 1, -1, 2 }, 2);
            foreach (var numbers in new[] { Array.Empty<int>(), new[] { 2, 3 }, new[] { 1, 1 } }) Domain("LAYOUT_NOT_INITIALIZED", () => ViewportCreateValidation.RequireLayout(layout, numbers, 1));
            Domain("UNSUPPORTED_LAYOUT", () => ViewportCreateValidation.RequireLayout(layout with { is_model = true }, new[] { 1 }, 1));
            Domain("UNSUPPORTED_LAYOUT", () => ViewportCreateValidation.RequireLayout(layout with { plot = layout.plot with { paper_units_native_value = 0 } }, new[] { 1 }, 1));
            ViewportCreateValidation.RequireLayout(layout, Enumerable.Range(1,256),1);
            return Task.CompletedTask;
        });
        await test("viewport creation verifies requested view and rejects collateral native defaults", () =>
        {
            var edit = ViewportCreateValidation.Parse(Wire.Element(Input()));
            var good = LayoutReadTests.Viewport(Owner) with { layout = Owner, number = -1, center = new[] { 150d, 100, 0 }, width = 120, height = 80,
                view_center = new[] { 20d, 30 }, view_target = new[] { 0d, 0, 0 }, view_direction = new[] { 0d, 0, 1 }, view_height = 800,
                twist_angle = Math.PI / 6, is_on = true, is_locked = true, custom_scale = .1, standard_scale_native_value = 1,
                front_clip_on = false, back_clip_on = false, non_rect_clip_on = false, clip_boundary = new("entity", "null", null, null, null), frozen_layers = Array.Empty<StructureDependency>() };
            ViewportCreateValidation.RequireDesired(edit, good, true);
            foreach (var bad in new[] { good with { number = 1 }, good with { is_on = false }, good with { width = 200 }, good with { custom_scale = 1 },
                good with { view_target = new[] { 0d, 0, 1 } }, good with { front_clip_on = true }, good with { is_perspective = true },
                good with { view_center = new[] { 0d, 0 } }, good with { is_locked = false }, good with { layout = Owner with { handle = "3C" } } })
            {
                if (bad.layout != good.layout) Domain("API_EXCEPTION", () => ViewportWriteValidation.RequireSame(good, bad));
                else Domain("API_EXCEPTION", () => ViewportCreateValidation.RequireDesired(edit, bad, true));
            }
            ViewportCreateValidation.RequireDesired(edit, good with { is_on = false }, false);
            return Task.CompletedTask;
        });
    }
}
