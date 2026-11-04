using NanoCAD.Contracts;

internal static class ExtrusionTests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        var bridge = Guid.NewGuid().ToString("D"); var doc = Guid.NewGuid().ToString("D"); var op = Guid.NewGuid().ToString("D");
        object Extrusion(double[][] points, double z = 0) => new { type = "Solid3d", kind = "extrusion", profile = points, base_z = z, height = 10 };
        CreateArguments Parse(object[] entities) => CreateValidation.Parse(Wire.Element(new { bridge_instance_id = bridge, document_id = doc, operation_id = op, expected_revision = "1", units = "Millimeters", entities }));
        await test("solid extrusion preserves profile and elevation and rejects invalid boundaries before native calls", () =>
        {
            double[][] triangle = { new[] { 0d, 0 }, new[] { 10d, 0 }, new[] { 0d, 6 } };
            double[][] concave = { new[] { 0d, 0 }, new[] { 6d, 0 }, new[] { 6d, 2 }, new[] { 2d, 2 }, new[] { 2d, 6 }, new[] { 0d, 6 } };
            var args = Parse(new[] { Extrusion(triangle), Extrusion(concave, 5) });
            if (args.Entities.Any(e => e is not ExtrudedSolidSpec) || ((ExtrudedSolidSpec)args.Entities[1]).BaseZ != 5) throw new Exception("Extrusion intent lost.");
            if (Parse(new[] { Extrusion(triangle) }).Fingerprint() == Parse(new[] { Extrusion(triangle, 1) }).Fingerprint()) throw new Exception("Elevation missing from identity.");
            foreach (var points in new[] { new[] { new[] { 0d, 0 }, new[] { 1d, 1 }, new[] { 2d, 2 } }, new[] { new[] { 0d, 0 }, new[] { 4d, 4 }, new[] { 0d, 4 }, new[] { 4d, 0 } }, new[] { new[] { 0d, 0 }, new[] { 0d, 0 }, new[] { 1d, 1 } } })
            { try { Parse(new[] { Extrusion(points) }); throw new Exception("Invalid profile accepted."); } catch (DomainException) { } }
            if (Parse(Enumerable.Repeat(Extrusion(triangle), 9).ToArray()).Entities.Length != 9) throw new Exception("Former extrusion count ceiling remains.");
            try { CreateValidation.ParseEntities(Wire.Element(new[] { Extrusion(triangle) }), "$.entities"); throw new Exception("Extrusion leaked into block scope."); } catch (DomainException) { }
            return Task.CompletedTask;
        });
    }
}
