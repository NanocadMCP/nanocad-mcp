using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class AngularOrdinateCreateTests
{
    private static readonly object Angular = new { type = "Dimension", kind = "angular_3point", dimension_style = "Standard", center = new[] { 10, 20 }, xline1 = new[] { 40, 20 }, xline2 = new[] { 10, 50 }, arc_point = new[] { 30, 40 } };
    private static readonly object Lines = new { type = "Dimension", kind = "angular_2line", dimension_style = "Standard", xline1_start = new[] { 0, 20 }, xline1_end = new[] { 40, 20 }, xline2_start = new[] { 10, 0 }, xline2_end = new[] { 10, 50 }, arc_point = new[] { 30, 40 } };
    private static object Ordinate(string axis = "x", int x = 40) => new { type = "Dimension", kind = "ordinate", dimension_style = "Standard", origin = new[] { 10, 20 }, defining_point = new[] { x, 50 }, leader_end_point = new[] { 60, 80 }, axis };
    private static EntitySpec Parse(object entity) => CreateValidation.ParseEntities(Wire.Element(new[] { entity }), allowDimensions: true).Single();
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        void Reject(object e) { try { Parse(e); } catch (DomainException) { return; } throw new Exception("Invalid angular/ordinate request accepted."); }
        await test("angular ordinate creation independently resolves intersection and coordinate axis", () =>
        {
            foreach (var spec in new[] { Parse(Angular), Parse(Lines) })
                if (spec is not AngularDimensionSpec a || a.Center != new Point2(10, 20) || Math.Abs(a.Angle - Math.PI / 2) > 1e-12) throw new Exception("Intersection or angular intent wrong.");
            if (Parse(Ordinate()) is not OrdinateDimensionSpec { UsingXAxis: true } || Parse(Ordinate("y")) is not OrdinateDimensionSpec { UsingXAxis: false }) throw new Exception("Axis lost.");
            var first = new CreateArguments("b", "d", "7", "op", "Millimeters", new[] { Parse(Ordinate()) });
            if (first.Fingerprint() == (first with { Entities = new[] { Parse(Ordinate(x: 41)) } }).Fingerprint()) throw new Exception("Coordinate lost from replay fingerprint.");
            return Task.CompletedTask;
        });
        await test("angular ordinate creation rejects ambiguous sectors parallel lines and degenerate coordinates", () =>
        {
            object Three(int[] center, int[] first, int[] second, int[] arc) => new { type = "Dimension", kind = "angular_3point", dimension_style = "Standard", center, xline1 = first, xline2 = second, arc_point = arc };
            Reject(Three(new[] { 0, 0 }, new[] { 10, 0 }, new[] { 0, -10 }, new[] { 10, -10 }));
            Reject(Three(new[] { 0, 0 }, new[] { 10, 0 }, new[] { 0, 10 }, new[] { -10, 10 }));
            Reject(Three(new[] { 0, 0 }, new[] { 10, 0 }, new[] { 0, 10 }, new[] { 5, 0 }));
            Reject(Three(new[] { 0, 0 }, new[] { 0, 0 }, new[] { 0, 10 }, new[] { 5, 5 }));
            Reject(new { type = "Dimension", kind = "angular_2line", dimension_style = "Standard", xline1_start = new[] { 0, 0 }, xline1_end = new[] { 10, 0 }, xline2_start = new[] { 0, 10 }, xline2_end = new[] { 10, 10 }, arc_point = new[] { 5, 5 } });
            Reject(Ordinate("z")); Reject(Ordinate(x: 10));
            Reject(new { type = "Dimension", kind = "ordinate", dimension_style = "Standard", origin = new[] { 0, 0 }, defining_point = new[] { 10, 10 }, leader_end_point = new[] { 20, 10 }, axis = "x" });
            return Task.CompletedTask;
        });
        await test("angular ordinate creation is byte bounded and publishes each supported kind", () =>
        {
            if (CreateValidation.ParseEntities(Wire.Element(Enumerable.Repeat(Angular, 17).ToArray()), allowDimensions: true).Length != 17) throw new Exception("Former dimension count ceiling remains.");
            var kinds = ToolCatalog.Tools.Single(t => t.Name == "nanocad.create_entities").InputSchema.GetProperty("properties").GetProperty("entities").GetProperty("items").GetProperty("oneOf").EnumerateArray().Where(v => v.GetProperty("properties").GetProperty("type").GetProperty("const").GetString() == "Dimension").Select(v => v.GetProperty("properties").GetProperty("kind").GetProperty("const").GetString()!).ToArray();
            var expected = new[] { "aligned", "rotated", "radial", "diametric", "angular_3point", "angular_2line", "ordinate", "arc_length", "jogged_radial", "projected_aligned" };
            if (kinds.Length != expected.Length || !expected.ToHashSet(StringComparer.Ordinal).SetEquals(kinds))
                throw new Exception("The published dimension kind set differs from the supported contract.");
            return Task.CompletedTask;
        });
    }
}
