using System.Text.Json;
using System.Text.Json.Nodes;
using System.Reflection;
using NanoCAD.Contracts;
using NanoCAD.Ipc;

internal static class DynamicBlockPropertiesTests
{
    const string Bridge = "11111111-1111-4111-8111-111111111111";
    const string Document = "22222222-2222-4222-8222-222222222222";
    static string Token => new('a', 64);
    static void Need(bool condition, string message) { if (!condition) throw new Exception(message); }
    static JsonObject Input(string type = "double", JsonNode? value = null) => new()
    {
        ["operation"] = "set_dynamic_block_property",
        ["handles"] = new JsonArray("000aD"),
        ["expected_properties_token"] = Token,
        ["property_index"] = 2,
        ["property_name"] = "Distance1",
        ["value"] = new JsonObject { ["type"] = type, ["value"] = value ?? JsonValue.Create(12.5) }
    };
    static SetDynamicBlockPropertySpec Parse(JsonObject value) => DynamicBlockPropertiesValidation.Parse(Wire.Element(value), "$.operations[0]");
    static void Reject(JsonObject value, string path)
        => Reject(Wire.Element(value), path);
    static void Reject(JsonElement value, string path)
    {
        try { DynamicBlockPropertiesValidation.Parse(value, "$.operations[0]"); }
        catch (DomainException error)
        {
            Need(error.Error.code == "INVALID_ARGUMENT" && Wire.Element(error.Error.details!).GetProperty("path").GetString() == path,
                "Dynamic property refusal lost the actual input field."); return;
        }
        throw new Exception("Invalid native dynamic-property input was admitted.");
    }
    internal static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("dynamic typed value schema keeps callable fields visible without conditional schema support", () =>
        {
            var type = typeof(NanoCAD.McpServer.ToolService).Assembly.GetType("NanoCAD.McpServer.DynamicBlockPropertiesSchema", true)!;
            var schema = (JsonObject)type.GetMethod("Operation", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;
            var typed = Wire.Element(schema).GetProperty("properties").GetProperty("value");
            var fields = typed.GetProperty("properties");
            Need(typed.GetProperty("type").GetString() == "object" && !typed.GetProperty("additionalProperties").GetBoolean() &&
                typed.GetProperty("required").EnumerateArray().Select(item => item.GetString()).ToHashSet().SetEquals(["type", "value"]),
                "A client that ignores conditional branches cannot see the closed callable value envelope.");
            string[] types = ["double", "int16", "int32", "string", "point2d", "point3d"];
            Need(fields.GetProperty("type").GetProperty("enum").EnumerateArray().Select(item => item.GetString()).ToHashSet().SetEquals(types) &&
                fields.GetProperty("value").GetProperty("type").EnumerateArray().Select(item => item.GetString()).ToHashSet().SetEquals(["number", "string", "array"]) &&
                fields.GetProperty("value").GetProperty("minItems").GetInt32() == 2 && fields.GetProperty("value").GetProperty("maxItems").GetInt32() == 3 &&
                fields.GetProperty("value").GetProperty("items").GetProperty("type").GetString() == "number",
                "Flat native type/value fields lost their numeric, string or native point shapes.");
            Need(typed.GetProperty("oneOf").EnumerateArray().Select(branch => branch.GetProperty("properties").GetProperty("type").GetProperty("const").GetString())
                .ToHashSet().SetEquals(types), "Exact native type-specific branches were removed while exposing flat fields.");
            return Task.CompletedTask;
        });
        await test("dynamic strings preserve valid native Unicode and return exact paths for malformed Unicode", () =>
        {
            var valid = Input("string", JsonValue.Create("Native\t🔧\n")); valid["property_name"] = "Property\t🔧\n";
            Need(Parse(valid).PropertyName == "Property\t🔧\n" && Parse(valid).Value.value.GetString() == "Native\t🔧\n",
                "Valid native strings acquired arbitrary naming or length restrictions.");
            foreach (var pair in new (string Marker, JsonObject Input, string Path)[]
            {
                ("Distance1", Input(), "$.operations[0].property_name"),
                ("double", Input(), "$.operations[0].value.type"),
                ("TOKEN", Input("string", JsonValue.Create("TOKEN")), "$.operations[0].value.value"),
                ("000aD", Input(), "$.operations[0].handles[0]")
            })
            {
                using var malformed = JsonDocument.Parse(pair.Input.ToJsonString().Replace("\"" + pair.Marker + "\"", "\"\\uD800\"", StringComparison.Ordinal));
                Reject(malformed.RootElement, pair.Path);
            }
            return Task.CompletedTask;
        });
        await test("dynamic property selectors retain snapshot ordinal name token and exact native value types", () =>
        {
            var parsed = Parse(Input());
            Need(parsed.Handle == "AD" && parsed.ExpectedPropertiesToken == Token.ToUpperInvariant() && parsed.PropertyIndex == 2 &&
                parsed.PropertyName == "Distance1" && parsed.Value.type == "double" && parsed.Value.value.GetDouble() == 12.5,
                "The native snapshot selector or value was reinterpreted.");
            foreach (var pair in new (string Type, JsonNode Value)[]
            {
                ("int16", JsonValue.Create(short.MinValue)!), ("int32", JsonValue.Create(int.MaxValue)!),
                ("string", JsonValue.Create("")!), ("point2d", new JsonArray(1, 2)), ("point3d", new JsonArray(1, 2, -3))
            })
            {
                var result = Parse(Input(pair.Type, pair.Value));
                Need(result.Value.type == pair.Type, "The exact native runtime type was inferred from numeric magnitude.");
            }
            var integerDouble = Parse(Input("double", JsonValue.Create(1)));
            var integer = Parse(Input("int32", JsonValue.Create(1)));
            Need(!DynamicBlockPropertiesValidation.SameValue(integerDouble.Value, integer.Value), "Different native types became interchangeable.");
            Need(DynamicBlockPropertiesValidation.SameValue(new("double", Wire.Element(-0d)), new("double", Wire.Element(0d))) &&
                DynamicBlockPropertiesValidation.SameValue(new("point2d", Wire.Element(new[] { -0d, 1d })), new("point2d", Wire.Element(new[] { 0d, 1d }))),
                "Equivalent finite native zero values were treated as different geometry.");
            return Task.CompletedTask;
        });
        await test("dynamic property input rejects coercion guessed bounds identity aliases and extra writes", () =>
        {
            foreach (var pair in new (string Type, JsonNode Value)[]
            {
                ("int16", JsonValue.Create(32768)!), ("int32", JsonValue.Create(1.5)!),
                ("string", JsonValue.Create(12)!), ("point2d", new JsonArray(1, 2, 3)),
                ("point3d", new JsonArray(1, 2)), ("double", JsonValue.Create("12")!)
            }) Reject(Input(pair.Type, pair.Value), "$.operations[0].value.value");
            foreach (string type in new[] { "boolean", "object_id", "radius", "degrees", "Millimeters" })
                Reject(Input(type, JsonValue.Create(1)), "$.operations[0].value.type");
            var negative = Input(); negative["property_index"] = -1; Reject(negative, "$.operations[0].property_index");
            var shortToken = Input(); shortToken["expected_properties_token"] = new string('A', 63); Reject(shortToken, "$.operations[0].expected_properties_token");
            var nameOnly = Input(); nameOnly.Remove("property_index"); Reject(nameOnly, "$.operations[0].property_index");
            var twoHandles = Input(); twoHandles["handles"] = new JsonArray("AD", "AE"); Reject(twoHandles, "$.operations[0].handles");
            var guessed = Input(); guessed["parameter_id"] = "Distance1"; Reject(guessed, "$.operations[0].parameter_id");
            try { DynamicBlockPropertiesValidation.Batch([Parse(Input()), new MoveSpec(["AE"], new(1, 0))]); }
            catch (DomainException) { return Task.CompletedTask; }
            throw new Exception("A second operation could use a collection invalidated by native regeneration.");
        });
        await test("dynamic native value and current selector changes are included in ordinary replay fingerprints", () =>
        {
            ModifyArguments Arguments(SetDynamicBlockPropertySpec operation) => new(Bridge, Document, "7", Guid.Empty.ToString(), "Millimeters", [operation]);
            var baseline = Parse(Input()); string fingerprint = Arguments(baseline).Fingerprint();
            Need(Arguments(Parse(Input("double", JsonValue.Create(-0d)))).Fingerprint() ==
                Arguments(Parse(Input("double", JsonValue.Create(0d)))).Fingerprint(), "Equivalent native zero values changed replay intent.");
            foreach (var changed in new[]
            {
                baseline with { PropertyIndex = 3 }, baseline with { PropertyName = "Other" },
                baseline with { ExpectedPropertiesToken = new string('B', 64) },
                baseline with { Value = new("int32", Wire.Element(12)) }, baseline with { Value = new("double", Wire.Element(13d)) }
            }) Need(Arguments(changed).Fingerprint() != fingerprint, "A native selector, token or typed value disappeared from replay intent.");
            return Task.CompletedTask;
        });
        await test("native dynamic attribute effects preserve identity literals style user format and bindings", () =>
        {
            var original = new JsonObject
            {
                ["handle"] = "A1",
                ["owner"] = "AD",
                ["Tag"] = "PART",
                ["TextString"] = "User value",
                ["style"] = "F1",
                ["Height"] = 2.5,
                ["position"] = new JsonArray(0, 0, 0),
                ["Rotation"] = 0,
                ["Invisible"] = false,
                ["display"] = new JsonObject { ["common"] = new JsonObject { ["visible"] = true, ["color"] = "ByLayer" } },
                ["metadata"] = new JsonObject { ["binding"] = "B7" }
            };
            string Meaning(JsonObject state) => DynamicBlockPropertiesValidation.RetainedAttributeState(Wire.Element(state)).GetRawText();
            var nativeEffect = original.DeepClone().AsObject(); nativeEffect["position"] = new JsonArray(10, 0, 0);
            nativeEffect["Rotation"] = 1.5; nativeEffect["Invisible"] = true; nativeEffect["display"]!["common"]!["visible"] = false;
            Need(Meaning(original) == Meaning(nativeEffect), "A native movement/orientation/visibility effect became an identity or literal rewrite.");
            foreach (string field in new[] { "handle", "owner", "Tag", "TextString", "style", "Height", "metadata" })
            {
                var corrupted = nativeEffect.DeepClone().AsObject(); corrupted[field] = "changed";
                Need(Meaning(original) != Meaning(corrupted), "A native effect hid a changed attribute identity, user format, literal or binding.");
            }
            return Task.CompletedTask;
        });
        await test("dynamic property receipts preserve native representations and fresh inventory on existing replay", () =>
        {
            EntityRef Ref(string handle) => new(Bridge, Document, handle);
            var value = new DynamicBlockPropertyValue("double", Wire.Element(12d));
            var property = new DynamicBlockPropertyInfo(2, "Distance1", "Native parameter", 40,
                new("object_id", Wire.Element(new { handle = "AD", database_scope = "current_document" })),
                2, "Distance", "System.Double", value, [], false, true, true, true, null);
            var inventory = new DynamicBlockPropertiesInventory(Ref("AD"), Ref("B1"), Ref("C2"), Ref("C2"), Token.ToUpperInvariant(),
                "current_complete_snapshot_ordinal", "native_parameter_units_and_frame; no_conversion", [property], true, false);
            var change = new DynamicBlockPropertyChange(0, Ref("AD"), Ref("B1"), Ref("C1"), Ref("C2"), 2, "Distance1",
                value, value, "before_selector_and_native_wrapper_witness",
                [new(2, "Distance1", property with { value = new("double", Wire.Element(10d)) }, property, true)], [], [Ref("D1"), Ref("D2")], [Ref("D2"), Ref("D1")], inventory);
            var legacy = new ModifiedEntities("operation", "committed", false, [Ref("AD")], [], [], [], "not_guaranteed");
            Need(!Wire.Element(legacy).TryGetProperty("dynamic_block_changes", out _), "Ordinary receipts acquired required dynamic metadata.");
            var result = Result.Ok("original", legacy with { dynamic_block_changes = [change] });
            var replay = WriteJob.ForCaller(result, "lookup", true);
            Need(replay.data is ModifiedEntities { replayed: true, dynamic_block_changes: not null }, "Ordinary result replay lost the optional dynamic receipt.");
            var serialized = Wire.Element(replay.data!).GetProperty("dynamic_block_changes")[0];
            Need(serialized.GetProperty("previous_representation").GetProperty("handle").GetString() == "C1" &&
                serialized.GetProperty("representation").GetProperty("handle").GetString() == "C2" &&
                serialized.GetProperty("previous_attribute_order")[0].GetProperty("handle").GetString() == "D1" &&
                serialized.GetProperty("attribute_order")[0].GetProperty("handle").GetString() == "D2" &&
                !serialized.GetProperty("inventory").GetProperty("geometry_prediction_available").GetBoolean(),
                "Native presentation identity was aliased or validation acquired a fabricated geometry prediction.");
            return Task.CompletedTask;
        });
        await test("dynamic property admission reserves known mandatory receipt bytes before evaluation", () =>
        {
            EntityRef Ref(string handle) => new(Bridge, Document, handle);
            var small = new DynamicBlockPropertyValue("string", Wire.Element("A"));
            var allowed = new DynamicBlockPropertyValue("string", Wire.Element(new string('x', 95000)));
            var property = new DynamicBlockPropertyInfo(0, "Lookup", "", 1,
                new("object_id", Wire.Element(new { handle = "AD", database_scope = "current_document" })),
                0, "NoUnits", "System.String", small, [small, allowed], false, true, true, true, null);
            var inventory = new DynamicBlockPropertiesInventory(Ref("AD"), Ref("B1"), Ref("C2"), Ref("C2"), Token.ToUpperInvariant(),
                "current_complete_snapshot_ordinal", "native_parameter_units_and_frame; no_conversion", [property], true, false);
            SerializedCapacity357.Check(inventory, Wire.PayloadBudget - 8192);
            var edit = new SetDynamicBlockPropertySpec("AD", Token.ToUpperInvariant(), 0, "Lookup", small);
            var args = new ModifyArguments(Bridge, Document, "7", Guid.Empty.ToString(), "Millimeters", [edit]);
            try { SerializedCapacity357.Check(DynamicBlockPropertiesValidation.ProjectedResult(args, edit, inventory), Wire.PayloadBudget - 8192); }
            catch (DomainException error) when (error.Error.code == "RESPONSE_LIMIT_EXCEEDED") { return Task.CompletedTask; }
            throw new Exception("A readable inventory's known duplicated receipt could exceed transport only after the native setter.");
        });
    }
}
