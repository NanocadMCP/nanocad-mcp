using System.Globalization;

namespace NanoCADBridge;

internal static class ModelHolePlacementSlots
{
    internal sealed record Reference(int slot, bool is_null, string? object_id, int gs_marker, double? parameter, string parameter_bits, string data);

    // Managed McGeomParam exposes these four fields. Complex paths and viewport references are unavailable here.
    internal static Reference Observe(int slot, bool isNull, string? objectId, int marker, double parameter, long data) =>
        new(slot, isNull, objectId, marker, double.IsFinite(parameter) ? parameter : null,
            BitConverter.DoubleToInt64Bits(parameter).ToString("X16", CultureInfo.InvariantCulture), data.ToString(CultureInfo.InvariantCulture));
}
