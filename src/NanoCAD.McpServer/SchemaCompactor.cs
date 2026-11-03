using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class SchemaCompactor
{
    // Some MCP clients render local references as unknown instead of resolving them.
    // Publish self-contained input branches; never trade callable fields for byte savings.
    public static JsonElement InlineInput(JsonElement schema)
    {
        var root = JsonNode.Parse(schema.GetRawText())!.AsObject();
        JsonNode? Expand(JsonNode? node, HashSet<string> stack)
        {
            if (node is JsonArray array)
                return new JsonArray(array.Select(x => Expand(x, stack)).ToArray());
            if (node is not JsonObject obj) return node?.DeepClone();
            var result = new JsonObject();
            if (obj["$ref"] is JsonValue reference)
            {
                string path = reference.GetValue<string>();
                if (!path.StartsWith("#/", StringComparison.Ordinal) || !stack.Add(path))
                    throw new InvalidOperationException("Input schema requires finite local references.");
                JsonNode? target = root;
                foreach (string part in path[2..].Split('/'))
                    target = target?[part.Replace("~1", "/").Replace("~0", "~")];
                result = Expand(target ?? throw new InvalidOperationException("Missing input schema reference."), stack)!.AsObject();
                stack.Remove(path);
            }
            foreach (var pair in obj)
                if (pair.Key is not ("$ref" or "$defs" or "definitions"))
                    result[pair.Key] = Expand(pair.Value, stack);
            return result;
        }
        return JsonSerializer.SerializeToElement(Expand(root, new HashSet<string>(StringComparer.Ordinal)));
    }

    // Keep every callable field and validation keyword inline. Only direct
    // variant operating prose moves to focused guidance backed by the full input.
    public static JsonElement PublishInput(string operation, JsonElement input)
    {
        string? field = operation == "create_entities" ? "entities"
            : operation is "modify_entities" or "modify_structure" ? "operations" : null;
        if (field is null) return input;
        var root = JsonNode.Parse(input.GetRawText())!.AsObject();
        var items = root["properties"]?[field]?["items"]?.AsObject()
            ?? throw new InvalidOperationException("Missing published variant array.");
        var branches = items["oneOf"]?.AsArray()
            ?? throw new InvalidOperationException("Missing published variant branches.");
        if (items.ContainsKey("description"))
            throw new InvalidOperationException("Variant discovery would overwrite existing guidance.");
        foreach (var branch in branches)
            (branch?.AsObject() ?? throw new InvalidOperationException("Invalid published branch.")).Remove("description");
        string discriminator = operation == "create_entities" ? "kind or type" : "operation";
        items["description"] = "All input fields and validation remain inline. Before writing a variant, read " +
            $"get_guidance(topic=operation, operation={operation}, variant=exact {discriminator} value) " +
            "for its full operating limits and complete input contract. No trial writes.";
        return JsonSerializer.SerializeToElement(root);
    }

    // Factor identical schema objects through local JSON Schema references.
    // Values, descriptions, required fields and validation keywords are unchanged.
    public static JsonElement Compact(JsonElement schema)
    {
        var root = JsonNode.Parse(schema.GetRawText())!.AsObject();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        void Count(JsonObject node)
        {
            string key = node.ToJsonString();
            if (key.Length >= 150) counts[key] = counts.GetValueOrDefault(key) + 1;
            foreach (var child in Children(node)) Count(child);
        }
        Count(root);
        var definitions = root["$defs"] as JsonObject ?? new JsonObject();
        var added = new Dictionary<string, string>(StringComparer.Ordinal);
        int sequence = 0;
        JsonObject Rewrite(JsonObject node, bool canShare)
        {
            string key = node.ToJsonString();
            if (canShare && counts.GetValueOrDefault(key) > 1)
            {
                if (!added.TryGetValue(key, out var name))
                {
                    do { name = "mcp_shared_" + sequence++; } while (definitions.ContainsKey(name));
                    added.Add(key, name);
                    definitions[name] = Rewrite(node, false);
                }
                return new JsonObject { ["$ref"] = "#/$defs/" + name };
            }
            var copy = (JsonObject)node.DeepClone();
            foreach (var pair in copy.ToArray())
            {
                if (pair.Value is JsonObject map && MapKeywords.Contains(pair.Key))
                {
                    // Existing definitions are left intact; their references keep their meaning.
                    if (pair.Key is "$defs" or "definitions") continue;
                    foreach (var property in map.ToArray()) if (property.Value is JsonObject item) map[property.Key] = Rewrite(item, true);
                }
                else if (pair.Value is JsonObject item && SingleKeywords.Contains(pair.Key)) copy[pair.Key] = Rewrite(item, true);
                else if (pair.Value is JsonArray array && ArrayKeywords.Contains(pair.Key))
                    for (int i = 0; i < array.Count; i++) if (array[i] is JsonObject alternative) array[i] = Rewrite(alternative, true);
            }
            return copy;
        }
        var result = Rewrite(root, false);
        if (added.Count > 0)
        {
            // Detach only after traversal; definitions may belong to the input root.
            result["$defs"] = definitions.DeepClone();
        }
        var compact = JsonSerializer.SerializeToElement(result);
        return compact.GetRawText().Length < schema.GetRawText().Length ? compact : schema;
    }

    private static readonly HashSet<string> MapKeywords = ["properties", "patternProperties", "$defs", "definitions", "dependentSchemas"];
    private static readonly HashSet<string> SingleKeywords = ["items", "additionalProperties", "not", "if", "then", "else", "contains", "propertyNames", "unevaluatedProperties"];
    private static readonly HashSet<string> ArrayKeywords = ["oneOf", "anyOf", "allOf", "prefixItems"];

    private static IEnumerable<JsonObject> Children(JsonObject node)
    {
        foreach (var pair in node)
        {
            if (pair.Value is JsonObject map && MapKeywords.Contains(pair.Key))
            {
                if (pair.Key is "$defs" or "definitions") continue;
                foreach (var item in map.Select(p => p.Value).OfType<JsonObject>()) yield return item;
            }
            else if (pair.Value is JsonObject item && SingleKeywords.Contains(pair.Key)) yield return item;
            else if (pair.Value is JsonArray array && ArrayKeywords.Contains(pair.Key))
                foreach (var alternative in array.OfType<JsonObject>()) yield return alternative;
        }
    }
}
