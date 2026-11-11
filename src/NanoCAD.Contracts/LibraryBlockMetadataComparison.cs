using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record LibraryBlockObservedValue(string kind, int json_utf8_bytes, string? json_sha256, bool value_included, JsonElement? value);
public sealed record LibraryBlockMetadataDifference(string path, string reason, LibraryBlockObservedValue expected, LibraryBlockObservedValue actual);

// Error evidence only. Large metadata remains fully compared; the failure
// receipt contains one differing field and bounded values, not the payload.
public static class LibraryBlockMetadataComparison
{
    const int ValueByteBudget = 2048;
    static string Field(string path, string name) => path + "/" + name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
    static LibraryBlockObservedValue Observe(JsonElement? value)
    {
        if (value == null) return new("missing", 0, null, false, null);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value.Value, Wire.Json);
        bool included = bytes.Length <= ValueByteBudget;
        return new(value.Value.ValueKind.ToString(), bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)), included, included ? value.Value.Clone() : null);
    }
    public static LibraryBlockMetadataDifference? FirstDifference(JsonElement expected, JsonElement actual)
    {
        LibraryBlockMetadataDifference Difference(string path, string reason, JsonElement? left, JsonElement? right) => new(path, reason, Observe(left), Observe(right));
        LibraryBlockMetadataDifference? Visit(JsonElement left, JsonElement right, string path)
        {
            if (left.ValueKind != right.ValueKind) return Difference(path, "value_kind", left, right);
            if (left.ValueKind == JsonValueKind.Object)
            {
                foreach (var field in left.EnumerateObject())
                {
                    string child = Field(path, field.Name);
                    if (!right.TryGetProperty(field.Name, out var counterpart)) return Difference(child, "missing_actual_property", field.Value, null);
                    var difference = Visit(field.Value, counterpart, child); if (difference != null) return difference;
                }
                foreach (var field in right.EnumerateObject())
                    if (!left.TryGetProperty(field.Name, out _)) return Difference(Field(path, field.Name), "extra_actual_property", null, field.Value);
                return null;
            }
            if (left.ValueKind == JsonValueKind.Array)
            {
                int common = Math.Min(left.GetArrayLength(), right.GetArrayLength());
                for (int index = 0; index < common; index++)
                {
                    var difference = Visit(left[index], right[index], path + "/" + index.ToString(CultureInfo.InvariantCulture));
                    if (difference != null) return difference;
                }
                if (left.GetArrayLength() != right.GetArrayLength()) return Difference(path, "array_length", left, right);
                return null;
            }
            bool same = left.ValueKind == JsonValueKind.String
                ? string.Equals(left.GetString(), right.GetString(), StringComparison.Ordinal)
                : string.Equals(left.GetRawText(), right.GetRawText(), StringComparison.Ordinal);
            return same ? null : Difference(path, "value", left, right);
        }
        return Visit(expected, actual, "$");
    }
}
