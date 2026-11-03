using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CadFitSplineCreator408
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new DomainException("NATIVE_FIT_SPLINE_MISMATCH", message);
    }
    private static Point3d Local(Point2 value) => new(value.X, value.Y, 0);
    private static Vector3d LocalVector(Point2 value) => new(value.X, value.Y, 0);

    internal static Spline CreateLocal(FitSplineSpec408 spec, Action? check = null)
    {
        var cancel = check ?? (() => { }); cancel();
        Require((spec.StartTangent == null) == (spec.EndTangent == null), "Endpoint tangent conditions must be supplied together.");
        var values = new Point3d[spec.FitPoints.Length];
        for (int i = 0; i < values.Length; i++) { cancel(); values[i] = Local(spec.FitPoints[i]); }
        using var points = new Point3dCollection(values);
        // Installed PE: both managed 'order' arguments are passed unchanged to
        // OdDbSpline.setFitData's degree slot. No degree+1 conversion occurs.
        // The collection is caller-owned; construction copies its native data.
        var spline = spec.StartTangent is { } start && spec.EndTangent is { } end
            ? new Spline(points, LocalVector(start), LocalVector(end), FitSplineSpec408.NativeDegree, spec.FitTolerance)
            : new Spline(points, FitSplineSpec408.NativeDegree, spec.FitTolerance);
        try { VerifyCreated(spline, spec with { Workplane = null }, cancel); return spline; }
        catch { spline.Dispose(); throw; }
    }

    // Writer applies the ordinary creation frame once. Verify that resulting
    // curve directly in WCS; never transform a resident entity for checking.
    internal static void VerifyCreated(Spline spline, FitSplineSpec408 spec, Action cancel)
    {
        cancel(); Require(spline.GetType() == typeof(Spline) && !spline.IsNull, "Creation did not return a complete native Spline.");
        var shape = CadSplineReader.ReadReported(spline, cancel);
        var fit = shape.native_inspection.fit_data;
        // Stored fit construction metadata is optional native observation.
        // The actual finite curve, requested points/endpoints and derivative
        // directions remain mandatory even if the SDK returns NURBS only.
        Require(shape.degree == FitSplineSpec408.NativeDegree && !shape.closed && !shape.periodic && shape.planar,
            "Creation did not retain the requested open planar cubic fit construction.");
        Require(shape.control_points.Length >= FitSplineSpec408.NativeDegree + 1 &&
            shape.knots.Length == (long)shape.control_points.Length + FitSplineSpec408.NativeDegree + 1 &&
            shape.end_parameter > shape.start_parameter && shape.knots.Zip(shape.knots.Skip(1), (a, b) => a <= b).All(x => x) &&
            (shape.rational ? shape.weights.Length == shape.control_points.Length && shape.weights.All(w => w > 0) :
                shape.weights.Length == 0 || shape.weights.Length == shape.control_points.Length && shape.weights.All(w => w > 0)),
            "Native fitted curve has an incomplete or inconsistent finite NURBS definition.");
        if (fit != null)
            Require(fit.degree == FitSplineSpec408.NativeDegree && fit.points.Length == spec.FitPoints.Length,
                "Available native fit definition changed its degree or ordered fit-point count.");
        var plane = spec.Workplane ?? DrawingWorkplane.XY;
        var normal = DrawingWorkplane.Normal(plane);
        var origin = new Point3d(plane.Origin.X, plane.Origin.Y, plane.Origin.Z);
        var planeNormal = new Vector3d(normal.X, normal.Y, normal.Z);
        Point3d World(Point2 value)
        {
            var p = DrawingWorkplane.World(plane, value); return new(p.X, p.Y, p.Z);
        }
        Vector3d WorldVector(Point2 value) => new(
            plane.XAxis.X * value.X + plane.YAxis.X * value.Y,
            plane.XAxis.Y * value.X + plane.YAxis.Y * value.Y,
            plane.XAxis.Z * value.X + plane.YAxis.Z * value.Y);
        static Point3d Point(double[] p) => new(p[0], p[1], p[2]);
        // Absolute native geometry resolution plus coordinate roundoff. This is
        // not a percentage/mass/bbox oracle or a fit-algorithm identity check.
        double precision = 1e-7;
        foreach (var p in shape.control_points)
        {
            cancel(); precision = Math.Max(precision, 32 * 2.2204460492503131e-16 * p.Max(Math.Abs));
        }
        foreach (var p in shape.control_points)
        {
            cancel(); Require(Math.Abs((Point(p) - origin).DotProduct(planeNormal)) <= precision,
                "Native fitted curve left the explicitly requested workplane; no projection is performed.");
        }
        double allowedDeviation = spec.FitTolerance + precision;
        for (int i = 0; i < spec.FitPoints.Length; i++)
        {
            cancel(); var expected = World(spec.FitPoints[i]);
            if (fit != null)
                Require((Point(fit.points[i]) - expected).Length <= precision,
                    "Available native fit definition changed a requested fit point or its order.");
            var nearest = spline.GetClosestPointTo(expected, false); cancel();
            Require(double.IsFinite(nearest.X) && double.IsFinite(nearest.Y) && double.IsFinite(nearest.Z) &&
                (nearest - expected).Length <= allowedDeviation,
                "Actual native curve misses a requested fit point beyond its specified fit tolerance.");
        }
        Require((spline.StartPoint - World(spec.FitPoints[0])).Length <= allowedDeviation &&
            (spline.EndPoint - World(spec.FitPoints[^1])).Length <= allowedDeviation,
            "Native curve endpoints differ from the requested ordered fit endpoints.");

        if (spec.StartTangent is { } start && spec.EndTangent is { } end)
        {
            void Direction(Vector3d actual, Vector3d wanted, string observation)
            {
                cancel();
                object DiagnosticNumber(double value) => double.IsFinite(value) ? value : value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                var details = new { observation, actual = new[] { DiagnosticNumber(actual.X), DiagnosticNumber(actual.Y), DiagnosticNumber(actual.Z) }, requested = new[] { wanted.X, wanted.Y, wanted.Z } };
                if (!double.IsFinite(actual.X) || !double.IsFinite(actual.Y) || !double.IsFinite(actual.Z) ||
                    !double.IsFinite(actual.Length) || actual.Length <= 0)
                    throw new DomainException("NATIVE_FIT_SPLINE_MISMATCH", observation + ": Native endpoint has no finite nonzero tangent.", false, details);
                if ((actual.GetNormal() - wanted.GetNormal()).Length > 1e-7)
                    throw new DomainException("NATIVE_FIT_SPLINE_MISMATCH", observation + ": Actual native endpoint tangent does not follow the requested forward direction.", false, details);
            }
            // Actual curve directions are required independently of optional
            // stored fit-condition getters. The broad native reader retains
            // failures of those getters without inventing replacement data.
            Direction(spline.GetFirstDerivative(spline.StartParam), WorldVector(start), "actual_start_derivative");
            Direction(spline.GetFirstDerivative(spline.EndParam), WorldVector(end), "actual_end_derivative");
            if (fit is { tangents_exist: true })
            {
                if (fit.start_tangent is { } storedStart && new Vector3d(storedStart[0], storedStart[1], storedStart[2]).Length > 0)
                    Direction(new(storedStart[0], storedStart[1], storedStart[2]), WorldVector(start), "native_start_fit_tangent");
                if (fit.end_tangent is { } storedEnd && new Vector3d(storedEnd[0], storedEnd[1], storedEnd[2]).Length > 0)
                    Direction(new(storedEnd[0], storedEnd[1], storedEnd[2]), WorldVector(end), "native_end_fit_tangent");
            }
        }
        cancel();
    }
}
