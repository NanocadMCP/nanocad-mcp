using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record QueryBounds407(double[] Min, double[] Max, string Mode);
public sealed record QueryText407(string Value, string Match, bool CaseSensitive);
public sealed record QueryBlock407(string? Handle, string? Name, bool CaseSensitive);

// Filters describe the requested observation, not a write selection or an exact
// geometric-intersection predicate. They are all included in the cursor identity.
public static class TargetedRead407
{
    private static DomainException Invalid(string message) => new("INVALID_ARGUMENT", message);
    private static void Fields(JsonElement value, string label, params string[] allowed)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid(label + " must be an object.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in value.EnumerateObject())
            if (!seen.Add(item.Name) || !allowed.Contains(item.Name)) throw Invalid("Unknown or duplicate " + label + " field: " + item.Name + ".");
    }
    private static string? Text(JsonElement value, string name, bool required = false)
    {
        if (!value.TryGetProperty(name, out var item))
        {
            if (required) throw Invalid(name + " is required.");
            return null;
        }
        if (item.ValueKind != JsonValueKind.String || item.GetString() is not { Length: > 0 } result)
            throw Invalid(name + " must be a nonempty string.");
        return result;
    }
    private static bool Flag(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var item)) return false;
        if (item.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw Invalid(name + " must be a boolean.");
        return item.GetBoolean();
    }
    public static QueryBounds407? Bounds(JsonElement input)
    {
        if (!input.TryGetProperty("bounds", out var value)) return null;
        Fields(value, "bounds", "min", "max", "mode");
        double[] Point(string name)
        {
            if (!value.TryGetProperty(name, out var point) || point.ValueKind != JsonValueKind.Array || point.GetArrayLength() != 2)
                throw Invalid("bounds." + name + " must be [x,y] in WCS drawing units.");
            return point.EnumerateArray().Select(n => n.ValueKind == JsonValueKind.Number && n.TryGetDouble(out var result) && double.IsFinite(result)
                ? result : throw Invalid("bounds coordinates must be finite numbers.")).ToArray();
        }
        var min = Point("min"); var max = Point("max");
        if (min[0] > max[0] || min[1] > max[1]) throw Invalid("bounds.min must not exceed bounds.max.");
        var mode = Text(value, "mode") ?? "intersects";
        if (mode is not ("intersects" or "within")) throw Invalid("bounds.mode must be intersects or within.");
        return new(min, max, mode);
    }
    public static QueryText407? TextFilter(JsonElement input)
    {
        if (!input.TryGetProperty("text", out var value)) return null;
        Fields(value, "text", "value", "match", "case_sensitive");
        var match = Text(value, "match") ?? "contains";
        if (match is not ("contains" or "equals")) throw Invalid("text.match must be contains or equals.");
        return new(Text(value, "value", true)!, match, Flag(value, "case_sensitive"));
    }
    public static QueryBlock407? BlockFilter(JsonElement input)
    {
        if (!input.TryGetProperty("block_definition", out var value)) return null;
        Fields(value, "block_definition", "handle", "name", "case_sensitive");
        var handle = Text(value, "handle"); var name = Text(value, "name");
        if (handle == null && name == null) throw Invalid("block_definition requires handle or name.");
        return new(handle == null ? null : Validation.NormalizeHandle(handle), name, Flag(value, "case_sensitive"));
    }
    public static string[]? Sections(JsonElement input)
    {
        if (!input.TryGetProperty("sections", out var value)) return null;
        if (value.ValueKind != JsonValueKind.Array) throw Invalid("sections must be an array; [] requests identities/ownership only.");
        var sections = value.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String && item.GetString() is { } name &&
            name is "geometry" or "editing" or "appearance" ? name : throw Invalid("sections allows geometry, editing and appearance.")).ToArray();
        if (sections.Distinct(StringComparer.Ordinal).Count() != sections.Length) throw Invalid("Duplicate detail sections are not allowed.");
        return sections.OrderBy(name => name, StringComparer.Ordinal).ToArray();
    }
}
