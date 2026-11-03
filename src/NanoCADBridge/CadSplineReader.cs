using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CadSplineReader
{
    // Observation must not inherit the narrower legacy editor's creation scope.
    // The established native reader reports fitted/periodic NURBS and fit data,
    // with finite/native-unavailable observations and actual byte budgets.
    internal static NativeSplineGeometry342 ReadReported(Spline spline, Action check) => CadNativeCurveInspection342.ReadSpline(spline, check);
    internal static SplineGeometry Read(Spline spline, Action check)
    {
        check();
        if (spline.GetType() != typeof(Spline) || spline.IsNull)
            throw new DomainException("UNSUPPORTED_SPLINE", "Only a non-null exact native Spline is supported.");
        int degree = spline.Degree, count = spline.NumControlPoints;
        bool fit = spline.HasFitData, rational = spline.IsRational, closed = spline.Closed;
        SplineVerification.CheckScope(degree, count, fit, false, closed);
        check();
        var native = spline.NurbsData;
        // NurbsData.init allocates this native collection; its getter returns the same backing field.
        using var points = native.GetControlPoints();
        SplineVerification.CheckScope(native.Degree, points.Count, fit, native.Periodic, native.Closed);
        var knots = native.GetKnots(); var weights = native.GetWeights(); // Managed DoubleCollections.
        if (degree != native.Degree || count != points.Count || rational != native.Rational || closed != native.Closed ||
            knots.Count != (long)count + degree + 1 || weights.Count > count)
            throw new DomainException("NATIVE_SPLINE_MISMATCH", "Native spline and NURBS data disagree.");
        static double[] Point(Point3d p) => new[] { p.X, p.Y, p.Z };
        var capacity=new SerializedCapacity357(cancel:check);var controls=new List<double[]>();
        for (int i = 0; i < count; i++) { check();var value=Point(points[i]);capacity.Add(value);controls.Add(value); }
        var knotValues=new List<double>();var weightValues=new List<double>();
        for (int i = 0; i < knots.Count; i++) { check();capacity.Add(knots[i]);knotValues.Add(knots[i]); }
        for (int i = 0; i < weights.Count; i++) { check();capacity.Add(weights[i]);weightValues.Add(weights[i]); }
        var result = new SplineGeometry(degree, rational, native.Periodic, closed, spline.IsPlanar, fit, controls.ToArray(), knotValues.ToArray(), weightValues.ToArray(),
            native.ControlPointTolerance, native.KnotTolerance, spline.StartParam, spline.EndParam, Point(spline.StartPoint), Point(spline.EndPoint));
        check(); SplineVerification.Verify(result); return result;
    }
    internal static double Length(Spline spline, SplineGeometry data, Action check)
    {
        double first = spline.GetDistanceAtParameter(data.start_parameter); check();
        double last = spline.GetDistanceAtParameter(data.end_parameter); check();
        return SplineVerification.LengthFromDistances(first, last);
    }
}
