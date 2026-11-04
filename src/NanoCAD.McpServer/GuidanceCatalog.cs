using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace NanoCAD.McpServer;

internal static class GuidanceCatalog
{
    public static Tool Tool() => new()
    {
        Name = "nanocad.get_guidance",
        Description = "Read focused operating help. topic=commands lists tool categories. topic=schema returns input JSON. With an exact variant, topic=operation shows its item schema and topic=schema includes the common request fields. Override with include_envelope. Other topics include parametric, documentation, point_clouds and recovery.",
        InputSchema = JsonSerializer.SerializeToElement(new JsonObject
        {
            ["type"] = "object", ["additionalProperties"] = false,
            ["required"] = new JsonArray("topic"),
            ["properties"] = new JsonObject
            {
                ["topic"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("commands", "schema", "operation", "parametric", "documentation", "point_clouds", "recovery") },
                ["section"] = new JsonObject { ["type"] = "string", ["description"] = "Optional for topic=commands only. Omit for the compact top-level index.", ["enum"] = new JsonArray(CommandTreeCatalog.SectionNames.Select(n => (JsonNode?)JsonValue.Create(n)).ToArray()) },
                ["operation"] = new JsonObject { ["type"] = "string", ["description"] = "Required for topic=operation or schema. Tool name without nanocad. prefix.",
                    ["enum"] = new JsonArray(ToolCatalog.OperationNames.Select(n => (JsonNode?)JsonValue.Create(n)).ToArray()) },
                ["variant"] = new JsonObject { ["type"] = "string", ["description"] = "For topic=schema or operation: exact entity type or operation name from the returned variants. Omit to read the branch index." },
                ["include_envelope"] = new JsonObject { ["type"] = "boolean", ["description"] = "With an exact variant, true includes the common request fields and false returns a reference to them. Defaults to true for schema and false for operation." }
            },
            ["allOf"] = new JsonArray(
                new JsonObject { ["if"] = new JsonObject { ["properties"] = new JsonObject { ["topic"] = new JsonObject { ["enum"] = new JsonArray("schema", "operation") } } },
                    ["then"] = new JsonObject { ["required"] = new JsonArray("operation") }, ["else"] = Forbidden("operation", "variant", "include_envelope") },
                new JsonObject { ["if"] = new JsonObject { ["required"] = new JsonArray("include_envelope") }, ["then"] = new JsonObject { ["required"] = new JsonArray("variant") } },
                new JsonObject { ["if"] = new JsonObject { ["properties"] = new JsonObject { ["topic"] = new JsonObject { ["const"] = "commands" } } },
                    ["else"] = Forbidden("section") })
        }),
        Annotations = new() { ReadOnlyHint = true, DestructiveHint = false, IdempotentHint = true, OpenWorldHint = false }
    };

