using System.Text.Json;
using System.Text.Json.Serialization;

namespace NanoCAD.Contracts;

public sealed record LibraryBlockCreationStamp(string application, int index, int low_word, int high_word, [property: JsonIgnore] long value, JsonElement raw_records);

// The installed native block-reference creation-time implementation stores the
// exact Int64 as low/high Int32 DXF1071 words. This helper validates data only;
// the bridge separately proves the actual native object/getter and ownership.
public static class LibraryBlockCreationTime
{
    public const string Application = "mc3dCrTime";
    static void Need(bool condition, string message)
    { if (!condition) throw new DomainException("NATIVE_BLOCK_LIBRARY_MISMATCH", message); }
    static bool Code(JsonElement record, int value) => record.ValueKind == JsonValueKind.Object &&
        record.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.Number && code.TryGetInt32(out int actual) && actual == value;
    public static bool ContainsApplication(JsonElement xdata) => xdata.ValueKind == JsonValueKind.Array && xdata.EnumerateArray().Any(record =>
        Code(record, 1001) && record.TryGetProperty("value", out var name) && name.ValueKind == JsonValueKind.String &&
        string.Equals(name.GetString(), Application, StringComparison.OrdinalIgnoreCase));
    static bool Shape(JsonElement record, int code) => Code(record, code) && record.EnumerateObject().Count() == 2 && record.TryGetProperty("value", out _);
    public static LibraryBlockCreationStamp ParseAdded(JsonElement sourceXdata, JsonElement actualXdata)
    {
        Need(sourceXdata.ValueKind is JsonValueKind.Null or JsonValueKind.Array, "The original source XData observation is unavailable.");
        Need(!ContainsApplication(sourceXdata), "Existing source creation-time application data remains subject to exact preservation; it is not a newly added field.");
        Need(actualXdata.ValueKind == JsonValueKind.Array, "The added native creation-time data is unavailable.");
        int found = -1;
        for (int index = 0; index < actualXdata.GetArrayLength(); index++)
        {
            var record = actualXdata[index];
            if (!Code(record, 1001) || !record.TryGetProperty("value", out var name) || name.ValueKind != JsonValueKind.String ||
                !string.Equals(name.GetString(), Application, StringComparison.OrdinalIgnoreCase)) continue;
            Need(found < 0, "The destination contains repeated creation-time application blocks."); found = index;
        }
        Need(found >= 0 && found + 2 < actualXdata.GetArrayLength(), "A complete added creation-time application block is unavailable.");
        var app = actualXdata[found]; var low = actualXdata[found + 1]; var high = actualXdata[found + 2];
        Need(Shape(app, 1001) && app.GetProperty("value").GetString() == Application && Shape(low, 1071) && Shape(high, 1071),
            "The native creation-time block has a different application, DXF layout or additional fields.");
        Need(found + 3 == actualXdata.GetArrayLength() || Code(actualXdata[found + 3], 1001), "The creation-time application contains additional payload.");
        Need(low.GetProperty("value").ValueKind == JsonValueKind.Number && low.GetProperty("value").TryGetInt32(out _) &&
            high.GetProperty("value").ValueKind == JsonValueKind.Number && high.GetProperty("value").TryGetInt32(out _),
            "Native creation-time words must be actual signed Int32 DXF1071 values.");
        int lowWord = low.GetProperty("value").GetInt32(), highWord = high.GetProperty("value").GetInt32();
        long combined = unchecked((long)(((ulong)(uint)highWord << 32) | (uint)lowWord));
        return new(Application, found, lowWord, highWord, combined, Wire.Element(new[] { app, low, high }));
    }
    public static LibraryBlockCreationStamp ReadAdded(JsonElement sourceXdata, JsonElement actualXdata, long nativeCreationTime)
    {
        var stamp = ParseAdded(sourceXdata, actualXdata);
        // The supplied IMcBlockRef base implementation returns zero. Zero does
        // not corroborate an actual implemented native creation-time getter.
        Need(nativeCreationTime != 0 && stamp.value == nativeCreationTime, "The added words do not match an implemented native block-reference creation-time getter.");
        return stamp;
    }
    public static JsonElement ExpectedXData(JsonElement mappedSourceXdata, LibraryBlockCreationStamp stamp)
    {
        Need(mappedSourceXdata.ValueKind is JsonValueKind.Null or JsonValueKind.Array, "Mapped source XData is unavailable.");
        Need(!ContainsApplication(mappedSourceXdata), "Mapped source application data cannot be replaced by a new creation-time field.");
        var original = mappedSourceXdata.ValueKind == JsonValueKind.Null ? Array.Empty<JsonElement>() : mappedSourceXdata.EnumerateArray().ToArray();
        Need(stamp.index >= 0 && stamp.index <= original.Length, "The added creation-time block is not at a source application boundary.");
        Need(stamp.index == original.Length || Code(original[stamp.index], 1001), "The added creation-time block splits source application data.");
        // Revalidate a caller's record rather than trusting a constructed DTO.
        var checkedStamp = ReadAdded(JsonSerializer.SerializeToElement<object?>(null, Wire.Json), stamp.raw_records, stamp.value);
        Need(checkedStamp.application == stamp.application && checkedStamp.low_word == stamp.low_word && checkedStamp.high_word == stamp.high_word,
            "The creation-time witness differs from its retained raw records.");
        return Wire.Element(original.Take(stamp.index).Concat(stamp.raw_records.EnumerateArray()).Concat(original.Skip(stamp.index)).ToArray());
    }
}
