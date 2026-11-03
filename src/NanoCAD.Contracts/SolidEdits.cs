using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record SolidEditTransform(string Kind, Point3? Displacement, Point3? Origin, Point3? Direction, double Angle, double Factor);
public sealed record TransformSolidSpec(string[] Targets, SolidEditTransform Transform) : EditSpec("transform_solid", Targets);

public static partial class ModifyValidation
{
    private static TransformSolidSpec ParseSolidEdit(JsonElement item, string path, string[] handles)
    {
        Shape(item, path, "operation", "handles", "transform");
        
        var value = Required(item, "transform", path); path += ".transform";
        if (value.ValueKind != JsonValueKind.Object) throw Invalid(path, "Expected transform object.");
        string kind = Text(value, "kind", path, 16);
        Point3 Triple(string field)
        {
            var point = Required(value, field, path);
            if (point.ValueKind != JsonValueKind.Array || point.GetArrayLength() != 3)
                throw Invalid(path + "." + field, "Expected WCS XYZ triple.", "INVALID_GEOMETRY");
            return new(Number(point[0], path + "." + field, -1e6, 1e6), Number(point[1], path + "." + field, -1e6, 1e6), Number(point[2], path + "." + field, -1e6, 1e6));
        }
        double Length(Point3 p) => Math.Sqrt(p.X * p.X + p.Y * p.Y + p.Z * p.Z);
        SolidEditTransform transform;
        if (kind == "translation")
        {
            Shape(value, path, "kind", "displacement");
            var delta = Triple("displacement");
            if (Length(delta) > 1e6) throw Invalid(path, "Translation exceeds 1000000 mm.", "INVALID_GEOMETRY");
            transform = new(kind, delta, null, null, 0, 1);
        }
        else if (kind == "scale")
        {
            Shape(value, path, "kind", "center", "factor");
            transform = new(kind, null, Triple("center"), null, 0, Number(Required(value, "factor", path), path + ".factor", 1e-6, 1e6));
        }
        else if (kind is "rotation" or "mirror")
        {
            string origin = kind == "rotation" ? "axis_origin" : "plane_origin";
            string direction = kind == "rotation" ? "axis_direction" : "plane_normal";
            if (kind == "rotation") Shape(value, path, "kind", origin, direction, "angle");
            else Shape(value, path, "kind", origin, direction);
            var vector = Triple(direction);
            if (Length(vector) < 1e-9) throw Invalid(path + "." + direction, "Direction must be nonzero.", "INVALID_GEOMETRY");
            transform = new(kind, null, Triple(origin), vector, kind == "rotation" ? Number(Required(value, "angle", path), path + ".angle", -2 * Math.PI, 2 * Math.PI) : 0, 1);
        }
        else throw Invalid(path + ".kind", "Use translation, rotation, mirror or positive uniform scale.");
        return new(handles, transform);
    }
}