    public static CallToolResult Read(IDictionary<string, JsonElement>? arguments)
    {
        if (arguments is null || arguments.Keys.Any(k => k is not ("topic" or "operation" or "variant" or "section" or "include_envelope")) ||
            !arguments.TryGetValue("topic", out var topicValue) || topicValue.ValueKind != JsonValueKind.String)
            throw new McpProtocolException("Supply a guidance topic.", McpErrorCode.InvalidParams);
        string topic = topicValue.GetString()!;
        bool hasOperation = arguments.TryGetValue("operation", out var value);
        bool hasVariant = arguments.TryGetValue("variant", out var variant);
        bool hasSection = arguments.TryGetValue("section", out var section);
        bool includeEnvelope = topic == "schema" || !hasVariant;
        if (arguments.TryGetValue("include_envelope", out var envelope))
        {
            if (topic is not ("schema" or "operation") || !hasVariant || envelope.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new McpProtocolException("include_envelope must be a boolean and requires an exact variant for topic=schema or operation.", McpErrorCode.InvalidParams);
            includeEnvelope = envelope.GetBoolean();
        }
        if (hasSection && (topic != "commands" || section.ValueKind != JsonValueKind.String || !CommandTreeCatalog.SectionNames.Contains(section.GetString(), StringComparer.Ordinal)))
            throw new McpProtocolException("section selects an existing category for topic=commands only.", McpErrorCode.InvalidParams);
        if (topic == "commands")
        {
            if (hasOperation || hasVariant) throw new McpProtocolException("Command-tree help uses only topic and optional section.", McpErrorCode.InvalidParams);
            return new() { Content = [new TextContentBlock { Text = CommandTreeCatalog.Read(hasSection ? section.GetString() : null) }] };
        }
        if (hasVariant && ((topic != "schema" && topic != "operation") || variant.ValueKind != JsonValueKind.String))
            throw new McpProtocolException("variant is a string for topic=schema or operation only.", McpErrorCode.InvalidParams);
        string text;
        if (topic == "schema")
        {
            if (!hasOperation || value.ValueKind != JsonValueKind.String || !ToolCatalog.OperationNames.Contains(value.GetString()))
                throw new McpProtocolException("Choose an existing operation for schema help.", McpErrorCode.InvalidParams);
            return new() { Content = [new TextContentBlock { Text = ReadInput(value.GetString()!, hasVariant ? variant.GetString() : null, includeEnvelope) }] };
        }
        if (topic == "operation")
        {
            if (!hasOperation || value.ValueKind != JsonValueKind.String || !ToolCatalog.OperationNames.Contains(value.GetString()))
                throw new McpProtocolException("Choose an existing operation for operation help.", McpErrorCode.InvalidParams);
            string operation = value.GetString()!;
            string inputContract = ReadInput(operation, hasVariant ? variant.GetString() : null, includeEnvelope);
            text = hasVariant ? "" : OperationGuidance357.Read(operation) + "\n\n" + ToolCatalog.DetailedDescription(operation) + "\n\n";
            text += (includeEnvelope ? "Actual input contract (JSON):\n" : "Variant input contract (JSON):\n") + inputContract;
        }
        else
        {
            if (hasOperation || topic is not ("parametric" or "documentation" or "point_clouds" or "recovery"))
                throw new McpProtocolException("Choose parametric, documentation, point_clouds or recovery without an operation.", McpErrorCode.InvalidParams);
            using var stream = typeof(ServerGuidance).Assembly.GetManifestResourceStream("NanoCAD.McpServer.Guidance." + topic)
                ?? throw new InvalidOperationException("Embedded operating help is missing.");
            using var reader = new StreamReader(stream);
            text = reader.ReadToEnd();
        }
        return new() { Content = [new TextContentBlock { Text = text }] };
    }
    private static JsonObject Forbidden(params string[] names) => new()
    {
        ["not"] = new JsonObject { ["anyOf"] = new JsonArray(names.Select(n => (JsonNode?)new JsonObject { ["required"] = new JsonArray(n) }).ToArray()) }
    };
    internal static string ReadSchema(string operation, string? variant) => ReadInput(operation, variant, true);

    private static string ReadInput(string operation, string? variant, bool includeEnvelope)
    {
        // Read the full canonical input, independent of publication prose projection.
        var schema = JsonNode.Parse(ToolCatalog.InputContract(operation).GetRawText())!.AsObject();
        string? field = operation == "create_entities" ? "entities" : operation is "modify_entities" or "modify_structure" ? "operations" : null;
        if (field is null)
        {
            if (variant is not null) throw new McpProtocolException("This tool has no selectable schema branches.", McpErrorCode.InvalidParams);
            return schema.ToJsonString();
        }
        var properties = schema["properties"]!.AsObject();
        var branches = properties[field]!["items"]!["oneOf"]!.AsArray();
        string Key(JsonNode node) => (operation == "create_entities"
            ? node["properties"]!["kind"]?["const"] ?? node["properties"]!["type"]!["const"]
            : node["properties"]!["operation"]!["const"])!.GetValue<string>();
        if (variant is null)
        {
            var envelope = (JsonObject)properties.DeepClone();
            envelope.Remove(field);
            var index = new JsonObject {
                ["tool"] = "nanocad." + operation, ["array_field"] = field,
                ["request_fields"] = envelope, ["required"] = schema["required"]!.DeepClone(),
                ["variants"] = new JsonArray(branches.Select(b => Key(b!)).Distinct().Select(k => (JsonNode?)JsonValue.Create(k)).ToArray()),
                ["next"] = "Read topic=operation with the exact variant for its item schema, or topic=schema for the complete input."
            };
            if (operation == "modify_entities") index["host_limits"] = ToolUsage.ModifyEntitiesHostLimits;
            return index.ToJsonString();
        }
        var selected = branches.Where(b => Key(b!) == variant).ToArray();
        if (selected.Length == 0) throw new McpProtocolException("Unknown variant. Read the schema branch index first.", McpErrorCode.InvalidParams);
        var item = selected.Length == 1 ? selected[0]!.DeepClone()
            : new JsonObject { ["oneOf"] = new JsonArray(selected.Select(b => b!.DeepClone()).ToArray()) };
        if (!includeEnvelope)
            return new JsonObject
            {
                ["tool"] = "nanocad." + operation,
                ["array_field"] = field,
                ["variant"] = variant,
                ["item_schema"] = item,
                ["envelope"] = $"Read get_guidance(topic=schema, operation={operation}) for common request fields, or include_envelope=true for the complete variant input."
            }.ToJsonString();
        properties[field]!["items"] = item;
        return schema.ToJsonString();
    }

}
