using System.Text.Json;

namespace NanoCAD.Contracts;

// Native metadata numbers are integer lexemes. Floating-point native values
// already carry exact typed bit strings in NativeOwnedMetadata.Value.
public static class NativeMetadataEquality
{
    public static bool Same(JsonElement expected, JsonElement actual) => LibraryBlockMetadataComparison.FirstDifference(expected, actual) == null;
}
