using System.Globalization;

namespace NanoCAD.Contracts;

// Exact wire value for an observed native handle, not an ObjectId/EntityRef.
// The signed managed accessor carries all bits of the native UInt64 handle.
public static class NativeMetadataValues
{
    public static object Handle(long nativeValue) => new
    {
        native_type = "Handle",
        handle = unchecked((ulong)nativeValue).ToString("X", CultureInfo.InvariantCulture)
    };
}
