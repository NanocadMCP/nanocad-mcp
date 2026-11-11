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
