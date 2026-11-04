using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class SolidFinishingCopyTests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        string bridge = Guid.NewGuid().ToString("D"), doc = Guid.NewGuid().ToString("D"), op = Guid.NewGuid().ToString("D");
        Dictionary<string, object> Item(string kind) => new() { ["type"] = "Solid3d", ["kind"] = kind, ["source_handle"] = "AB", ["topology_token"] = new string('A', 64), [kind == "shell_copy" ? "remove_faces" : "edges"] = new[] { "1" }, [kind == "shell_copy" ? "thickness" : "radius"] = 1d };
        CreateArguments Parse(params object[] entities) => CreateValidation.Parse(Wire.Element(new { bridge_instance_id = bridge, document_id = doc, operation_id = op, expected_revision = "1", units = "Millimeters", entities }));
        void Reject(object item) { try { Parse(item); } catch (DomainException) { return; } throw new Exception("Unsafe finishing input accepted."); }
        await test("solid finishing copies fingerprint source snapshot selection and dimensions", () =>
        {
            foreach (string kind in new[] { "fillet_copy", "chamfer_copy", "shell_copy" })
            {
                var item = Item(kind);
                if (kind == "chamfer_copy") { item.Remove("radius"); item["base_face"] = "2"; item["base_distance"] = 1d; item["other_distance"] = 2d; }
                var baseline = Parse(item); var spec = (SolidFinishingCopySpec)baseline.Entities.Single();
                if (spec.Kind != kind || spec.SourceHandle != "AB" || spec.Subentities.Single() != "1" || spec.Size != 1) throw new Exception("Finishing intent lost.");
                var changes = new Dictionary<string, object> { ["source_handle"] = "AC", ["topology_token"] = new string('B', 64), [kind == "shell_copy" ? "remove_faces" : "edges"] = new[] { "3" }, [kind == "shell_copy" ? "thickness" : kind == "fillet_copy" ? "radius" : "base_distance"] = 2d };
                if (kind == "chamfer_copy") { changes["base_face"] = "4"; changes["other_distance"] = 3d; }
                foreach (var change in changes) { var altered = new Dictionary<string, object>(item) { [change.Key] = change.Value }; if (Parse(altered).Fingerprint() == baseline.Fingerprint()) throw new Exception("Finishing fingerprint collision: " + change.Key); }
            }
            return Task.CompletedTask;
        });
        await test("solid finishing copies reject invalid and ambiguous native identity controls", () =>
        {
            foreach (var change in new Dictionary<string, object> { ["topology_token"] = "old", ["source_handle"] = "0", ["edges"] = new[] { "1", "1" }, ["radius"] = 0d, ["erase_source"] = true }) { var item = Item("fillet_copy"); item[change.Key] = change.Value; Reject(item); }
            foreach (string id in new[] { "0", "-1", "01", "9223372036854775808", "1.0", "1e2" }) { var item = Item("fillet_copy"); item["edges"] = new[] { id }; Reject(item); }
            var empty = Item("fillet_copy"); empty["edges"] = Array.Empty<string>(); Reject(empty);
            var many = Item("fillet_copy"); many["edges"] = Enumerable.Range(1, 17).Select(i => i.ToString()).ToArray();
            if (((SolidFinishingCopySpec)Parse(many).Entities.Single()).Subentities.Length != 17) throw new Exception("Former edge count ceiling remains.");
            foreach (string missing in new[] { "topology_token", "source_handle", "edges", "radius" }) { var item = Item("fillet_copy"); item.Remove(missing); Reject(item); }
            return Task.CompletedTask;
        });
        await test("solid finishing copies retain batch and block definition limits", () =>
        {
            Parse(Enumerable.Repeat<object>(Item("fillet_copy"), 300).ToArray());
            try { CreateValidation.ParseEntities(Wire.Element(new[] { Item("fillet_copy") })); throw new Exception("Finishing allowed inside definition."); } catch (DomainException) { }
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!, "tools-list.json"), JsonSerializer.Serialize(ToolCatalog.Tools, Wire.Json));
            return Task.CompletedTask;
        });
    }
}
