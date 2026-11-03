using System.Text;
using System.Text.Json;
using static NanoCAD.Contracts.StructureWriteValidation;

namespace NanoCAD.Contracts;

public sealed record SetTextStyleFontEdit(string Handle,string? ExpectedTypeface,string Typeface,string RequiredText):StructureEdit("set_text_style_font");

public sealed record TextStyleChanges(double? Height, double? WidthFactor);
public sealed record CreateTextStyleEdit(string Name, string BaseStyleHandle, TextStyleChanges Properties)
    : StructureEdit("create_text_style");
public sealed record SetTextStylePropertiesEdit(string Handle, string ExpectedName, TextStyleChanges Properties)
    : StructureEdit("set_text_style_properties");
public sealed record EffectiveTextStyleProperties(double Height, double WidthFactor);

// Shared validation for the public structural operation since candidate 0.8.
public static class StyleWriteValidation
{
    public static SetTextStyleFontEdit ParseFont(JsonElement input,string path)
    {
        Shape(input,path,"operation","handle","expected_typeface","typeface","required_text");
        string face=Text(input,"typeface",path,31);
        if(face.Any(char.IsControl)||face.IndexOfAny(new[]{'/', '\\', ':', '@'})>=0)throw Invalid(path+".typeface","Use an installed horizontal font family, not a file path.");
        var expected=Required(input,"expected_typeface",path);
        string? expectedFace=expected.ValueKind==JsonValueKind.Null?null:Text(input,"expected_typeface",path,255);
        return new(Validation.NormalizeHandle(Text(input,"handle",path,16)),expectedFace,face,Text(input,"required_text",path,4096));
    }
    public static bool ValidHeight(double value) => double.IsFinite(value) && (value == 0 || value is >= 1e-9 and <= 1e6);
    public static bool ValidWidth(double value) => double.IsFinite(value) && value is >= 0.01 and <= 100;
    private static double Number(JsonElement input, string path, bool height)
    {
        if (input.ValueKind != JsonValueKind.Number || !input.TryGetDouble(out double value) || !(height ? ValidHeight(value) : ValidWidth(value)))
            throw Invalid(path, height ? "Expected zero or a finite height within 1e-9..1e6 mm." : "Expected a finite width factor within 0.01..100.");
        if (value == 0)
        {
            // A nonzero JSON number that underflows to double zero must not
            // silently turn fixed-height intent into the variable-height mode.
            string raw = input.GetRawText();
            int exponent = raw.IndexOfAny(new[] { 'e', 'E' });
            if (raw.Take(exponent < 0 ? raw.Length : exponent).Any(c => c is >= '1' and <= '9'))
                throw Invalid(path, "Nonzero height underflows the supported number range.");
        }
        return value == 0 ? 0 : value;
    }
    public static CreateTextStyleEdit ParseCreate(JsonElement input, string path = "$")
    {
        Shape(input, path, "operation", "name", "base_style_handle", "properties");
        if (Text(input, "operation", path, 32) != "create_text_style") throw Invalid(path + ".operation", "Expected create_text_style.");
        string name = Text(input, "name", path, 255);
        if (!BlockWriteValidation.OrdinaryName(name)) throw Invalid(path + ".name", "Expected an ordinary unqualified text-style name.");
        string handle = Validation.NormalizeHandle(Text(input, "base_style_handle", path, 16));
        double? height = null, width = null;
        if (input.TryGetProperty("properties", out var properties))
        {
            Shape(properties, path + ".properties", "height", "width_factor");
            if (!properties.EnumerateObject().Any()) throw Invalid(path + ".properties", "At least one property is required when supplied.");
            if (properties.TryGetProperty("height", out var h)) height = Number(h, path + ".properties.height", true);
            if (properties.TryGetProperty("width_factor", out var w)) width = Number(w, path + ".properties.width_factor", false);
        }
        return new(name, handle, new(height, width));
    }
    public static SetTextStylePropertiesEdit ParseSet(JsonElement input, string path = "$")
    {
        Shape(input, path, "operation", "handle", "expected_name", "properties");
        string handle = Validation.NormalizeHandle(Text(input, "handle", path, 16));
        string name = Text(input, "expected_name", path, 255);
        var properties = Required(input, "properties", path);
        var parsed = ParseCreate(Wire.Element(new { operation = "create_text_style", name, base_style_handle = handle, properties }), path);
        return new(handle, name, parsed.Properties);
    }
    public static EffectiveTextStyleProperties ResolveProperties(TextStyleChanges changes, double baseHeight, double baseWidth)
    {
        if (!ValidHeight(baseHeight) || !ValidWidth(baseWidth))
            throw new DomainException("UNSUPPORTED_STYLE_TYPE", "Base style values exceed the supported finite ranges.");
        if (changes.Height is { } h && !ValidHeight(h) || changes.WidthFactor is { } w && !ValidWidth(w))
            throw Invalid("properties", "Text style overrides exceed their supported ranges.");
        double height = changes.Height ?? baseHeight, width = changes.WidthFactor ?? baseWidth;
        return new(height == 0 ? 0 : height, width);
    }
    public static int ValidateBatch(IEnumerable<StructureEdit> operations)
    {
        var batch = new List<StructureEdit>(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int styles = 0;
        foreach (var operation in operations)
        {
            batch.Add(operation);
            if (operation is CreateTextStyleEdit style)
            {
                styles++;
                if (!names.Add(style.Name)) throw Invalid("operations", "Duplicate new text style name ignoring case.", "INVALID_OPERATION_ORDER");
            }
        }
        if (batch.Count == 0) throw Invalid("operations", "At least one structural operation is required.");
        int objects = BlockWriteValidation.ValidateBatch(batch) + styles;
        BlockWriteValidation.CheckObjectBudget(objects);
        return objects;
    }
}

// Native adapters can yield TypedValue code/value pairs without materializing
// an arbitrary ResultBuffer. This policy consumes at most four entries.
public sealed record FontMetadataValue(int Code, object? Value);
public sealed record FontMetadataSignature(string Application, string Family, int Flags);
public static class StyleFontMetadata
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static DomainException Unsupported() => new("UNSUPPORTED_STYLE_TYPE", "Only empty or bounded ACAD font metadata is supported; source metadata is not stripped.");
    public static FontMetadataSignature? Read(IEnumerable<FontMetadataValue>? values)
    {
        if (values == null) return null;
        using var iterator = values.GetEnumerator();
        return ReadCore(iterator.MoveNext, () => iterator.Current);
    }
    // Owns and disposes the enumerator. Convert Current only for the first
    // three entries: native ResultBuffer.Current can materialize arbitrary data.
    public static FontMetadataSignature? Read(System.Collections.IEnumerator iterator, Func<object, FontMetadataValue> convert, Action check)
    {
        try { return ReadCore(() => { check(); return iterator.MoveNext(); }, () => convert(iterator.Current)); }
        finally { (iterator as IDisposable)?.Dispose(); }
    }
    private static FontMetadataSignature? ReadCore(Func<bool> moveNext, Func<FontMetadataValue> current)
    {
        if (!moveNext()) return null;
        var application = current();
        if (!moveNext()) throw Unsupported();
        var family = current();
        if (!moveNext()) throw Unsupported();
        var flags = current();
        if (moveNext()) throw Unsupported();
        if (application is not { Code: 1001, Value: string app } || app != "ACAD" ||
            family is not { Code: 1000, Value: string text } || text.Length is < 1 or > 255 || text.Any(char.IsControl) ||
            flags is not { Code: 1071, Value: int bits }) throw Unsupported();
        try { StrictUtf8.GetByteCount(text); }
        catch (EncoderFallbackException) { throw Unsupported(); }
        return new(app, text, bits);
    }
}
