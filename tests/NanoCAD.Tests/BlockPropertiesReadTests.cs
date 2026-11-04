using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class BlockPropertiesReadTests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        void Check(bool ok) { if (!ok) throw new Exception("Block stored properties expectation failed."); }
        await test("block properties read shares stored property schema and exposes attribute style and coordinates", () =>
        {
            var tool = ToolCatalog.Tools.Single(t => t.Name == "nanocad.inspect_block"); var defs = AuditServerContractSupport.Output("inspect_block").GetProperty("$defs");
            var insert = defs.GetProperty("insert_info").GetProperty("properties"); var attr = defs.GetProperty("attribute_info").GetProperty("properties");
            Check(insert.GetProperty("properties").GetRawText() == attr.GetProperty("properties").GetRawText());
            Check(attr.GetProperty("layer").GetProperty("properties").TryGetProperty("handle", out _));
            var format = defs.GetProperty("attribute_text_format"); Check(format.GetProperty("required").GetArrayLength() == 7 && format.GetProperty("description").GetString()!.Contains("radians"));
            Check(tool.Annotations!.ReadOnlyHint == true && AuditServerContractSupport.Help("inspect_block").Contains("stored", StringComparison.OrdinalIgnoreCase));
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!, "tools-list.json"), JsonSerializer.Serialize(ToolCatalog.Tools, Wire.Json)); return Task.CompletedTask;
        });
        await test("block properties read serializes native color modes and style identity without inheritance resolution", () =>
        {
            var data = (BlockInspection)Phase4Tests.ProtocolData("inspect_block", "11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222");
            var root = data.nodes[0]; var attribute = root.insert!.attributes[0];
            var colored = root.insert with { properties = new { visible = false, color = new { method = "true_color", aci = (int?)null, rgb = new[] { 10, 120, 230 } }, linetype = "Dashed", lineweight = new { mode = "explicit", value_hundredth_mm = 35, native_value = 35 } } };
            var value = Wire.Element(data with { nodes = new[] { root with { insert = colored } } }); var insert = value.GetProperty("nodes")[0].GetProperty("insert");
            Check(insert.GetProperty("properties").GetProperty("color").GetProperty("rgb")[1].GetInt32() == 120 && !insert.GetProperty("properties").GetProperty("visible").GetBoolean());
            var a = insert.GetProperty("attributes")[0]; Check(a.GetProperty("properties").GetProperty("color").GetProperty("method").GetString() == "by_layer" && a.GetProperty("text_format").GetProperty("text_style_ref").GetProperty("handle").GetString() == "AD");
            Check(a.GetProperty("layer").GetProperty("handle").GetString() == "A" && a.GetProperty("coordinate_system").GetString() == "WCS" && a.GetProperty("text_format").GetProperty("alignment_point").GetArrayLength() == 3);
            return Task.CompletedTask;
        });
    }
}
