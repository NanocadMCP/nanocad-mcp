using System.Text.Json;
using System.Text;

namespace NanoCAD.Contracts;

public sealed record ModelBodyNameEdit(string[] Targets, string Name, string? ExpectedName)
    : EditSpec("name_model_body", Targets);

public sealed record ModelBodyNameGroup(string name, EntityRef @ref)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? description { get; init; }
}
public sealed record ModelBodyNameChange(EntityRef body_ref, string previous_name, string name,
    string body_name_source, string? native_tree_name, ModelBodyNameGroup? body_name_group, string? part_name = null);

public static class BodyNameGroupKey
{
    // The Object Grouping dialog accepts 31 characters, without spaces.
    public static string Available(string description, string handle, Func<string, bool> occupied)
    {
        var text = new StringBuilder();
        foreach (var c in description)
        {
            if (text.Length == 31) break;
            if (char.IsLetterOrDigit(c) || c is '$' or '-' or '_')
                text.Append(char.ToUpperInvariant(c));
            else if (text.Length > 0 && text[^1] != '_') text.Append('_');
        }
        string stem = text.ToString().Trim('_');
        if (stem.Length == 0) stem = "BODY_" + handle;
        string key = stem;
        for (long index = 2; occupied(key); index++)
        {
            string suffix = "_" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            key = stem[..Math.Min(stem.Length, 31 - suffix.Length)] + suffix;
        }
        return key;
    }
}

public static partial class ModifyValidation
{
    private static ModelBodyNameEdit ParseModelBodyName(JsonElement item, string path, string[] handles)
    {
        Shape(item, path, "operation", "handles", "name", "expected_name");
        if (handles.Length != 1)
            throw Invalid(path + ".handles", "Select one solid handle for each body name.");
        string name = Text(item, "name", path);
        if (string.IsNullOrWhiteSpace(name))
            throw Invalid(path + ".name", "A body display name needs visible text.");
        string? expected = null;
        if (item.TryGetProperty("expected_name", out var previous))
            expected = previous.ValueKind == JsonValueKind.String && previous.ValueEquals("") ? "" : Text(item, "expected_name", path);
        return new(handles, name, expected);
    }

    private static void CheckModelBodyNames(List<EditSpec> edits)
    {
        if (!edits.Any(edit => edit is ModelBodyNameEdit)) return;
        if (edits.Any(edit => edit is not ModelBodyNameEdit))
            throw Invalid("$.operations", "Group body name changes in a separate request.");
        if (edits.Select(edit => edit.Handles[0]).Distinct(StringComparer.Ordinal).Count() != edits.Count)
            throw Invalid("$.operations", "Each body handle may occur only once in a naming request.");
    }
}
