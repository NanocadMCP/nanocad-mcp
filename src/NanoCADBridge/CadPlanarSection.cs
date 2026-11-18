using NanoCAD.Contracts;
using System.Text.Json;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Brep = Teigha.BoundaryRepresentation.Brep;
using Face = Teigha.BoundaryRepresentation.Face;

namespace NanoCADBridge;

// Owns only temporary SDK objects. No transaction writes or editor/viewport changes.
internal sealed class CadPlanarSection : IDisposable
{
    private readonly Action _cancel;
    private DBObjectCollection? _edges;
    private Curve[]? _curves;
    internal SketchPlaneSpec Frame { get; }
    internal string Status { get; }
    internal Region? Region { get; }
    internal Point3d Origin { get; }
    internal Vector3d X { get; }
    internal Vector3d Y { get; }
    internal Vector3d Normal => X.CrossProduct(Y).GetNormal();

    private CadPlanarSection(SketchPlaneSpec frame, string status, Region? region, Action cancel)
    {
        Frame = frame;
        Status = status;
        Region = region;
        Origin = new(frame.Origin.X, frame.Origin.Y, frame.Origin.Z);
        X = new(frame.XAxis.X, frame.XAxis.Y, frame.XAxis.Z);
        Y = new(frame.YAxis.X, frame.YAxis.Y, frame.YAxis.Z);
        _cancel = cancel;
    }

    internal Point3d World(Point2 uv) => Origin + X * uv.X + Y * uv.Y;
    internal double[] Local(Point3d p) => [(p - Origin).DotProduct(X), (p - Origin).DotProduct(Y)];

    internal static CadPlanarSection Open(Solid3d body, SketchPlaneSpec frame, Action cancel, Point2[]? window = null)
    {
        var p = new Point3d(frame.Origin.X, frame.Origin.Y, frame.Origin.Z);
        var n = DrawingWorkplane.Normal(frame);
        var normal = new Vector3d(n.X, n.Y, n.Z).GetNormal();
        cancel();
        var extents = body.GeometricExtents;
        double min = double.PositiveInfinity, max = double.NegativeInfinity;
        foreach (double x in new[] { extents.MinPoint.X, extents.MaxPoint.X })
            foreach (double y in new[] { extents.MinPoint.Y, extents.MaxPoint.Y })
                foreach (double z in new[] { extents.MinPoint.Z, extents.MaxPoint.Z })
                {
                    double d = (new Point3d(x, y, z) - p).DotProduct(normal);
                    min = Math.Min(min, d);
                    max = Math.Max(max, d);
                }
        if (min > Tolerance.Global.EqualPoint || max < -Tolerance.Global.EqualPoint)
            return new(frame, "outside", null, cancel);

        using var plane = new Plane(p, normal);
        Region? region = null;
        try
        {
            cancel();
            region = body.GetSection(plane);
            cancel();
            if (region == null || region.IsNull || region.Area == 0)
                return new(frame, "tangent_or_empty", null, cancel);
            if (!region.ObjectId.IsNull || !double.IsFinite(region.Area) || region.Area < 0)
                throw new DomainException("SECTION_UNAVAILABLE", "The native section is not a detached finite-area region.");
            if (window != null)
            {
                using var clip = Window(frame, window);
                region.BooleanOperation(BooleanOperationType.BoolIntersect, clip);
                if (region.IsNull || region.Area == 0) return new(frame, "empty_window", null, cancel);
            }
            var result = new CadPlanarSection(frame, "area", region, cancel);
            region = null; // Result owns the detached object.
            return result;
        }
        catch (Teigha.Runtime.Exception error)
        {
            throw new DomainException("SECTION_UNAVAILABLE", "The kernel did not return a verifiable planar section.", false,
                new { native_status = error.ErrorStatus.ToString() });
        }
        finally { region?.Dispose(); }
    }

    private static Region Window(SketchPlaneSpec frame, Point2[] window)
    {
        Point3d P(double x, double y)
        {
            var v = DrawingWorkplane.World(frame, new(x, y));
            return new(v.X, v.Y, v.Z);
        }
        var low = window[0]; var high = window[1];
        var corners = new[] { P(low.X, low.Y), P(high.X, low.Y), P(high.X, high.Y), P(low.X, high.Y) };
        using var edges = new DBObjectCollection();
        try
        {
            for (int i = 0; i < corners.Length; i++) edges.Add(new Line(corners[i], corners[(i + 1) % corners.Length]));
            using var regions = Region.CreateFromCurves(edges);
            try
            {
                if (regions.Count != 1 || regions[0] is not Region region || !region.ObjectId.IsNull)
                    throw new DomainException("SECTION_WINDOW_UNAVAILABLE", "The section window did not form one detached rectangle.");
                return (Region)region.Clone();
            }
            finally { foreach (DBObject item in regions) item.Dispose(); }
        }
        finally { foreach (DBObject item in edges) item.Dispose(); }
    }

