using System.Text.Json;
using static NanoCAD.Contracts.StructureWriteValidation;

namespace NanoCAD.Contracts;

public sealed record DimensionStyleChanges(double? DimensionScale, double? TextHeight, double? ArrowSize, DimensionStyleDefaults364? Defaults = null);
public sealed record CreateDimensionStyleEdit(string Name, string BaseStyleHandle, DimensionStyleChanges Properties)
    : StructureEdit("create_dimension_style");
public sealed record SetDimensionStylePropertiesEdit(string Handle, string ExpectedName, DimensionStyleChanges Properties)
    : StructureEdit("set_dimension_style_properties");

public static class DimensionStyleWriteValidation
{
    public static bool ValidNumber(double value) => double.IsFinite(value) && value is >= 1e-9 and <= 1e6;
    public static bool OrdinaryName(string name) => BlockWriteValidation.OrdinaryName(name) && !DimensionStyleMetadata.FamilyNameCandidate(name);
    public static CreateDimensionStyleEdit ParseCreate(JsonElement input, string path = "$")
    {
        Shape(input, path, "operation", "name", "base_style_handle", "properties");
        if (Text(input, "operation", path, 32) != "create_dimension_style") throw Invalid(path + ".operation", "Expected create_dimension_style.");
        string name = Text(input, "name", path, 255);
        if (!OrdinaryName(name)) throw Invalid(path + ".name", "Expected an ordinary unqualified name without a dimension-family suffix.");
        string handle = Validation.NormalizeHandle(Text(input, "base_style_handle", path, 16));
        double? scale = null, height = null, arrow = null; DimensionStyleDefaults364? defaults = null;
        if (input.TryGetProperty("properties", out var properties))
        {
            Shape(properties, path + ".properties", new[] { "dimension_scale", "text_height", "arrow_size" }.Concat(DimensionStyleDefaultsValidation364.Names).ToArray());
            if (!properties.EnumerateObject().Any()) throw Invalid(path + ".properties", "At least one property is required when supplied.");
            double? Number(string key)
            {
                if (!properties.TryGetProperty(key, out var value)) return null;
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double number) || !ValidNumber(number))
                    throw Invalid(path + ".properties." + key, "Expected a finite positive number within 1e-9..1e6.");
                return number;
            }
            scale = Number("dimension_scale"); height = Number("text_height"); arrow = Number("arrow_size");
            defaults = DimensionStyleDefaultsValidation364.Parse(properties, path + ".properties");
        }
        return new(name, handle, new(scale, height, arrow, defaults));
    }
    public static int ValidateBatch(IEnumerable<StructureEdit> operations)
    {
        var batch = new List<StructureEdit>(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int styles = 0;
        foreach (var operation in operations)
        {
            batch.Add(operation);
            if (operation is CreateDimensionStyleEdit style)
            {
                styles++;
                if (!names.Add(style.Name)) throw Invalid("operations", "Duplicate new dimension style name ignoring case.", "INVALID_OPERATION_ORDER");
            }
        }
        int objects = StyleWriteValidation.ValidateBatch(batch) + styles;
        BlockWriteValidation.CheckObjectBudget(objects);
        return objects;
    }
    public static SetDimensionStylePropertiesEdit ParseSet(JsonElement input, string path = "$")
    {
        Shape(input, path, "operation", "handle", "expected_name", "properties");
        string handle = Validation.NormalizeHandle(Text(input, "handle", path, 16));
        string name = Text(input, "expected_name", path, 255);
        var properties = Required(input, "properties", path);
        var parsed = ParseCreate(Wire.Element(new { operation = "create_dimension_style", name, base_style_handle = handle, properties }), path);
        return new(handle, name, parsed.Properties);
    }
    public static DimensionDependency[] Dependencies(DimensionStyleProperties source)
    {
        var r = source.references;
        return new[] { r.arrow, r.first_arrow, r.second_arrow, r.leader_arrow, r.extension1_linetype,
            r.extension2_linetype, r.dimension_linetype, r.text_style };
    }
    public static DimensionStyleProperties Expected(string sourceName, EntityRef sourceRef, DimensionStyleProperties source, DimensionStyleChanges changes)
    {
        DimensionStyleMetadata.Validate(sourceName, sourceRef, source);
        bool UnsupportedColor(DimensionColor color) => color.method is not ("by_layer" or "by_block" or "aci" or "true_color" or "none") ||
            color.has_color_name || color.color_name.Length != 0 || color.book_name.Length != 0 || color.aci is < 0 or > 256;
        if (!OrdinaryName(sourceName) || source.is_dependent || source.family_name_candidate ||
            source.annotative_state is not ("false" or "not_applicable") || source.has_extension_dictionary || source.has_fields || source.has_xdata ||
            Dependencies(source).Any(d => d.state == "unavailable") || source.references.text_style.state != "resolved" ||
            new[] { source.colors.dimension, source.colors.extension, source.colors.text, source.colors.text_fill }.Any(UnsupportedColor) ||
            source.lineweights.dimension.mode == "unknown" || source.lineweights.extension.mode == "unknown" ||
            !ValidNumber(source.dimension_scale) || !ValidNumber(source.text_height) || !ValidNumber(source.arrow_size))
            throw new DomainException("UNSUPPORTED_STYLE_TYPE", "Base dimension style is outside the ordinary, non-annotative, metadata-preserving creation capability.");
        foreach (var value in new[] { changes.DimensionScale, changes.TextHeight, changes.ArrowSize })
            if (value.HasValue && !ValidNumber(value.Value)) throw Invalid("properties", "Dimension style overrides exceed their finite positive ranges.");
        return source with { dimension_scale = changes.DimensionScale ?? source.dimension_scale,
            text_height = changes.TextHeight ?? source.text_height, arrow_size = changes.ArrowSize ?? source.arrow_size,
            variables = changes.Defaults == null ? source.variables : DimensionStyleDefaultsValidation364.Expected(source.variables, changes.Defaults) };
    }
    public static void RequireSame(DimensionStyleProperties expected, DimensionStyleProperties actual)
    {
        if (!JsonSerializer.SerializeToUtf8Bytes(expected, Wire.Json).AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(actual, Wire.Json)))
            throw new DomainException("API_EXCEPTION", "Dimension style cloning or setters changed an unexpected observed property; the batch cannot commit.");
    }
}
