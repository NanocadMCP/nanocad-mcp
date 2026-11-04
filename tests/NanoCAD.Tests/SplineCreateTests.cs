using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class SplineCreateTests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        string bridge = Guid.NewGuid().ToString("D"), doc = Guid.NewGuid().ToString("D"), operation = Guid.NewGuid().ToString("D");
        object Entity(double degree = 2, double[][]? points = null, double[]? knots = null, double[]? weights = null, string layer = "0") => new
        {
            type = "Spline",
            degree,
            control_points = points ?? new[] { new[] { 0d, 0d }, new[] { 3d, 4d }, new[] { 8d, 0d } },
            knots = knots ?? new[] { 0d, 0d, 0d, 1d, 1d, 1d },
            weights = weights ?? new[] { 1d, .5, 1d },
            layer
        };
        CreateArguments Parse(params object[] entities) => CreateValidation.Parse(Wire.Element(new { bridge_instance_id = bridge, document_id = doc, operation_id = operation, expected_revision = "12", units = "Millimeters", entities }));
        void Deny(Action action) { try { action(); } catch (DomainException) { return; } throw new Exception("Invalid spline creation accepted"); }
        await test("spline creation retains NURBS intent and replay fingerprint", () =>
        {
            var args = Parse(Entity()); var spec = (SplineSpec)args.Entities.Single();
            if (spec.Degree != 2 || spec.ControlPoints[1] != new Point2(3, 4) || !spec.Knots.SequenceEqual(new[] { 0d, 0d, 0d, 1d, 1d, 1d }) || !spec.Weights.SequenceEqual(new[] { 1d, .5, 1d })) throw new Exception("Spline intent lost");
            foreach (var changed in new[]{Entity(points:new[]{new[]{0d,0d},new[]{3d,5d},new[]{8d,0d}}),Entity(weights:new[]{1d,.6,1d}),
                Entity(weights:Array.Empty<double>()),Entity(layer:"Other"),Entity(degree:1,knots:new[]{0d,0d,.5,1d,1d})})
                if (args.Fingerprint() == Parse(changed).Fingerprint()) throw new Exception("Spline intent missing from fingerprint");
            var a = Parse(Entity(degree: 1, knots: new[] { 0d, 0d, .4, 1d, 1d })); var b = Parse(Entity(degree: 1, knots: new[] { 0d, 0d, .6, 1d, 1d }));
            if (a.Fingerprint() == b.Fingerprint()) throw new Exception("Knot missing from fingerprint"); return Task.CompletedTask;
        });
        await test("spline creation rejects malformed clamping multiplicities and degeneracy", () =>
        {
            foreach (var bad in new[]{Entity(degree:1.5),Entity(degree:0),Entity(degree:6),Entity(weights:new[]{1d}),Entity(weights:new[]{0d,1d,1d}),
                Entity(weights:new[]{1d,101d,1d}),Entity(knots:new[]{0d,0d,1d,1d}),Entity(knots:new[]{0d,0d,.1,1d,1d,1d}),
                Entity(points:new[]{new[]{0d,0d},new[]{3d,4d},new[]{0d,0d}}),Entity(points:new[]{new[]{0d,0d},new[]{0d,1e-8},new[]{8d,0d}}),
                Entity(points:new[]{new[]{0d,0d,0d},new[]{3d,4d},new[]{8d,0d}}),Entity(points:new[]{new[]{1e9+1,0d},new[]{3d,4d},new[]{8d,0d}})}) Deny(() => Parse(bad));
            var four = Enumerable.Range(0, 4).Select(i => new[] { (double)i, 0d }).ToArray();
            foreach (var knots in new[] { new[] { 0d, 0d, .5, .5, 1d, 1d }, new[] { 0d, 0d, .7, .5, 1d, 1d }, new[] { 0d, 0d, 1e-8, .5, 1d, 1d }, new[] { 0d, 0d, .5, 1d, 1d, 1d } })
                Deny(() => Parse(Entity(1, four, knots, Array.Empty<double>())));
            Deny(() => Parse(Entity(points: Enumerable.Range(0, 65).Select(i => new[] { (double)i, 0d }).ToArray())));
            Deny(() => Parse(new { type = "Spline", degree = 2, control_points = four, knots = new[] { 0, 0, 0, 1, 1, 1 }, weights = Array.Empty<double>(), closed = true })); return Task.CompletedTask;
        });
        await test("spline creation enforces mixed source work budgets in either order", () =>
        {
            Parse(Enumerable.Repeat(Entity(), 17).ToArray());
            object poly = new { type = "Polyline", vertices = Enumerable.Range(0, 1000).Select(i => new { point = new[] { i, 0 } }).ToArray() };
            var ten = Enumerable.Repeat(poly, 10).ToArray(); Parse(ten);
            Parse(ten.Concat(new[] { Entity() }).ToArray()); Parse(new[] { Entity() }.Concat(ten).ToArray()); return Task.CompletedTask;
        });
        await test("spline creation verifies native representation against requested intent", () =>
        {
            var spec = (SplineSpec)Parse(Entity()).Entities.Single();
            var controls = spec.ControlPoints.Select(p => new[] { p.X, p.Y, 0 }).ToArray();
            var good = new SplineGeometry(2, true, false, false, true, false, controls, spec.Knots, spec.Weights, 1e-7, 1e-7, 0, 1, controls[0], controls[^1]); SplineCreation.RequireMatch(spec, good);
            Deny(() => SplineCreation.RequireMatch(spec, good with { control_points = new[] { controls[0], new[] { 4d, 5d, 0d }, controls[^1] } }));
            Deny(() => SplineCreation.RequireMatch(spec, good with { weights = new[] { 1d, .7, 1d } })); Deny(() => SplineCreation.RequireMatch(spec, good with { rational = false, weights = Array.Empty<double>() }));
            SplineCreation.RequireMatch(spec, good with { knots = new[] { 0d, 0d, 0d, 2d, 2d, 2d }, end_parameter = 2 });
            SplineCreation.RequireMatch(spec, good with { weights = new[] { 2d, 1d, 2d } });
            Deny(() => SplineCreation.RequireMatch(spec, good with { weights = new[] { 2d, .8d, 2d } }));
            var nonrational = spec with { Weights = Array.Empty<double>() }; SplineCreation.RequireMatch(nonrational, good with { rational = false, weights = Array.Empty<double>() });
            SplineCreation.RequireMatch(nonrational, good with { rational = false, weights = new[] { 1d, 1d, 1d } }); return Task.CompletedTask;
        });
        await test("spline creation schema retains explicit scope and block opt-in", () =>
        {
            Deny(() => CreateValidation.ParseEntities(Wire.Element(new[] { Entity() })));
            var tool = ToolCatalog.Tools.Single(t => t.Name == "nanocad.create_entities"); var variants = tool.InputSchema.GetProperty("properties").GetProperty("entities").GetProperty("items").GetProperty("oneOf");
            var variant = variants.EnumerateArray().Single(v => v.GetProperty("properties").GetProperty("type").GetProperty("const").GetString() == "Spline" && !v.GetProperty("properties").TryGetProperty("construction", out _));
            if (variant.GetProperty("properties").GetProperty("control_points").TryGetProperty("maxItems", out _) || !AuditServerContractSupport.Help("create_entities", "Spline").Contains("clamped") || tool.Annotations?.ReadOnlyHint != false) throw new Exception("Spline schema metadata mismatch");
            if (!BlockWriteSchema.Define().GetRawText().Contains("\"Spline\"")) throw new Exception("Block curve schema missing");
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!, "tools-list.json"), JsonSerializer.Serialize(ToolCatalog.Tools, Wire.Json)); return Task.CompletedTask;
        });
    }
}
