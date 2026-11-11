using System.Text.Json;
using NanoCAD.Contracts;

internal static class NativeMetadataEqualityTests
{
    static JsonElement Json(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }
    static void Need(bool condition, string message)
    { if (!condition) throw new Exception(message); }
    static void Same(JsonElement left, JsonElement right, bool expected, string message)
    {
        Need(NativeMetadataEquality.Same(left, right) == expected && NativeMetadataEquality.Same(right, left) == expected, message);
    }

    internal static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("native metadata distinguishes adjacent signed and unsigned64 user values", () =>
        {
            foreach (var values in new (long Before, long After)[]
            {
                (9007199254740992L, 9007199254740993L),
                (long.MaxValue - 1, long.MaxValue),
                (long.MinValue, long.MinValue + 1)
            })
            {
                var before = Wire.Element(new { payload = new { sequence = values.Before } });
                var after = Wire.Element(new { payload = new { sequence = values.After } });
                Same(before, after, false, "An adjacent signed64 user value was silently accepted as unchanged.");
                Same(before, Json(before.GetRawText()), true, "An unchanged signed64 payload did not survive JSON transport.");
            }
            foreach (var values in new (ulong Before, ulong After)[]
            {
                (0x8000000000000000UL, 0x8000000000000001UL),
                (ulong.MaxValue - 1, ulong.MaxValue)
            })
            {
                var before = Wire.Element(new { payload = new { sequence = values.Before } });
                var after = Wire.Element(new { payload = new { sequence = values.After } });
                Same(before, after, false, "An adjacent unsigned64 user value lost its high-bit distinction.");
                Same(before, Json(before.GetRawText()), true, "An unchanged unsigned64 payload did not survive JSON transport.");
            }
            return Task.CompletedTask;
        });
        await test("native metadata compares decoded strings and escaped property names", () =>
        {
            var literal = Json("""{"name":"C:/Library/Valve","symbol":"Ω","line":"A\nB","quoted":"\"part\"","path":"C:\\Library\\Valve"}""");
            var escaped = Json("""{"path":"C:\u005cLibrary\u005cValve","quoted":"\u0022part\u0022","line":"A\u000AB","symbol":"\u03A9","na\u006de":"C:\/Library\/Valve"}""");
            Same(literal, escaped, true, "Equivalent string content or property-name escaping became a false metadata mismatch.");
            Same(Json("""{"label":"Valve"}"""), Json("""{"label":"valve"}"""), false,
                "Literal user text was compared without its ordinal case distinction.");
            return Task.CompletedTask;
        });
        await test("native metadata preserves exact object key sets while ignoring object field order", () =>
        {
            var original = Json("""{"common":{"owner":"A","enabled":true},"payload":[{"tag":"B","value":null}]}""");
            var reordered = Json("""{"payload":[{"value":null,"tag":"B"}],"common":{"enabled":true,"owner":"A"}}""");
            Same(original, reordered, true, "Object serialization order changed the meaning of retained native metadata.");
            Same(Json("""{"owner":"A","payload":null}"""), Json("""{"owner":"A"}"""), false,
                "An actual null-valued metadata property was confused with an absent property.");
            Same(Json("""{"owner":"A"}"""), Json("""{"Owner":"A"}"""), false,
                "A different metadata property name was treated as the same key.");
            return Task.CompletedTask;
        });
        await test("native metadata retains array order length and primitive value kinds", () =>
        {
            Same(Json("""[{"handle":"A"},{"handle":"B"}]"""), Json("""[{"handle":"B"},{"handle":"A"}]"""), false,
                "Ordered native buffer or dictionary-entry records were treated as an unordered set.");
            Same(Json("[null]"), Json("[]"), false, "A null array element disappeared without a metadata mismatch.");
            Same(Json("[true,null,17,\"17\"]"), Json("[true,null,17,\"17\"]"), true,
                "An unchanged mixed native payload did not compare equal.");
            foreach (var pair in new (string Before, string After)[]
            {
                ("true", "false"), ("true", "1"), ("false", "0"),
                ("null", "\"null\""), ("17", "\"17\""), ("{}", "[]")
            })
                Same(Json(pair.Before), Json(pair.After), false, "A metadata Boolean, null or JSON value-kind change was lost.");
            return Task.CompletedTask;
        });
        await test("native float metadata retains exact typed bit strings", () =>
        {
            foreach (var pair in new (string Kind, string Before, string After)[]
            {
                ("double", "3FF0000000000000", "3FF0000000000001"),
                ("double", "0000000000000000", "8000000000000000"),
                ("double", "7FF8000000000000", "7FF8000000000001"),
                ("float", "3F800000", "3F800001"),
                ("float", "00000000", "80000000")
            })
            {
                var before = Wire.Element(new { native_type = pair.Kind, bits = pair.Before });
                var after = Wire.Element(new { native_type = pair.Kind, bits = pair.After });
                Same(before, after, false, "A typed native floating-point payload lost its exact bits.");
                Same(before, Json(before.GetRawText()), true, "Unchanged native floating-point bits did not survive JSON transport.");
            }
            Same(Wire.Element(new { native_type = "float", bits = "00000000" }),
                Wire.Element(new { native_type = "double", bits = "0000000000000000" }), false,
                "A typed native value discriminator was ignored.");
            return Task.CompletedTask;
        });
    }
}
