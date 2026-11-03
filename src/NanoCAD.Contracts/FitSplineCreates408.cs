using System.Text.Json;

namespace NanoCAD.Contracts;

// A separate construction grammar preserves the existing control-point Spline
// request. Fit points are points on the requested curve, not NURBS controls.
public sealed record FitSplineSpec408(Point2[] FitPoints, double FitTolerance,
    Point2? StartTangent, Point2? EndTangent, string LayerName) : EntitySpec("Spline", LayerName)
{
    public const int NativeDegree = 3;
    public string Construction => "fit_points";
}

public static partial class CreateValidation
{
    internal static FitSplineSpec408 ParseFitSpline408(JsonElement item, string path, string layer)
    {
        Shape(item, path, "type", "layer", "construction", "fit_points", "fit_tolerance", "start_tangent", "end_tangent");
        if (Text(item, "construction", path, 16) != "fit_points")
            throw Invalid(path + ".construction", "Expected fit_points construction.");
        var raw = Required(item, "fit_points", path);
        if (raw.ValueKind != JsonValueKind.Array || raw.GetArrayLength() < 2)
            throw Invalid(path + ".fit_points", "An open fitted curve requires at least two ordered local XY fit points.", "INVALID_GEOMETRY");
        var points = new Point2[raw.GetArrayLength()];
        for (int i = 0; i < points.Length; i++)
        {
            var value = raw[i]; string at = path + $".fit_points[{i}]";
            if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 2)
                throw Invalid(at, "Expected local XY pair.", "INVALID_GEOMETRY");
            points[i] = new(Number(value[0], at + "[0]"), Number(value[1], at + "[1]"));
            if (i > 0 && points[i] == points[i - 1])
                throw Invalid(at, "Consecutive fit points must differ.", "INVALID_GEOMETRY");
        }
        if (points[0] == points[^1])
            throw Invalid(path + ".fit_points", "This construction creates an open native cubic Spline; first and last fit points must differ.", "INVALID_GEOMETRY");
        double tolerance = Number(Required(item, "fit_tolerance", path), path + ".fit_tolerance", 0);
        bool hasStart = item.TryGetProperty("start_tangent", out _), hasEnd = item.TryGetProperty("end_tangent", out _);
        if (hasStart != hasEnd)
            throw Invalid(path, "Supply both endpoint tangents or omit both for native automatic tangents.");
        Point2? start = null, end = null;
        if (hasStart)
        {
            var startValue = Point(item, "start_tangent", path); var endValue = Point(item, "end_tangent", path);
            foreach (var (name, tangent) in new[] { ("start_tangent", startValue), ("end_tangent", endValue) })
                if (Math.Max(Math.Abs(tangent.X), Math.Abs(tangent.Y)) < Minimum)
                    throw Invalid(path + "." + name, "Endpoint tangent must be a nonzero local vector with a component of at least 1e-9.", "INVALID_GEOMETRY");
            start = startValue; end = endValue;
        }
        return new(points, tolerance, start, end, layer);
    }
}
