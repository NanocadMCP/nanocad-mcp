using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class DynamicBlockPropertiesOutputSchemaTests
{
    private static void Need(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    private static JsonObject Schema(string method, JsonObject? reference = null)
    {
        var type = typeof(NanoCAD.McpServer.ToolService).Assembly.GetType("NanoCAD.McpServer.DynamicBlockPropertiesSchema", true)!;
        return (JsonObject)type.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [reference ?? new JsonObject { ["$ref"] = "#/$defs/ref" }])!;
    }
    private static JsonObject Fields(JsonObject schema) => schema["properties"]!.AsObject();
    private static JsonObject Info(JsonObject inventory) => Fields(inventory)["properties"]!["items"]!.AsObject();
    private static JsonObject NonNull(JsonNode schema) => schema["anyOf"]!.AsArray()
        .Select(item => item!.AsObject()).Single(item => item["type"]?.GetValue<string>() != "null");
    private static void ClosedRecord<T>(JsonObject schema)
    {
        string[] fields = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => property.Name).ToArray();
        Need(schema["type"]?.GetValue<string>() == "object" && schema["additionalProperties"]?.GetValue<bool>() == false &&
            Fields(schema).Select(field => field.Key).ToHashSet(StringComparer.Ordinal).SetEquals(fields) &&
            schema["required"]!.AsArray().Select(item => item!.GetValue<string>()).ToHashSet(StringComparer.Ordinal).SetEquals(fields),
            "The output schema does not match the complete closed wire record " + typeof(T).Name + ".");
    }
    private static Dictionary<string, JsonObject> Kinds(JsonNode schema) => schema["oneOf"]!.AsArray()
        .Select(item => item!.AsObject()).ToDictionary(item => Fields(item)["type"]!["const"]!.GetValue<string>(), StringComparer.Ordinal);

    internal static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("dynamic output schemas retain exact inventory receipt and nullable collection-slot records", () =>
        {
            var inventory = Schema("Inventory"); var change = Schema("Change");
            ClosedRecord<DynamicBlockPropertiesInventory>(inventory);
            ClosedRecord<DynamicBlockPropertyInfo>(Info(inventory));
            ClosedRecord<DynamicBlockPropertyChange>(change);
            var changes = Fields(change)["property_changes"]!["items"]!.AsObject();
            ClosedRecord<DynamicBlockObservedPropertyChange>(changes);
            foreach (string field in new[] { "previous_state", "state" })
            {
                var optional = Fields(changes)[field]!;
                Need(optional["anyOf"]!.AsArray().Any(item => item?["type"]?.GetValue<string>() == "null"),
                    "Absent before/after collection slots must remain nullable observations.");
                ClosedRecord<DynamicBlockPropertyInfo>(NonNull(optional));
            }
            var attributes = Fields(change)["attribute_changes"]!["items"]!.AsObject();
            ClosedRecord<DynamicBlockAttributeChange>(attributes);
            foreach (string field in new[] { "previous_state", "state" })
                Need(Fields(attributes)[field]!["type"]!.GetValue<string>() == "object" &&
                    Fields(attributes)[field]!["additionalProperties"]!.GetValue<bool>(),
                    "Actual opaque attribute snapshots must retain native members without fabricated replacement records.");
            foreach (string field in new[] { "previous_attribute_order", "attribute_order" })
                Need(Fields(change)[field]!["type"]!.GetValue<string>() == "array" &&
                    Fields(change)[field]!["items"]!["$ref"]!.GetValue<string>() == "#/$defs/ref",
                    "The actual before/after attribute identity order is missing.");
            return Task.CompletedTask;
        });

        await test("dynamic output distinguishes native writable types from raw read-only ObjectId observations", () =>
        {
            string[] writable = ["double", "int16", "int32", "string", "point2d", "point3d"];
            var inventory = Schema("Inventory"); var info = Fields(Info(inventory));
            var observed = Kinds(info["value"]!);
            Need(observed.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(writable.Concat(["object_id", "null", "unsupported"])),
                "A native observed value family was omitted or an unsupported setter type was invented.");
            Need(Kinds(Fields(Schema("Change"))["requested_value"]!).Keys.ToHashSet(StringComparer.Ordinal).SetEquals(writable),
                "The requested write value admits a read-only native observation type.");
            foreach (var (kind, length) in new[] { ("point2d", 2), ("point3d", 3) })
            {
                var point = Fields(observed[kind])["value"]!;
                Need(point["minItems"]!.GetValue<int>() == length && point["maxItems"]!.GetValue<int>() == length &&
                    point["items"]!["type"]!.GetValue<string>() == "number", "Native parameter point dimensionality was converted or lost.");
            }
            Need(Fields(observed["int16"])["value"]!["minimum"]!.GetValue<short>() == short.MinValue &&
                Fields(observed["int16"])["value"]!["maximum"]!.GetValue<short>() == short.MaxValue &&
                Fields(observed["int32"])["value"]!["minimum"]!.GetValue<int>() == int.MinValue &&
                Fields(observed["int32"])["value"]!["maximum"]!.GetValue<int>() == int.MaxValue,
                "The native integer value widths were coerced.");
            var identity = info["native_block_identity"]!.AsObject();
            Need(Fields(identity)["type"]!["const"]!.GetValue<string>() == "object_id", "The actual property BlockId became a guessed EntityRef.");
            var identityValue = Fields(identity)["value"]!.AsObject();
            var variants = identityValue["oneOf"]!.AsArray().Select(item => item!.AsObject()).ToArray();
            Need(variants.Length == 3 && variants.All(item => item["additionalProperties"]!.GetValue<bool>() == false) &&
                !identityValue.ToJsonString().Contains("$ref", StringComparison.Ordinal),
                "Raw native ObjectIds must expose their actual null/full/error observations without foreign EntityRefs.");
            var full = variants.Single(item => Fields(item).ContainsKey("raw_id_token"));
            Need(Fields(full).Select(item => item.Key).ToHashSet(StringComparer.Ordinal).SetEquals(
                ["is_null", "is_valid", "handle", "raw_id_token", "is_erased", "is_effectively_erased", "database_scope", "identity_scope"]) &&
                Fields(full)["database_scope"]!["enum"]!.AsArray().Select(item => item!.GetValue<string>()).ToHashSet(StringComparer.Ordinal)
                    .SetEquals(["current_document", "foreign_database", "unavailable"]) &&
                variants.Any(item => Fields(item).ContainsKey("observation_error")),
                "The native safe ObjectId observation lost its database scope, runtime token or unavailable state.");
            return Task.CompletedTask;
        });

        await test("dynamic property route publishes a callable typed selector and reaches the native Paper editor contract", async () =>
        {
            const string bridge = "11111111-1111-4111-8111-111111111111", document = "22222222-2222-4222-8222-222222222222";
            var input = Wire.Element(new
            {
                bridge_instance_id = bridge,
                document_id = document,
                operation_id = "33333333-3333-4333-8333-333333333333",
                expected_revision = "7",
                units = "Millimeters",
                target_space = "paper",
                operations = new[] {new
                {
                    operation="set_dynamic_block_property",handles=new[]{"AD"},expected_properties_token=new string('A',64),
                    property_index=0,property_name="Distance",value=new {type="double",value=12d}
                }}
            });
            var parsed = ModifyValidation.Parse(input);
            Need(parsed.TargetSpace == "paper" && parsed.Operations is [SetDynamicBlockPropertySpec { Handle: "AD", PropertyIndex: 0 }],
                "The published dynamic property branch did not reach its specialized Paper parser.");
            var focused = AuditServerContractSupport.Branch("modify_entities", "set_dynamic_block_property");
            var fields = focused.GetProperty("properties");
            Need(fields.GetProperty("value").GetProperty("properties").TryGetProperty("type", out _) &&
                fields.GetProperty("value").GetProperty("properties").TryGetProperty("value", out _) &&
                focused.GetProperty("required").EnumerateArray().Any(value => value.GetString() == "expected_properties_token"),
                "Current tokens or flat typed-value fields are missing from focused MCP publication.");
            var response = await new ToolService().CallAsync("get_guidance", new Dictionary<string, JsonElement>
            { ["topic"] = Wire.Element("commands"), ["section"] = Wire.Element("2d") }, default);
            using var catalog = JsonDocument.Parse(response.Content.OfType<TextContentBlock>().Single().Text);
            var route = catalog.RootElement.GetProperty("routes").EnumerateArray()
                .Single(item => item.GetProperty("tool").GetString() == "nanocad.modify_entities");
            Need(route.GetProperty("variants").EnumerateArray().Any(value => value.GetString() == "set_dynamic_block_property"),
                "The useful existing-block edit is absent from 2D command navigation.");
            var entity = AuditServerContractSupport.Output("inspect_objects").GetProperty("$defs").GetProperty("block_reference");
            Need(entity.GetProperty("properties").TryGetProperty("dynamic_properties", out _) &&
                entity.GetProperty("properties").TryGetProperty("dynamic_properties_unavailable_reason", out _),
                "Optional native-property failures cannot retain the base block geometry in the published output.");
        });
        await test("dynamic output builders detach all caller references across repeated schema composition", () =>
        {
            var reference = new JsonObject { ["$ref"] = "#/$defs/ref", ["description"] = "Caller reference" };
            var inventory = Schema("Inventory", reference); var change = Schema("Change", reference);
            Need(reference.Parent == null && inventory.Parent == null && change.Parent == null,
                "A reusable caller schema node was attached to a returned schema.");
            Fields(inventory)["instance_ref"]!["description"] = "Changed locally";
            Need(reference["description"]!.GetValue<string>() == "Caller reference" &&
                Fields(inventory)["dynamic_definition_ref"]!["description"]!.GetValue<string>() == "Caller reference" &&
                Fields(change)["source"]!["description"]!.GetValue<string>() == "Caller reference" &&
                Fields(Fields(change)["inventory"]!.AsObject())["instance_ref"]!["description"]!.GetValue<string>() == "Caller reference",
                "Changing one schema occurrence changed another reference or future inventory.");
            Fields(change)["previous_attribute_order"]!["items"]!["description"] = "Changed order reference";
            Need(Fields(change)["attribute_order"]!["items"]!["description"]!.GetValue<string>() == "Caller reference" &&
                Fields(Schema("Change", reference))["previous_attribute_order"]!["items"]!["description"]!.GetValue<string>() == "Caller reference",
                "The before/after attribute arrays or repeated builder calls share mutable schema nodes.");
            return Task.CompletedTask;
        });
    }
}
