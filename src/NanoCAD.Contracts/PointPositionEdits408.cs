using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record PointPositionEdit408(string[] Targets, Point3 ExpectedPosition, Point3 Position,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] SketchPlaneSpec? Workplane = null)
    : EditSpec("set_point_position", Targets);

public static partial class ModifyValidation
{
    private static PointPositionEdit408 ParsePointPosition408(JsonElement item, string path, string[] handles)
    {
        Shape(item, path, "operation", "handles", "expected_position", "position", "workplane");
        if (handles.Length != 1)
            throw Invalid(path + ".handles", "Use one Point per position edit; batch multiple edits for distinct points.");
        var expected = PointCoordinates408.Parse(Required(item, "expected_position", path), path + ".expected_position");
        var position = PointCoordinates408.Parse(Required(item, "position", path), path + ".position");
        var frame = item.TryGetProperty("workplane", out var workplane) ? DrawingWorkplane.Parse(workplane) : null;
        _ = PointCoordinates408.World(expected, frame); _ = PointCoordinates408.World(position, frame);
        return new(handles, expected, position, frame);
    }
}
