using NanoCAD.Contracts;

internal static class SketchMobilityTests
{
    private static SketchConstraintSpec S(string kind, string a, string? b = null, string? ap = null, string? bp = null, double? value = null)
        => new(kind, a, b, ap, bp, value);
    private static SketchConstraintCurve Line(string id, double x, double y, double xx, double yy) =>
        new(id, "line", new(x, y), new(xx, yy));
    private static SketchConstraintCurve Circle(string id, double x, double y, double r) => new(id, "circle", Center: new(x, y), Radius: r);
    private static SketchMobilityRelation[] Retain(params SketchConstraintSpec[] specs) =>
        specs.Select((s, i) => new SketchMobilityRelation("existing-" + i, s)).ToArray();
    private static void Check(bool success, string message)
    {
        if (!success) throw new InvalidOperationException(message);
    }
    private static void Rejected(Action check)
    {
        try { check(); }
        catch (DomainException e) when (e.Error.code == "CONSTRAINT_PLAN_REJECTED") { return; }
        throw new InvalidOperationException("An uncertified required group was accepted.");
    }

    internal static async Task Run(Func<string, Func<Task>, Task> test)
    {
        Task Case(string name, Action run) => test("constraint mobility " + name, () =>
        {
            run();
            return Task.CompletedTask;
        });
        await Case("rank and redundancy on horizontal lines", Geometry);
        await Case("retained and conflicting driving dimensions", Drivers);
        await Case("protected translation and curved paths", Protected);
        await Case("component isolation and verified authoring", Coverage);
        await Case("singular solutions and trimmed arcs", Branches);
        await Case("symmetry and partially implied compound relations", Compound);
        await Case("rounded holder independent constraints and width edit", () => { Holder(89); Holder(99); });
    }

    private static void Geometry()
    {
        var lines = new[] { Line("a", 0, 0, 8, 0), Line("A", 0, 3, 9, 3) };
        var baseLines = Retain(S("horizontal", "a"), S("horizontal", "A"));
        var candidates = new[] { S("parallel", "a", "A"), S("distance_y", "a", "A", "start", "start", 5) };
        var report = SketchMobility.Plan(lines, baseLines, candidates);
        Check(report.remaining_dof == 6 && report.planned_dof == 5, "absolute spacing can change without removing longitudinal motion");
        Check(report.decisions[0].classification == "implied" && report.decisions[0].witnesses.Length == 2, "parallel follows from two horizontals");
        Check(report.EffectiveCandidates(candidates).SequenceEqual(candidates.Skip(1)), "only the independent dimension should be emitted");
        var lineSize = Retain(S("horizontal", "a"), S("length", "a", value: 8));
        report = SketchMobility.Plan(lines, lineSize, new[] { S("distance_x", "a", "a", "start", "end", 8) });
        Check(report.decisions[0].classification == "implied", "horizontal span of a sized horizontal segment is already fixed");
        report = SketchMobility.Plan(lines, baseLines.Append(new SketchMobilityRelation("extra", candidates[0])).ToArray());
        Check(report.components.Single().existing_issues.Single().constraint_id == "extra", "audit must identify a redundant retained relation by native ID");
    }

    private static void Drivers()
    {
        var circle = new[] { Circle("circle", 2, 3, 5) };
        var radius = Retain(S("radius", "circle", value: 5));
        var d10 = new[] { S("diameter", "circle", value: 10) };
        var report = SketchMobility.Plan(circle, radius, d10);
        Check(report.remaining_dof == 2 && report.decisions[0].classification == "implied", "equivalent radial parameter");
        Rejected(() => report.EffectiveCandidates(d10)); // The user asked for a driving dimension.
        report.RequireCompatibleReplacement();
        var d12 = new[] { S("diameter", "circle", value: 12) };
        report = SketchMobility.Plan(circle, radius, d12);
        Check(report.decisions[0].classification == "conflict" && report.planned_dof == null && report.decisions[0].witnesses.SequenceEqual(new[] { "existing-0" }), "conflicting dimension witness");
        Rejected(report.RequireCompatibleReplacement);
        Rejected(() => report.EffectiveCandidates(d12));
        var selfConflict = new[] { d12[0], radius[0].Spec! };
        report = SketchMobility.Plan(circle, Array.Empty<SketchMobilityRelation>(), selfConflict);
        Rejected(() => report.EffectiveCandidates(selfConflict));
        report = SketchMobility.Plan(circle, Retain(d12[0], radius[0].Spec!));
        Check(report.state == "existing_conflict" && report.remaining_dof == null, "conflicting old network must not report zero freedoms");
        report = SketchMobility.Plan(circle, radius, d10, replacingDrivers: true);
        Check(report.state == "regular" && report.planned_dof == 2, "replacing a retained equivalent driver should not reject the existing graph");
    }

