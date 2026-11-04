using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record PointSpec408(Point3 Position, string LayerName) : EntitySpec("Point", LayerName);
public sealed record PointFromNativeSnapSpec(NativeCurveSnap PositionSnap, string LayerName) : EntitySpec("Point", LayerName);

public static class PointCoordinates408
{
    public static Point3 Parse(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 3 ||
            value.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.Number || !item.TryGetDouble(out var number) || !double.IsFinite(number)))
            throw new DomainException("INVALID_ARGUMENT", path + ": Expected three finite coordinates in millimeters.", false, new { path });
        return new(value[0].GetDouble(), value[1].GetDouble(), value[2].GetDouble());
    }

    public static Point3 World(Point3 position, SketchPlaneSpec? frame)
    {
        if (frame == null) return position;
        var normal = DrawingWorkplane.Normal(frame);
        var world = new Point3(frame.Origin.X + position.X * frame.XAxis.X + position.Y * frame.YAxis.X + position.Z * normal.X,
            frame.Origin.Y + position.X * frame.XAxis.Y + position.Y * frame.YAxis.Y + position.Z * normal.Y,
            frame.Origin.Z + position.X * frame.XAxis.Z + position.Y * frame.YAxis.Z + position.Z * normal.Z);
        if (!new[] { world.X, world.Y, world.Z }.All(double.IsFinite))
            throw new DomainException("INVALID_GEOMETRY", "Point coordinates transformed to WCS must remain finite.");
        return world;
    }
}

public static partial class CreateValidation
{
    internal static EntitySpec ParsePoint408(JsonElement item, string path, string layer)
    {
        Shape(item, path, "type", "layer", "position", "position_snap");
        if (item.TryGetProperty("position_snap", out var snap))
        {
            if (item.TryGetProperty("position", out _)) throw Invalid(path, "Choose one position or position_snap.");
            return new PointFromNativeSnapSpec(NativeCurveSnap.Parse(snap, path + ".position_snap"), layer);
        }
        return new PointSpec408(PointCoordinates408.Parse(Required(item, "position", path), path + ".position"), layer);
    }
}
