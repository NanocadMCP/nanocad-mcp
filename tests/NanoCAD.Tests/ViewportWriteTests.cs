using System.Text.Json;
using NanoCAD.Contracts;

internal static class ViewportWriteTests
{
    private static readonly EntityRef Owner = new("11111111-1111-4111-8111-111111111111", "22222222-2222-4222-8222-222222222222", "38");
    private static void Check(bool ok, string text) { if (!ok) throw new Exception(text); }
    private static void Domain(string code, Action action)
    { try { action(); throw new Exception("Expected " + code); } catch (DomainException e) { Check(e.Error.code == code, e.Error.code); } }
    public static Dictionary<string, object?> Input() => new()
    {
        ["operation"] = "set_viewport_view",
        ["layout_handle"] = "1e",
        ["handle"] = "38",
        ["properties"] = new Dictionary<string, object?> { ["view_center"] = new[] { 40d, -15 }, ["view_height"] = 500d, ["twist_angle"] = Math.PI / 2, ["is_locked"] = true }
    };
    private static SetViewportViewEdit Parse(object value) => ViewportWriteValidation.Parse(Wire.Element(value));
    public static ViewportProperties Before() => LayoutReadTests.Viewport(Owner) with
    {
        layout = Owner with { handle = "1E" },
        is_locked = false,
        number = -1,
        view_direction = new[] { 0d, 0, 1 },
        view_target = new[] { 0d, 0, 0 },
        front_clip_on = false,
        back_clip_on = false,
        non_rect_clip_on = false,
        clip_boundary = new("entity", "null", null, null, null)
    };
    public static ViewportProperties After() => Before() with
    {
        view_center = new[] { 40d, -15 },
        view_height = 500,
        twist_angle = Math.PI / 2,
        is_locked = true,
        custom_scale = .1,
        standard_scale_native_value = 7
    };
    public static Dictionary<string, object?> ProtocolInput(string bridge, string document) => new()
    {
        ["bridge_instance_id"] = bridge,
        ["document_id"] = document,
        ["expected_revision"] = "3",
        ["operation_id"] = Guid.NewGuid().ToString("D"),
        ["units"] = "Millimeters",
        ["operations"] = new[] { Input() }
    };
    public static ModifiedStructure ProtocolData(StructureWriteArguments args) => new(args.OperationId, "committed", false,
        new[] { new StructureChange(0, "set_viewport_view", new(args.BridgeId, args.DocumentId, "38"), "viewport", "Sheet A3:-1", false) }, "single_command");
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("viewport paper frame resizing verifies explicit model scale and center", () =>
        {
            var edit = Parse(new { operation = "set_viewport_view", layout_handle = "1E", handle = "38", properties = new { center = new[] { 210, 160 }, width = 280, height = 200, view_height = 200 } });
            var before = Before(); var after = before with { center = new[] { 210d, 160, before.center[2] }, width = 280, height = 200, view_height = 200, custom_scale = 1, standard_scale_native_value = 16 };
            ViewportWriteValidation.RequireDesired(before, after, edit);
            Domain("API_EXCEPTION", () => ViewportWriteValidation.RequireDesired(before, after with { custom_scale = .5 }, edit));
            Domain("VIEWPORT_LOCKED", () => ViewportWriteValidation.RequireWritable(before with { is_locked = true }, edit));
            return Task.CompletedTask;
        });
        await test("viewport view edits preserve model-unit intent and derived native scale", () =>
        {
            var edit = Parse(Input()); Check(edit.LayoutHandle == "1E" && edit.Handle == "38", "Canonical identity");
            ViewportWriteValidation.RequireDesired(Before(), After(), edit);
            var lower = Before() with { custom_scale = .2, standard_scale_native_value = 5 };
            var locking = edit with { Properties = new(null, null, null, true) };
            ViewportWriteValidation.RequireDesired(lower, lower with { is_locked = true }, locking);
            var input = ProtocolInput(Owner.bridge_instance_id, Owner.document_id); var parsed = StructureWriteValidation.Parse(Wire.Element(input));
            Check(parsed.Operations.Single() is SetViewportViewEdit, "Public typed route");
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!, "viewport-write-examples.json"), JsonSerializer.Serialize(new
            {
                inputs = new[] { Input(),
                new Dictionary<string, object?> { ["operation"] = "set_viewport_view", ["layout_handle"] = "1E", ["handle"] = "38", ["properties"] = new { is_locked = false } } },
                before = Before(),
                after = After(),
                native_executed = false
            }, Wire.Json));
            return Task.CompletedTask;
        });
        await test("viewport parser rejects hidden context changes malformed points and lossy numbers", () =>
        {
            foreach (object properties in new object[] { new { }, new { view_center = new[] { 1, 2, 3 } }, new { view_center = new[] { 1e10, 0 } }, new { view_height = 0 }, new { view_height = 1e-10 },
                new { view_height = "500" }, new { twist_angle = -1 }, new { twist_angle = 2 * Math.PI }, new { is_locked = 1 }, new { is_on = 1 }, new { custom_scale = .2 }, new { width = 0 },
                new { view_direction = new[] { 0, 0, 0 } }, new { frozen_layer_handles = new[] { "a", "00A" } } })
            { var input = Input(); input["properties"] = properties; Domain("INVALID_ARGUMENT", () => Parse(input)); }
            var supported = Parse(new { operation = "set_viewport_view", layout_handle = "1E", handle = "38", properties = new { width = 100, is_on = true, view_direction = new[] { 1, 0, 1 }, view_target = new[] { 0, 0, 1 } } });
            Check(supported.Properties.Width == 100 && supported.Properties.IsOn == true && supported.Properties.ViewDirection!.SequenceEqual(new[] { 1d, 0, 1 }) && supported.Properties.ViewTarget!.SequenceEqual(new[] { 0d, 0, 1 }), "Expanded viewport intent changed.");
            foreach (string field in new[] { "activate", "save", "layer", "command", "plot_configuration" })
            { var input = Input(); input[field] = true; Domain("INVALID_ARGUMENT", () => Parse(input)); }
            using var tiny = JsonDocument.Parse("{\"operation\":\"set_viewport_view\",\"layout_handle\":\"1E\",\"handle\":\"38\",\"properties\":{\"twist_angle\":1e-999}}");
            Domain("INVALID_ARGUMENT", () => ViewportWriteValidation.Parse(tiny.RootElement));
            using var duplicate = JsonDocument.Parse("{\"operation\":\"set_viewport_view\",\"layout_handle\":\"1E\",\"handle\":\"38\",\"properties\":{\"is_locked\":true,\"is_locked\":false}}");
            Domain("INVALID_ARGUMENT", () => ViewportWriteValidation.Parse(duplicate.RootElement));
            return Task.CompletedTask;
        });
        await test("viewport policy protects overview perspective clipping and locked views", () =>
        {
            var before = Before(); var edit = Parse(Input());
            foreach (var bad in new[] { before with { number = 1 }, before with { is_perspective = true }, before with { non_rect_clip_on = true }, before with { front_clip_on = true },
                before with { back_clip_on = true }, before with { view_direction = new[] { 0d, 0, 0 } },
                before with { clip_boundary = new("entity", "resolved", Owner with { handle = "36" }, "Polyline", null) } })
                Domain("UNSUPPORTED_VIEWPORT", () => ViewportWriteValidation.RequireWritable(bad, edit));
            // Nonzero oblique directions and WCS targets are now supported;
            // perspective, clipping, paper overview and locked views stay guarded.
            ViewportWriteValidation.RequireWritable(before with { view_direction = new[] { 1d, 0, 1 }, view_target = new[] { 0d, 0, 1 } }, edit);
            Domain("VIEWPORT_LOCKED", () => ViewportWriteValidation.RequireWritable(before with { is_locked = true }, edit));
            ViewportWriteValidation.RequireWritable(before with { is_locked = true }, edit with { Properties = edit.Properties with { IsLocked = false } });
            ViewportWriteValidation.RequireWritable(before with { is_locked = true }, edit with { Properties = new(null, null, null, false) });
            return Task.CompletedTask;
        });
        await test("viewport batches bound work and reject repeated targets or mixed edit families", () =>
        {
            var edit = Parse(Input()); var batch = Enumerable.Range(100, 16).Select(i => edit with { Handle = i.ToString("X") }).ToArray();
            Check(ViewportWriteValidation.ValidateBatch(batch) == 0, "Viewport edits create no objects");
            ViewportWriteValidation.ValidateBatch(batch.Append(edit));
            Domain("INVALID_OPERATION_ORDER", () => ViewportWriteValidation.ValidateBatch(new[] { edit, edit }));
            Domain("INVALID_OPERATION_ORDER", () => ViewportWriteValidation.ValidateBatch(new StructureEdit[] { edit, new SetLayerEdit("2F", new(null, null, null, null, null, false, null)) }));
            return Task.CompletedTask;
        });
        await test("viewport native verification catches collateral geometry metadata and scale changes", () =>
        {
            var edit = Parse(Input()); var after = After();
            foreach (var bad in new[] { after with { width = 101 }, after with { is_on = false }, after with { view_target = new[] { 1d, 0, 0 } }, after with { number = 2 },
                after with { frozen_layers = Array.Empty<StructureDependency>() }, after with { lens_length = 75 }, after with { custom_scale = .11 }, after with { standard_scale_native_value = -1 },
                after with { view_center = new[] { 40d, -14 } }, after with { view_height = 501 }, after with { twist_angle = 0 }, after with { is_locked = false } })
                Domain("API_EXCEPTION", () => ViewportWriteValidation.RequireDesired(Before(), bad, edit));
            Domain("API_EXCEPTION", () => ViewportWriteValidation.RequireSame(after, after with { standard_scale_native_value = 6 }));
            return Task.CompletedTask;
        });
        await test("viewport canonical fingerprint detects changed view intent and preserves handle spelling equivalence", () =>
        {
            var input = ProtocolInput(Owner.bridge_instance_id, Owner.document_id); var parsed = StructureWriteValidation.Parse(Wire.Element(input));
            var same = Input(); same["layout_handle"] = "1E"; input["operations"] = new[] { same };
            Check(StructureWriteValidation.Parse(Wire.Element(input)).Fingerprint() == parsed.Fingerprint(), "Canonical handle fingerprint");
            same["properties"] = new { view_height = 501 };
            Check(StructureWriteValidation.Parse(Wire.Element(input)).Fingerprint() != parsed.Fingerprint(), "Changed intent fingerprint");
            return Task.CompletedTask;
        });
    }
}










