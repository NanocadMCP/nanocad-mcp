using System.Text;

namespace NanoCAD.Contracts;

public sealed record LinetypeStyleReference(string target_kind, string state, EntityRef? @ref, string? name, string? reason);
public sealed record LinetypeElement(int index, double length, string text, int shape_number, LinetypeStyleReference style,
    double[] offset, double rotation, double scale, bool is_ucs_oriented);
public sealed record LinetypeProperties(bool is_dependent, string description, double pattern_length, int dash_count,
    bool is_scaled_to_fit, LinetypeElement[] elements, bool has_extension_dictionary, bool has_fields, bool has_xdata);

public static class LinetypeMetadata
{
    public const int MaxElementText = 4096, MaxDescription = 16384;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    public static readonly string[] DependencyReasons = { "invalid_id", "erased", "wrong_type", "wrong_owner", "read_failed" };
    private static void Fail(string message) => throw new DomainException("OBJECT_READ_FAILED", message);
    public static string Text(string? text, int limit)
    {
        if (text is null) throw new DomainException("OBJECT_READ_FAILED", "Missing linetype text.");
        if (text.Length > limit) throw new DomainException("ENTITY_TOO_LARGE", "Linetype text exceeds the UTF-16 limit; it was not truncated.");
        try { StrictUtf8.GetByteCount(text); }
        catch (EncoderFallbackException) { Fail("Linetype text contains invalid Unicode."); }
        return text;
    }
    public static int Count(int count)
    {
        if (count < 0) Fail("Negative linetype element count.");
        return count;
    }
    private static void Identity(EntityRef? value)
    {
        if (value is null || !Guid.TryParseExact(value.bridge_instance_id, "D", out var bridge) || bridge == Guid.Empty ||
            !Guid.TryParseExact(value.document_id, "D", out var document) || document == Guid.Empty)
            throw new DomainException("OBJECT_READ_FAILED", "Invalid linetype reference lifetime.");
        if (value.handle is null) throw new DomainException("OBJECT_READ_FAILED", "Missing linetype reference handle.");
        try { if (Validation.NormalizeHandle(value.handle) != value.handle) Fail("Noncanonical linetype reference handle."); }
        catch (DomainException) { Fail("Invalid linetype reference handle."); }
    }
    public static LinetypeProperties Validate(EntityRef owner, LinetypeProperties value)
    {
        Identity(owner); Text(value.description, MaxDescription); Count(value.dash_count);
        if (!double.IsFinite(value.pattern_length)) Fail("Non-finite linetype pattern length.");
        if (value.elements is null || value.elements.Length != value.dash_count) Fail("Incomplete linetype pattern.");
        for (int index = 0; index < value.elements!.Length; index++)
        {
            var item = value.elements[index];
            if (item is null) throw new DomainException("OBJECT_READ_FAILED", "Missing linetype element.");
            if (item.index != index || item.offset is null || item.offset.Length != 2) Fail("Invalid linetype element index/offset.");
            foreach (double number in new[] { item.length, item.rotation, item.scale }.Concat(item.offset!))
                if (!double.IsFinite(number)) Fail("Non-finite linetype element value.");
            Text(item.text, MaxElementText);
            var reference = item.style;
            if (reference is null || reference.target_kind != "text_style") Fail("Invalid linetype style reference type.");
            if (reference!.state == "resolved")
            {
                Identity(reference.@ref); Text(reference.name, MaxDescription);
                if (reference.reason != null || reference.@ref!.document_id != owner.document_id || reference.@ref.bridge_instance_id != owner.bridge_instance_id)
                    Fail("Linetype style reference has a different lifetime or unexpected reason.");
            }
            else if (reference.state is "null" or "unavailable")
            {
                if (reference.@ref != null || reference.name != null || (reference.state == "null" ? reference.reason != null : reference.reason is null || !DependencyReasons.Contains(reference.reason)))
                    Fail("Unresolved linetype style reference claims an identity or unknown reason.");
            }
            else Fail("Unknown linetype style reference state.");
        }
        return value;
    }
}
