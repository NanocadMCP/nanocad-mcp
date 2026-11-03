using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class SolidReadTests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("solid read exposes bounded native metadata with explicit history scope", () =>
        {
            var args=Validation.Parse("query_objects", Wire.Element(new {document_id=Guid.NewGuid().ToString("D"),types=new[]{"Solid3d"}}));
            if(args.Types![0]!="Solid3d") throw new Exception("Solid filter lost.");
            var inspect=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.inspect_objects");
            var solid=inspect.OutputSchema!.Value.GetProperty("$defs").GetProperty("solid3d").GetProperty("properties");
            if(solid.GetProperty("history_scope").GetProperty("const").GetString()!="native_flags_only_feature_graph_not_exposed" || inspect.Annotations?.ReadOnlyHint!=true)
                throw new Exception("Solid inspection overclaims history or mutation scope.");
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!,"tools-list.json"),JsonSerializer.Serialize(ToolCatalog.Tools,Wire.Json));
            return Task.CompletedTask;
        });
    }
}
