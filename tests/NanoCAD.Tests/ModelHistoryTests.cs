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
        await test("part chain two stored hole slots can use the same parent with distinct markers", () =>
        {
            var first = NanoCADBridge.ModelHolePlacementSlots.Observe(0, false, "A", 12, 0.25, 3);
            var second = NanoCADBridge.ModelHolePlacementSlots.Observe(1, false, "A", 17, 0.75, 20000000001L);
            if (first is not { slot: 0, object_id: "A", gs_marker: 12, parameter: 0.25 } ||
                second is not { slot: 1, object_id: "A", gs_marker: 17, data: "20000000001" })
                throw new Exception("Geometric placement slots were collapsed or did not preserve order.");
            return Task.CompletedTask;
        });
        await test("part chain follows only a selected native history ID in one part", () =>
        {
            var sketchId = Guid.NewGuid().ToString("B");
            object Request(string? focus, string[] handles) => new { bridge_instance_id = bridge, document_id = doc,
                expected_revision = "9", handles, history_focus = focus, include_downstream_history = true };
            var args = Validation.Parse("inspect_model_history", Wire.Element(Request(sketchId, ["A"])));
            if (!args.IncludeDownstreamHistory || args.HistoryFocus != sketchId.ToUpperInvariant()) throw new Exception("Lost the selected native source.");
            if (Validation.Parse("inspect_model_history", Wire.Element(Input(["A"]))).IncludeDownstreamHistory) throw new Exception("Unexpected traversal.");
            Reject(Request("issues", ["A"])); Reject(Request(sketchId, ["A", "B"])); Reject(Request(null, ["A"]));
            return Task.CompletedTask;
        });
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
