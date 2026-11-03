using System.Text.Json;

namespace NanoCAD.Contracts;

// WCS authoring is a separate grammar: existing planar fit/control Spline
// requests retain their local XY and optional workplane meaning.
public sealed record SpatialFitSplineSpec441(Point3[] FitPoints, double FitTolerance,
    Point3? StartTangent, Point3? EndTangent, string LayerName) : EntitySpec("Spline", LayerName)
{
    public const int NativeDegree = 3;
    public string Construction => "fit_points_3d";
}

public sealed record Polyline3dSpec441(Point3[] Vertices, bool Closed, string LayerName) : EntitySpec("Polyline3d", LayerName);

public static partial class CreateValidation
{
    private static Point3 SpatialPoint441(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 3)
            throw Invalid(path, "Expected WCS XYZ triple in millimeters.", "INVALID_GEOMETRY");
        return new(Number(value[0], path + "[0]"), Number(value[1], path + "[1]"), Number(value[2], path + "[2]"));
    }

    private static bool SpatialCoincident441(Point3 first, Point3 second) =>
        Math.Abs(first.X - second.X) < Minimum && Math.Abs(first.Y - second.Y) < Minimum && Math.Abs(first.Z - second.Z) < Minimum;

    internal static SpatialFitSplineSpec441 ParseSpatialFitSpline441(JsonElement item, string path, string layer)
    {
        Shape(item, path, "type", "layer", "construction", "fit_points", "fit_tolerance", "start_tangent", "end_tangent");
        if (Text(item, "construction", path, 32) != "fit_points_3d")
            throw Invalid(path + ".construction", "Expected fit_points_3d construction.");
        var raw = Required(item, "fit_points", path);
        if (raw.ValueKind != JsonValueKind.Array || raw.GetArrayLength() < 2)
            throw Invalid(path + ".fit_points", "An open spatial fitted curve requires at least two ordered WCS XYZ fit points.", "INVALID_GEOMETRY");
        var points = new Point3[raw.GetArrayLength()];
        for (int index = 0; index < points.Length; index++)
        {
            string at = path + $".fit_points[{index}]";
            points[index] = SpatialPoint441(raw[index], at);
            if (index > 0 && SpatialCoincident441(points[index - 1], points[index]))
                throw Invalid(at, "Consecutive fit points must differ by at least the coordinate resolution 1e-9.", "INVALID_GEOMETRY");
        }
        if (SpatialCoincident441(points[0], points[^1]))
            throw Invalid(path + ".fit_points", "fit_points_3d creates an open native cubic Spline; first and last fit points must differ.", "INVALID_GEOMETRY");
        double tolerance = Number(Required(item, "fit_tolerance", path), path + ".fit_tolerance", 0);
        bool hasStart = item.TryGetProperty("start_tangent", out var rawStart), hasEnd = item.TryGetProperty("end_tangent", out var rawEnd);
        if (hasStart != hasEnd)
            throw Invalid(path, "Supply both endpoint tangents or omit both for native automatic tangents.");
        Point3? start = null, end = null;
        if (hasStart)
        {
            var startValue = SpatialPoint441(rawStart, path + ".start_tangent");
            var endValue = SpatialPoint441(rawEnd, path + ".end_tangent");
            foreach (var (name, vector) in new[] { ("start_tangent", startValue), ("end_tangent", endValue) })
                if (Math.Max(Math.Abs(vector.X), Math.Max(Math.Abs(vector.Y), Math.Abs(vector.Z))) < Minimum)
                    throw Invalid(path + "." + name, "Endpoint tangent must be a nonzero WCS vector with a component of at least 1e-9.", "INVALID_GEOMETRY");
            start = startValue; end = endValue;
        }
        return new(points, tolerance, start, end, layer);
    }

    internal static Polyline3dSpec441 ParsePolyline3d441(JsonElement item, string path, string layer)
    {
        Shape(item, path, "type", "layer", "vertices", "closed");
        bool closed = Boolean(item, "closed", path, false);
        var raw = Required(item, "vertices", path);
        if (raw.ValueKind != JsonValueKind.Array || raw.GetArrayLength() < (closed ? 3 : 2))
            throw Invalid(path + ".vertices", "Expected at least two WCS XYZ vertices, at least three for a closed Polyline3d.", "INVALID_GEOMETRY");
        var vertices = new Point3[raw.GetArrayLength()];
        for (int index = 0; index < vertices.Length; index++)
        {
            string at = path + $".vertices[{index}]";
            vertices[index] = SpatialPoint441(raw[index], at);
            if (index > 0 && SpatialCoincident441(vertices[index - 1], vertices[index]))
                throw Invalid(at, "Zero-length 3D segment.", "INVALID_GEOMETRY");
        }
        if (closed && SpatialCoincident441(vertices[0], vertices[^1]))
            throw Invalid(path + ".vertices", "Closed Polyline3d must not repeat its first vertex; closed adds the final segment.", "INVALID_GEOMETRY");
        return new(vertices, closed, layer);
    }
}
