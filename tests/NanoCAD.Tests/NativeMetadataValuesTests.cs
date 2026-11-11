using System.Globalization;
using System.Text.Json;
using NanoCAD.Contracts;

internal static class NativeMetadataValuesTests
{
    static void Need(bool condition, string message) { if (!condition) throw new Exception(message); }
    internal static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("native Handle metadata preserves zero high bits and the full unsigned64 value", () =>
        {
            foreach (var pair in new (long Value, string Hex)[]
            {
                (0,"0"),(long.MinValue,"8000000000000000"),(long.MaxValue,"7FFFFFFFFFFFFFFF"),(-1,"FFFFFFFFFFFFFFFF"),
                (0xFFFFFFFFL,"FFFFFFFF"),(0x123456789ABCDEF0L,"123456789ABCDEF0")
            })
            {
                var value = Wire.Element(NativeMetadataValues.Handle(pair.Value));
                Need(value.GetProperty("native_type").GetString() == "Handle" && value.GetProperty("handle").GetString() == pair.Hex,
                    "A native handle became a signed decimal, lost high bits, or lost its typed representation.");
                Need(ulong.Parse(value.GetProperty("handle").GetString()!, NumberStyles.HexNumber, CultureInfo.InvariantCulture) == unchecked((ulong)pair.Value),
                    "The exact native64-bit handle value cannot round-trip from its wire representation.");
                Need(value.EnumerateObject().Select(field => field.Name).ToHashSet().SetEquals(["native_type", "handle"]),
                    "A raw observed Handle acquired an inferred object/database/foreign EntityRef identity.");
            }
            return Task.CompletedTask;
        });
        await test("native typed Handle metadata remains distinct from identical literal hexadecimal text", () =>
        {
            var literal = Wire.Element(new { code = 1000, value = "DEADBEEF" });
            var typed = Wire.Element(new { code = 1005, value = NativeMetadataValues.Handle(0xDEADBEEFL) });
            Need(literal.GetProperty("value").ValueKind == JsonValueKind.String && literal.GetProperty("value").GetString() == "DEADBEEF" &&
                typed.GetProperty("value").ValueKind == JsonValueKind.Object && typed.GetProperty("value").GetProperty("handle").GetString() == "DEADBEEF",
                "Literal text was interpreted as a native reference or a native reference was reduced to literal text.");
            return Task.CompletedTask;
        });
    }
}
