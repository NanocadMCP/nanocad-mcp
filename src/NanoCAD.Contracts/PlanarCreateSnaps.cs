using System.Text.Json;
using System.Text.Json.Serialization;

namespace NanoCAD.Contracts;

public sealed record PlanarPointInput(Point2? Point, NativeCurveSnap? Snap)
{
    public static PlanarPointInput Parse(JsonElement item, string field, string path)
    {
        bool literal = item.TryGetProperty(field, out _);
        bool snapped = item.TryGetProperty(field + "_snap", out var snap);
        if (literal == snapped)
            throw InputError.Create(path + "." + field, "Supply exactly one coordinate or " + field + "_snap.");
        return literal
            ? new(CreateValidation.PlanarLiteralPoint(item, field, path), null)
            : new(null, NativeCurveSnap.Parse(snap, path + "." + field + "_snap"));
    }
}

public interface INativeSnappedCreation
{
    EntitySpec Materialize(Func<PlanarPointInput, Point2> resolve);
}

public sealed record PlanarVertexInput(PlanarPointInput Position, double Bulge);

public sealed record PolylineFromNativeSnapsSpec(PlanarVertexInput[] Vertices, bool Closed, string LayerName)
    : EntitySpec("Polyline", LayerName), INativeSnappedCreation
{
    [JsonIgnore]
    public string SourcePath { get; init; } = "$.entities[0]";

    public EntitySpec Materialize(Func<PlanarPointInput, Point2> resolve)
    {
        var vertices = Vertices.Select(vertex => new Vertex2(resolve(vertex.Position), vertex.Bulge)).ToArray();
        return CreateValidation.CheckedPolyline(vertices, Closed, Layer, SourcePath) with { Workplane = Workplane };
    }
}

public sealed record CircleFromNativeSnapSpec(PlanarPointInput Center, double Radius, string LayerName)
    : EntitySpec("Circle", LayerName), INativeSnappedCreation
{
    [JsonIgnore]
    public string SourcePath { get; init; } = "$.entities[0]";

    public EntitySpec Materialize(Func<PlanarPointInput, Point2> resolve)
    {
        var center = resolve(Center);
        return CreateValidation.ParsePlanarCircle(Wire.Element(new
        {
            type = "Circle",
            layer = Layer,
            center = new[] { center.X, center.Y },
            radius = Radius
        }), SourcePath, Layer, allowSnaps: false) with
        { Workplane = Workplane };
    }
}

public sealed record ArcFromNativeSnapSpec(PlanarPointInput Center, double Radius, double StartAngle,
    double SweepAngle, string LayerName) : EntitySpec("Arc", LayerName), INativeSnappedCreation
{
    [JsonIgnore]
    public string SourcePath { get; init; } = "$.entities[0]";

    public EntitySpec Materialize(Func<PlanarPointInput, Point2> resolve)
    {
        var center = resolve(Center);
        return CreateValidation.ParsePlanarArc(Wire.Element(new
        {
            type = "Arc",
            layer = Layer,
            center = new[] { center.X, center.Y },
            radius = Radius,
            start_angle = StartAngle,
            sweep_angle = SweepAngle
        }), SourcePath, Layer, allowSnaps: false) with
        { Workplane = Workplane };
    }
}

