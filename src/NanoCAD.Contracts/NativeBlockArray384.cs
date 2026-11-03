using System.Text.Json;
using static NanoCAD.Contracts.StructureWriteValidation;

namespace NanoCAD.Contracts;

public sealed record NativeBlockArrayParameters384(int Rows, int Columns, double RowSpacing, double ColumnSpacing);
public sealed record NativeBlockArrayChanges384(int? Rows, int? Columns, double? RowSpacing, double? ColumnSpacing)
{
    public NativeBlockArrayParameters384 Apply(NativeBlockArrayParameters384 before) => new(
        Rows ?? before.Rows, Columns ?? before.Columns, RowSpacing ?? before.RowSpacing, ColumnSpacing ?? before.ColumnSpacing);
}
public sealed record InsertBlockArray384(string DefinitionHandle, Point2 Position, string Layer, double Rotation,
    double Scale, NativeBlockArrayParameters384 Parameters) : StructureEdit("insert_block_array");
public sealed record SetBlockArray384(string Handle, string ArrayToken, NativeBlockArrayChanges384 Properties)
    : StructureEdit("set_block_array_properties");

public static class NativeBlockArrayValidation384
{
    // The native declaration and installed native call signatures use UInt16.
    // The managed Int16 facade transports the same bits; this is not a product count cap.
    public static int Count(JsonElement input, string name, string path)
    {
        var value = Required(input, name, path);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int count) || count < 1 || count > ushort.MaxValue)
            throw Invalid(path + "." + name, "Expected a positive native UInt16 count (1..65535).");
        return count;
    }
    public static double Spacing(JsonElement input, string name, string path)
    {
        var value = Required(input, name, path);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double number) || !double.IsFinite(number))
            throw Invalid(path + "." + name, "Expected finite native spacing in drawing millimeters.", "INVALID_GEOMETRY");
        return number == 0 ? 0 : number;
    }
    public static void Validate(NativeBlockArrayParameters384 value)
    {
        if (value.Rows < 1 || value.Rows > ushort.MaxValue || value.Columns < 1 || value.Columns > ushort.MaxValue ||
            !double.IsFinite(value.RowSpacing) || !double.IsFinite(value.ColumnSpacing) ||
            !double.IsFinite(value.RowSpacing * (value.Rows - 1)) || !double.IsFinite(value.ColumnSpacing * (value.Columns - 1)))
            throw Invalid("parameters", "Native counts and the finite array span must be representable.", "INVALID_GEOMETRY");
    }
    public static StructureEdit Parse(JsonElement input, string path)
    {
        string kind = Text(input, "operation", path, 32);
        if (kind == "insert_block_array")
        {
            Shape(input, path, "operation", "definition_handle", "position", "layer", "rotation", "scale", "rows", "columns", "row_spacing", "column_spacing");
            // Reuse the actual ordinary-insertion placement grammar without exposing attributes.
            var ordinary = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in input.EnumerateObject())
                if (property.Name is not ("rows" or "columns" or "row_spacing" or "column_spacing")) ordinary.Add(property.Name, property.Value.Clone());
            ordinary["operation"] = JsonSerializer.SerializeToElement("insert_block");
            var insert = BlockWriteValidation.ParseInsert(JsonSerializer.SerializeToElement(ordinary), path);
            var parameters = new NativeBlockArrayParameters384(Count(input, "rows", path), Count(input, "columns", path),
                Spacing(input, "row_spacing", path), Spacing(input, "column_spacing", path));
            Validate(parameters);
            return new InsertBlockArray384(insert.DefinitionHandle, insert.Position, insert.Layer, insert.Rotation, insert.Scale, parameters);
        }
        if (kind != "set_block_array_properties") throw Invalid(path + ".operation", "Expected a native block-array operation.");
        Shape(input, path, "operation", "handle", "array_token", "properties");
        string token = Text(input, "array_token", path, 64);
        if (token.Length != 64 || token.Any(c => !Uri.IsHexDigit(c))) throw Invalid(path + ".array_token", "Expected the complete native array token from inspect_objects.");
        var properties = Required(input, "properties", path); string target = path + ".properties";
        Shape(properties, target, "rows", "columns", "row_spacing", "column_spacing");
        if (!properties.EnumerateObject().Any()) throw Invalid(target, "At least one requested native array property is required.");
        int? OptionalCount(string name) => properties.TryGetProperty(name, out _) ? Count(properties, name, target) : null;
        double? OptionalSpacing(string name) => properties.TryGetProperty(name, out _) ? Spacing(properties, name, target) : null;
        return new SetBlockArray384(Validation.NormalizeHandle(Text(input, "handle", path, 16)), token.ToUpperInvariant(),
            new(OptionalCount("rows"), OptionalCount("columns"), OptionalSpacing("row_spacing"), OptionalSpacing("column_spacing")));
    }
    public static void Batch(IEnumerable<StructureEdit> operations)
    {
        var values = operations.ToArray();
        if (!values.Any(value => value is InsertBlockArray384 or SetBlockArray384)) return;
        if (values.Any(value => value is not (InsertBlockArray384 or SetBlockArray384)))
            throw Invalid("operations", "Use a separate native block-array group; source definitions and layers must already be committed.", "INVALID_OPERATION_ORDER");
        var handles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edit in values.OfType<SetBlockArray384>())
            if (!handles.Add(edit.Handle)) throw Invalid("operations", "Edit each existing native array once with its fresh token.", "INVALID_OPERATION_ORDER");
    }
}
