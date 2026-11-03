using System.Text;

namespace NanoCAD.Contracts;

// Text-style properties for the existing get_structure operation, since 0.8.
public sealed record TextFontDescriptor(string? typeface, bool bold, bool italic, int character_set, int pitch_and_family);
public sealed record TextFontMetadata(string status, string? family, int? flags)
{
    public static TextFontMetadata From(FontMetadataSignature? signature) => signature == null
        ? new("empty", null, null) : new("acad_font", signature.Family, signature.Flags);
    public static TextFontMetadata Unsupported() => new("unsupported", null, null);
}
public sealed record StyleBaseEligibility(bool eligible, string[] reasons);
public sealed record TextStyleProperties(bool is_dependent, string font_file, string big_font_file, double height, double width_factor,
    bool is_vertical, bool is_shape_file, TextFontDescriptor font_descriptor, double oblique_angle, int generation_flags,
    string annotative_state, int annotative_native_value, bool has_extension_dictionary, bool has_fields,
    TextFontMetadata font_metadata, StyleBaseEligibility base_eligibility);

public sealed record TextStyleFacts(string Name, bool IsDependent, string FontFile, string BigFontFile, double Height, double WidthFactor,
    bool IsVertical, bool IsShapeFile, TextFontDescriptor Font, double ObliqueAngle, int GenerationFlags, int AnnotativeNativeValue,
    bool HasExtensionDictionary, bool HasFields, TextFontMetadata FontMetadata);

public static class TextStyleMetadata
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    public static readonly string[] ReasonCodes = { "qualified_name", "dependent", "vertical", "shape", "generation_flags", "oblique",
        "annotative", "extension_dictionary", "fields", "font_metadata", "big_font", "height", "width_factor", "font_source" };
    private static string Text(string value, int limit)
    {
        if (value.Length > limit) throw new DomainException("ENTITY_TOO_LARGE", "Text style metadata exceeds the supported string length; it was not truncated.");
        try { StrictUtf8.GetByteCount(value); }
        catch (EncoderFallbackException) { throw new DomainException("OBJECT_READ_FAILED", "Text style metadata contains invalid Unicode."); }
        return value;
    }
    // The public style read represents either native empty spelling as null.
    // Reuse this exact representation for expected_typeface write preconditions.
    public static string? DescribeTypeface(string? value) => value is { Length: > 0 } face ? Text(face, 255) : null;
    public static TextStyleProperties Describe(TextStyleFacts facts)
    {
        Text(facts.Name, 16384); Text(facts.FontFile, 16384); Text(facts.BigFontFile, 16384);
        string? face = DescribeTypeface(facts.Font.typeface);
        if (!double.IsFinite(facts.Height) || !double.IsFinite(facts.WidthFactor) || !double.IsFinite(facts.ObliqueAngle))
            throw new DomainException("OBJECT_READ_FAILED", "Text style contains non-finite numeric metadata.");
        if (facts.FontMetadata.status == "acad_font")
        {
            if (facts.FontMetadata.family == null || facts.FontMetadata.flags == null)
                throw new DomainException("OBJECT_READ_FAILED", "Incomplete font metadata signature.");
            // Enforce the same bounded signature if a native adapter supplies DTOs.
            StyleFontMetadata.Read(new[] { new FontMetadataValue(1001, "ACAD"), new(1000, facts.FontMetadata.family), new(1071, facts.FontMetadata.flags.Value) });
        }
        else if (facts.FontMetadata.status is not ("empty" or "unsupported") || facts.FontMetadata.family != null || facts.FontMetadata.flags != null)
            throw new DomainException("OBJECT_READ_FAILED", "Invalid font metadata state.");
        // Installed SDK metadata: True=0, False=1, NotApplicable=2. Preserve
        // unknown native values, but never treat them as a negative boolean.
        string annotative = facts.AnnotativeNativeValue switch { 0 => "true", 1 => "false", 2 => "not_applicable", _ => "unknown" };
        var reasons = new List<string>();
        void Reject(bool condition, string reason) { if (condition) reasons.Add(reason); }
        Reject(!BlockWriteValidation.OrdinaryName(facts.Name), "qualified_name");
        Reject(facts.IsDependent, "dependent"); Reject(facts.IsVertical, "vertical"); Reject(facts.IsShapeFile, "shape");
        Reject(facts.GenerationFlags != 0, "generation_flags"); Reject(facts.ObliqueAngle != 0, "oblique");
        Reject(annotative is not ("false" or "not_applicable"), "annotative");
        Reject(facts.HasExtensionDictionary, "extension_dictionary"); Reject(facts.HasFields, "fields");
        Reject(facts.FontMetadata.status == "unsupported", "font_metadata"); Reject(facts.BigFontFile.Length != 0, "big_font");
        Reject(!StyleWriteValidation.ValidHeight(facts.Height), "height"); Reject(!StyleWriteValidation.ValidWidth(facts.WidthFactor), "width_factor");
        Reject(string.IsNullOrWhiteSpace(facts.FontFile) && string.IsNullOrWhiteSpace(face), "font_source");
        return new(facts.IsDependent, facts.FontFile, facts.BigFontFile, facts.Height, facts.WidthFactor, facts.IsVertical, facts.IsShapeFile,
            facts.Font with { typeface = face }, facts.ObliqueAngle, facts.GenerationFlags, annotative, facts.AnnotativeNativeValue,
            facts.HasExtensionDictionary, facts.HasFields, facts.FontMetadata, new(reasons.Count == 0, reasons.ToArray()));
    }
}
