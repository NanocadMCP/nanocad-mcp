using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CadSpatialCurveCreator441
{
    internal sealed record Polyline3dShape441(string polyline_type, bool closed, bool planar, int vertex_count, double[][] vertices);

    private static void Need(bool condition, string message)
    {
        if (!condition) throw new DomainException("NATIVE_SPATIAL_CURVE_MISMATCH", message);
    }

    private static Point3d Point(Point3 point) => new(point.X, point.Y, point.Z);
    private static Vector3d Vector(Point3 vector) => new(vector.X, vector.Y, vector.Z);
    private static bool Finite(Point3d point) => double.IsFinite(point.X) && double.IsFinite(point.Y) && double.IsFinite(point.Z);
    private static double Resolution(IEnumerable<Point3> points)
    {
        double result = 1e-7;
        foreach (var point in points)
            result = Math.Max(result, 32 * 2.2204460492503131e-16 * Math.Max(Math.Abs(point.X), Math.Max(Math.Abs(point.Y), Math.Abs(point.Z))));
        return result;
    }

    internal static Spline Create(SpatialFitSplineSpec441 spec, Action? check = null)
    {
        var cancel = check ?? (() => { }); cancel();
        Need(spec.Workplane == null, "Spatial fit points and tangents are WCS values; a workplane cannot transform them.");
        Need((spec.StartTangent == null) == (spec.EndTangent == null), "Endpoint tangent conditions must be supplied together.");
        var values = new Point3d[spec.FitPoints.Length];
        for (int index = 0; index < values.Length; index++) { cancel(); values[index] = Point(spec.FitPoints[index]); }
        using var points = new Point3dCollection(values);
        // Installed PE confirms the argument named 'order' is passed unchanged
        // into the native degree slot, as in the assessed planar408 route.
        // Native construction copies the caller-owned XYZ point collection.
        var spline = spec.StartTangent is { } start && spec.EndTangent is { } end
            ? new Spline(points, Vector(start), Vector(end), SpatialFitSplineSpec441.NativeDegree, spec.FitTolerance)
            : new Spline(points, SpatialFitSplineSpec441.NativeDegree, spec.FitTolerance);
        try { VerifyCreated(spline, spec, cancel); return spline; }
        catch { spline.Dispose(); throw; }
    }

    internal static void VerifyCreated(Spline spline, SpatialFitSplineSpec441 spec, Action cancel)
    {
        cancel();
        Need(spec.Workplane == null && spline.GetType() == typeof(Spline) && !spline.IsNull,
            "Spatial construction requires an exact native WCS Spline.");
        var shape = CadSplineReader.ReadReported(spline, cancel);
        var fit = shape.native_inspection.fit_data;
        // Native fit construction metadata can be absent or partially
        // unavailable after normalization. Actual curve geometry is checked
        // unconditionally below; no fit records are reconstructed from input.
        Need(shape.degree == SpatialFitSplineSpec441.NativeDegree && !shape.closed && !shape.periodic,
            "Spatial creation did not retain an open nonperiodic native cubic fit construction.");
        Need(shape.control_points.Length >= SpatialFitSplineSpec441.NativeDegree + 1 &&
            shape.knots.Length == (long)shape.control_points.Length + SpatialFitSplineSpec441.NativeDegree + 1 &&
            shape.end_parameter > shape.start_parameter && shape.knots.Zip(shape.knots.Skip(1), (first, second) => first <= second).All(value => value) &&
            (shape.rational ? shape.weights.Length == shape.control_points.Length && shape.weights.All(value => value > 0) :
                shape.weights.Length == 0 || shape.weights.Length == shape.control_points.Length && shape.weights.All(value => value > 0)),
            "Native spatial fit has an incomplete or inconsistent finite NURBS definition.");
        if (fit != null)
            Need(fit.degree == SpatialFitSplineSpec441.NativeDegree && fit.points.Length == spec.FitPoints.Length,
                "Available native spatial fit definition changed its degree or ordered fit-point count.");
        double precision = Resolution(spec.FitPoints);
        foreach (var point in shape.control_points)
        {
            cancel(); precision = Math.Max(precision, 32 * 2.2204460492503131e-16 * point.Max(Math.Abs));
        }
        double allowedDeviation = spec.FitTolerance + precision;
        for (int index = 0; index < spec.FitPoints.Length; index++)
        {
            cancel(); var expected = Point(spec.FitPoints[index]);
            if (fit != null)
            {
                var actual = fit.points[index];
                Need((new Point3d(actual[0], actual[1], actual[2]) - expected).Length <= precision,
                    "Available native spatial fit changed a requested WCS fit point or its order.");
            }
            var closest = spline.GetClosestPointTo(expected, false); cancel();
            Need(Finite(closest) && (closest - expected).Length <= allowedDeviation,
                "Actual native spatial curve misses a requested fit point beyond the specified fit tolerance.");
        }
        Need((spline.StartPoint - Point(spec.FitPoints[0])).Length <= allowedDeviation &&
            (spline.EndPoint - Point(spec.FitPoints[^1])).Length <= allowedDeviation,
            "Native spatial endpoints differ from the requested ordered WCS endpoints.");
        if (spec.StartTangent is { } start && spec.EndTangent is { } end)
        {
            static void Direction(Vector3d actual, Vector3d wanted, string observation)
            {
                if (!double.IsFinite(actual.X) || !double.IsFinite(actual.Y) || !double.IsFinite(actual.Z) ||
                    !double.IsFinite(actual.Length) || actual.Length <= 0 || (actual.GetNormal() - wanted.GetNormal()).Length > 1e-7)
                    throw new DomainException("NATIVE_SPATIAL_CURVE_MISMATCH", observation + ": Actual native tangent does not follow the requested forward WCS direction.");
            }
            // Geometric endpoint directions remain mandatory when optional
            // native fit-condition getters are absent or unavailable.
            cancel(); Direction(spline.GetFirstDerivative(spline.StartParam), Vector(start), "actual_start_derivative");
            cancel(); Direction(spline.GetFirstDerivative(spline.EndParam), Vector(end), "actual_end_derivative");
            if (fit is { tangents_exist: true })
            {
                if (fit.start_tangent is { } storedStart && new Vector3d(storedStart[0], storedStart[1], storedStart[2]).Length > 0)
                    Direction(new(storedStart[0], storedStart[1], storedStart[2]), Vector(start), "native_start_fit_tangent");
                if (fit.end_tangent is { } storedEnd && new Vector3d(storedEnd[0], storedEnd[1], storedEnd[2]).Length > 0)
                    Direction(new(storedEnd[0], storedEnd[1], storedEnd[2]), Vector(end), "native_end_fit_tangent");
            }
        }
        cancel();
    }

    internal static Polyline3d Create(Polyline3dSpec441 spec, Action? check = null)
    {
        var cancel = check ?? (() => { }); cancel();
        Need(spec.Workplane == null, "Polyline3d vertices are WCS values; a workplane cannot transform them.");
        var values = new Point3d[spec.Vertices.Length];
        for (int index = 0; index < values.Length; index++) { cancel(); values[index] = Point(spec.Vertices[index]); }
        using var points = new Point3dCollection(values);
        // SimplePoly stores actual straight 3D segments. It is not a fitted or
        // faceted replacement for the separate smooth Spline construction.
        var polyline = new Polyline3d(Poly3dType.SimplePoly, points, spec.Closed);
        try { VerifyCreated(polyline, spec, cancel); return polyline; }
        catch { polyline.Dispose(); throw; }
    }

    // GetGeCurve supplies an owned native geometric copy for resident roots
    // and detached clones alike. Read the exact SimplePoly definition; never
    // use the PolylineCurve3d(curve, epsilon) approximation constructor.
    internal static Polyline3dShape441 ReadPolyline3d(Polyline3d polyline, Action cancel)
    {
        cancel();
        if (polyline.GetType() != typeof(Polyline3d) || polyline.UnmanagedObject == IntPtr.Zero || polyline.PolyType != Poly3dType.SimplePoly)
            throw new DomainException("UNSUPPORTED_POLYLINE3D", "Exact spatial polyline inspection requires a native SimplePoly Polyline3d.");
        using var geometry = polyline.GetGeCurve(); cancel();
        var points = new List<double[]>(); var capacity = new SerializedCapacity357(cancel: cancel);
        bool closingEndpointRemoved = false;
        void Add(Point3d point)
        {
            cancel();
            if (!Finite(point)) throw new DomainException("INVALID_GEOMETRY", "Native spatial polyline contains a nonfinite WCS fit point.");
            var value = new[] { point.X, point.Y, point.Z }; capacity.Add(value); points.Add(value);
        }
        static bool SameEndpoint(Point3d first, Point3d second)
        {
            if (!Finite(first) || !Finite(second)) return false;
            double magnitude = Math.Max(Math.Max(Math.Abs(first.X), Math.Max(Math.Abs(first.Y), Math.Abs(first.Z))),
                Math.Max(Math.Abs(second.X), Math.Max(Math.Abs(second.Y), Math.Abs(second.Z))));
            return (first - second).Length <= Math.Max(1e-7, 32 * 2.2204460492503131e-16 * magnitude);
        }
        if (geometry is PolylineCurve3d native && geometry.GetType() == typeof(PolylineCurve3d))
        {
            int count = native.NumberOfFitPoints;
            if (count < 2) throw new DomainException("NATIVE_POLYLINE3D_READ_UNAVAILABLE", "Native spatial polyline has fewer than two reported fit points.");
            for (int index = 0; index < count; index++) { cancel(); Add(native.FitPointAt(index)); }
        }
        else if (geometry is CompositeCurve3d composite && geometry.GetType() == typeof(CompositeCurve3d))
        {
            // Installed GetCurves calls getCurveList and wraps each native
            // child with CreateShared. These are owned shared references,
            // released before the parent geometric copy, not DB entities.
            var children = composite.GetCurves();
            try
            {
                if (children == null || children.Length == 0)
                    throw new DomainException("NATIVE_POLYLINE3D_READ_UNAVAILABLE", "Native spatial polyline composite has no ordered segments.");
                Point3d? previous = null;
                for (int index = 0; index < children.Length; index++)
                {
                    cancel();
                    if (children[index] is not LineSegment3d segment || segment.GetType() != typeof(LineSegment3d))
                        throw new DomainException("NATIVE_POLYLINE3D_READ_UNAVAILABLE", "Native SimplePoly composite contains a non-line segment; no approximation is performed.", false,
                            new { segment_index = index, runtime_type = children[index]?.GetType().FullName });
                    var start = segment.StartPoint; var end = segment.EndPoint;
                    if (previous is { } endpoint && !SameEndpoint(endpoint, start))
                        throw new DomainException("NATIVE_POLYLINE3D_READ_UNAVAILABLE", "Native spatial polyline segments are not connected in their reported forward order.", false,
                            new { segment_index = index });
                    if (index == 0) Add(start);
                    Add(end); previous = end;
                }
                // A closed composite includes its actual final closing edge.
                // Store each vertex once after checking that edge reaches the
                // first actual endpoint. Never sort, reverse or project edges.
                if (polyline.Closed)
                {
                    var first = points[0]; var last = points[^1];
                    if (!SameEndpoint(new(first[0], first[1], first[2]), new(last[0], last[1], last[2])))
                        throw new DomainException("NATIVE_POLYLINE3D_READ_UNAVAILABLE", "Native closed spatial polyline composite does not close at its first vertex.");
                    points.RemoveAt(points.Count - 1); closingEndpointRemoved = true;
                }
            }
            finally
            {
                if (children != null)
                    foreach (var child in children.Distinct<Curve3d>(ReferenceEqualityComparer.Instance)) child?.Dispose();
            }
        }
        else throw new DomainException("NATIVE_POLYLINE3D_READ_UNAVAILABLE", "Native SimplePoly geometry did not expose an exact polyline or ordered line-segment composite definition.", false,
            new { runtime_type = geometry?.GetType().FullName });
        bool closed = polyline.Closed;
        // Geometric closure may be represented by a repeated first point.
        // Remove only an exact terminal duplicate; stored entity closure is
        // reported separately and still adds the final straight segment.
        if (closed && !closingEndpointRemoved && points[0].SequenceEqual(points[^1])) points.RemoveAt(points.Count - 1);
        if (points.Count < (closed ? 3 : 2))
            throw new DomainException("NATIVE_POLYLINE3D_READ_UNAVAILABLE", "Native spatial polyline has insufficient distinct ordered vertices for its closure.");
        var result = new Polyline3dShape441(polyline.PolyType.ToString(), closed, polyline.IsPlanar, points.Count, points.ToArray());
        NativeCurveRead342.CheckSerializedBudget(result, NativeCurveRead342.PublicGeometryByteBudget, cancel);
        cancel(); return result;
    }

    internal static void VerifyCreated(Polyline3d polyline, Polyline3dSpec441 spec, Action cancel)
    {
        cancel();
        Need(spec.Workplane == null && polyline.GetType() == typeof(Polyline3d) && polyline.UnmanagedObject != IntPtr.Zero &&
            polyline.PolyType == Poly3dType.SimplePoly && polyline.Closed == spec.Closed,
            "Creation did not retain an exact native SimplePoly Polyline3d with the requested closure.");
        double precision = Resolution(spec.Vertices);
        Need(Finite(polyline.StartPoint) && Finite(polyline.EndPoint) &&
            (polyline.StartPoint - Point(spec.Vertices[0])).Length <= precision &&
            (spec.Closed || (polyline.EndPoint - Point(spec.Vertices[^1])).Length <= precision),
            "Native Polyline3d endpoints differ from the requested ordered WCS endpoints.");
        var geometry = ReadPolyline3d(polyline, cancel);
        Need(geometry.vertex_count == spec.Vertices.Length, "Native Polyline3d vertex count differs from the requested definition.");
        for (int index = 0; index < spec.Vertices.Length; index++)
        {
            cancel(); var value = geometry.vertices[index]; var actual = new Point3d(value[0], value[1], value[2]);
            Need(Finite(actual) && (actual - Point(spec.Vertices[index])).Length <= precision,
                "Native Polyline3d changed a requested WCS vertex or its order.");
        }
        if (polyline.ObjectId.IsNull) { cancel(); return; }
        var transaction = polyline.Database.TransactionManager.TopTransaction ?? throw new DomainException("OBJECT_READ_FAILED",
            "Resident Polyline3d verification requires the current transaction.");
        int count = 0;
        foreach (ObjectId id in polyline)
        {
            cancel();
            Need(!id.IsNull && id.IsValid && !id.IsErased && !id.IsEffectivelyErased,
                "New Polyline3d contains an unavailable native vertex identity.");
            var value = transaction.GetObject(id, OpenMode.ForRead);
            Need(value.GetType() == typeof(PolylineVertex3d) && value.OwnerId == polyline.ObjectId && value.Database == polyline.Database,
                "New Polyline3d child must be an exact local PolylineVertex3d owned by this root.");
            var vertex = (PolylineVertex3d)value;
            Need(count < spec.Vertices.Length && vertex.VertexType.ToString() == "SimpleVertex" && Finite(vertex.Position) &&
                (vertex.Position - Point(spec.Vertices[count])).Length <= precision,
                "New Polyline3d did not retain its ordered simple WCS vertex definition.");
            count++;
        }
        Need(count == spec.Vertices.Length, "New Polyline3d did not retain every requested WCS vertex.");
        cancel();
    }
}
