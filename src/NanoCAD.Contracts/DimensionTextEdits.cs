using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record ReplaceDimensionTextSpec(string[] Targets, string ExpectedText, string Text) : EditSpec("replace_dimension_text", Targets);
public static class DimensionTextContent
{
    public const int CreatedOverrideLength = 4096;
    public const int EditLength = 256;
    public static bool IsPlain(string text, int maxLength = EditLength)
    {
        if (text.Length > maxLength || text.Any(char.IsControl) || text.IndexOfAny(new[] { '\\', '{', '}', '%' }) >= 0) return false;
        var literal = text.Replace("<>", "", StringComparison.Ordinal);
        return literal.IndexOfAny(new[] { '<', '>' }) < 0 && text.Length - literal.Length <= 2;
    }
    public static string DisplayMode(string text, bool hasFields) => hasFields || !IsPlain(text, CreatedOverrideLength) ? "field_or_formatted"
        : text.Length == 0 || text == "<>" ? "automatic_measurement"
        : text.Contains("<>", StringComparison.Ordinal) ? "measurement_with_affixes" : "literal_override";
    public static bool PreservesMeasurement(string text) => IsPlain(text) && (text.Length == 0 || text.Contains("<>", StringComparison.Ordinal));
}
public static partial class ModifyValidation
{
    private static ReplaceDimensionTextSpec ParseDimensionText(JsonElement item, string path, string[] handles)
    {
        Shape(item, path, "operation", "handles", "expected_text", "text");
        if (handles.Length != 1) throw Invalid(path, "Dimension text replacement requires one handle.");
        string ReadText(string name, int limit)
        {
            var value = Required(item, name, path);
            if (value.ValueKind != JsonValueKind.String || !DimensionTextContent.IsPlain(value.GetString()!, limit))
                throw Invalid(path + "." + name, $"Use at most {limit} plain characters with at most one <> measurement token and no fields, formatting or controls.");
            return value.GetString()!;
        }
        var expected = ReadText("expected_text", DimensionTextContent.CreatedOverrideLength); var text = ReadText("text", DimensionTextContent.EditLength);
        if (!DimensionTextContent.PreservesMeasurement(text)) throw Invalid(path + ".text", "Use an empty string for the native value or one <> token with a plain prefix/suffix.");
        return new(handles, expected, text);
    }
}
