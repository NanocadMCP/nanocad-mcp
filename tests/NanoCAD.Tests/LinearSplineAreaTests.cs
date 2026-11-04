using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class LinearSplineAreaTests
{
    private static SplineGeometry Polygon(double[][] vertices, double[]? weights = null)
    {
        var p = vertices.Append(vertices[0]).ToArray(); int n = p.Length;
        var knots = new[] { 0d, 0d }.Concat(Enumerable.Range(1, n - 2).Select(i => (double)i / (n - 1))).Concat(new[] { 1d, 1d }).ToArray();
        return new(1, weights != null, true, true, true, false, p, knots, weights ?? Array.Empty<double>(), 1e-7, 1e-7, 0, 1, p[0], p[^1]);
    }
    private static readonly double[][] L = new[] { new[] { 0d, 0d, 0d }, new[] { 30d, 0d, 0d }, new[] { 30d, 10d, 0d }, new[] { 10d, 10d, 0d }, new[] { 10d, 30d, 0d }, new[] { 0d, 30d, 0d } };
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("linear spline area agrees with independent triangle and concave rectangle differences", () =>
        {
            var triangle = Polygon(new[] { new[] { 0d, 0d, 0d }, new[] { 20d, 0d, 0d }, new[] { 20d, 15d, 0d } });
            foreach (var item in new[]{(triangle,150d),(Polygon(L),900d-400d),(Polygon(L.Reverse().ToArray()),500d),
                (Polygon(L,new[]{.01,2d,3d,10d,100d,1d,.01}),500d),
                (Polygon(L.Select(p=>new[]{p[0]+1e8,p[1]-1e8,17d}).ToArray()),500d)})
                if (LinearSplineMetrics.TryCalculate(item.Item1)?.Area != item.Item2) throw new Exception("Independent polygon area differs or depends on orientation/weights/origin.");
            return Task.CompletedTask;
        });
        await test("linear spline area leaves spatial crossing touching and degenerate boundaries unavailable", () =>
        {
            var shapes = new[]{
                new[]{new[]{0d,0d,0d},new[]{20d,10d,0d},new[]{0d,10d,0d},new[]{20d,0d,0d}},
                new[]{new[]{0d,0d,0d},new[]{20d,0d,0d},new[]{10d,0d,0d}},
                new[]{new[]{0d,0d,0d},new[]{20d,0d,0d},new[]{10d,0d,0d},new[]{10d,10d,0d},new[]{0d,10d,0d}},
                new[]{new[]{0d,0d,0d},new[]{20d,0d,0d},new[]{20d,15d,1d}}};
            foreach (var points in shapes)
            {
                var metric = LinearSplineMetrics.TryCalculate(Polygon(points));
                if (metric == null || metric.Length <= 0 || metric.Area != null) throw new Exception("Unavailable area destroyed valid length or invented a planar simple area.");
            }
            if (LinearSplineMetrics.TryCalculate(Polygon(L) with { planar = false })?.Area != null) throw new Exception("Native nonplanar classification ignored.");
            return Task.CompletedTask;
        });
        await test("linear spline area publishes its source and retains geometric limitations", () =>
        {
            var tool = ToolCatalog.Tools.Single(t => t.Name == "nanocad.analyze_geometry");
            var schema = tool.OutputSchema!.Value.GetRawText();
            if (!schema.Contains("analytic_degree1_simple_xy_polygon") || !AuditServerContractSupport.Help("analyze_geometry").Contains("Smooth, spatial, degenerate or intersecting Spline area remains null")) throw new Exception("Area provenance or limitation missing.");
            // Hatch availability is independent of area calculation and is
            // covered by the explicit write-recovery policy tests.
            return Task.CompletedTask;
        });
    }
}
