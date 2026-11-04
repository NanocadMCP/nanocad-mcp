using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class AuditServerContractSupport
{
    public static string Help(string operation, string? variant = null) => Guidance("operation", operation, variant);

    public static JsonElement Input(string operation, string? variant = null) =>
        JsonSerializer.Deserialize<JsonElement>(Guidance("schema", operation, variant));

    public static JsonElement Branch(string operation, string variant)
    {
        string field = operation == "create_entities" ? "entities" : "operations";
        return Input(operation, variant).GetProperty("properties").GetProperty(field).GetProperty("items");
    }

    public static JsonElement Output(string operation) => Expand(
        ToolCatalog.Tools.Single(tool => tool.Name == "nanocad." + operation).OutputSchema
        ?? throw new Exception("Published output schema is missing: " + operation));

    // Resolve published local references before inspecting semantic schema fields.
    // This accepts representation compaction without bypassing the actual catalog.
    public static JsonElement Expand(JsonElement schema)
    {
        var root = JsonNode.Parse(schema.GetRawText())!;
        JsonNode? Visit(JsonNode? node, HashSet<string> references)
        {
            if (node is JsonArray array)
                return new JsonArray(array.Select(item => Visit(item, references)).ToArray());
            if (node is not JsonObject obj) return node?.DeepClone();
            var result = new JsonObject();
            if (obj["$ref"] is JsonValue reference)
            {
                string path = reference.GetValue<string>();
                if (!path.StartsWith("#/", StringComparison.Ordinal) || !references.Add(path))
                    throw new Exception("Expected a finite local schema reference: " + path);
                JsonNode? target = root;
                foreach (string part in path[2..].Split('/'))
                    target = target?[part.Replace("~1", "/").Replace("~0", "~")];
                result = Visit(target ?? throw new Exception("Missing schema reference: " + path), references)!.AsObject();
                references.Remove(path);
            }
            foreach (var property in obj)
                if (property.Key != "$ref") result[property.Key] = Visit(property.Value, references);
            return result;
        }
        return JsonSerializer.SerializeToElement(Visit(root, new HashSet<string>(StringComparer.Ordinal)));
    }

    private static string Guidance(string topic, string operation, string? variant)
    {
        var arguments = new Dictionary<string, JsonElement>
        {
            ["topic"] = Wire.Element(topic),
            ["operation"] = Wire.Element(operation)
        };
        if (variant != null) arguments["variant"] = Wire.Element(variant);
        var response = new ToolService().CallAsync("get_guidance", arguments, default).GetAwaiter().GetResult();
        if (response.IsError == true) throw new Exception("Public guidance request failed: " + operation);
        return string.Join("\n", response.Content.OfType<TextContentBlock>().Select(block => block.Text));
    }
}
