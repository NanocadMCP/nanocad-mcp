using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CadSelectedContours
{
    private const double Tol = ContourAssemblyGeometry.Tolerance;
    private sealed record Piece(string Handle, int Segment, Curve Curve, Extents3d Bounds);
    private static double[] P(Point3d p) => [p.X, p.Y, p.Z];
    private static double Length(Curve curve) => curve switch
    {
        Line l => l.Length,
        Arc a => a.Length,
        Circle c => c.Circumference,
        Polyline p => p.Length,
        _ => throw new DomainException("UNSUPPORTED_ENTITY_TYPE", "Expected a finite Line, Arc, Circle or Polyline.")
    };

    internal static object Read(GeometryAnalysisArguments args, DocumentState state, Transaction tr, ObjectId owner, Action check)
    {
        var frame = args.Workplane!;
        var db = state.Document.Database;
        var curves = new Dictionary<string, Curve>(StringComparer.Ordinal);
        var sources = new List<SelectedContours.Source>();
        var pieces = new List<Piece>();
        var exploded = new List<DBObjectCollection>();
        var regions = new Dictionary<int, Region>();
        var capacity = new SerializedCapacity357(Wire.PayloadBudget - 8192, check);
        Point2 Local(Point3d p) => DrawingWorkplane.Local(frame, new(p.X, p.Y, p.Z));
        try
        {
            foreach (var handle in args.Read.Handles!)
            {
                check();
                var id = CadStructureWriter.Find(db, handle);
                if (id.IsErased || id.IsEffectivelyErased || tr.GetObject(id, OpenMode.ForRead) is not Curve curve || curve.OwnerId != owner)
                    throw new DomainException("OUT_OF_SCOPE", "Select current top-level Model Space curves.", false, new { handle });
                if (curve.GetType() != typeof(Line) && curve.GetType() != typeof(Arc) && curve.GetType() != typeof(Circle) && curve.GetType() != typeof(Polyline))
                    throw new DomainException("UNSUPPORTED_ENTITY_TYPE", "Select native Line, Arc, Circle or lightweight Polyline centerlines.", false, new { handle });
                CadCurveWorkplane.Require(curve, frame, check);
                if (!double.IsFinite(Length(curve)) || Length(curve) <= 0)
                    throw new DomainException("DEGENERATE_GEOMETRY", "A selected curve has no finite positive length.", false, new { handle });
                curves.Add(handle, curve);
                sources.Add(new(handle, Local(curve.StartPoint), Local(curve.EndPoint), curve.Closed));
                if (curve is not Polyline poly) pieces.Add(new(handle, 0, curve, curve.GeometricExtents));
                else
                {
                    var children = new DBObjectCollection(); exploded.Add(children); poly.Explode(children);
                    if (children.Count != (poly.Closed ? poly.NumberOfVertices : poly.NumberOfVertices - 1))
                        throw new DomainException("CONTOUR_SEGMENTS_UNAVAILABLE", "Exploded segments differ from the selected Polyline inventory.", false, new { handle });
                    for (int i = 0; i < children.Count; i++)
                    {
                        check();
                        if (children[i] is not Curve child || child is not (Line or Arc) || !child.ObjectId.IsNull)
                            throw new DomainException("CONTOUR_SEGMENTS_UNAVAILABLE", "The polyline did not return detached Line/Arc segments.", false, new { handle });
                        pieces.Add(new(handle, i, child, child.GeometricExtents));
                    }
                }
            }
            var graph = SelectedContours.Build(sources, Tol, check);
            var groups = graph.Components;
            var byHandle = groups.SelectMany((g, i) => g.handles.Select(h => (h, i))).ToDictionary(p => p.h, p => p.i);
            var contacts = new List<object>();
            var issues = groups.Select(_ => new HashSet<string>()).ToArray();
            var selfConflict = new HashSet<int>();
            var sorted = pieces.OrderBy(p => p.Bounds.MinPoint.X).ToArray();
            bool AtEnd(Curve c, Point3d p) => !c.Closed && (c.StartPoint.DistanceTo(p) <= Tol || c.EndPoint.DistanceTo(p) <= Tol);
            for (int i = 0; i < sorted.Length; i++)
                for (int j = i + 1; j < sorted.Length && sorted[j].Bounds.MinPoint.X <= sorted[i].Bounds.MaxPoint.X + Tol; j++)
                {
                    check();
                    var first = sorted[i]; var second = sorted[j]; var a = first.Bounds; var b = second.Bounds;
                    if (a.MaxPoint.Y + Tol < b.MinPoint.Y || b.MaxPoint.Y + Tol < a.MinPoint.Y ||
                        a.MaxPoint.Z + Tol < b.MinPoint.Z || b.MaxPoint.Z + Tol < a.MinPoint.Z) continue;
                    int g = byHandle[first.Handle], h = byHandle[second.Handle];
                    object? contact = null;
                    try
                    {
                        var rel = CadCurveRelations.Read(first.Curve, second.Curve, check, workplane: frame);
                        bool ExpectedJoin(double[] coords)
                        {
                            if (g != h) return false;
                            var p = new Point3d(coords);
                            if (!AtEnd(first.Curve, p) || !AtEnd(second.Curve, p)) return false;
                            if (first.Handle != second.Handle)
                                return AtEnd(curves[first.Handle], p) && AtEnd(curves[second.Handle], p);
                            int diff = Math.Abs(first.Segment - second.Segment);
                            return diff == 1 || curves[first.Handle] is Polyline { Closed: true } pl && diff == pl.NumberOfVertices - 1;
                        }
                        var extra = rel.Points.Where(p => !ExpectedJoin(p)).ToArray();
                        if (extra.Length == 0 && rel.Overlaps.Length == 0) continue;
                        string kind = rel.Overlaps.Length > 0 ? "overlap" : "touch_or_cross";
                        issues[g].Add(kind); issues[h].Add(kind);
                        if (g == h) selfConflict.Add(g);
                        contact = new
                        {
                            kind,
                            first = first.Handle,
                            first_segment = first.Segment,
                            second = second.Handle,
                            second_segment = second.Segment,
                            points_wcs = extra,
                            overlaps = rel.Overlaps
                        };
                    }
                    catch (Exception ex) when (ex is Teigha.Runtime.Exception || ex is DomainException d && d.Error.code == "NATIVE_RELATION_MISMATCH")
                    {
                        issues[g].Add("relation_unavailable"); issues[h].Add("relation_unavailable");
                        if (g == h) selfConflict.Add(g);
                        contact = new { kind = "relation_unavailable", first = first.Handle, first_segment = first.Segment, second = second.Handle, second_segment = second.Segment, reason = ex.Message };
                    }
                    capacity.Add(contact); contacts.Add(contact);
                }
            for (int i = 0; i < groups.Length; i++)
            {
                check();
                if (groups[i].status != "loop" || selfConflict.Contains(i)) continue;
                try
                {
                    var region = LoopRegion(groups[i].handles.Select(h => curves[h]), check);
                    if (region != null) regions.Add(i, region);
                    else issues[i].Add("native_loop_unverified");
                }
                catch (Teigha.Runtime.Exception) { issues[i].Add("native_loop_unverified"); }
            }
            var rows = new List<object>(); int holes = 0; bool complete = true;
            for (int i = 0; i < groups.Length; i++)
            {
                var group = groups[i];
                string role = "undetermined"; var enclosing = new List<int>();
                if (regions.TryGetValue(i, out var region))
                {
                    var point = curves[group.handles[0]].StartPoint;
                    bool available = issues[i].Count == 0;
                    for (int j = 0; j < groups.Length; j++)
                    {
                        check();
                        if (i == j || groups[j].status != "loop") continue;
                        if (!regions.TryGetValue(j, out var other))
                        {
                            var bounds = curves[groups[j].handles[0]].GeometricExtents;
                            foreach (var h in groups[j].handles.Skip(1)) bounds.AddExtents(curves[h].GeometricExtents);
                            if (!PointContainment.OutsideNativeBounds(P(point), P(bounds.MinPoint), P(bounds.MaxPoint))) available = false;
                            continue;
                        }
                        try
                        {
                            string inside = CadBoundaryTrace408.Containment(other, point, check);
                            if (inside == "inside") enclosing.Add(j);
                            else if (inside != "outside") available = false;
                        }
                        catch (Exception ex) when (ex is Teigha.Runtime.Exception || ex is DomainException d && d.Error.code == "NATIVE_CONTAINMENT_MISMATCH")
                        { available = false; }
                    }
                    if (available) role = enclosing.Count % 2 != 0 ? "hole" : enclosing.Count == 0 ? "outer" : "island";
                }
                if (role == "hole") holes++;
                complete &= role != "undetermined";
                var row = new
                {
                    index = i,
                    group.status,
                    group.handles,
                    group.order,
                    group.junctions,
                    role,
                    enclosing = enclosing.ToArray(),
                    area = region?.Area,
                    perimeter = region?.Perimeter,
                    issues = issues[i].OrderBy(v => v, StringComparer.Ordinal).ToArray()
                };
                capacity.Add(row); rows.Add(row);
            }
            capacity.Add(graph.Gaps);
            return new
            {
                mode = args.Mode,
                workplane = new
                {
                    origin = new[] { frame.Origin.X, frame.Origin.Y, frame.Origin.Z },
                    x_axis = new[] { frame.XAxis.X, frame.XAxis.Y, frame.XAxis.Z },
                    y_axis = new[] { frame.YAxis.X, frame.YAxis.Y, frame.YAxis.Z }
                },
                units = new { name = db.Insunits.ToString(), value = (int)db.Insunits },
                endpoint_tolerance = Tol,
                components = rows,
                nearest_gaps = graph.Gaps,
                contacts,
                confirmed_hole_count = holes,
                classification_complete = complete
            };
        }
        finally
        {
            foreach (var r in regions.Values) r.Dispose();
            foreach (var collection in exploded)
            {
                try { foreach (DBObject child in collection) child.Dispose(); }
                finally { collection.Dispose(); }
            }
        }
    }

    private static Region? LoopRegion(IEnumerable<Curve> curves, Action check)
    {
        using var clones = new DBObjectCollection();
        try
        {
            double perimeter = 0;
            foreach (var curve in curves) { check(); clones.Add((Curve)curve.Clone()); perimeter += Length(curve); }
            using var regions = Region.CreateFromCurves(clones);
            try
            {
                if (regions.Count != 1 || regions[0] is not Region r || !r.ObjectId.IsNull || r.IsNull ||
                    !double.IsFinite(r.Area) || r.Area <= 0 || Math.Abs(r.Perimeter - perimeter) > Tol * clones.Count + 1e-12 * perimeter) return null;
                return (Region)r.Clone();
            }
            finally { foreach (DBObject temp in regions) temp.Dispose(); }
        }
        finally { foreach (DBObject temp in clones) temp.Dispose(); }
    }
}
