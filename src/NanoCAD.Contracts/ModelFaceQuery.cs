using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record ModelFaceQuery(string Surface, double[]? Direction, double AngleToleranceDegrees,
    double[]? DatumPoint, double? MaxDistance, double? MinRadius, double? MaxRadius, int StartIndex, int PageSize, string? AdjacentTo)
{
    public bool DirectBrep { get; init; }
    public string? CylinderRole { get; init; }

    public static ModelFaceQuery Parse(JsonElement input)
    {
        DomainException Invalid(string message) => new("INVALID_ARGUMENT", "face_query: " + message);
        if (input.ValueKind != JsonValueKind.Object) throw Invalid("Expected an object.");
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in input.EnumerateObject())
        {
            if (property.Name is not ("surface" or "direction" or "angle_tolerance_degrees" or "datum_point" or "max_distance" or "adjacent_to_face_id" or
                "min_radius" or "max_radius" or "start_index" or "page_size" or "identity_space" or "cylinder_role") || !fields.TryAdd(property.Name, property.Value))
                throw Invalid("Unknown or duplicate property " + property.Name + ".");
        }
        double Value(JsonElement v, string field)
        {
            if (v.ValueKind != JsonValueKind.Number || !v.TryGetDouble(out var n) || !double.IsFinite(n)) throw Invalid(field + " must be finite.");
            return n;
        }
        double? Nonnegative(string field)
        {
            if (!fields.TryGetValue(field, out var v)) return null;
            double n = Value(v, field);
            return n >= 0 ? n : throw Invalid(field + " must be nonnegative.");
        }
        double[]? Triple(string field)
        {
            if (!fields.TryGetValue(field, out var v)) return null;
            if (v.ValueKind != JsonValueKind.Array || v.GetArrayLength() != 3) throw Invalid(field + " requires three WCS values.");
            return v.EnumerateArray().Select(n => Value(n, field)).ToArray();
        }
        int Index(string field, int minimum, int fallback)
        {
            if (!fields.TryGetValue(field, out var v)) return fallback;
            if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var n) || n < minimum) throw Invalid("Invalid " + field + ".");
            return n;
        }
        if (!fields.TryGetValue("surface", out var type) || type.ValueKind != JsonValueKind.String ||
            type.GetString() is not ("plane" or "cylinder")) throw Invalid("Choose plane or cylinder.");
        string surface = type.GetString()!;
        var direction = Triple("direction");
        if (direction != null)
        {
            double scale = direction.Max(Math.Abs);
            if (scale == 0) throw Invalid("direction must be nonzero.");
            direction = direction.Select(v => v / scale).ToArray();
            double norm = Math.Sqrt(direction.Sum(v => v * v));
            direction = direction.Select(v => v / norm).ToArray();
        }
        var angle = Nonnegative("angle_tolerance_degrees");
        if (angle > 90 || angle.HasValue && direction == null) throw Invalid("Angle tolerance requires a direction and a value from 0 to 90.");
        var datum = Triple("datum_point");
        var distance = Nonnegative("max_distance");
        if ((datum == null) != (distance == null)) throw Invalid("Specify datum_point and max_distance together.");
        var min = Nonnegative("min_radius");
        var max = Nonnegative("max_radius");
        if ((min.HasValue || max.HasValue) && surface != "cylinder" || min > max) throw Invalid("A radius range must be ordered and applies to cylinders.");
        string? adjacent = null;
        if (fields.TryGetValue("adjacent_to_face_id", out var anchor))
        {
            if (anchor.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(anchor.GetString())) throw Invalid("Choose a current model_face_id for adjacency.");
            adjacent = anchor.GetString();
        }
        bool direct = false;
        if (fields.TryGetValue("identity_space", out var space))
        {
            if (space.ValueKind != JsonValueKind.String || space.GetString() is not ("model" or "brep"))
                throw Invalid("identity_space must be model or brep.");
            direct = space.GetString() == "brep";
        }
        string? role = null;
        if (fields.TryGetValue("cylinder_role", out var roleValue))
        {
            if (!direct || surface != "cylinder" || roleValue.ValueKind != JsonValueKind.String || roleValue.GetString() is not ("bore" or "external"))
                throw Invalid("cylinder_role requires a brep cylinder query and must be bore or external.");
            role = roleValue.GetString();
        }
        if (direct && adjacent != null && (!long.TryParse(adjacent, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var nativeId) || nativeId <= 0 ||
            nativeId.ToString(System.Globalization.CultureInfo.InvariantCulture) != adjacent))
            throw Invalid("A brep neighbor requires the current positive native face ID from the same body.");
        return new(surface, direction, angle ?? 0, datum, distance, min, max, Index("start_index", 0, 0), Index("page_size", 1, 30), adjacent)
        { DirectBrep = direct, CylinderRole = role };
    }

    // A plane's normal and a cylinder's axis describe undirected supporting geometry.
    public bool Accept(double[] origin, double[] axis, double? radius, out double? datumDistance)
    {
        datumDistance = null;
        if (Surface == "cylinder" && (radius == null || !double.IsFinite(radius.Value) || radius <= 0 || radius < MinRadius || radius > MaxRadius)) return false;
        double norm = Math.Sqrt(axis.Sum(x => x * x));
        if (!double.IsFinite(norm) || norm == 0) return false;
        var unit = axis.Select(x => x / norm).ToArray();
        if (Direction != null && Math.Abs(Direction.Zip(unit, (x, y) => x * y).Sum()) + 1e-14 <
            Math.Cos(AngleToleranceDegrees * Math.PI / 180)) return false;
        if (DatumPoint == null) return true;
        var delta = DatumPoint.Zip(origin, (a, b) => a - b).ToArray();
        var axial = delta.Zip(unit, (a, b) => a * b).Sum();
        datumDistance = Surface == "plane" ? Math.Abs(axial) :
            Math.Sqrt(delta.Select((a, i) => a - axial * unit[i]).Sum(a => a * a));
        return datumDistance <= MaxDistance;
    }
}
