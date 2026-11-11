using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class DimensionCreateSnapTests
{
    private static readonly string[] Kinds = ["aligned", "rotated", "radial", "diametric", "angular_3point",
        "angular_2line", "ordinate", "arc_length", "jogged_radial"];

    private static void Need(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    private static DomainException Reject(Action action)
    {
        try { action(); }
        catch (DomainException error) { return error; }
        throw new Exception("Expected an invalid dimension request to be refused.");
    }

    private static CreateArguments Parse(object entity, string targetSpace = "model") => CreateValidation.Parse(Wire.Element(new
    {
        bridge_instance_id = "10000000-0000-0000-0000-000000000001",
        document_id = "10000000-0000-0000-0000-000000000002",
        operation_id = "10000000-0000-0000-0000-000000000003",
        expected_revision = "0",
        units = "Millimeters",
        target_space = targetSpace,
        entities = new[] { entity }
    }));

    private static Dictionary<string, object> Literal(string kind)
    {
        var fields = new Dictionary<string, object>(StringComparer.Ordinal)
        { ["type"] = "Dimension", ["kind"] = kind, ["layer"] = "Dimensions", ["dimension_style"] = "Standard" };
        void Point(string name, double x, double y) => fields.Add(name, new[] { x, y });
        switch (kind)
        {
            case "aligned":
            case "rotated":
                Point("xline1", 0, 0); Point("xline2", 10, 0); Point("dimline_point", 5, 5);
                if (kind == "rotated") fields.Add("rotation", .2);
                break;
            case "radial":
                Point("center", 0, 0); Point("chord_point", 10, 0); fields.Add("leader_length", 3d); break;
            case "diametric":
                Point("chord_point", 10, 0); Point("far_chord_point", -10, 0); fields.Add("leader_length", 3d); break;
            case "angular_3point":
            case "arc_length":
                Point("center", 0, 0); Point("xline1", 10, 0); Point("xline2", 0, 10); Point("arc_point", 6, 6); break;
            case "angular_2line":
                Point("xline1_start", 0, 0); Point("xline1_end", 10, 0);
                Point("xline2_start", 0, 0); Point("xline2_end", 0, 10); Point("arc_point", 6, 6); break;
            case "ordinate":
                Point("origin", 0, 0); Point("defining_point", 10, 20); Point("leader_end_point", 10, 30);
                fields.Add("axis", "x"); break;
            case "jogged_radial":
                Point("center", 0, 0); Point("chord_point", 10, 0); Point("override_center", 3, 0); Point("jog_point", 5, 2);
                fields.Add("jog_angle", .7); break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
        return fields;
    }

    private static Dictionary<string, object> Snap(string handle, string token = "a") => new(StringComparer.Ordinal)
    { ["handle"] = handle, ["expected_curve_token"] = new string(token[0], 64), ["snap"] = "mid" };

    private static (Dictionary<string, object> Fields, Dictionary<string, Point2> Resolved) Snapped(string kind, int only = -1)
    {
        var fields = Literal(kind);
        var resolved = new Dictionary<string, Point2>(StringComparer.Ordinal);
        var roles = DimensionCreateSnaps.Roles(kind);
        for (int i = 0; i < roles.Length; i++)
        {
            if (only >= 0 && only != i) continue;
            string field = DimensionCreateSnaps.Field(roles[i]), handle = (0xA0 + i).ToString("X");
            var point = (double[])fields[field]; resolved.Add(handle, new(point[0], point[1]));
            fields.Remove(field); fields.Add(field + "_snap", Snap(handle));
        }
        return (fields, resolved);
    }

    private static EntitySpec Resolve(SnappedDimensionSpec spec, IReadOnlyDictionary<string, Point2> points) =>
        spec.Materialize(input => input.Point ?? points[input.Snap!.Handle]);

    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("dimension snaps materialize every ordinary point role with existing geometry semantics", () =>
        {
            foreach (string kind in Kinds)
            {
                var expected = Parse(Literal(kind)).Entities.Single();
                for (int role = -1; role < DimensionCreateSnaps.Roles(kind).Length; role++)
                {
                    var (fields, points) = Snapped(kind, role);
                    var parsed = Parse(fields).Entities.Single();
                    Need(parsed is SnappedDimensionSpec, kind + " did not reach its typed snap parser.");
                    var actual = Resolve((SnappedDimensionSpec)parsed, points);
                    Need(expected.GetType() == actual.GetType() && Wire.Element(expected).GetRawText() == Wire.Element(actual).GetRawText(),
                        kind + " changed its ordinary dimensional geometry or options after snap resolution.");
                    Need(actual is IDimensionCreation, kind + " lost the existing dimension ownership/style admission interface.");
                }
            }
            return Task.CompletedTask;
        });

        await test("dimension snaps reject ambiguous inputs and validate resolved geometry before creation", () =>
        {
            foreach (string kind in Kinds)
            {
                var (fields, _) = Snapped(kind);
                var spec = (SnappedDimensionSpec)Parse(fields).Entities.Single();
                Reject(() => spec.Materialize(_ => new(0, 0)));
                string field = DimensionCreateSnaps.Field(DimensionCreateSnaps.Roles(kind)[0]);
                fields.Add(field, new[] { 0d, 0 }); Reject(() => Parse(fields)); fields.Remove(field);
                fields.Remove(field + "_snap"); Reject(() => Parse(fields));
            }
            var (angular, _) = Snapped("angular_3point", 0);
            var angularSpec = (SnappedDimensionSpec)Parse(angular).Entities.Single();
            Reject(() => angularSpec.Materialize(input => input.Point ?? new(1e6 + 1, 0)));
            var (bad, _) = Snapped("aligned");
            var nested = (SnappedDimensionSpec)CreateValidation.ParseEntities(Wire.Element(new[] { bad }),
                "$.operations[4].entities", allowDimensions: true).Single();
            Need(Reject(() => nested.Materialize(_ => new(0, 0))).Message.StartsWith("$.operations[4].entities[0]", StringComparison.Ordinal),
                "Resolved geometry failure lost its original entity path.");
            foreach (var (kind, field, value) in new (string, string, object)[]
            {
                ("rotated", "rotation", 2 * Math.PI), ("radial", "leader_length", -1d),
                ("ordinate", "axis", "z"), ("jogged_radial", "jog_angle", Math.PI),
                ("angular_3point", "text_override", "fixed text")
            })
            {
                var invalid = Snapped(kind).Fields; invalid[field] = value;
                Reject(() => Parse(invalid));
            }
            return Task.CompletedTask;
        });

        await test("dimension snaps preserve explicit association intent without inventing bindings", () =>
        {
            var (fields, points) = Snapped("aligned");
            Need(((DimensionSpec)Resolve((SnappedDimensionSpec)Parse(fields).Entities.Single(), points)).Associations == null,
                "One-time snaps fabricated persistent source associations.");
            fields.Add("associations", new[]
            {
                new { role = "xline1", source_handle = "a0", snap = "midpoint" },
                new { role = "xline2", source_handle = "a1", snap = "end" }
            });
            var bindings = ((DimensionSpec)Resolve((SnappedDimensionSpec)Parse(fields).Entities.Single(), points)).Associations!;
            Need(bindings.Length == 2 && bindings[0] == new DimensionSourcePoint385("xline1", "A0", "midpoint") &&
                bindings[1] == new DimensionSourcePoint385("xline2", "A1", "end"), "Explicit native associations were lost or rewritten.");
            foreach (string kind in new[] { "projected_aligned", "projected_diametric" })
            {
                var projected = new Dictionary<string, object>
                {
                    ["type"] = "Dimension",
                    ["kind"] = kind,
                    ["view_handle"] = "AA",
                    [kind == "projected_aligned" ? "line_index" : "arc_index"] = 0,
                    ["dimline_point"] = new[] { 1, 2 }
                };
                string space = kind == "projected_diametric" ? "paper" : "model";
                Need(Parse(projected, space).Entities.Single() is ProjectedAlignedDimensionSpec or ProjectedDiametricDimensionSpec,
                    "Dedicated projected dimension binding was displaced.");
                projected.Remove("dimline_point"); projected.Add("dimline_point_snap", Snap("A0"));
                Reject(() => Parse(projected, space));
            }
            return Task.CompletedTask;
        });

        await test("dimension snap replay intent is canonical and includes native source guards", () =>
        {
            var (fields, _) = Snapped("radial");
            var first = Parse(fields);
            var reordered = fields.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            foreach (string field in reordered.Keys.Where(key => key.EndsWith("_snap", StringComparison.Ordinal)).ToArray())
            {
                var value = ((Dictionary<string, object>)reordered[field]).Reverse().ToDictionary(pair => pair.Key, pair => pair.Value);
                value["handle"] = ((string)value["handle"]).ToLowerInvariant();
                value["expected_curve_token"] = ((string)value["expected_curve_token"]).ToUpperInvariant();
                reordered[field] = value;
            }
            Need(first.Fingerprint() == Parse(reordered).Fingerprint(), "JSON field order or hexadecimal case changed normalized replay intent.");
            var original = (SnappedDimensionSpec)first.Entities.Single();
            Need(first.Fingerprint() == (first with { Entities = [original with { SourcePath = "$.different[7]" }] }).Fingerprint(),
                "Diagnostic source paths leaked into operation fingerprints.");
            ((Dictionary<string, object>)reordered["center_snap"])["expected_curve_token"] = new string('B', 64);
            Need(first.Fingerprint() != Parse(reordered).Fingerprint(), "A changed source geometry guard was omitted from replay intent.");
            ((Dictionary<string, object>)reordered["center_snap"])["expected_curve_token"] = new string('A', 64);
            reordered["leader_length"] = 4d;
            Need(first.Fingerprint() != Parse(reordered).Fingerprint(), "Dimension scalar intent was omitted from replay fingerprints.");
            var defaults = Snapped("radial").Fields; defaults.Remove("leader_length");
            var explicitZero = new Dictionary<string, object>(defaults) { ["leader_length"] = 0d, ["text_override"] = "<>" };
            Need(Parse(defaults).Fingerprint() == Parse(explicitZero).Fingerprint(), "Optional default scalar normalization changed replay intent.");
            return Task.CompletedTask;
        });

        await test("dimension snap schemas advertise all ordinary roles and preserve projected contracts", () =>
        {
            var variants = ToolCatalog.Tools.Single(tool => tool.Name == "nanocad.create_entities").InputSchema
                .GetProperty("properties").GetProperty("entities").GetProperty("items").GetProperty("oneOf").EnumerateArray()
                .Where(branch => branch.GetProperty("properties").GetProperty("type").GetProperty("const").GetString() == "Dimension").ToArray();
            foreach (string kind in Kinds)
            {
                var branch = variants.Single(value => value.GetProperty("properties").GetProperty("kind").GetProperty("const").GetString() == kind);
                var properties = branch.GetProperty("properties");
                var required = branch.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray();
                Need(!properties.TryGetProperty("workplane", out _), "Ordinary dimensions unexpectedly advertised workplane support.");
                foreach (var role in DimensionCreateSnaps.Roles(kind))
                {
                    string field = DimensionCreateSnaps.Field(role);
                    Need(properties.TryGetProperty(field, out _) && properties.TryGetProperty(field + "_snap", out _) && !required.Contains(field),
                        kind + "/" + field + " snap alternative is missing or the literal remains mandatory.");
                    Need(branch.GetProperty("allOf").EnumerateArray().Any(condition => condition.TryGetProperty("oneOf", out var alternatives) &&
                        alternatives.EnumerateArray().SelectMany(value => value.GetProperty("required").EnumerateArray())
                            .Select(value => value.GetString()).SequenceEqual(new[] { field, field + "_snap" })),
                        kind + "/" + field + " does not enforce one coordinate alternative.");
                }
            }
            foreach (var projected in variants.Where(value => value.GetProperty("properties").GetProperty("kind").GetProperty("const").GetString()!.StartsWith("projected_", StringComparison.Ordinal)))
                Need(!projected.GetProperty("properties").EnumerateObject().Any(field => field.Name.EndsWith("_snap", StringComparison.Ordinal)),
                    "Ordinary one-time snaps leaked into the dedicated projection binding contract.");
            return Task.CompletedTask;
        });
    }
}
