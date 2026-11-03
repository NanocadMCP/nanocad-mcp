using System.Text.Json;

namespace NanoCAD.Contracts;

// Internal retained profile/verification data may exceed a public transport
// frame. Bound it by its real serialized bytes, not a vertex or edge count.
internal sealed class ProfileGeometryBudget
{
    private const long MaxBytes = 8L * 1024 * 1024;
    private readonly Action? _cancel;
    private long _bytes = 2;
    internal ProfileGeometryBudget(Action? cancel = null) => _cancel = cancel;
    internal void Add<T>(T value)
    {
        _cancel?.Invoke();
        byte[] serialized;
        try { serialized = JsonSerializer.SerializeToUtf8Bytes(value, Wire.Json); }
        catch (ArgumentException) { throw new DomainException("INVALID_GEOMETRY", "Retained profile contains nonfinite geometry data."); }
        _bytes += serialized.LongLength + 1;
        if (_bytes > MaxBytes)
            throw new DomainException("RESPONSE_LIMIT_EXCEEDED", "Retained profile geometry exceeds the actual8MiB serialized byte budget.");
    }
    internal static void Profile(ModelProfileSpec profile, Action? cancel = null)
    {
        var budget = new ProfileGeometryBudget(cancel);
        budget.Add(new { profile.Kind, profile.Center, profile.Radius });
        if (profile.Vertices != null)
            for (int index = 0; index < profile.Vertices.Length; index++)
                budget.Add(new { vertex = profile.Vertices[index], bulge = profile.Bulges != null && index < profile.Bulges.Length ? (double?)profile.Bulges[index] : null });
    }
}
