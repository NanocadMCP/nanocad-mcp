using System.Text.Json;
using NanoCAD.Contracts;

internal static class LibraryBlockCreationTimeTests
{
    static JsonElement Json(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }
    static void Need(bool condition, string message)
    { if (!condition) throw new Exception(message); }
    static JsonElement Stamp(int low, int high) => Wire.Element(new object[]
    {
        new { code = 1001, value = "mc3dCrTime" },
        new { code = 1071, value = low },
        new { code = 1071, value = high }
    });
    static void Reject(JsonElement source, JsonElement actual, long nativeValue, string message)
    {
        try { _ = LibraryBlockCreationTime.ReadAdded(source, actual, nativeValue); }
        catch (DomainException error)
        {
            Need(error.Error.code == "NATIVE_BLOCK_LIBRARY_MISMATCH",
                "Invalid creation-stamp evidence did not produce the native library mismatch refusal.");
            return;
        }
        throw new Exception(message);
    }

    internal static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("library creation stamp admits the observed native three-record value", () =>
        {
            var source = Json("null");
            var actual = Stamp(-1505348656, 31289785);
            var stamp = LibraryBlockCreationTime.ReadAdded(source, actual, 134388606063490000L);
            Need(stamp.application == "mc3dCrTime" && stamp.index == 0 &&
                stamp.low_word == -1505348656 && stamp.high_word == 31289785 &&
                stamp.value == 134388606063490000L,
                "The actual native creation stamp lost its application, position, signed words or exact value.");
            Need(NativeMetadataEquality.Same(LibraryBlockCreationTime.ExpectedXData(source, stamp), actual),
                "The admitted native stamp did not reconstruct the exact observed XData.");
            return Task.CompletedTask;
        });
        await test("library creation stamp preserves both unsigned words and signed64 high bits", () =>
        {
            foreach (var value in new (int Low, int High, long Native)[]
            {
                (-1, 1, 8589934591L),
                (int.MinValue, 0, 2147483648L),
                (0, int.MinValue, long.MinValue),
                (int.MinValue, -1, -2147483648L),
                (-1, -1, -1L)
            })
            {
                var actual = Stamp(value.Low, value.High);
                var stamp = LibraryBlockCreationTime.ReadAdded(Json("[]"), actual, value.Native);
                Need(stamp.value == value.Native && stamp.low_word == value.Low && stamp.high_word == value.High,
                    "A signed low word was sign-extended, a high word was truncated, or native64 bits were changed.");
                Need(NativeMetadataEquality.Same(LibraryBlockCreationTime.ExpectedXData(Json("[]"), stamp), actual),
                    "A full-width creation stamp did not retain the exact original typed records.");
            }
            return Task.CompletedTask;
        });
        await test("library creation stamp rejects malformed duplicate or expanded native records", () =>
        {
            foreach (var invalid in new[]
            {
                "null",
                "{}",
                """[{"code":1001,"value":"mc3dCrTime"},{"code":1071,"value":-1505348656}]""",
                """[{"code":1001,"value":"mc3dCrTime"},{"code":1070,"value":-1505348656},{"code":1071,"value":31289785}]""",
                """[{"code":1001,"value":"mc3dCrTime"},{"code":1071,"value":"-1505348656"},{"code":1071,"value":31289785}]""",
                """[{"code":1001,"value":"mc3dCrTime"},{"code":1071,"value":-2147483649},{"code":1071,"value":31289785}]""",
                """[{"code":1001,"value":"mc3dCrTime"},{"code":1071,"value":-1505348656},{"code":1071,"value":2147483648}]""",
                """[{"code":1001,"value":"mc3dCrTime"},{"code":1071,"value":-1505348656},{"code":1071,"value":null}]""",
                """[{"code":1001,"value":"mc3dCrTime"},{"value":-1505348656},{"code":1071,"value":31289785}]""",
                """[{"code":1001,"value":"mc3dCrTime","extra":true},{"code":1071,"value":-1505348656},{"code":1071,"value":31289785}]""",
                """[{"code":1001,"value":"mc3dCrTime"},{"code":1071,"value":-1505348656,"extra":true},{"code":1071,"value":31289785}]""",
                """[{"code":1001,"value":"mc3dCrTime"},{"code":1071,"value":-1505348656},{"code":1071,"value":31289785},{"code":1000,"value":"extra user data"}]""",
                """[{"code":1001,"value":"mc3dCrTime"},{"code":1071,"value":-1505348656},{"code":1071,"value":31289785},{"code":1001,"value":"mc3dCrTime"},{"code":1071,"value":-1505348656},{"code":1071,"value":31289785}]"""
            })
                Reject(Json("null"), Json(invalid), 134388606063490000L,
                    "Malformed, duplicate or additional creation-stamp data was admitted as the one native field.");
            return Task.CompletedTask;
        });
        await test("library creation stamp never normalizes an application already present in the source", () =>
        {
            var actual = Stamp(-1505348656, 31289785);
            Reject(actual, actual, 134388606063490000L,
                "Existing source creation-stamp data was treated as a newly generated intrinsic field.");
            Reject(Json("""[{"code":1001,"value":"mc3dCrTime"},{"code":1000,"value":"retained user payload"}]"""),
                actual, 134388606063490000L, "A malformed existing source application was silently replaced.");
            Reject(Json("""[{"code":1001,"value":"MC3DCRTIME"},{"code":1000,"value":"retained user payload"}]"""),
                actual, 134388606063490000L, "A case variant of an existing reserved source application escaped preservation.");
            return Task.CompletedTask;
        });
        await test("library creation stamp retains unrelated apps literals mapped references and buffer order", () =>
        {
            var source = Json("""[{"code":1001,"value":"USER_A"},{"code":1000,"value":"mc3dCrTime"},{"code":1005,"value":{"native_type":"Handle","handle":"544"}},{"code":1001,"value":"USER_Z"},{"code":1071,"value":2147483647},{"code":1000,"value":"KEEP"}]""");
            var mappedSource = Json("""[{"code":1001,"value":"USER_A"},{"code":1000,"value":"mc3dCrTime"},{"code":1005,"value":{"native_type":"Handle","handle":"A19"}},{"code":1001,"value":"USER_Z"},{"code":1071,"value":2147483647},{"code":1000,"value":"KEEP"}]""");
            var actual = Json("""[{"code":1001,"value":"USER_A"},{"code":1000,"value":"mc3dCrTime"},{"code":1005,"value":{"native_type":"Handle","handle":"A19"}},{"code":1001,"value":"mc3dCrTime"},{"code":1071,"value":-1505348656},{"code":1071,"value":31289785},{"code":1001,"value":"USER_Z"},{"code":1071,"value":2147483647},{"code":1000,"value":"KEEP"}]""");
            var stamp = LibraryBlockCreationTime.ReadAdded(source, actual, 134388606063490000L);
            Need(stamp.index == 3, "The actual native application position was replaced with an inferred order.");
            var expected = LibraryBlockCreationTime.ExpectedXData(mappedSource, stamp);
            Need(NativeMetadataEquality.Same(expected, actual),
                "Intrinsic stamp admission changed unrelated application data, literal text, mapped handles or record order.");
            foreach (var altered in new[]
            {
                actual.GetRawText().Replace("\"KEEP\"", "\"CHANGED\"", StringComparison.Ordinal),
                actual.GetRawText().Replace("\"USER_Z\"", "\"user_z\"", StringComparison.Ordinal),
                actual.GetRawText().Replace("\"A19\"", "\"544\"", StringComparison.Ordinal),
                actual.GetRawText().Replace("2147483647", "2147483646", StringComparison.Ordinal),
                actual.GetRawText().Replace("{\"code\":1071,\"value\":2147483647},{\"code\":1000,\"value\":\"KEEP\"}",
                    "{\"code\":1000,\"value\":\"KEEP\"},{\"code\":1071,\"value\":2147483647}", StringComparison.Ordinal)
            })
                Need(!NativeMetadataEquality.Same(expected, Json(altered)),
                    "Creation-stamp admission hid an unrelated literal, reference, numeric value or ordered-record change.");
            return Task.CompletedTask;
        });
        await test("library creation stamp requires the exact nonzero native getter witness", () =>
        {
            var actual = Stamp(-1505348656, 31289785);
            Reject(Json("null"), actual, 134388606063490001L,
                "A native getter value different by one bit was accepted for the added creation stamp.");
            Reject(Json("null"), actual, 0,
                "An unavailable/default native getter value was accepted as a stamp witness.");
            Reject(Json("null"), Stamp(0, 0), 0,
                "An all-zero stamp and default native getter fabricated a supported creation-time observation.");
            return Task.CompletedTask;
        });
        await test("library creation-time receipt preserves opaque64 bits raw words and reference roles", () =>
        {
            var raw = Stamp(-1, int.MinValue);
            var stamp = LibraryBlockCreationTime.ReadAdded(Json("null"), raw, unchecked((long)0x80000000FFFFFFFFUL));
            var target = new EntityRef("bridge", "document", "A19");
            var owner = new EntityRef("bridge", "document", "A10");
            var application = new EntityRef("bridge", "document", "A01");
            var legacy = new BlockLibraryDwgResult("C:/Library/source.dwg", new string('A', 64), [0, 0, 0],
                [0, 0, 0], 1, [], [], []);
            Need(!Wire.Element(legacy).TryGetProperty("native_creation_times", out _),
                "An absent optional creation-time receipt changed the legacy library result.");
            var result = Wire.Element(legacy with
            {
                native_creation_times = [new("544", target, owner, application, "80000000FFFFFFFF", raw, true,
                    "actual_native_getter_and_identity")]
            });
            var receipt = result.GetProperty("native_creation_times")[0];
            Need(receipt.GetProperty("native_value_hex16").ValueKind == JsonValueKind.String &&
                receipt.GetProperty("native_value_hex16").GetString() == "80000000FFFFFFFF",
                "The high-bit native creation value was exposed as a lossy JSON number or changed hexadecimal text.");
            Need(receipt.GetProperty("source_handle").GetString() == "544" &&
                receipt.GetProperty("source_application_absent").GetBoolean() &&
                receipt.GetProperty("corroboration").GetString() == "actual_native_getter_and_identity",
                "The receipt lost the source selector or scope of its native corroboration.");
            foreach (var role in new[] { (Name: "target_ref", Value: target), (Name: "owner_ref", Value: owner),
                (Name: "registered_application_ref", Value: application) })
            {
                var reference = receipt.GetProperty(role.Name);
                Need(reference.GetProperty("handle").GetString() == role.Value.handle &&
                    reference.GetProperty("bridge_instance_id").GetString() == role.Value.bridge_instance_id &&
                    reference.GetProperty("document_id").GetString() == role.Value.document_id,
                    "The receipt conflated the actual target, owner or registered-application reference roles.");
            }
            var records = receipt.GetProperty("raw_records");
            Need(records.GetArrayLength() == 3 && records[0].GetProperty("code").GetInt32() == 1001 &&
                records[0].GetProperty("value").GetString() == "mc3dCrTime" &&
                records[1].GetProperty("code").GetInt32() == 1071 && records[1].GetProperty("value").GetInt32() == -1 &&
                records[2].GetProperty("code").GetInt32() == 1071 && records[2].GetProperty("value").GetInt32() == int.MinValue,
                "The receipt did not retain the original signed Int32 native creation-time records.");
            Need(!Wire.Element(stamp).TryGetProperty("value", out _) && !receipt.TryGetProperty("value", out _),
                "An opaque native Int64 escaped into JSON as a potentially lossy numeric field.");
            return Task.CompletedTask;
        });
    }
}