    private static void Protected()
    {
        var guide = new[] { Circle("c", 0, 2, 3), Line("ref", 0, 0, 4, 0) };
        var guideRelations = Retain(S("distance_y", "c", "ref", "center", "start", 2), S("radius", "c", value: 3));
        var along = new[] { new SketchMobilityMotion("slide along X", new[] { "c" }, 1, 0) };
        var fix = new[] { S("fix_point", "c", ap: "center") };
        var report = SketchMobility.Plan(guide, guideRelations, fix, fixedGeometry: new[] { "ref" }, preserve: along);
        Check(report.remaining_dof == 1 && report.decisions[0].classification == "removes_intended_motion", "one free motion is not permission to lock the center");
        Rejected(() => report.EffectiveCandidates(fix));
        report = SketchMobility.Plan(guide, guideRelations, fix, inventoryComplete: false, fixedGeometry: new[] { "ref" });
        Check(report.remaining_dof == null, "partial inventory is never zero DOF");
        var freeRadius = new[] { S("radius", "c", value: 3) };
        report = SketchMobility.Plan(guide, Array.Empty<SketchMobilityRelation>(), freeRadius, inventoryComplete: false, preserve: along);
        Check(report.decisions[0].classification == "unverified_intended_motion", "opaque protected component");
        Rejected(() => report.EffectiveCandidates(freeRadius));
        var orbit = new[] { Circle("a", 5, 0, 1), new SketchConstraintCurve("origin", "point", Center: new(0, 0)) };
        var tether = Retain(S("distance", "a", "origin", "center", "center", 5));
        var verticalSlide = new[] { new SketchMobilityMotion("vertical slide", new[] { "a" }, 0, 1) };
        report = SketchMobility.Plan(orbit, tether, fixedGeometry: new[] { "origin" }, preserve: verticalSlide);
        Check(report.remaining_dof == 2 && report.components[0].movements_before.Single(m => m.requested).status == "blocked",
            "a tangent to a circular path is not a permitted straight-line slide");
    }

    private static void Coverage()
    {
        var circle = new[] { Circle("circle", 2, 3, 5) };
        var d10 = new[] { S("diameter", "circle", value: 10) };
        var lines = new[] { Line("a", 0, 0, 8, 0), Line("A", 0, 3, 9, 3) };
        var baseLines = Retain(S("horizontal", "a"), S("horizontal", "A"));
        var parallel = S("parallel", "a", "A");
        var isolated = new[] { circle[0], new SketchConstraintCurve("opaque", "spline") };
        var report = SketchMobility.Plan(isolated, new[] { new SketchMobilityRelation("opaque-solver-equation", null, new[] { "opaque" }) }, d10);
        Check(report.remaining_dof == null && report.decisions[0].dof_before == 3 && report.decisions[0].dof_after == 2, "independent supported component stays measurable");
        Check(report.EffectiveCandidates(d10, requireIndependentProof: true).Length == 1, "unrelated opaque components cannot suppress a certified candidate");
        var opaque = SketchMobility.Plan(circle, Retain(), d10, inventoryComplete: false);
        Check(opaque.EffectiveCandidates(d10).Length == 1, "explicit legacy operations retain native assessment on unknown input");
        bool refused = false;
        try { opaque.EffectiveCandidates(d10, requireIndependentProof: true); }
        catch (DomainException e) when (e.Error.code == "CONSTRAINT_PLAN_UNVERIFIED") { refused = true; }
        Check(refused, "strict authoring must reject unverified freedom on its own affected component");
        report = SketchMobility.Plan(lines, baseLines, new[] { parallel }, inventoryComplete: false);
        Check(report.EffectiveCandidates(new[] { parallel }, requireIndependentProof: true).Length == 0,
            "a proven implication consumes no freedom and needs no native mutation");
    }

    private static void Branches()
    {
        var boundary = new[] { Circle("a", 0, 0, 5), Circle("b", 10, 0, 5) };
        var fixedCenters = Retain(S("fix", "a"), S("fix", "b"));
        var report = SketchMobility.Plan(boundary, fixedCenters, new[] { S("tangent", "a", "b") });
        Check(report.decisions[0].classification != "independent", "a tangent derivative alone must not establish a new independent constraint");
        var freeArc = new[] { new SketchConstraintCurve("arc", "arc", new(1, 0), new(0, 1), new(0, 0), 1, Math.PI / 2), Line("l", -5, -1, 5, -1) };
        report = SketchMobility.Plan(freeArc, Retain(), new[] { S("tangent", "arc", "l") });
        Check(report.decisions[0].classification == "unknown", "contact on the omitted part of a trimmed circle");
        var fixedPoint = new[] { Circle("c", 1, 0, 1), new SketchConstraintCurve("origin", "point", Center: new(0, 0)) };
        var fid = new[] { "origin" };
        // The second distance is tangent to the first locus at the test pose.
        // The Jacobians are parallel although the two constraints are independent away from that singularity.
        var singular = fixedPoint.Append(new SketchConstraintCurve("other", "point", Center: new(2, 0))).ToArray();
        var lockRadius = Retain(S("distance", "c", "origin", "center", "center", 1));
        report = SketchMobility.Plan(singular, lockRadius, new[] { S("distance", "c", "other", "center", "center", 1) },
            fixedGeometry: new[] { "other", "origin" });
        Check(report.decisions[0].classification == "unknown" && report.planned_dof == null, "singular tangency is never an implied affine equation");
        report = SketchMobility.Plan(fixedPoint, Retain(S("fix", "c")), new[] { S("distance", "c", "origin", "center", "center", 1) }, fixedGeometry: fid);
        Check(report.decisions[0].classification == "no_independent_motion", "a fully defined component cannot accept a fresh obligatory relation");
    }

