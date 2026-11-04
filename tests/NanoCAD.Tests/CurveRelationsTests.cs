using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class CurveRelationsTests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        object Input(string[] handles, object? point = null) => point == null ? new { bridge_instance_id = Guid.NewGuid().ToString("D"), document_id = Guid.NewGuid().ToString("D"), expected_revision = "8", mode = "relations", handles }
            : new { bridge_instance_id = Guid.NewGuid().ToString("D"), document_id = Guid.NewGuid().ToString("D"), expected_revision = "8", mode = "relations", handles, point };
        await test("curve relations requires two ordered distinct finite curve references", () =>
        {
            var parsed = GeometryAnalysisValidation.Parse(Wire.Element(Input(new[] { "B", "A" })));
            if (parsed.Mode != "relations" || !parsed.Read.Handles!.SequenceEqual(new[] { "B", "A" })) throw new Exception("Relation references lost.");
            foreach (var input in new[] { Input(new[] { "A" }), Input(new[] { "A", "B", "C" }), Input(new[] { "A", "0a" }), Input(new[] { "A", "B" }, new[] { 0, 0, 0 }) })
            { bool refused = false; try { GeometryAnalysisValidation.Parse(Wire.Element(input)); } catch (DomainException) { refused = true; } if (!refused) throw new Exception("Unsafe relations arguments accepted."); }
            return Task.CompletedTask;
        });
        await test("curve relations rejects unbounded empty extended or inconsistent native ranges", () =>
        {
            void Reject(Action action) { try { action(); } catch (DomainException e) when (e.Error.code == "NATIVE_RELATION_MISMATCH") { return; } throw new Exception("Invalid native overlap accepted."); }
            CurveRelationsVerification.Range(2, 4, 0, 10); CurveRelationsVerification.Lengths(5, 5);
            Reject(() => CurveRelationsVerification.Range(2, 2, 0, 10)); Reject(() => CurveRelationsVerification.Range(4, 2, 0, 10));
            Reject(() => CurveRelationsVerification.Range(-1, 4, 0, 10)); Reject(() => CurveRelationsVerification.Range(2, 11, 0, 10));
            Reject(() => CurveRelationsVerification.Range(2, double.PositiveInfinity, 0, 10)); Reject(() => CurveRelationsVerification.Range(2, 4, double.NaN, 10));
            Reject(() => CurveRelationsVerification.Lengths(0, 0)); Reject(() => CurveRelationsVerification.Lengths(5, 4.9)); Reject(() => CurveRelationsVerification.Lengths(-1, -1));
            var a = new[] { 0d, 0d, 0d }; var b = new[] { 5d, 0d, 0d };
            CurveRelationsVerification.Endpoints(a, b, b, a); CurveRelationsVerification.Endpoints(a, a, a, a); // Full circle: lengths must separately be positive.
            Reject(() => CurveRelationsVerification.Endpoints(a, b, a, new[] { 5d, 1d, 0d }));
            return Task.CompletedTask;
        });
        await test("curve relations schema scopes overlap separately from old intersections", () =>
        {
            var tool = ToolCatalog.Tools.Single(t => t.Name == "nanocad.analyze_geometry");
            var variants = AuditServerContractSupport.Output("analyze_geometry").GetProperty("properties").GetProperty("data").GetProperty("anyOf")[0].GetProperty("oneOf").EnumerateArray().ToArray();
            var props = variants.Single(v => v.TryGetProperty("properties", out var properties) && properties.TryGetProperty("mode", out var mode) && mode.GetProperty("const").GetString() == "relations").GetProperty("properties");
            var previous = variants.Single(v => v.TryGetProperty("properties", out var properties) && properties.TryGetProperty("mode", out var mode) && mode.GetProperty("const").GetString() == "intersections").GetProperty("properties");
            if (tool.Annotations?.ReadOnlyHint != true || !props.GetProperty("overlap_evaluated").GetProperty("const").GetBoolean() || previous.GetProperty("overlap_evaluated").GetProperty("const").GetBoolean() || !props.GetProperty("result_scope").GetProperty("enum").EnumerateArray().Select(v => v.GetString()).ToHashSet().SetEquals(new[] { "finite_curves_in_WCS_XY_Z0", "finite_curves_in_explicit_workplane" }) || props.GetProperty("overlaps").TryGetProperty("maxItems", out _)) throw new Exception("Overlap scope changed.");
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!, "tools-list.json"), JsonSerializer.Serialize(ToolCatalog.Tools, Wire.Json)); return Task.CompletedTask;
        });
    }
}
