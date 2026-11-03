using System.Text;
using System.Text.Json;
using static NanoCAD.Contracts.StructureWriteValidation;

namespace NanoCAD.Contracts;

public sealed record CreateLinetypeEdit(string Name, string Description, double[] Pattern) : StructureEdit("create_linetype");

public static class LinetypeWriteValidation
{
    // This host loses linetype patterns beyond255 elements on DWG save.
    public const int MaxElements = 255;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    public static bool OrdinaryName(string name) => BlockWriteValidation.OrdinaryName(name) &&
        !new[] { "ByLayer", "ByBlock", "Continuous" }.Contains(name, StringComparer.OrdinalIgnoreCase);
    public static double Validate(CreateLinetypeEdit item)
    {
        if (!OrdinaryName(item.Name) || item.Description.Length > 1024 || item.Description.Any(char.IsControl))
            throw Invalid("linetype", "Expected an ordinary new linetype name and a bounded plain description.");
        try { StrictUtf8.GetByteCount(item.Name); StrictUtf8.GetByteCount(item.Description); }
        catch (EncoderFallbackException) { throw Invalid("linetype", "Invalid Unicode."); }
        if (item.Pattern is null || item.Pattern.Length is < 2 or > MaxElements || item.Pattern.Length % 2 != 0)
            throw Invalid("pattern", "Expected 2..255 elements, alternating dash/dot and gap.");
        double total = 0;
        for (int index = 0; index < item.Pattern.Length; index++)
        {
            double value = item.Pattern[index], magnitude = Math.Abs(value);
            if (!double.IsFinite(value) || magnitude > 1e6 || magnitude != 0 && magnitude < 1e-9 ||
                (index % 2 == 0 ? value < 0 : value >= 0))
                throw Invalid("pattern[" + index + "]", "Dash/dot must be nonnegative; gap negative. Nonzero magnitudes must be 1e-9..1e6 mm.");
            total += magnitude;
        }
        if (total is < 1e-9 or > 1e6) throw Invalid("pattern", "Period must be within 1e-9..1e6 mm.");
        return total;
    }
    public static CreateLinetypeEdit ParseCreate(JsonElement input, string path = "$")
    {
        Shape(input, path, "operation", "name", "description", "pattern");
        if (Text(input, "operation", path, 32) != "create_linetype") throw Invalid(path, "Expected create_linetype.");
        var array = Required(input, "pattern", path);
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() is < 2 or > MaxElements)
            throw Invalid(path + ".pattern", "Expected 2..255 numeric elements.");
        var pattern = new List<double>();
        foreach (var number in array.EnumerateArray())
        {
            if (number.ValueKind != JsonValueKind.Number || !number.TryGetDouble(out double value)) throw Invalid(path + ".pattern", "Expected numbers.");
            if (value == 0)
            {
                var raw = number.GetRawText(); int exponent = raw.IndexOfAny(new[] { 'e', 'E' });
                if (raw.Take(exponent < 0 ? raw.Length : exponent).Any(c => c is >= '1' and <= '9'))
                    throw Invalid(path + ".pattern", "Nonzero element underflows to zero; dot intent must be explicit.");
            }
            pattern.Add(value == 0 ? 0 : value);
        }
        var item = new CreateLinetypeEdit(Text(input, "name", path, 255), Text(input, "description", path, 1024, empty: true), pattern.ToArray());
        Validate(item); return item;
    }
    public static int ValidateBatch(IEnumerable<StructureEdit> operations)
    {
        var batch = new List<StructureEdit>(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); int count = 0;
        foreach (var operation in operations)
        {
            batch.Add(operation);
            if (operation is CreateLinetypeEdit item)
            {
                Validate(item);
                count++;
                if (!names.Add(item.Name)) throw Invalid("operations", "Duplicate new linetype name ignoring case.", "INVALID_OPERATION_ORDER");
            }
        }
        int total = DimensionStyleWriteValidation.ValidateBatch(batch) + count;
        BlockWriteValidation.CheckObjectBudget(total); return total;
    }
    public static LinetypeProperties Expected(CreateLinetypeEdit item) => new(false, item.Description, Validate(item), item.Pattern.Length, false,
        item.Pattern.Select((length, index) => new LinetypeElement(index, length, "", 0, new("text_style", "null", null, null, null), new[] { 0d, 0d }, 0, 0, false)).ToArray(),
        false, false, false);
    public static void RequireSame(LinetypeProperties expected, LinetypeProperties actual)
    {
        if (!JsonSerializer.SerializeToUtf8Bytes(expected, Wire.Json).AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(actual, Wire.Json)))
            throw new DomainException("API_EXCEPTION", "Native linetype creation changed an unexpected pattern or metadata field; batch cannot commit.");
    }
}
