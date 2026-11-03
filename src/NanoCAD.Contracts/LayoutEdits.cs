using System.Text.Json;
using static NanoCAD.Contracts.StructureWriteValidation;

namespace NanoCAD.Contracts;

public sealed record RenameLayoutEdit(string Handle, string ExpectedName, string Name) : StructureEdit("rename_layout");
public sealed record LayoutChanges(int TabOrder);
public sealed record SetLayoutPropertiesEdit(string Handle, string ExpectedName, LayoutChanges Properties) : StructureEdit("set_layout_properties");

public static class LayoutEditValidation
{
    public static StructureEdit Parse(JsonElement input, string path)
    {
        string operation = Text(input, "operation", path, 32);
        Shape(input, path, "operation", "handle", "expected_name", operation == "rename_layout" ? "name" : "properties");
        string handle = Validation.NormalizeHandle(Text(input, "handle", path, 16));
        string expected = Text(input, "expected_name", path, 255);
        if (operation == "rename_layout")
        {
            string name = Text(input, "name", path, 255);
            if (!BlockWriteValidation.OrdinaryName(name) || string.Equals(name, "Model", StringComparison.OrdinalIgnoreCase)) throw Invalid(path + ".name", "Expected an ordinary paper layout name; Model is reserved.");
            if (string.Equals(expected, name, StringComparison.OrdinalIgnoreCase)) throw Invalid(path + ".name", "Specify a different layout name; case-only/no-op rename is not supported.");
            return new RenameLayoutEdit(handle, expected, name);
        }
        var properties = Required(input, "properties", path);
        Shape(properties, path + ".properties", "tab_order");
        var number = Required(properties, "tab_order", path + ".properties");
        if (number.ValueKind != JsonValueKind.Number || !number.TryGetInt32(out int order) || order < 1) throw Invalid(path, "tab_order requires a positive integer paper-tab position.");
        return new SetLayoutPropertiesEdit(handle, expected, new(order));
    }
    public static void Batch(IReadOnlyCollection<StructureEdit> edits)
    {
        if (edits.Any(e => e is RenameLayoutEdit or SetLayoutPropertiesEdit) && edits.Count != 1)
            throw Invalid("operations", "Edit one existing layout in a separate request; tab order may move its neighboring paper tabs.", "INVALID_OPERATION_ORDER");
    }
}
