using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class NativeCurveInspection342Tests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        static void Need(bool value, string message) { if (!value) throw new Exception(message); }
        static NativeSplineGeometry342 Observed()
        {
            var controls = Enumerable.Range(0, 257).Select(i => new[] { (double)i, Math.Sin(i), 4d }).ToArray();
            var knots = Enumerable.Range(0, 272).Select(i => (double)i - 7).ToArray();
            var weights = Enumerable.Range(0, controls.Length).Select(i => i == 128 ? .5 : 1d).ToArray();
            return new(7, true, true, true, true, true, controls, knots, weights,
                1e-8, 1e-10, 0, 2, controls[0], controls[0],
                new("complete_reported_native_nurbs", new(new[] { 0d, 0, 4 }, new[] { 0d, 0, 1 }), null,
                    new(3, new[] { controls[0], controls[128], controls[256] }, .01, true, new[] { 1d, 0, 0 }, new[] { -1d, 0, 0 }, "Chord"),
                    null, "reported native fit only", "legacy editing remains separate", new(7, 257, true, true)));
        }
        await test("block342 broad native spline observation keeps legacy edit admission separate", () =>
        {
            var observed = Observed(); NativeCurveRead342.RequireFinite(observed);
            Need(NativeCurveRead342.CheckSerializedBudget(observed, NativeCurveRead342.PublicGeometryByteBudget) > 0,
                "Finite observed native definition was refused by an input cardinality rule.");
            var legacy = new SplineGeometry(observed.degree, observed.rational, observed.periodic, observed.closed, observed.planar, observed.has_fit_data,
                observed.control_points, observed.knots, observed.weights, observed.control_point_tolerance, observed.knot_tolerance,
                observed.start_parameter, observed.end_parameter, observed.start_point, observed.end_point);
            try { CurvedEditScope.Require(legacy); throw new Exception("Broad observation accidentally admitted legacy editing."); }
            catch (DomainException error) { Need(error.Error.code == "UNSUPPORTED_SPLINE", "Legacy admission changed."); }
            return Task.CompletedTask;
        });
        await test("block342 native snapshot bytes token and cancellation preserve full data", () =>
        {
            var value = Observed(); int exact = JsonSerializer.SerializeToUtf8Bytes(value, Wire.Json).Length;
            Need(NativeCurveRead342.CheckSerializedBudget(value, exact) == exact, "Nested observations were double counted.");
            try { NativeCurveRead342.CheckSerializedBudget(value, exact - 1); throw new Exception("Actual overflow was accepted."); }
            catch (DomainException error) { Need(error.Error.code == "RESPONSE_LIMIT_EXCEEDED", "Overflow did not remain explicit."); }
            var original = Wire.Element(value); string first = NativeCurveRead342.Token(original);
            var changedWeights = value.weights.ToArray(); changedWeights[1] += .125;
            Need(first.Length == 64 && first == NativeCurveRead342.Token(Wire.Element(value)) &&
                first != NativeCurveRead342.Token(Wire.Element(value with { weights = changedWeights })), "Token omitted actual NURBS state.");
            int visits = 0;
            try { NativeCurveRead342.RequireFinite(value, () => { if (++visits == 10) throw new OperationCanceledException(); }); throw new Exception("Cancellation was swallowed."); }
            catch (OperationCanceledException) { Need(visits == 10, "Read continued after cancellation."); }
            return Task.CompletedTask;
        });
        await test("block342 published curve observations widen read without bounded spline input rules", () =>
        {
            var definitions = ToolCatalog.Tools.Single(t => t.Name == "nanocad.inspect_objects").OutputSchema!.Value.GetProperty("$defs");
            var spline = definitions.GetProperty("spline"); var properties = spline.GetProperty("properties");
            Need(!properties.GetProperty("degree").TryGetProperty("maximum", out _) &&
                !properties.GetProperty("control_points").TryGetProperty("maxItems", out _) &&
                !properties.GetProperty("knots").TryGetProperty("maxItems", out _) &&
                !properties.GetProperty("weights").TryGetProperty("maxItems", out _) &&
                properties.GetProperty("has_fit_data").GetProperty("type").GetString() == "boolean" &&
                !spline.GetProperty("required").EnumerateArray().Any(n => n.GetString() == "native_inspection"), "New read retained legacy degree/fit/count gates or broke older outputs.");
            foreach (string family in new[] { "line", "arc", "circle", "polyline", "ellipse", "spline" })
            {
                var schema = definitions.GetProperty(family); var companion = schema.GetProperty("properties").GetProperty("native_curve");
                Need(companion.GetProperty("properties").GetProperty("curve_token").GetProperty("pattern").GetString() == "^[0-9A-F]{64}$" &&
                    !schema.GetProperty("required").EnumerateArray().Any(n => n.GetString() == "native_curve"), "Native interval/token is unmatched or mandatory for an older bridge.");
            }
            return Task.CompletedTask;
        });
    }
}
