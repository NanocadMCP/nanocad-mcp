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
        Description = "Read focused operating help. topic=commands returns the compact main command tree; optional section returns one category, including the separate PointCloud tools and modes. topic=schema returns the actual input JSON/index; topic=operation returns operating help and the same input contract. Select an exact variant to load only its branch. Other topics: parametric, documentation, point_clouds or recovery. No CAD access/writes. Read only help needed for the task; do not fetch all topics at startup or trial-write to discover fields.",
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
                ["variant"] = new JsonObject { ["type"] = "string", ["description"] = "For topic=schema or operation: exact entity type or operation name from the returned variants. Omit to read the branch index without loading every geometry schema." }
            },
            ["allOf"] = new JsonArray(
                new JsonObject { ["if"] = new JsonObject { ["properties"] = new JsonObject { ["topic"] = new JsonObject { ["enum"] = new JsonArray("schema", "operation") } } },
                    ["then"] = new JsonObject { ["required"] = new JsonArray("operation") }, ["else"] = Forbidden("operation", "variant") },
                new JsonObject { ["if"] = new JsonObject { ["properties"] = new JsonObject { ["topic"] = new JsonObject { ["const"] = "commands" } } },
                    ["else"] = Forbidden("section") })
        }),
        Annotations = new() { ReadOnlyHint = true, DestructiveHint = false, IdempotentHint = true, OpenWorldHint = false }
    };

    public static CallToolResult Read(IDictionary<string, JsonElement>? arguments)
    {
        if (arguments is null || arguments.Keys.Any(k => k is not ("topic" or "operation" or "variant" or "section")) ||
            !arguments.TryGetValue("topic", out var topicValue) || topicValue.ValueKind != JsonValueKind.String)
            throw new McpProtocolException("Supply a guidance topic.", McpErrorCode.InvalidParams);
        string topic = topicValue.GetString()!;
        bool hasOperation = arguments.TryGetValue("operation", out var value);
        bool hasVariant = arguments.TryGetValue("variant", out var variant);
        bool hasSection = arguments.TryGetValue("section", out var section);
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
            return new() { Content = [new TextContentBlock { Text = ReadSchema(value.GetString()!, hasVariant ? variant.GetString() : null) }] };
        }
        if (topic == "operation")
        {
            if (!hasOperation || value.ValueKind != JsonValueKind.String || !ToolCatalog.OperationNames.Contains(value.GetString()))
                throw new McpProtocolException("Choose an existing operation for operation help.", McpErrorCode.InvalidParams);
            string operation = value.GetString()!;
            string inputContract=ReadSchema(operation,hasVariant?variant.GetString():null);
            text=hasVariant
                ?ToolUsage.Purpose(operation)+" Use topic=recovery for an uncertain outcome."
                :OperationGuidance357.Read(operation)+"\n\n"+ToolCatalog.DetailedDescription(operation);
            text+="\n\nActual input contract (JSON):\n"+inputContract;
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
    internal static string ReadSchema(string operation, string? variant)
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
                ["next"] = "Read topic=schema with this operation and the exact needed variant. The response is its complete input schema. Use it for the real task; no trial writes are needed."
            };
            if (operation == "modify_entities") index["host_limits"] = ToolUsage.ModifyEntitiesHostLimits;
            return index.ToJsonString();
        }
        var selected = branches.Where(b => Key(b!) == variant).ToArray();
        if (selected.Length == 0) throw new McpProtocolException("Unknown variant. Read the schema branch index first.", McpErrorCode.InvalidParams);
        properties[field]!["items"] = selected.Length == 1 ? selected[0]!.DeepClone()
            : new JsonObject { ["oneOf"] = new JsonArray(selected.Select(b => b!.DeepClone()).ToArray()) };
        return schema.ToJsonString();
    }

}
