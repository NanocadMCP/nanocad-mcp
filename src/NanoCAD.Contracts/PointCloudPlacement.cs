using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record PointCloudPlacementSpec(string[] Targets, SolidEditTransform Transform)
    : EditSpec("transform_point_cloud", Targets);

public static partial class ModifyValidation
{
    private static PointCloudPlacementSpec ParsePointCloudPlacement(JsonElement item, string path, string[] handles)
    {
        Shape(item, path, "operation", "handles", "transform");
        var value = Required(item, "transform", path); path += ".transform";
        var kind = Text(value, "kind", path, 16);
        Point3 Triple(string field)
        {
            var point = Required(value, field, path);
            if (point.ValueKind != JsonValueKind.Array || point.GetArrayLength() != 3)
                throw Invalid(path + "." + field, "Expected WCS XYZ triple.");
            return new(Number(point[0], path + "." + field, -1e9, 1e9),
                Number(point[1], path + "." + field, -1e9, 1e9), Number(point[2], path + "." + field, -1e9, 1e9));
        }
        if (kind == "translation")
        {
            Shape(value, path, "kind", "displacement");
            return new(handles, new(kind, Triple("displacement"), null, null, 0, 1));
        }
        if (kind == "rotation")
        {
            Shape(value, path, "kind", "axis_origin", "axis_direction", "angle");
            var axis = Triple("axis_direction");
            if (Math.Sqrt(axis.X * axis.X + axis.Y * axis.Y + axis.Z * axis.Z) < 1e-9)
                throw Invalid(path + ".axis_direction", "Rotation axis must be nonzero.");
            return new(handles, new(kind, null, Triple("axis_origin"), axis,
                Number(Required(value, "angle", path), path + ".angle", -2 * Math.PI, 2 * Math.PI), 1));
        }
        throw Invalid(path + ".kind", "Use translation or rotation of the cloud insertion; cloud data is not edited.");
    }
}