public static partial class CreateValidation
{
    private static EntitySpec ParsePlanarPolyline(JsonElement item, string path, string layer, bool allowSnaps)
    {
        Shape(item, path, "type", "layer", "vertices", "closed");
        bool closed = Boolean(item, "closed", path, false);
        var array = Required(item, "vertices", path);
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() < 2)
            throw Invalid(path + ".vertices", "A polyline requires at least two vertices.", "INVALID_GEOMETRY");
        var vertices = new List<PlanarVertexInput>(array.GetArrayLength());
        bool snapped = false;
        foreach (var vertex in array.EnumerateArray())
        {
            string at = $"{path}.vertices[{vertices.Count}]";
            if (allowSnaps) Shape(vertex, at, "point", "point_snap", "bulge");
            else Shape(vertex, at, "point", "bulge");
            var point = PlanarPointInput.Parse(vertex, "point", at);
            double bulge = vertex.TryGetProperty("bulge", out var b) ? Number(b, at + ".bulge", -1000, 1000) : 0;
            vertices.Add(new(point, bulge));
            snapped |= point.Snap != null;
        }
        if (!closed && vertices[^1].Bulge != 0)
            throw Invalid(path, "The last bulge of an open polyline must be zero.", "INVALID_GEOMETRY");
        if (closed && vertices.Count == 2 && vertices[0].Bulge == -vertices[1].Bulge)
            throw Invalid(path, "The two closing segments retrace the same locus.", "INVALID_GEOMETRY");
        if (snapped) return new PolylineFromNativeSnapsSpec(vertices.ToArray(), closed, layer) { SourcePath = path };
        return CheckedPolyline(vertices.Select(v => new Vertex2(v.Position.Point!, v.Bulge)).ToArray(), closed, layer, path);
    }

    internal static PolylineSpec CheckedPolyline(Vertex2[] vertices, bool closed, string layer, string path)
    {
        foreach (var vertex in vertices)
            if (!double.IsFinite(vertex.Point.X) || !double.IsFinite(vertex.Point.Y) ||
                Math.Abs(vertex.Point.X) > Maximum || Math.Abs(vertex.Point.Y) > Maximum)
                throw Invalid(path + ".vertices", "Resolved polyline vertex exceeds the supported drawing range.", "INVALID_GEOMETRY");
        for (int i = 1; i < vertices.Length; i++)
            if (Coincident(vertices[i - 1].Point, vertices[i].Point))
                throw Invalid($"{path}.vertices[{i}]", "Zero-length segment.", "INVALID_GEOMETRY");
        if (closed && Coincident(vertices[0].Point, vertices[^1].Point))
            throw Invalid(path, "Closed polyline must not repeat its first point.", "INVALID_GEOMETRY");
        if (closed && vertices.All(vertex => vertex.Bulge == 0))
        {
            var origin = vertices[0].Point;
            var direction = vertices[1].Point;
            if (vertices.Skip(2).All(p => Math.Abs((direction.X - origin.X) * (p.Point.Y - origin.Y) -
                (direction.Y - origin.Y) * (p.Point.X - origin.X)) < Minimum * Minimum))
                throw Invalid(path, "Closed polyline is collinear.", "INVALID_GEOMETRY");
        }
        return new PolylineSpec(vertices, closed, layer);
    }

    internal static Point2 PlanarLiteralPoint(JsonElement item, string field, string path) =>
        Point(item, field, path);

    internal static EntitySpec ParsePlanarCircle(JsonElement item, string path, string layer, bool allowSnaps)
    {
        bool snapped = allowSnaps && item.TryGetProperty("center_snap", out _);
        Shape(item, path, snapped
            ? new[] { "type", "layer", "center", "center_snap", "radius" }
            : new[] { "type", "layer", "center", "radius" });
        double radius = Number(Required(item, "radius", path), path + ".radius", Minimum);
        return snapped
            ? new CircleFromNativeSnapSpec(PlanarPointInput.Parse(item, "center", path), radius, layer) { SourcePath = path }
            : new CircleSpec(Point(item, "center", path), radius, layer);
    }

    internal static EntitySpec ParsePlanarArc(JsonElement item, string path, string layer, bool allowSnaps)
    {
        bool snapped = allowSnaps && item.TryGetProperty("center_snap", out _);
        Shape(item, path, snapped
            ? new[] { "type", "layer", "center", "center_snap", "radius", "start_angle", "sweep_angle" }
            : new[] { "type", "layer", "center", "radius", "start_angle", "sweep_angle" });
        double angle = Number(Required(item, "start_angle", path), path + ".start_angle", 0, Math.PI * 2);
        double sweep = Number(Required(item, "sweep_angle", path), path + ".sweep_angle", Minimum, Math.PI * 2);
        if (angle >= Math.PI * 2 || sweep >= Math.PI * 2)
            throw Invalid(path, "Arc angles must be below 2π; use Circle for a full circle.", "INVALID_GEOMETRY");
        double radius = Number(Required(item, "radius", path), path + ".radius", Minimum);
        return snapped
            ? new ArcFromNativeSnapSpec(PlanarPointInput.Parse(item, "center", path), radius, angle, sweep, layer) { SourcePath = path }
            : new ArcSpec(Point(item, "center", path), radius, angle, sweep, layer);
    }
}
