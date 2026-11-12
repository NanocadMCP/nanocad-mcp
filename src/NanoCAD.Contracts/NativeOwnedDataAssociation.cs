namespace NanoCAD.Contracts;

// This is an internal native-reader wire receipt, not a persistent identity or
// a substitute for the current host/physical selection and native owner proof.
public sealed record NativeOwnedDataAssociationExpected(string Drawing, string TopDocument, string OwnerDocument, string Root, string Data);
public sealed record NativeOwnedDataAssociationRead(string drawing_work_id, string top_document_work_id, string owner_document_work_id,
    string root_work_id, string data_work_id, string parent_work_id, string? native_document_work_id,
    string? host_document_work_id, string? host_top_document_work_id, int native_phase, string native_hresult,
    string proof = "actual_current_physical_root_and_native_owner_lookup");

public static class NativeOwnedDataAssociation
{
    public const int SlotCount = 9;
    public const int SlotWidth = 39;
    public const int BufferLength = SlotCount * SlotWidth;
    public const int CompletePhase = 9;

    static DomainException Invalid(string message, object? details = null) => new("DEPENDENCY_OWNERSHIP_MISMATCH", message, false, details);
    static string Id(string value)
    {
        if (!Guid.TryParseExact(value, "B", out var id) || id == Guid.Empty)
            throw Invalid("An actual native association identifier is unavailable or malformed.");
        return id.ToString("B").ToUpperInvariant();
    }

    public static NativeOwnedDataAssociationRead Parse(int nativeStatus, int nativePhase, char[] text, NativeOwnedDataAssociationExpected expected)
    {
        string hresult = "0x" + unchecked((uint)nativeStatus).ToString("X8");
        if (text == null || text.Length != BufferLength)
            throw Invalid("The native association reader returned a different bounded ABI buffer.", new { expected_characters = BufferLength, actual_characters = text?.Length, native_hresult = hresult, native_phase = nativePhase });
        var raw = new string?[SlotCount];
        for (int slot = 0; slot < SlotCount; slot++)
        {
            int start = slot * SlotWidth, end = Array.IndexOf(text, '\0', start, SlotWidth);
            if (end < start)
                throw Invalid("A native association GUID slot is unterminated.", new { slot, native_hresult = hresult, native_phase = nativePhase });
            for (int index = end + 1; index < start + SlotWidth; index++)
                if (text[index] != '\0') throw Invalid("A native association GUID slot contains trailing data.", new { slot, native_hresult = hresult, native_phase = nativePhase });
            raw[slot] = end == start ? null : new string(text, start, end - start);
        }
        if (nativeStatus != 0 || nativePhase != CompletePhase)
            throw Invalid("The native owner-to-data association could not be positively verified.",
                new { native_hresult = hresult, native_phase = nativePhase, observed_slots = raw });
        var values = raw.Select((value, slot) => value == null && slot >= 6 ? null : Id(value ?? "")).ToArray();
        var drawing = Id(expected.Drawing); var top = Id(expected.TopDocument); var owner = Id(expected.OwnerDocument);
        var root = Id(expected.Root); var data = Id(expected.Data);
        if (values[0] != drawing || values[1] != top || values[2] != owner || values[3] != root || values[4] != data || values[5] != root ||
            values[6] != null && values[6] != owner || values[7] != null && values[7] != owner || values[8] != null && values[8] != top)
            throw Invalid("The actual native owner/data identifiers conflict with the current selected context.",
                new { native_phase = nativePhase, observed_slots = values, expected });
        return new(values[0]!, values[1]!, values[2]!, values[3]!, values[4]!, values[5]!, values[6], values[7], values[8], nativePhase, "0x00000000");
    }
}
