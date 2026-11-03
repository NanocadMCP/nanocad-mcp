using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using NanoCAD.McpServer;

internal static class Phase4Tests
{
    private static void Check(bool test, string message) { if (!test) throw new Exception(message); }
    private static void Domain(string code, Action action)
    { try { action(); throw new Exception("Expected " + code); } catch (DomainException e) { Check(e.Error.code == code, "Unexpected " + e.Error.code); } }
    public static object ProtocolData(string operation, string bridge, string document)
    {
        EntityRef Ref(string handle) => new(bridge, document, handle);
        if (operation == "get_structure") return new StructurePage("layers", new[] {
            new StructureRecord(Ref("A"), "layers", "Check", new { is_off = false, is_frozen = false, is_locked = true, is_dependent = false,
                is_plottable = true, color = new { method = "aci", aci = 3, rgb = (int[]?)null }, linetype = Ref("B"), lineweight = -3 }) }, 1, 1, true, null);
        double[] matrix = { 0, -3, 0, 109, 2, 0, 0, 46, 0, 0, 1, 0, 0, 0, 0, 1 };
        double[] identity = { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };
        var block = new BlockInfo(Ref("AA"), "Test block", false, false, false, false, false, false, new[] { 2d, 3, 0 }, "Millimeters", null);
        var stored=new{visible=true,color=new{method="by_layer",aci=(int?)null,rgb=(int[]?)null},linetype="ByLayer",lineweight=new{mode="by_layer",value_hundredth_mm=(int?)null,native_value=-1}};
        var attribute = new AttributeInfo(Ref("AC"), "TAG", "Value", new[] { 100d, 50, 0 }, 3, 0, false, false, "WCS", identity,
            new("0","A"),stored,new(Ref("AD"),"Standard",1,0,"TextLeft","TextBase",new double[3]));
        var root = new BlockNode(new[] { "AB" }, new(Ref("AB"), "BlockReference", "test.BlockReference", new("0", "A"), "model", "supported"), identity,
            null, new(block, new[] { 100d, 50, 0 }, new[] { 0d, 0, 1 }, new[] { 2d, 3, 1 }, Math.PI / 2, matrix, matrix, new[] { attribute }, false, "expanded",stored), null);
        return new BlockInspection(Ref("AB"), new[] { root }, true, Array.Empty<string>());
    }
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        string doc = Guid.NewGuid().ToString("D");
        await test("structure validation: sections, identity, strict fields and normalized block handles", () =>
        {
            foreach (string section in StructureValidation.Sections)
                Check(Validation.Parse("get_structure", Wire.Element(new { document_id = doc, section, page_size = 2 })).Section == section, "section lost");
            var block = Validation.Parse("inspect_block", Wire.Element(new { document_id = doc, handle = "000a", max_depth = 0, max_nodes = 1 }));
            Check(block.BlockHandle == "A" && block.MaxDepth == 0 && block.MaxNodes == 1, "block defaults/normalization");
            Domain("INVALID_ARGUMENT", () => Validation.Parse("get_structure", Wire.Element(new { section = "blocks" })));
            Domain("INVALID_ARGUMENT", () => Validation.Parse("get_structure", Wire.Element(new { document_id = doc, section = "commands" })));
            Domain("INVALID_ARGUMENT", () => Validation.Parse("inspect_block", Wire.Element(new { document_id = doc, handle = "A", max_depth = -1 })));
            Domain("INVALID_ARGUMENT", () => Validation.Parse("inspect_block", Wire.Element(new { document_id = doc, handle = "A", max_nodes = 0 })));
            Domain("INVALID_ARGUMENT", () => Validation.Parse("inspect_block", Wire.Element(new { document_id = doc, handle = "A", max_depth = 1.5 })));
            Domain("INVALID_HANDLE", () => Validation.Parse("inspect_block", Wire.Element(new { document_id = doc, handle = "0" })));
            using var duplicate = JsonDocument.Parse("{\"document_id\":\"" + doc + "\",\"handle\":\"A\",\"handle\":\"B\"}");
            Domain("INVALID_ARGUMENT", () => Validation.Parse("inspect_block", duplicate.RootElement));
            Domain("INVALID_ARGUMENT", () => Validation.Parse("inspect_block", Wire.Element(new { document_id = doc, handle = "A", command = "_EXPLODE" })));
            return Task.CompletedTask;
        });
        await test("structural snapshots: numeric handle order, section isolation and revision invalidation", () =>
        {
            var pages = new SnapshotPages<StructureRecord>(i => i.@ref.handle);
            StructureRecord Item(string h) => new(new("bridge", doc, h), "blocks", "Block" + h, new { });
            var first = pages.First(doc, "1", null, "blocks", new[] { Item("10"), Item("2"), Item("FFFFFFFFFFFFFFFF") }, 1);
            Check(first.items.Single().@ref.handle == "2" && !first.complete, "numeric order");
            Domain("INVALID_CURSOR", () => pages.Next(first.next_cursor!, doc, "1", null, "layers", 1));
            Domain("STALE_DOCUMENT", () => pages.Next(first.next_cursor!, doc, "2", null, "blocks", 1));
            var rest = pages.Next(first.next_cursor!, doc, "1", null, "blocks", 500);
            Check(rest.complete && rest.items.Select(i => i.@ref.handle).SequenceEqual(new[] { "10", "FFFFFFFFFFFFFFFF" }), "structural page lost/repeated entries");
            return Task.CompletedTask;
        });
        await test("structural read validation rejects commands before contacting the host", async () =>
        {
            int calls = 0; string bridge = Guid.NewGuid().ToString("D");
            using var listener = new BridgeListener(Discovery.Describe(bridge, "structure-test-no-CAD"), (request, _) =>
            { calls++; return Task.FromResult(Result.Ok(request.requestId, new { })); }, new JsonLog("structure-test"));
            var args = new Dictionary<string, JsonElement> { ["bridge_instance_id"] = Wire.Element(bridge), ["document_id"] = Wire.Element(doc),
                ["handle"] = Wire.Element("A"), ["save"] = Wire.Element(true) };
            var result = await new ToolService().CallAsync("inspect_block", args, default);
            Check(result.IsError == true && calls == 0, "unknown structural argument reached native host");
        });
    }
}
