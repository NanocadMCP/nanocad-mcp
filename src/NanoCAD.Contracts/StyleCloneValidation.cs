using System.Text.Json;

namespace NanoCAD.Contracts;

public static class StyleCloneValidation
{
    public static TextStyleProperties Expected(TextStyleProperties source, TextStyleChanges changes)
    {
        if (!source.base_eligibility.eligible)
            throw new DomainException("UNSUPPORTED_STYLE_TYPE", "Base text style is outside this creation capability.", false, new { reasons = source.base_eligibility.reasons });
        var effective = StyleWriteValidation.ResolveProperties(changes, source.height, source.width_factor);
        return source with { height = effective.Height, width_factor = effective.WidthFactor };
    }
    public static void RequireSame(TextStyleProperties expected, TextStyleProperties actual)
    {
        // Both values are bounded, finite, typed snapshots from the same reader.
        // Include every field, including nested metadata and eligibility reasons.
        if (!JsonSerializer.SerializeToUtf8Bytes(expected, Wire.Json).AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(actual, Wire.Json)))
            throw new DomainException("API_EXCEPTION", "Native style cloning or setters changed an unexpected observed property; the batch cannot commit.");
    }
}
