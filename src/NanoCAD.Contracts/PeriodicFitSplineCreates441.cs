using System.Text.Json;

namespace NanoCAD.Contracts;

// Separate grammar leaves the existing open fit_points and explicit NURBS
// requests unchanged. The SDK fits these points and closes the curve with the native smooth-closure operation;
// they are not control vertices and the caller does not repeat the seam point.
public sealed record PeriodicFitSplineSpec441(Point2[] FitPoints, double FitTolerance,
    string LayerName) : EntitySpec("Spline", LayerName)
{
    public const int NativeDegree = 3;
    public string Construction => "closed_fit_points";
}

public static partial class CreateValidation
{
    internal static PeriodicFitSplineSpec441 ParsePeriodicFitSpline441(JsonElement item, string path, string layer)
    {
        Shape(item, path, "type", "layer", "construction", "fit_points", "fit_tolerance");
        if (Text(item, "construction", path, 32) != "closed_fit_points")
            throw Invalid(path + ".construction", "Expected closed_fit_points construction.");
        var raw = Required(item, "fit_points", path);
        if (raw.ValueKind != JsonValueKind.Array || raw.GetArrayLength() < 3)
            throw Invalid(path + ".fit_points", "A closed fitted curve requires at least three ordered local XY fit points.", "INVALID_GEOMETRY");
        var points = new Point2[raw.GetArrayLength()];
        var distinct = new HashSet<Point2>();
        for (int i = 0; i < points.Length; i++)
        {
            var value = raw[i]; string at = path + $".fit_points[{i}]";
            if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 2)
                throw Invalid(at, "Expected local XY pair.", "INVALID_GEOMETRY");
            points[i] = new(Number(value[0], at + "[0]"), Number(value[1], at + "[1]"));
            if (i > 0 && points[i] == points[i - 1])
                throw Invalid(at, "Consecutive fit points must differ.", "INVALID_GEOMETRY");
            distinct.Add(points[i]);
        }
        if (points[0] == points[^1])
            throw Invalid(path + ".fit_points", "Closed fit_points must not repeat the first point at the seam; native smooth closure adds the connecting span.", "INVALID_GEOMETRY");
        if (distinct.Count < 3)
            throw Invalid(path + ".fit_points", "A closed fitted curve requires at least three distinct fit points.", "INVALID_GEOMETRY");
        double tolerance = Number(Required(item, "fit_tolerance", path), path + ".fit_tolerance", 0);
        return new(points, tolerance, layer);
    }
}
