using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CadSolidSectionAnalysis
{
    private static double[] P(Point3d p) => [p.X, p.Y, p.Z];
    private static double[] V(Vector3d v) => [v.X, v.Y, v.Z];

    internal static object Read(GeometryAnalysisArguments args, DocumentState state, Transaction tx,
        ObjectId modelId, string instance, CancellationToken cancellation)
    {
        var request = args.Section!;
        var handle = args.Read.Handles!.Single();
        var id = CadStructureWriter.Find(state.Document.Database, handle);
        if (id.IsErased || id.IsEffectivelyErased || tx.GetObject(id, OpenMode.ForRead) is not Solid3d solid || solid.OwnerId != modelId)
            throw new DomainException("OUT_OF_SCOPE", "Choose a current top-level Model Space Solid3d for the section.");
        Action check = cancellation.ThrowIfCancellationRequested;
        using var section = CadPlanarSection.Open(solid, request.Plane, check, request.Window);
        var region = section.Region;
        var units = state.Document.Database.Insunits;
        double? empty = section.Status is "outside" or "empty_window" ? 0 : null;
        object? measurement = request.Segment == null ? null : Measure(section, request.Segment, check);
        return new
        {
            mode = args.Mode,
            body = new EntityRef(instance, state.Id, handle),
            units = new { name = units.ToString(), value = (int)units },
            solver = "native_solid_section_region",
            workplane = new { origin = P(section.Origin), x_axis = V(section.X), y_axis = V(section.Y), normal = V(section.Normal) },
            status = section.Status,
            area = region?.Area ?? empty,
            perimeter = region?.Perimeter ?? empty,
            faces = region != null && request.Segment == null ? (object)CadRegionBooleanEditor.SectionFaces(region, check) : null,
            window = request.Window?.Select(p => new[] { p.X, p.Y }),
            measurement
        };
    }

    private static object Measure(CadPlanarSection section, Point2[] ends, Action check)
    {
        var from = section.World(ends[0]);
        var to = section.World(ends[1]);
        double length = from.DistanceTo(to);
        double tolerance = Tolerance.Global.EqualPoint;
        if (!double.IsFinite(length) || length <= tolerance)
            throw new DomainException("INVALID_GEOMETRY", "The requested segment is shorter than the native point tolerance.");
        var axis = (to - from).GetNormal();
        var transverse = section.Normal.CrossProduct(axis).GetNormal();
        var positions = new SortedSet<double> { 0, length };
        var contacts = new SortedSet<double>();
        var overlaps = new List<(double Start, double End)>();
        object Station(double t)
        {
            var p = from + axis * t;
            return new { distance = t, local = section.Local(p), wcs = P(p) };
        }
        void Add(double t)
        {
            if (t < -tolerance || t > length + tolerance) return;
            t = Math.Clamp(t, 0, length);
            positions.Add(t);
            contacts.Add(t);
        }
        var curves = section.BoundaryCurves();
        var unsupported = curves.FirstOrDefault(curve => curve is not (Line or Circle or Arc or Spline or Ellipse));
        if (unsupported != null)
            return new
            {
                status = "unsupported_boundary",
                unavailable_reason = "Segment intersection is unverified for " + unsupported.GetType().Name,
                segment = new[] { Station(0), Station(length) },
                intervals = (object?)null
            };
        using var segment = new Line(from, to);
        foreach (var curve in curves)
        {
            check();
            if (curve is Line edge)
            {
                var a = edge.StartPoint - from;
                var b = edge.EndPoint - from;
                double h0 = a.DotProduct(transverse), h1 = b.DotProduct(transverse);
                double t0 = a.DotProduct(axis), t1 = b.DotProduct(axis);
                if (Math.Abs(h0) <= tolerance && Math.Abs(h1) <= tolerance)
                {
                    double low = Math.Max(0, Math.Min(t0, t1)), high = Math.Min(length, Math.Max(t0, t1));
                    if (low <= high) { Add(low); Add(high); overlaps.Add((low, high)); }
                }
                else
                {
                    if (Math.Abs(h0) <= tolerance) Add(t0);
                    if (Math.Abs(h1) <= tolerance) Add(t1);
                    if (Math.Sign(h0) != Math.Sign(h1)) Add(t0 + (t1 - t0) * (h0 / (h0 - h1)));
                }
            }
            else if (curve is Ellipse or Spline)
            {
                // These are actual finite trims of the section Region. Keep overlaps and tangencies as well as crossings.
                var hits = CadCurveRelations.IntersectFinite(segment, curve, section.Normal, check);
                double Along(double[] point) => (new Point3d(point) - from).DotProduct(axis);
                foreach (var point in hits.Points) Add(Along(point));
                foreach (var span in hits.Overlaps)
                {
                    var a = Math.Clamp(Along(span.first_start), 0, length);
                    var b = Math.Clamp(Along(span.first_end), 0, length);
                    Add(a);
                    Add(b);
                    overlaps.Add((Math.Min(a, b), Math.Max(a, b)));
                }
            }
            else
            {
                var (center, radius) = curve is Circle c ? (c.Center, c.Radius) : (((Arc)curve).Center, ((Arc)curve).Radius);
                var v = center - from;
                double height = Math.Abs(v.DotProduct(transverse));
                if (height > radius + tolerance) continue;
                double foot = v.DotProduct(axis);
                double offset = Math.Sqrt(Math.Max(0, (radius - height) * (radius + height)));
                void CheckPoint(double t)
                {
                    if (t < -tolerance || t > length + tolerance) return;
                    var witness = from + axis * t;
                    if (curve is Circle || witness.DistanceTo(curve.GetClosestPointTo(witness, false)) <= tolerance)
                        Add(t);
                }
                CheckPoint(foot - offset);
                if (offset != 0) CheckPoint(foot + offset);
            }
        }
        var cuts = positions.ToArray();
        var intervals = new List<object>();
        var budget = new SerializedCapacity357(Wire.PayloadBudget - 8192, check);
        bool complete = true;
        for (int i = 1; i < cuts.Length; i++)
        {
            check();
            double a = cuts[i - 1], b = cuts[i], middle = a + (b - a) / 2;
            string classification;
            if (b - a <= tolerance) classification = "undetermined";
            else if (overlaps.Any(o => middle >= o.Start && middle <= o.End)) classification = "boundary";
            else classification = section.Classify(from + axis * middle);
            complete &= classification != "undetermined";
            var span = new { classification, start = Station(a), end = Station(b), length = b - a };
            budget.Add(span);
            intervals.Add(span);
        }
        return new
        {
            status = complete ? "complete" : "partial",
            segment = new[] { Station(0), Station(length) },
            length,
            boundary_contacts = contacts.Select(Station),
            intervals,
            point_tolerance = tolerance
        };
    }
}
