namespace NanoCAD.Contracts;

// Internal native-reader receipt. Expected identities validate the current
// selected context; they never supply either returned view reference.
public sealed record NativeDrawingViewLocalCutExpected(string Drawing, string TopDocument, string OwnerDocument, string Sketch);
public sealed record NativeDrawingViewLocalCutRead(string Drawing, string TopDocument, string OwnerDocument, string Sketch,
    string? TargetView, string? DepthView, Point3 DepthPoint, double Depth);

public static class NativeDrawingViewLocalCutData
{
    public const int SlotWidth = 39;
    public const int SlotCount = 6;
    public const int BufferLength = SlotWidth * SlotCount;
    public const int ValueCount = 4;
    public const int CompletePhase = 6;

    static DomainException Invalid(string message, object? details = null) => new("DRAWING_VIEW_STATE_UNAVAILABLE", message, false, details);
    static string Identity(string value)
    {
        if (!Guid.TryParseExact(value, "B", out var id) || id == Guid.Empty)
            throw Invalid("The native local-cut reader returned a malformed actual work ID.");
        return id.ToString("B").ToUpperInvariant();
    }

    public static NativeDrawingViewLocalCutRead Parse(int status, int phase, char[] text, double[] numbers, NativeDrawingViewLocalCutExpected expected)
    {
        string hresult = "0x" + unchecked((uint)status).ToString("X8");
        if (text == null || text.Length != BufferLength || numbers == null || numbers.Length != ValueCount)
            throw Invalid("The native local-cut reader returned a different bounded ABI buffer.", new { native_hresult = hresult, native_phase = phase });
        var slots = new string?[SlotCount];
        for (int slot = 0; slot < SlotCount; slot++)
        {
            int start = slot * SlotWidth, end = Array.IndexOf(text, '\0', start, SlotWidth);
            if (end < start) throw Invalid("A native local-cut GUID slot is unterminated.", new { slot, native_hresult = hresult, native_phase = phase });
            for (int i = end + 1; i < start + SlotWidth; i++)
                if (text[i] != '\0') throw Invalid("A native local-cut GUID slot has trailing data.", new { slot, native_hresult = hresult, native_phase = phase });
            slots[slot] = end == start ? null : new string(text, start, end - start);
        }
        if (status != 0 || phase != CompletePhase)
            throw Invalid("The public native local-cut data could not be read completely.", new { native_hresult = hresult, native_phase = phase, observed_ids = slots });
        for (int i = 0; i < SlotCount; i++)
            slots[i] = slots[i] == null && i >= 4 ? null : Identity(slots[i] ?? "");
        if (slots[0] != Identity(expected.Drawing) || slots[1] != Identity(expected.TopDocument) ||
            slots[2] != Identity(expected.OwnerDocument) || slots[3] != Identity(expected.Sketch))
            throw Invalid("The actual local-cut reader context differs from the current selected native sketch.", new { native_phase = phase, observed_ids = slots, expected });
        if (numbers.Any(value => !double.IsFinite(value)))
            throw Invalid("Native local-cut point/depth data is not finite.", new { native_phase = phase });
        return new(slots[0]!, slots[1]!, slots[2]!, slots[3]!, slots[4], slots[5], new(numbers[0], numbers[1], numbers[2]), numbers[3]);
    }
}
