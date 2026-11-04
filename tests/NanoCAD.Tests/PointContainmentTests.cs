using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class PointContainmentTests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        object Input(string[] handles, double[]? point = null) => point == null ? new { bridge_instance_id = Guid.NewGuid().ToString("D"), document_id = Guid.NewGuid().ToString("D"), expected_revision = "10", mode = "containment", handles }
            : new { bridge_instance_id = Guid.NewGuid().ToString("D"), document_id = Guid.NewGuid().ToString("D"), expected_revision = "10", mode = "containment", handles, point };
        await test("point containment validates explicit single contour and planar point", () =>
        {
            var valid = GeometryAnalysisValidation.Parse(Wire.Element(Input(new[] { "A" }, new[] { 0d, 0d, 0d })));
            if (valid.Mode != "containment" || valid.Point![2] != 0) throw new Exception("Containment point lost");
            foreach (var input in new[] { Input(new[] { "A" }), Input(new[] { "A", "B" }, new[] { 0d, 0d, 0d }), Input(new[] { "A" }, new[] { 0d, 0d, 1e-12 }), Input(new[] { "A" }, new[] { 0d, 0d }), Input(new[] { "A" }, new[] { 1e13, 0d, 0d }) })
            { bool rejected = false; try { GeometryAnalysisValidation.Parse(Wire.Element(input)); } catch (DomainException) { rejected = true; } if (!rejected) throw new Exception("Unsafe containment query accepted."); }
            return Task.CompletedTask;
        });
        await test("point containment rejects ambiguous native classification and missing topology", () =>
        {
            if (PointContainment.Classify("Inside", false) != "inside" || PointContainment.Classify("Outside", true) != "outside" || PointContainment.Classify("OnBoundary", false) != "boundary") throw new Exception("Tri-state mapping failed");
            foreach (var invalid in new[] { ("Inside", true), ("Outside", false), ("OnBoundary", true), ("Unknown", true) })
            { try { PointContainment.Classify(invalid.Item1, invalid.Item2); } catch (DomainException e) when (e.Error.code == "NATIVE_CONTAINMENT_MISMATCH") { continue; } throw new Exception("Ambiguous native result accepted"); }
            return Task.CompletedTask;
        });
        await test("point containment native bounds only exclude distant exterior and preserve the boundary neighborhood", () =>
        {
            double[] min = { 20, 0, 0 }, max = { 30, 10, 0 };
            foreach (double[] point in new[] { new[] { 25d, 5d, 0d }, new[] { 20d, 5d, 0d }, new[] { 20 - 1e-8, 5d, 0d }, new[] { 30 + 1e-8, 5d, 0d } })
                if (PointContainment.OutsideNativeBounds(point, min, max)) throw new Exception("Interior/boundary neighborhood excluded");
            if (!PointContainment.OutsideNativeBounds(new[] { 19d, 5d, 0d }, min, max) || !PointContainment.OutsideNativeBounds(new[] { 25d, 11d, 0d }, min, max)) throw new Exception("Distant exterior not excluded");
            try { PointContainment.OutsideNativeBounds(new[] { 25d, 5d, 0d }, max, min); } catch (DomainException) { return Task.CompletedTask; }
            throw new Exception("Inverted native extents accepted");
        });
        await test("simple polygon containment guard accepts winding and concavity but rejects crossing touching and degeneration", () =>
        {
            var rectangle = new[] { new Point2(0, 0), new Point2(10, 0), new Point2(10, 10), new Point2(0, 10) };
            SimplePolygonValidation.Validate(rectangle); SimplePolygonValidation.Validate(rectangle.Reverse().ToArray());
            SimplePolygonValidation.Validate(new[] { new Point2(0, 0), new Point2(10, 0), new Point2(10, 10), new Point2(6, 10), new Point2(6, 4), new Point2(4, 4), new Point2(4, 10), new Point2(0, 10) });
            var bad = new[]{
                new[]{new Point2(0,0),new Point2(10,10),new Point2(0,10),new Point2(10,0)},
                new[]{new Point2(0,0),new Point2(10,0),new Point2(5,0),new Point2(5,10),new Point2(0,10)},
                new[]{new Point2(0,0),new Point2(0,0),new Point2(1,1)},
                new[]{new Point2(0,0),new Point2(1,0),new Point2(2,0)},
                new[]{new Point2(0,0),new Point2(double.NaN,0),new Point2(0,10)}};
            foreach (var polygon in bad) { bool rejected = false; try { SimplePolygonValidation.Validate(polygon); } catch (DomainException) { rejected = true; } if (!rejected) throw new Exception("Invalid closed boundary accepted."); }
            return Task.CompletedTask;
        });
        await test("point containment bounds use all WCS axes for oblique contours", () =>
        {
            double[] min = { -8.8, 44.4, 72 }, max = { 18.8, 75.6, 88 };
            if (!PointContainment.OutsideNativeBounds(new[] { -2.2, 65.4, 92d }, min, max)) throw new Exception("Oblique exterior beyond Z extent was missed.");
            if (PointContainment.OutsideNativeBounds(new[] { 0d, 60d, 88 + 1e-8 }, min, max)) throw new Exception("Z boundary tolerance band was excluded.");
            return Task.CompletedTask;
        });
        await test("point containment publishes native face method through existing read tool", () =>
        {
            var tool = ToolCatalog.Tools.Single(t => t.Name == "nanocad.analyze_geometry");
            var modes = AuditServerContractSupport.Output("analyze_geometry").GetProperty("properties").GetProperty("data").GetProperty("anyOf")[0].GetProperty("oneOf").EnumerateArray()
                .Where(v => v.TryGetProperty("properties", out var properties) && properties.TryGetProperty("mode", out _)).Select(v => v.GetProperty("properties").GetProperty("mode").GetProperty("const").GetString());
            if (tool.Annotations?.ReadOnlyHint != true || !modes.Contains("containment") || !tool.InputSchema.GetRawText().Contains("containment") || !AuditServerContractSupport.Help("analyze_geometry").Contains("BRep face")) throw new Exception("Native containment metadata absent.");
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!, "tools-list.json"), JsonSerializer.Serialize(ToolCatalog.Tools, Wire.Json)); return Task.CompletedTask;
        });
    }
}