    private static void Compound()
    {
        var mirror = new[] { Circle("a", -2, 3, 1), Circle("b", 2, 3, 1), Line("axis", 0, 0, 0, 7) };
        var mirrored = new[] { S("symmetric", "a", "b") with { Third = "axis" } };
        var report = SketchMobility.Plan(mirror, Retain(), mirrored, fixedGeometry: new[] { "axis" },
            preserve: new[] { new SketchMobilityMotion("slide both along mirror axis", new[] { "a", "b" }, 0, 1) });
        Check(report.planned_dof == 3 && report.decisions[0].classification == "independent", "circle reflection preserves vertical translation");
        report = SketchMobility.Plan(mirror, Retain(), mirrored, fixedGeometry: new[] { "axis" },
            preserve: new[] { new SketchMobilityMotion("slide both across mirror axis", new[] { "a", "b" }, 1, 0) });
        Check(report.decisions[0].classification == "removes_intended_motion", "mirror does not allow both circles to slide across the axis together");
        var pairs = new[] { Circle("a", 0, 0, 2), Circle("b", 0, 0, 1), Line("datum", -1, 0, 1, 0) };
        var sameX = Retain(S("distance_x", "a", "datum", "center", "start", 1), S("distance_x", "b", "datum", "center", "start", 1));
        report = SketchMobility.Plan(pairs, sameX, new[] { S("concentric", "a", "b") }, fixedGeometry: new[] { "datum" });
        Check(report.remaining_dof == 4 && report.decisions[0].classification == "partly_implied" && report.decisions[0].dof_after == 3, "shared X datum plus concentricity");
    }

    private static void Holder(double width)
    {
        const double r = 8, height = 122;
        // Both poses use the same fixed left and bottom dimensions. Only width changes.
        const double left = -44.5;
        double right = left + width;
        Point2[] vertices = [new(left, r), new(left + r, 0), new(right - r, 0), new(right, r),
            new(right, height - r), new(right - r, height), new(left + r, height), new(left, height - r)];
        Point2[] centers = [new(left + r, r), new(right - r, r), new(right - r, height - r), new(left + r, height - r)];
        var g = vertices.Select((p, i) => i % 2 == 0 ?
            new SketchConstraintCurve("e" + i, "arc", p, vertices[(i + 1) % 8], centers[i / 2], r, Math.PI / 2) :
            new SketchConstraintCurve("e" + i, "line", p, vertices[(i + 1) % 8])).ToList();
        g.Add(new("datum", "point", Center: new(-100, -10)));
        var stored = new List<SketchConstraintSpec>();
        void Stage(int expected, params SketchConstraintSpec[] add)
        {
            var report = SketchMobility.Plan(g, Retain(stored.ToArray()), add, fixedGeometry: new[] { "datum" });
            Check(report.planned_dof == expected,
                $"Holder {width}, {add.FirstOrDefault()?.Kind}: expected {expected}, observed {report.planned_dof}; {string.Join(", ", report.decisions.Select(d => d.classification + " " + d.reason))}");
            stored.AddRange(report.EffectiveCandidates(add));
        }
        Stage(36);
        Stage(20, Enumerable.Range(0, 8).Select(i => S("coincident", "e" + i, "e" + ((i + 1) % 8), "end", "start")).ToArray());
        Stage(16, S("horizontal", "e1"), S("vertical", "e3"), S("horizontal", "e5"), S("vertical", "e7"));
        Stage(16, S("parallel", "e5", "e1"));
        Stage(8, Enumerable.Range(0, 8).Select(i => S("tangent", "e" + i, "e" + ((i + 1) % 8))).ToArray());
        Stage(5, S("equal", "e0", "e2"), S("equal", "e2", "e4"), S("equal", "e4", "e6"));
        Stage(2, S("distance_x", "e7", "e3", "start", "start", width), S("distance_y", "e1", "e5", "start", "start", height), S("radius", "e0", value: r));
        Stage(0, S("distance_x", "e7", "datum", "start", "center", left + 100), S("distance_y", "e1", "datum", "start", "center", 10));
    }
}
