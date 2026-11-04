using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class GeometryAnalysisTests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        Dictionary<string, object> Input(string mode = "metrics", params string[] handles) => new()
        {
            ["bridge_instance_id"] = Guid.NewGuid().ToString("D"),
            ["document_id"] = Guid.NewGuid().ToString("D"),
            ["expected_revision"] = "00017",
            ["mode"] = mode,
            ["handles"] = handles.Length == 0 ? new[] { "0a" } : handles
        };
        void Reject(object input)
        {
            try { GeometryAnalysisValidation.Parse(input is JsonElement json ? json : Wire.Element(input)); }
            catch (DomainException) { return; }
            throw new Exception("Unsafe analysis arguments accepted.");
        }
        await test("geometry analysis validates native read modes identity and bounded input", () =>
        {
            var input = Input(); input["point"] = new[] { 1d, 2d, 3d };
            var parsed = GeometryAnalysisValidation.Parse(Wire.Element(input));
            if (parsed.Read.ExpectedRevision != "17" || parsed.Read.Handles![0] != "A" || parsed.Point![2] != 3 || Wire.ValidateBridge("analyze_geometry", Wire.Element(input)) != parsed.Read.BridgeId)
                throw new Exception("Analysis context lost.");
            foreach (var missing in new[] { "bridge_instance_id", "document_id", "expected_revision", "handles", "mode" })
            { var bad = Input(); bad.Remove(missing); Reject(bad); }
            Reject(Input("intersections", "1")); Reject(Input("intersections", "1", "2", "3"));
            Reject(Input("metrics", "a", "0A")); Reject(Input("metrics", "0")); Reject(Input("union", "1"));
            GeometryAnalysisValidation.Parse(Wire.Element(Input("metrics", Enumerable.Range(1, 65).Select(i => i.ToString("X")).ToArray())));
            var extended = Input(); extended["extend"] = true; Reject(extended);
            var wrongMode = Input("intersections", "1", "2"); wrongMode["point"] = new[] { 0, 0, 0 }; Reject(wrongMode);
            foreach (var point in new object[] { new[] { 1d, 2d }, new[] { 1d, 2d, 1e13 }, new object[] { 1, 2, "3" } })
            { var bad = Input(); bad["point"] = point; Reject(bad); }
            using var duplicate = JsonDocument.Parse(Wire.Element(Input()).GetRawText().Replace("\"mode\":\"metrics\"", "\"mode\":\"metrics\",\"mode\":\"metrics\""));
            Reject(duplicate.RootElement);
            GeometryAnalysisValidation.Parse(Wire.Element(Input("intersections", "1", "2")));
            return Task.CompletedTask;
        });
        await test("geometry analysis publishes read-only contract without topology claims", () =>
        {
            var tool = ToolCatalog.Tools.Single(t => t.Name == "nanocad.analyze_geometry");
            if (tool.Annotations?.ReadOnlyHint != true || tool.Annotations.DestructiveHint != false || Wire.WriteOperations.Contains("analyze_geometry"))
                throw new Exception("Analysis became a mutation.");
            var alternatives = AuditServerContractSupport.Output("analyze_geometry").GetProperty("properties").GetProperty("data").GetProperty("anyOf")[0].GetProperty("oneOf");
            var modes = alternatives.EnumerateArray().Where(branch => branch.TryGetProperty("properties", out var properties) && properties.TryGetProperty("mode", out _))
                .Select(branch => branch.GetProperty("properties").GetProperty("mode").GetProperty("const").GetString()).ToHashSet();
            var intersections = alternatives.EnumerateArray().Single(branch => branch.TryGetProperty("properties", out var properties) && properties.TryGetProperty("mode", out var mode) && mode.GetProperty("const").GetString() == "intersections");
            if (!new[] { "metrics", "intersections", "distance", "relations", "containment" }.All(modes.Contains) || intersections.GetProperty("properties").GetProperty("overlap_evaluated").GetProperty("const").GetBoolean())
                throw new Exception("Analysis schema misrepresents topology support.");
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!, "tools-list.json"), JsonSerializer.Serialize(ToolCatalog.Tools, Wire.Json));
            return Task.CompletedTask;
        });
        await test("geometry analysis workplane keeps WCS points and rejects off-plane or incompatible requests", () =>
        {
            var frame = new { origin = new[] { 20d, 30, 40 }, x_axis = new[] { .6, .8, 0 }, y_axis = new[] { -.48, .36, .8 } };
            var relation = Input("relations", "A", "B"); relation["workplane"] = frame;
            var parsed = GeometryAnalysisValidation.Parse(Wire.Element(relation));
            if (parsed.Workplane == null || parsed.Workplane.Origin.Z != 40 || parsed.Point != null) throw new Exception("Analysis frame lost.");
            var containment = Input("containment", "A"); containment["workplane"] = frame; containment["point"] = new[] { 18.8, 38.4, 48d };
            parsed = GeometryAnalysisValidation.Parse(Wire.Element(containment));
            if (parsed.Point![2] != 48) throw new Exception("WCS query was silently converted.");
            containment["point"] = new[] { 18.8, 38.4, 49d }; Reject(containment);
            foreach (var mode in new[] { "metrics", "distance", "intersections" }) { var bad = Input(mode, "A", "B"); bad["workplane"] = frame; Reject(bad); }
            relation["workplane"] = new { origin = new[] { 0, 0, 0 }, x_axis = new[] { 1, 0, 0 }, y_axis = new[] { 1, 0, 0 } }; Reject(relation);
            var schema = AuditServerContractSupport.Input("analyze_geometry").GetProperty("oneOf").EnumerateArray()
                .Single(branch => branch.TryGetProperty("properties", out var properties) && properties.TryGetProperty("mode", out var mode) && mode.TryGetProperty("enum", out _));
            if (!schema.GetProperty("properties").TryGetProperty("workplane", out _)) throw new Exception("Analysis frame missing from MCP schema.");
            return Task.CompletedTask;
        });
    }
}
