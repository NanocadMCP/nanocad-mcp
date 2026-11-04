using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class ModelHistoryTests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        var bridge = Guid.NewGuid().ToString("D"); var doc = Guid.NewGuid().ToString("D");
        object Input(string[] handles) => new { bridge_instance_id = bridge, document_id = doc, expected_revision = "18446744073709551615", handles };
        void Reject(object input)
        {
            try { Validation.Parse("inspect_model_history", Wire.Element(input)); }
            catch (DomainException) { return; }
            throw new Exception("Invalid history request accepted.");
        }
        await test("model history requires explicit context and bounded canonical handles", () =>
        {
            var args = Validation.Parse("inspect_model_history", Wire.Element(Input(new[] { "00af", "B0" })));
            if (!args.Handles!.SequenceEqual(new[] { "AF", "B0" }) || args.ExpectedRevision != "18446744073709551615") throw new Exception("History identity changed.");
            Reject(Input(Array.Empty<string>())); Validation.Parse("inspect_model_history", Wire.Element(Input(new[] { "1", "2", "3", "4", "5" })));
            Reject(new { document_id = doc, expected_revision = "9", handles = new[] { "1" } });
            Reject(new { bridge_instance_id = bridge, document_id = doc, handles = new[] { "1" } });
            Reject(new { bridge_instance_id = bridge, document_id = doc, expected_revision = "9", handles = new[] { "1" }, rebuild = true });
            return Task.CompletedTask;
        });
        await test("model history catalog is read only and separates graph scope from geometry", () =>
        {
            var tool = ToolCatalog.Tools.Single(t => t.Name == "nanocad.inspect_model_history");
            if (tool.Annotations?.ReadOnlyHint != true || tool.Annotations.DestructiveHint != false || Wire.WriteOperations.Contains("inspect_model_history")) throw new Exception("Read annotation changed.");
            if (tool.InputSchema.GetProperty("properties").GetProperty("handles").TryGetProperty("maxItems", out _)) throw new Exception("Unbounded or misleading graph contract.");
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!, "tools-list.json"), JsonSerializer.Serialize(ToolCatalog.Tools, Wire.Json));
            return Task.CompletedTask;
        });
        await test("model history selective sketch geometry bounds identity and keeps metadata default", () =>
        {
            var sketch = Guid.NewGuid().ToString("B");
            var args = Validation.Parse("inspect_model_history", Wire.Element(new { bridge_instance_id = bridge, document_id = doc, expected_revision = "9", handles = new[] { "A" }, sketch_geometry_ids = new[] { sketch } }));
            if (args.SketchGeometryIds?.Single() != sketch.ToUpperInvariant()) throw new Exception("Sketch identity not canonicalized.");
            if (Validation.Parse("inspect_model_history", Wire.Element(Input(new[] { "A" }))).SketchGeometryIds != null) throw new Exception("Ordinary history unexpectedly requests geometry.");
            foreach (var ids in new[] { Array.Empty<string>(), new[] { sketch, sketch.ToUpperInvariant() }, new[] { "A" }, new[] { Guid.Empty.ToString("B") } })
                Reject(new { bridge_instance_id = bridge, document_id = doc, expected_revision = "9", handles = new[] { "A" }, sketch_geometry_ids = ids });
            var many = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid().ToString("B")).ToArray();
            if (Validation.Parse("inspect_model_history", Wire.Element(new { bridge_instance_id = bridge, document_id = doc, expected_revision = "9", handles = new[] { "A" }, sketch_geometry_ids = many })).SketchGeometryIds!.Length != 5) throw new Exception("A former sketch count ceiling remains.");
            Reject(new { bridge_instance_id = bridge, document_id = doc, expected_revision = "9", handles = new[] { "A" }, sketch_geometry_ids = new[] { sketch }, solve = true });
            return Task.CompletedTask;
        });
    }
}
