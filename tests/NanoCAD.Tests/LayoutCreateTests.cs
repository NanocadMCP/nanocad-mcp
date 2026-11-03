using System.Text.Json;
using NanoCAD.Contracts;

internal static class LayoutCreateTests
{
    private static readonly EntityRef Owner = new("11111111-1111-4111-8111-111111111111", "22222222-2222-4222-8222-222222222222", "1E");
    private static void Check(bool ok, string why) { if (!ok) throw new Exception(why); }
    private static void Domain(string code, Action action)
    { try { action(); throw new Exception("Expected " + code); } catch (DomainException e) { Check(e.Error.code == code, e.Error.code); } }
    public static Dictionary<string, object?> Input() => new() { ["operation"] = "create_layout", ["name"] = "New A3", ["base_layout_handle"] = "001e" };
    public static Dictionary<string, object?> ProtocolInput(string bridge, string document) => new() {
        ["bridge_instance_id"] = bridge, ["document_id"] = document, ["expected_revision"] = "3", ["operation_id"] = Guid.NewGuid().ToString("D"),
        ["units"] = "Millimeters", ["operations"] = new[] { Input() } };
    public static ModifiedStructure ProtocolData(StructureWriteArguments args) => new(args.OperationId, "committed", false,
        new[] { new StructureChange(0, "create_layout", new(args.BridgeId, args.DocumentId, "90"), "layout", "New A3", true) }, "single_command");
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("layout creation parser retains Unicode and canonical source identity", () =>
        {
            var input = ProtocolInput(Owner.bridge_instance_id, Owner.document_id); var parsed = StructureWriteValidation.Parse(Wire.Element(input));
            Check(parsed.Operations.Single() == new CreateLayoutEdit("New A3", "1E"), "Typed public route");
            var same = Input(); same["base_layout_handle"] = "1E"; input["operations"] = new[] { same };
            Check(StructureWriteValidation.Parse(Wire.Element(input)).Fingerprint() == parsed.Fingerprint(), "Canonical replay fingerprint");
            same["base_layout_handle"] = "3C";
            Check(StructureWriteValidation.Parse(Wire.Element(input)).Fingerprint() != parsed.Fingerprint(), "Base layout participates in intent");
            same["base_layout_handle"] = "1E"; same["name"] = "Other";
            Check(StructureWriteValidation.Parse(Wire.Element(input)).Fingerprint() != parsed.Fingerprint(), "Name participates in intent");
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!, "layout-create-examples.json"),
                JsonSerializer.Serialize(new { inputs = new[] { Input(), same }, native_executed = false }, Wire.Json));
            return Task.CompletedTask;
        });
        await test("layout creation rejects hidden actions invalid names and duplicate fields", () =>
        {
            foreach (var name in new[] { "", " Model", "MODEL", "A|B", "A/B", "A\nB", new string('x', 256) })
            { var input = Input(); input["name"] = name; Domain("INVALID_ARGUMENT", () => LayoutCreateValidation.Parse(Wire.Element(input))); }
            foreach (var field in new[] { "activate", "copy_entities", "save", "command", "plot_configuration", "tab_order" })
            { var input = Input(); input[field] = true; Domain("INVALID_ARGUMENT", () => LayoutCreateValidation.Parse(Wire.Element(input))); }
            Domain("INVALID_ARGUMENT", () => LayoutCreateValidation.Validate(new("bad\ud800", "1E")));
            using var duplicate = JsonDocument.Parse("{\"operation\":\"create_layout\",\"name\":\"A\",\"name\":\"B\",\"base_layout_handle\":\"1E\"}");
            Domain("INVALID_ARGUMENT", () => LayoutCreateValidation.Parse(duplicate.RootElement));
            return Task.CompletedTask;
        });
        await test("layout batches reject mixed writes duplicate names and excess work", () =>
        {
            var edit = new CreateLayoutEdit("Sheet", "1E"); var batch = Enumerable.Range(1, 8).Select(n => edit with { Name = "Sheet " + n }).ToArray();
            Check(LayoutCreateValidation.ValidateBatch(batch) == 32, "Count all native records");
            Check(LayoutCreateValidation.ValidateBatch(batch.Append(edit))==36,"no arbitrary layout batch ceiling");
            Domain("INVALID_OPERATION_ORDER", () => LayoutCreateValidation.ValidateBatch(new[] { edit, edit with { Name = "SHEET" } }));
            Domain("INVALID_OPERATION_ORDER", () => LayoutCreateValidation.ValidateBatch(new StructureEdit[] { edit, new SetLayerEdit("2F", new(null, null, null, null, null, false, null)) }));
            return Task.CompletedTask;
        });
        await test("layout tab append protects existing order and bounds count and overflow", () =>
        {
            Check(LayoutCreateValidation.NextTabOrder(new[] { 0, 2, 20 }, 1, 2) == 22, "Append after maximum");
            Check(LayoutCreateValidation.NextTabOrder(Enumerable.Range(0, 248), 7, 8) == 255, "256 layout boundary");
            Check(LayoutCreateValidation.NextTabOrder(Enumerable.Range(0,249),0,8)==249,"no invented layout count ceiling");
            foreach (int order in new[] { -1, int.MaxValue }) Domain("UNSUPPORTED_LAYOUT", () => LayoutCreateValidation.NextTabOrder(new[] { order }, 0, 1));
            return Task.CompletedTask;
        });
        await test("layout metadata verification rejects collateral plot limits ownership and activation changes", () =>
        {
            var source = LayoutReadTests.Layout(Owner); var expected = LayoutCreateValidation.Expected(source, 4, Owner with { handle = "90" });
            Check(expected.plot == source.plot && expected.limits_max == source.limits_max && !expected.tab_selected && expected.tab_order == 4, "Stored plot data retained");
            foreach (var bad in new[] { expected with { is_model = true }, expected with { tab_selected = true }, expected with { tab_order = 3 },
                expected with { block_record = source.block_record }, expected with { limits_max = new[] { 11d, 8.5 } },
                expected with { plot = source.plot with { custom_scale_denominator = 1 } }, expected with { plot = source.plot with { paper_units_native_value = 0 } },
                expected with { plot = source.plot with { configuration_name = "Changed" } } })
                Domain("API_EXCEPTION", () => LayoutCreateValidation.RequireSame(expected, bad));
            return Task.CompletedTask;
        });
        await test("new layout native limits are bounded independently of copied plot settings", () =>
        {
            var expected = LayoutCreateValidation.Expected(LayoutReadTests.Layout(Owner), 4, Owner with { handle = "90" });
            var native = expected with { limits_min = new[] { 0d, 0 }, limits_max = new[] { 12d, 9 } };
            LayoutCreateValidation.RequireNew(expected, native);
            foreach (var bad in new[] { native with { limits_min = new[] { 13d, 0 } }, native with { limits_max = new[] { double.NaN, 9 } },
                native with { limits_max = new[] { 1e10, 9 } }, native with { limits_min = new[] { 0d } },
                native with { plot = native.plot with { custom_scale_denominator = 1 } }, native with { tab_selected = true },
                native with { block_record = Owner with { handle = "91" } } })
                Domain("API_EXCEPTION", () => LayoutCreateValidation.RequireNew(expected, bad));
            // Existing source layouts still require exact limits, including after a failed apply.
            Domain("API_EXCEPTION", () => LayoutCreateValidation.RequireSame(expected, native));
            return Task.CompletedTask;
        });
    }
}
