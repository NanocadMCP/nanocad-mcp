using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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

    public static JsonElement RestoreCreationKindAlternatives(JsonElement original, JsonElement published)
    {
        var source = JsonNode.Parse(original.GetRawText())!.AsObject();
        var restored = JsonNode.Parse(published.GetRawText())!.AsObject();
        var originalBranches = source["properties"]!["entities"]!["items"]!["oneOf"]!.AsArray();
        var publishedBranches = restored["properties"]!["entities"]!["items"]!["oneOf"]!.AsArray();
        var expanded = new List<JsonObject>();
        foreach (var value in publishedBranches)
        {
            var branch = value!.AsObject();
            if (branch["properties"]?["kind"] is not JsonObject discriminator || discriminator.ContainsKey("const") ||
                discriminator["enum"] is not JsonArray kinds)
            { expanded.Add((JsonObject)branch.DeepClone()); continue; }
            if (kinds.Count < 2 || kinds.Any(kind => kind is not JsonValue scalar || !scalar.TryGetValue<string>(out _)) ||
                kinds.Select(kind => kind!.GetValue<string>()).Distinct(StringComparer.Ordinal).Count() != kinds.Count)
                throw new Exception("Published kind alternatives are not distinct named variants.");
            foreach (var kind in kinds)
            {
                var single = (JsonObject)branch.DeepClone();
                var fields = single["properties"]!["kind"]!.AsObject(); fields.Remove("enum"); fields["const"] = kind!.DeepClone();
                expanded.Add(single);
            }
        }
        foreach (var branch in originalBranches)
        {
            int index = expanded.FindIndex(candidate => JsonNode.DeepEquals(candidate, branch));
            if (index < 0) throw new Exception("Published kind sharing changed a complete creation variant.");
            expanded.RemoveAt(index);
        }
        if (expanded.Count != 0) throw new Exception("Published kind sharing added creation alternatives.");
        restored["properties"]!["entities"]!["items"]!["oneOf"] = originalBranches.DeepClone();
        return JsonSerializer.SerializeToElement(restored);
    }

    public static JsonElement RestoreSharedItemProperties(JsonElement items, JsonElement branch)
    {
        var restored = JsonNode.Parse(branch.GetRawText())!.AsObject();
        var properties = restored["properties"]!.AsObject();
        if (items.TryGetProperty("properties", out var shared)) foreach (var field in shared.EnumerateObject())
        {
            if (!properties.TryGetPropertyValue(field.Name, out var declaration)) continue;
            var expected = new JsonObject();
            if (field.Value.TryGetProperty("type", out var type)) expected["type"] = JsonNode.Parse(type.GetRawText());
            if (!JsonNode.DeepEquals(declaration, expected))
                throw new Exception("Shared item validation replaced a field with unexpected constraints: " + field.Name);
            properties[field.Name] = JsonNode.Parse(field.Value.GetRawText());
        }
        if (items.TryGetProperty("patternProperties", out var patterns)) foreach (var pattern in patterns.EnumerateObject())
        {
            foreach (var field in properties.Where(field => Regex.IsMatch(field.Key, pattern.Name)).ToArray())
            {
                var schema = field.Value!.AsObject();
                if (schema.ContainsKey("allOf")) throw new Exception("Shared snap conditions overlap local conditions: " + field.Key);
                schema["allOf"] = JsonNode.Parse(pattern.Value.GetProperty("allOf").GetRawText());
                foreach (var leaf in pattern.Value.GetProperty("properties").EnumerateObject())
                {
                    var target = schema["properties"]![leaf.Name]!.AsObject();
                    if (target.ContainsKey("pattern")) throw new Exception("Shared snap regex overlaps a local regex: " + leaf.Name);
                    target["pattern"] = JsonNode.Parse(leaf.Value.GetProperty("pattern").GetRawText());
                }
            }
        }
        return JsonSerializer.SerializeToElement(restored);
    }

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
