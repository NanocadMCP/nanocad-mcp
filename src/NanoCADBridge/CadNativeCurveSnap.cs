using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CadNativeCurveSnap
{
    internal static PointSpec408 ResolvePoint(PointFromNativeSnapSpec spec, Database db, Transaction tr,
        ObjectId owner, Action cancel)
    {
        var position = Resolve(spec.PositionSnap, db, tr, owner, cancel);
        return new PointSpec408(new Point3(position.X, position.Y, position.Z), spec.LayerName);
    }

    internal static ResolvedNativeSnapLineSpec ResolveLine(LineFromNativeSnapSpec spec, Database db, Transaction tr,
        ObjectId owner, Action cancel)
    {
        Point3d Endpoint(Point2? literal, NativeCurveSnap? snap) => snap is null
            ? new Point3d(literal!.X, literal.Y, 0) : Resolve(snap, db, tr, owner, cancel);
        var start = Endpoint(spec.Start, spec.StartSnap);
        var end = Endpoint(spec.End, spec.EndSnap);
        if (!(start.DistanceTo(end) > Tolerance.Global.EqualPoint))
            throw new DomainException("INVALID_GEOMETRY", "The resolved native Line endpoints must differ.");
        return new(new Point3(start.X, start.Y, start.Z), new Point3(end.X, end.Y, end.Z), spec.LayerName);
    }

    internal static Line CreateLine(ResolvedNativeSnapLineSpec spec) =>
        new(new Point3d(spec.Start.X, spec.Start.Y, spec.Start.Z), new Point3d(spec.End.X, spec.End.Y, spec.End.Z));

    internal static void VerifyLine(Line line, ResolvedNativeSnapLineSpec spec)
    {
        if (!(line.StartPoint.DistanceTo(new Point3d(spec.Start.X, spec.Start.Y, spec.Start.Z)) <= Tolerance.Global.EqualPoint) ||
            !(line.EndPoint.DistanceTo(new Point3d(spec.End.X, spec.End.Y, spec.End.Z)) <= Tolerance.Global.EqualPoint))
            throw new DomainException("NATIVE_CURVE_SNAP_MISMATCH", "The created Line does not retain its resolved WCS endpoints.");
    }

    internal static Point3d Resolve(NativeCurveSnap snap, Database db, Transaction tr, ObjectId owner, Action cancel)
    {
        var curve = Read(snap.Handle, snap.ExpectedCurveToken, db, tr, owner, cancel);
        cancel();
        Point3d point = snap.Snap switch
        {
            "start" => curve.StartPoint,
            "end" => curve.EndPoint,
            "mid" => Midpoint(curve, cancel),
            "center" => curve switch
            {
                Arc arc => arc.Center,
                Circle circle => circle.Center,
                Ellipse ellipse => ellipse.Center,
                _ => throw new DomainException("UNSUPPORTED_CURVE_SNAP", "Center snap requires a native Arc, Circle or Ellipse.")
            },
            "nearest" => Nearest(curve, Pick(snap), snap.Tolerance!.Value, cancel),
            "intersection" => Intersection(curve, Read(snap.OtherHandle!, snap.OtherExpectedCurveToken!, db, tr, owner, cancel),
                Pick(snap), snap.Tolerance!.Value, cancel),
            _ => throw new DomainException("INVALID_ARGUMENT", "Unknown native curve snap.")
        };
        cancel();
        if (!Finite(point)) throw new DomainException("INVALID_GEOMETRY", "Native curve snap did not resolve to a finite WCS point.");
        return point;
    }

    private static Curve Read(string handle, string token, Database db, Transaction tr, ObjectId owner, Action cancel)
    {
        cancel();
        var id = CadStructureWriter.Find(db, handle);
        if (tr.GetObject(id, OpenMode.ForRead) is not Curve curve || curve.GetType() != typeof(Line) &&
            curve.GetType() != typeof(Arc) && curve.GetType() != typeof(Circle) && curve.GetType() != typeof(Polyline) &&
            curve.GetType() != typeof(Ellipse) && curve.GetType() != typeof(Spline))
            throw new DomainException("UNSUPPORTED_CURVE_SNAP", "Use a native Line, Arc, Circle, Polyline, Ellipse or Spline for curve snaps.");
        if (curve.Database != db || curve.OwnerId != owner)
            throw new DomainException("OUT_OF_SCOPE", "The snap source must belong to the requested active drawing space.");
        if (NativeCurveRead342.Token(CadNativeCurveInspection342.Shape(curve, cancel), cancel) != token)
            throw new DomainException("GEOMETRY_CHANGED", "The snap source differs from its expected native curve token; read its current shape.");
        return curve;
    }

    private static Point3d Pick(NativeCurveSnap snap)
    {
        var reference = snap.ReferencePointWcs!;
        return new(reference.X, reference.Y, reference.Z);
    }

    private static bool Finite(Point3d point) =>
        double.IsFinite(point.X) && double.IsFinite(point.Y) && double.IsFinite(point.Z);

    private static Point3d Midpoint(Curve curve, Action cancel)
    {
        var first = curve.GetDistanceAtParameter(curve.StartParam);
        cancel();
        var last = curve.GetDistanceAtParameter(curve.EndParam);
        if (!double.IsFinite(first) || !double.IsFinite(last) || last <= first)
            throw new DomainException("CURVE_SNAP_UNAVAILABLE", "A finite increasing native curve length is required for its half-length point.");
        cancel();
        return curve.GetPointAtDist(first / 2 + last / 2);
    }

    private static Point3d Nearest(Curve curve, Point3d reference, double tolerance, Action cancel)
    {
        var point = curve.GetClosestPointTo(reference, false);
        cancel();
        if (!Finite(point) || !(point.DistanceTo(reference) <= tolerance))
            throw new DomainException("NO_CURVE_SNAP", "The finite native closest point is outside the requested pick tolerance.");
        return point;
    }

    private static Point3d Intersection(Curve first, Curve second, Point3d reference, double tolerance, Action cancel)
    {
        using var hits = new Point3dCollection();
        cancel();
        first.IntersectWith(second, Intersect.OnBothOperands, hits, IntPtr.Zero, IntPtr.Zero);
        Point3d? candidate = null;
        var nativeTolerance = Tolerance.Global.EqualPoint;
        for (int i = 0; i < hits.Count; i++)
        {
            cancel();
            var hit = hits[i];
            if (!Finite(hit) || !(hit.DistanceTo(reference) <= tolerance)) continue;
            if (!(hit.DistanceTo(first.GetClosestPointTo(hit, false)) <= nativeTolerance) ||
                !(hit.DistanceTo(second.GetClosestPointTo(hit, false)) <= nativeTolerance))
                throw new DomainException("CURVE_SNAP_UNAVAILABLE", "Native intersection is outside a finite source locus.");
            if (candidate.HasValue && hit.DistanceTo(candidate.Value) > nativeTolerance)
                throw new DomainException("AMBIGUOUS_CURVE_SNAP", "More than one finite intersection lies within pick tolerance.");
            candidate ??= hit;
        }
        return candidate ?? throw new DomainException("NO_CURVE_SNAP", "No finite native intersection lies within pick tolerance.");
    }
}
