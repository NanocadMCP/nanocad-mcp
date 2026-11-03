using System.Text.Json;

namespace NanoCAD.Contracts;

// Counts describe native/managed representation, never an engineering model
// size policy. Admission charges complete serialized observations instead.
public sealed class SerializedCapacity357
{
    public const int SnapshotBytes = 8 * 1024 * 1024;
    private readonly long _maximum;
    private readonly Action? _cancel;
    private long _bytes = 2;
    private bool _first = true;
    public SerializedCapacity357(long maximum = SnapshotBytes, Action? cancel = null)
    {
        if (maximum < 2) throw new ArgumentOutOfRangeException(nameof(maximum));
        _maximum = maximum; _cancel = cancel;
    }
    public void Add<T>(T item)
    {
        _cancel?.Invoke();
        int bytes = JsonSerializer.SerializeToUtf8Bytes(item, Wire.Json).Length;
        _cancel?.Invoke();
        _bytes += bytes + (_first ? 0L : 1L); _first = false;
        if (_bytes > _maximum) throw TooLarge();
    }
    public static void Check<T>(T value, long maximum = SnapshotBytes, Action? cancel = null)
    {
        cancel?.Invoke();
        long bytes = JsonSerializer.SerializeToUtf8Bytes(value, Wire.Json).LongLength;
        cancel?.Invoke();
        if (bytes > maximum) throw TooLarge();
    }
    private static DomainException TooLarge() => new("RESPONSE_LIMIT_EXCEEDED",
        "Complete observation exceeds its actual serialized-byte budget; no items were truncated.");
    public static int Product(int first, int second, int minimum = 1)
    {
        long total = (long)first * second;
        if (first < 1 || second < 1 || total < minimum || total > int.MaxValue)
            throw new DomainException("INVALID_ARGUMENT", "Native indexed collection must fit its nonnegative Int32 representation and required minimum.");
        return (int)total;
    }
}