    // Region.Explode supplies finite curves with the actual trims of the section.
    // The collection retains ownership until the section is disposed.
    internal IReadOnlyList<Curve> BoundaryCurves()
    {
        if (_curves != null) return _curves;
        if (Region == null) return Array.Empty<Curve>();
        var collection = new DBObjectCollection();
        try
        {
            Region.Explode(collection);
            _cancel();
            var roots = collection.Cast<DBObject>().ToArray();
            var flattened = new List<Curve>();
            var pending = new Queue<(DBObject Item, int? ParentFaces)>(roots.Select(item => (item, (int?)null)));
            int FaceCount(Region region)
            {
                using var brep = new Brep(region); int count = 0;
                foreach (Face face in brep.Faces) using (face) { _cancel(); count++; }
                return count;
            }
            void Failure(string reason)
            {
                throw new DomainException("SECTION_BOUNDARY_UNAVAILABLE", reason, false, new
                {
                    root_types = roots.GroupBy(item => item.GetType().Name).Select(group => new { type = group.Key, count = group.Count() }).ToArray(),
                    finite_curve_count = flattened.Count
                });
            }
            // A section through a bored part can have several disconnected faces.
            // Read exact B-rep trims directly for linear components, even when
            // Explode groups or omits their curves at the first level.
            if (roots.Any(item => !item.ObjectId.IsNull)) Failure("Native section explosion returned a resident object.");
            if ((roots.Length == 0 || roots.Any(item => item is not Curve)) && TryLinearTrims(Region, out var lines))
            {
                foreach (var (start, end) in lines)
                {
                    _cancel();
                    var edge = new Line(start, end); collection.Add(edge); flattened.Add(edge);
                }
                pending.Clear();
            }
            int rootFaces = pending.Count > 0 && roots.Any(item => item is Region) ? FaceCount(Region) : 0;
            while (pending.TryDequeue(out var next))
            {
                _cancel();
                if (!next.Item.ObjectId.IsNull) Failure("Native section explosion returned a resident object.");
                if (next.Item is Curve curve) { flattened.Add(curve); continue; }
                if (next.Item is not Region piece) { Failure("The native section returned an unsupported boundary object."); continue; }
                int faces = FaceCount(piece);
                if (faces <= 0 || faces >= (next.ParentFaces ?? rootFaces))
                    Failure("Exploding a disjoint section did not produce a smaller native face set.");
                using var fragments = new DBObjectCollection();
                try { piece.Explode(fragments); }
                finally { foreach (DBObject child in fragments) collection.Add(child); }
                if (fragments.Count == 0) Failure("A detached section component did not yield boundary curves.");
                foreach (DBObject child in fragments) pending.Enqueue((child, faces));
            }
            if (flattened.Count == 0) Failure("The native section returned no finite boundary curves.");
            _curves = flattened.ToArray();
            _edges = collection;
            return _curves;
        }
        catch
        {
            try { foreach (DBObject item in collection) item.Dispose(); }
            finally { collection.Dispose(); }
            throw;
        }
    }

    private bool TryLinearTrims(Region region, out List<(Point3d, Point3d)> segments)
    {
        segments = [];
        var faces = CadRegionBooleanEditor.SectionFaces(region, _cancel);
        foreach (var face in faces.EnumerateArray())
            foreach (var loop in face.GetProperty("loops").EnumerateArray())
                foreach (var edge in loop.GetProperty("edges").EnumerateArray())
                {
                    _cancel();
                    var def = edge.GetProperty("geometry"); var nodes = def.GetProperty("nodes");
                    var node = nodes[def.GetProperty("root").GetInt32()];
                    while (node.GetProperty("geometry").TryGetProperty("native_curve", out var next)) node = nodes[next.GetInt32()];
                    if (node.GetProperty("runtime_type").GetString() is not ("Teigha.Geometry.LineSegment3d" or "Teigha.Geometry.Line3d")) return false;
                    var a = edge.GetProperty("start_vertex"); var b = edge.GetProperty("end_vertex");
                    if (a.ValueKind != JsonValueKind.Array || b.ValueKind != JsonValueKind.Array) return false;
                    var start = new Point3d(a[0].GetDouble(), a[1].GetDouble(), a[2].GetDouble());
                    var end = new Point3d(b[0].GetDouble(), b[1].GetDouble(), b[2].GetDouble());
                    if (start == end) return false;
                    segments.Add((start, end));
                }
        return segments.Count > 0;
    }

    internal string Classify(Point3d point)
    {
        if (Math.Abs((point - Origin).DotProduct(Normal)) > Tolerance.Global.EqualPoint)
            throw new DomainException("POINT_OUTSIDE_SECTION_PLANE", "Classify a point in the selected section plane.");
        if (Region == null) return Status is "outside" or "empty_window" ? "void" : "undetermined";
        using var brep = new Brep(Region);
        bool any = false, unknown = false, boundary = false;
        foreach (Face face in brep.Faces)
            using (face)
            {
                any = true;
                _cancel();
                try
                {
                    using var container = face.GetPointContainment(point, out var native);
                    string position = PointContainment.Classify(native.ToString(), container == null || container.IsNull);
                    if (position == "inside") return "material";
                    boundary |= position == "boundary";
                }
                catch (DomainException error) when (error.Error.code == "NATIVE_CONTAINMENT_MISMATCH") { unknown = true; }
                catch (Teigha.Runtime.Exception) { unknown = true; }
            }
        return boundary ? "boundary" : any && !unknown ? "void" : "undetermined";
    }

    public void Dispose()
    {
        try
        {
            if (_edges != null)
            {
                try { foreach (DBObject edge in _edges) edge.Dispose(); }
                finally { _edges.Dispose(); }
            }
        }
        finally { Region?.Dispose(); }
    }
}
