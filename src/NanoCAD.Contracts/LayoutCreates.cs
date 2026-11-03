using System.Text;
using System.Text.Json;
using static NanoCAD.Contracts.StructureWriteValidation;

namespace NanoCAD.Contracts;

public sealed record CreateLayoutEdit(string Name, string BaseLayoutHandle) : StructureEdit("create_layout");

public static class LayoutCreateValidation
{
    public const int MaxTabOrder = int.MaxValue;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    public static void Validate(CreateLayoutEdit operation)
    {
        if (!BlockWriteValidation.OrdinaryName(operation.Name) || string.Equals(operation.Name, "Model", StringComparison.OrdinalIgnoreCase))
            throw Invalid("name", "Expected an ordinary new paper layout name; Model is reserved.");
        try { StrictUtf8.GetByteCount(operation.Name); }
        catch (EncoderFallbackException) { throw Invalid("name", "Invalid Unicode."); }
        if (Validation.NormalizeHandle(operation.BaseLayoutHandle) != operation.BaseLayoutHandle)
            throw Invalid("base_layout_handle", "Expected a normalized nonzero layout handle.");
    }
    public static CreateLayoutEdit Parse(JsonElement input, string path = "$")
    {
        Shape(input, path, "operation", "name", "base_layout_handle");
        if (Text(input, "operation", path, 32) != "create_layout") throw Invalid(path, "Expected create_layout.");
        var result = new CreateLayoutEdit(Text(input, "name", path, 255), Validation.NormalizeHandle(Text(input, "base_layout_handle", path, 16)));
        Validate(result); return result;
    }
    public static int ValidateBatch(IEnumerable<StructureEdit> operations)
    {
        var batch = operations.ToArray(); var layouts = batch.OfType<CreateLayoutEdit>().ToArray();
        if (layouts.Length > 0)
        {
            if (layouts.Length != batch.Length) throw Invalid("operations", "Layout creation requires a separate batch.", "INVALID_OPERATION_ORDER");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var layout in layouts)
            { Validate(layout); if (!names.Add(layout.Name)) throw Invalid("name", "Duplicate layout name ignoring case.", "INVALID_OPERATION_ORDER"); }
        }
        // Includes layout, paper BTR, and its implicit block begin/end records.
        int budget = ViewportWriteValidation.ValidateBatch(batch) + 4 * layouts.Length;
        BlockWriteValidation.CheckObjectBudget(budget); return budget;
    }
    public static int NextTabOrder(IEnumerable<int> orders, int offset, int additions)
    {
        var existing = orders.ToArray();
        if (existing.Length == 0 || existing.Any(n => n < 0 || n > MaxTabOrder - additions) ||
            additions < 1 || offset < 0 || offset >= additions)
            throw new DomainException("UNSUPPORTED_LAYOUT", "Existing layout count or tab order exceeds the bounded creation scope.");
        return existing.Max() + offset + 1;
    }
    public static LayoutProperties Expected(LayoutProperties source, int order, EntityRef block) =>
        source with { tab_order = order, is_model = false, block_record = block, tab_selected = false };
    // Limits belong to the new layout, not PlotSettings.CopyFrom. Preserve every
    // copied plot field, and require finite ordered native limits for the new space.
    public static void RequireNew(LayoutProperties expected, LayoutProperties actual)
    {
        if (actual.limits_min.Length != 2 || actual.limits_max.Length != 2 ||
            actual.limits_min.Concat(actual.limits_max).Any(n => !double.IsFinite(n) || Math.Abs(n) > 1e9) ||
            Enumerable.Range(0, 2).Any(i => actual.limits_min[i] > actual.limits_max[i]))
            throw new DomainException("API_EXCEPTION", "New layout has invalid native limits; batch cannot commit.");
        RequireSame(expected with { limits_min = actual.limits_min, limits_max = actual.limits_max }, actual);
    }
    public static void RequireSame(LayoutProperties expected, LayoutProperties actual)
    {
        if (!JsonSerializer.SerializeToUtf8Bytes(expected, Wire.Json).AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(actual, Wire.Json)))
        {
            var left = Wire.Element(expected); var right = Wire.Element(actual);
            string field = left.EnumerateObject().First(p => p.Value.GetRawText() != right.GetProperty(p.Name).GetRawText()).Name;
            throw new DomainException("API_EXCEPTION", "Layout creation changed unexpected metadata; batch cannot commit.", false,
                new { property = field, expected = left.GetProperty(field), actual = right.GetProperty(field) });
        }
    }
}
