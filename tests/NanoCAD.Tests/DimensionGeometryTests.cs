using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class DimensionGeometryTests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("dimension geometry query filter is read-only and schema exposes exact supported kinds", () =>
        {
            var args = Validation.Parse("query_objects", Wire.Element(new { document_id = Guid.NewGuid().ToString("D"), types = new[] { "Dimension", "Line" } }));
            if (args.Types == null || !args.Types.Contains("Dimension")) throw new Exception("Dimension filter lost.");
            var tool = ToolCatalog.Tools.Single(t => t.Name == "nanocad.inspect_objects");
            var schema = AuditServerContractSupport.Output("inspect_objects");
            var variants = schema.GetProperty("$defs").GetProperty("dimension").GetProperty("oneOf");
            var expected = DimensionEditingValidation341.Kinds;
            var actual = variants.EnumerateArray().Select(v => v.GetProperty("properties").GetProperty("dimension_kind").GetProperty("const").GetString()).ToArray();
            if (!actual.ToHashSet(StringComparer.Ordinal).SetEquals(expected) || tool.Annotations?.ReadOnlyHint != true) throw new Exception("Dimension schema/read policy mismatch.");
            string run = Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!;
            File.WriteAllText(Path.Combine(run, "tools-list.json"), JsonSerializer.Serialize(ToolCatalog.Tools, Wire.Json));
            return Task.CompletedTask;
        });
    }
}
