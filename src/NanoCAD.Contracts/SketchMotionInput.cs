using System.Text.Json;

namespace NanoCAD.Contracts;

internal static class SketchMotionInput
{
    internal static bool RequireProof(JsonElement item)
    {
        if (!item.TryGetProperty("require_verified_constraints", out var flag)) return false;
        return flag.ValueKind is JsonValueKind.True or JsonValueKind.False ? flag.GetBoolean()
            : throw new DomainException("INVALID_ARGUMENT", "require_verified_constraints must be boolean.");
    }
    internal static string NativeId(string text) =>
        Guid.TryParseExact(text, "B", out var id) && id != Guid.Empty
            ? id.ToString("B").ToUpperInvariant()
            : throw new DomainException("INVALID_ARGUMENT", "Use the actual native member ID for a protected motion.");

    internal static SketchMobilityMotion[]? Read(JsonElement? input, Func<string, string> identity)
    {
        if (input is null) return null;
        static DomainException Invalid() => new("INVALID_ARGUMENT",
            "Each protected motion needs a distinct name, distinct selected object_ids and a nonzero finite in-plane direction (dx, dy).");
        if (input.Value.ValueKind != JsonValueKind.Array) throw Invalid();
        var result = new List<SketchMobilityMotion>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in input.Value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) throw Invalid();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in item.EnumerateObject())
                if (!keys.Add(field.Name) || field.Name is not ("name" or "object_ids" or "dx" or "dy")) throw Invalid();
            if (!item.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(name.GetString()) || !names.Add(name.GetString()!) ||
                !item.TryGetProperty("object_ids", out var ids) || ids.ValueKind != JsonValueKind.Array || ids.GetArrayLength() == 0)
                throw Invalid();
            var members = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in ids.EnumerateArray())
                if (entry.ValueKind != JsonValueKind.String || !members.Add(identity(entry.GetString()!))) throw Invalid();
            double Direction(string key) =>
                item.TryGetProperty(key, out var number) && number.ValueKind == JsonValueKind.Number &&
                number.TryGetDouble(out double value) && double.IsFinite(value) ? value : throw Invalid();
            double dx = Direction("dx"), dy = Direction("dy");
            double scale = Math.Max(Math.Abs(dx), Math.Abs(dy));
            if (scale == 0) throw Invalid();
            result.Add(new(name.GetString()!, members.ToArray(), dx / scale, dy / scale));
        }
        return result.ToArray();
    }

    internal static SketchMobilityMotion[]? Drawing(JsonElement? input, string[] handles)
    {
        var selected = handles.ToHashSet(StringComparer.Ordinal);
        return Read(input, text =>
        {
            var id = Validation.NormalizeHandle(text);
            return selected.Contains(id) ? id : throw new DomainException("INVALID_ARGUMENT", "Protected drawing motion uses a handle outside the selection.");
        });
    }
}
