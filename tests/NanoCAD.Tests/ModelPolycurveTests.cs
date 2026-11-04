using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class ModelPolycurveTests
{
    internal static async Task Run(Func<string, Func<Task>, Task> test)
    {
        var id = Guid.NewGuid().ToString("D");
        object Profile(double[]? b = null) => new { kind = "polycurve", vertices = new[] { new[] { 0d, 10 }, new[] { 0d, -10 }, new[] { 40d, -10 }, new[] { 40d, 10 } }, bulges = b ?? new[] { 1d, 0, 1, 0 } };
        ModelExtrusionSpec Parse(object profile, object? plane = null) => (ModelExtrusionSpec)CreateValidation.Parse(Wire.Element(new { bridge_instance_id = id, document_id = id, operation_id = id, expected_revision = "1", units = "Millimeters", entities = new[] { new { type = "Solid3d", kind = "model_extrusion", plane = plane ?? new { origin = new[] { 0, 0, 0 }, x_axis = new[] { 1, 0, 0 }, y_axis = new[] { 0, 1, 0 } }, profile, distance = 4, direction = "negative" } } })).Entities.Single();
        void Near(double a, double b) { if (Math.Abs(a - b) > Math.Max(1e-8, Math.Abs(b) * 1e-10)) throw new Exception($"Expected {b}, got {a}"); }
        void Deny(Action f) { try { f(); } catch (DomainException) { return; } throw new Exception("Invalid polycurve accepted"); }
        await test("model polycurve exact rotated bounds avoid transformed bounding rectangles", () =>
        {
            double d = 1 / Math.Sqrt(2); var spec = Parse(Profile(), new { origin = new[] { 100d, 200, 30 }, x_axis = new[] { d, d, 0 }, y_axis = new[] { -d, d, 0 } }); var v = ModelExtrusionGeometry.Measure(spec);
            Near(v.Volume, 4 * (800 + 100 * Math.PI)); Near(v.Centroid.X, 100 + 20 * d); Near(v.Centroid.Y, 200 + 20 * d); Near(v.Centroid.Z, 28); Near(v.Min.X, 90); Near(v.Min.Y, 190); Near(v.Max.X, 100 + 40 * d + 10); Near(v.Max.Y, 200 + 40 * d + 10); Near(v.Min.Z, 26); return Task.CompletedTask;
        });
        await test("model polycurve vertical plane preserves signed normal and curved profile", () =>
        {
            var spec = Parse(Profile(), new { origin = new[] { 10, 20, 30 }, x_axis = new[] { 0, 1, 0 }, y_axis = new[] { 0, 0, 1 } }); var v = ModelExtrusionGeometry.Measure(spec);
            Near(v.Centroid.X, 8); Near(v.Centroid.Y, 40); Near(v.Centroid.Z, 30); Near(v.Min.X, 6); Near(v.Max.X, 10); Near(v.Min.Y, 10); Near(v.Max.Y, 70); Near(v.Min.Z, 20); Near(v.Max.Z, 40); return Task.CompletedTask;
        });
        await test("model polycurve validates bulge count winding curvature and ambiguous fields", () =>
        {
            var half = Parse(new { kind = "polycurve", vertices = new[] { new[] { 10d, 0 }, new[] { -10d, 0 } }, bulges = new[] { 1d, 0 } }); var reversed = Parse(new { kind = "polycurve", vertices = new[] { new[] { -10d, 0 }, new[] { 10d, 0 } }, bulges = new[] { -1d, 0 } });
            var a = ModelExtrusionGeometry.Measure(half); var b = ModelExtrusionGeometry.Measure(reversed); Near(a.Volume, 200 * Math.PI); Near(a.Centroid.Y, 40 / (3 * Math.PI)); Near(b.Centroid.Y, a.Centroid.Y);
            Deny(() => Parse(Profile(new[] { 1d, 0, 1 }))); Deny(() => Parse(Profile(new[] { 1e-8, 0d, 1, 0 }))); Deny(() => Parse(new { kind = "polygon", vertices = new[] { new[] { 0, 0 }, new[] { 2, 0 }, new[] { 0, 2 } }, bulges = new[] { 0, 0, 0 } }));
            Deny(() => Parse(new { kind = "polycurve", vertices = new[] { new[] { 0, 0 }, new[] { 1, 0 } }, bulges = new[] { 0, 0 } })); return Task.CompletedTask;
        });
        await test("model polycurve native arc decoding honors reference vector normal and angle wrap", () =>
        {
            var plane = new SketchPlaneSpec(new(10, 20, 30), new(0, 1, 0), new(0, 0, 1));
            var arc = ModelPolycurve.FromSketchArc("A", new(10, 20, 30), 5, new(-1, 0, 0), new(0, 0, 1), 3 * Math.PI / 2, Math.PI / 2, plane);
            Near(arc.Start!.X, 5); Near(arc.Start.Y, 0); Near(arc.End!.X, -5); Near(arc.End.Y, 0); Near(arc.Sweep, Math.PI);
            var circle = ModelPolycurve.FromSketchArc("C", new(10, 20, 30), 5, new(1, 0, 0), new(0, 1, 0), 0, 2 * Math.PI, plane); if (circle.Kind != "circle") throw new Exception("Full circle lost");
            Deny(() => ModelPolycurve.FromSketchArc("B", new(11, 20, 30), 5, new(1, 0, 0), new(0, 1, 0), 0, Math.PI, plane)); return Task.CompletedTask;
        });
        await test("model polycurve exposed for native bases and processing with recovery guidance", () =>
        {
            foreach (var route in new[] { (Operation: "create_entities", Variant: "model_extrusion"), (Operation: "modify_entities", Variant: "add_model_extrusion") })
            { var text = AuditServerContractSupport.Help(route.Operation, route.Variant); foreach (var required in new[] { "polycurve", "bulges", "ROLLBACK_NOT_GUARANTEED" }) if (!text.Contains(required)) throw new Exception("Missing " + route.Operation + " " + required); }
            return Task.CompletedTask;
        });
    }
}
