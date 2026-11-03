namespace NanoCAD.Contracts;

public sealed record SplineGeometry(int degree, bool rational, bool periodic, bool closed, bool planar, bool has_fit_data,
    double[][] control_points, double[] knots, double[] weights, double control_point_tolerance, double knot_tolerance,
    double start_parameter, double end_parameter, double[] start_point, double[] end_point);

public static class SplineVerification
{
    private static DomainException Invalid() => new("NATIVE_SPLINE_MISMATCH", "Native spline controls, knots, weights, domain or endpoints are inconsistent or outside finite bounds.");
    private static bool Finite(double x) => double.IsFinite(x) && Math.Abs(x) <= 1e9;
    private static bool Point(double[] p) => p.Length == 3 && p.All(Finite);
    private static bool Near(double a, double b) => Math.Abs(a - b) <= 1e-7;
    private static bool Same(double[] a, double[] b) => a.Zip(b, Near).All(x => x);
    public static void CheckScope(int degree, int count, bool fit, bool periodic, bool closed = false)
    {
        if (degree is < 1 or > 5 || count < degree + 1 || fit || periodic && !closed)
            throw new DomainException("UNSUPPORTED_SPLINE", "Spline detail supports degree1–5, complete finite controls, no fit data; closed curves require a clamped representation and matching endpoints.");
    }
    public static void Verify(SplineGeometry s)
    {
        SerializedCapacity357.Check(s);
        int count = s.control_points.Length;
        CheckScope(s.degree, count, s.has_fit_data, s.periodic, s.closed);
        if (s.knots.Length != count + s.degree + 1 || !s.control_points.All(Point) || !s.knots.All(Finite) ||
            !Point(s.start_point) || !Point(s.end_point) || !Finite(s.start_parameter) || !Finite(s.end_parameter) ||
            s.end_parameter <= s.start_parameter || !Near(s.start_parameter, s.knots[s.degree]) || !Near(s.end_parameter, s.knots[count]) ||
            !Finite(s.control_point_tolerance) || s.control_point_tolerance < 0 || !Finite(s.knot_tolerance) || s.knot_tolerance < 0)
            throw Invalid();
        for (int i = 1; i < s.knots.Length; i++) if (s.knots[i] < s.knots[i - 1]) throw Invalid();
        if (s.rational ? s.weights.Length != count : s.weights.Length != 0 && s.weights.Length != count) throw Invalid();
        if (s.weights.Any(w => !Finite(w) || w <= 0 || !s.rational && w != 1)) throw Invalid();
        if (s.knots.Take(s.degree + 1).All(k => k == s.start_parameter) && !Same(s.start_point, s.control_points[0])) throw Invalid();
        if (s.knots.Skip(count).All(k => k == s.end_parameter) && !Same(s.end_point, s.control_points[^1])) throw Invalid();
        if (s.closed && (!Same(s.start_point, s.end_point)||!s.knots.Take(s.degree+1).All(k=>k==s.start_parameter)||!s.knots.Skip(count).All(k=>k==s.end_parameter))) throw Invalid();
    }
    public static double LengthFromDistances(double first, double last)
    {
        double length = last - first;
        if (!double.IsFinite(first) || !double.IsFinite(last) || !double.IsFinite(length) || first < 0 || length <= 0) throw Invalid();
        return length;
    }
    public static void RequirePairScope(SplineGeometry spline,string mode="distance")
    {
        if(spline.closed)
        {
            if(mode is not ("intersections" or "relations") && !(mode=="distance"&&LinearSplineMetrics.TryCalculate(spline)!=null))throw new DomainException("UNSUPPORTED_SPLINE","Closed spline distance requires exact clamped degree1 segments; smooth closed distance remains excluded.");
            if(!LiesInWcsXy(spline))throw new DomainException("PLANAR_GEOMETRY_REQUIRED","Closed spline paired analysis requires every control point in WCS XY/Z=0.");
        }
        else if(spline.periodic)throw new DomainException("UNSUPPORTED_SPLINE","Open periodic spline paired analysis is not supported.");
    }
    public static bool LiesInWcsXy(SplineGeometry spline) => spline.control_points.All(p => p[2] == 0);
}
