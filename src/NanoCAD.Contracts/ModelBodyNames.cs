using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record ModelBodyNameEdit(string[] Targets, string Name, string? ExpectedName)
    : EditSpec("name_model_body", Targets);

public sealed record ModelBodyNameChange(EntityRef body_ref, string previous_name, string name);

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
