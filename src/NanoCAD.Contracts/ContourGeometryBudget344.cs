using System.Text.Json;

namespace NanoCAD.Contracts;

// Private contour plans and readback may exceed one public response frame.
// Charge actual serialized records while building them, then check the complete
// envelope separately. This is a byte limit, not a geometry/model count limit.
internal sealed class ContourGeometryBudget344
{
    internal const int MaximumBytes = 8 * 1024 * 1024;
    private readonly Action? _cancel;
    private long _bytes = 2;
    private bool _first = true;
    internal ContourGeometryBudget344(Action? cancel = null) => _cancel = cancel;
    internal void Add<T>(T value)
    {
        _cancel?.Invoke();
        _bytes += Bytes(value) + (_first ? 0L : 1L);
        _first = false;
        _cancel?.Invoke();
        if (_bytes > MaximumBytes) TooLarge();
    }
    internal static void Check<T>(T value, Action? cancel = null)
    {
        cancel?.Invoke();
        int bytes = Bytes(value);
        cancel?.Invoke();
        if (bytes > MaximumBytes) TooLarge();
    }
    private static int Bytes<T>(T value)
    {
        try { return JsonSerializer.SerializeToUtf8Bytes(value, Wire.Json).Length; }
        catch (ArgumentException) { throw new DomainException("INVALID_GEOMETRY", "Contour contains nonfinite stored geometry."); }
    }
    private static void TooLarge() => throw new DomainException("RESPONSE_LIMIT_EXCEEDED", "Complete contour data exceeds the actual8MiB serialized snapshot-byte budget; no geometry was truncated.");
}
