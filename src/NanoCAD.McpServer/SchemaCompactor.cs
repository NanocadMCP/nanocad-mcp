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

    // Keep every callable field and validation keyword inline. Variant and field
    // operating prose lives in focused guidance backed by the full input.
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
        void MoveProse(JsonObject schema)
        {
            schema.Remove("description");
            foreach (var child in Children(schema)) MoveProse(child);
        }
        foreach (var branch in branches)
            MoveProse(branch?.AsObject() ?? throw new InvalidOperationException("Invalid published branch."));
        string discriminator = operation == "create_entities" ? "kind or type" : "operation";
        items["description"] = "All input fields and validation remain inline. Field meanings and operating limits are in focused help. Before writing a variant, read " +
            $"get_guidance(topic=operation, operation={operation}, variant=exact {discriminator} value) " +
            "for its full operating limits and complete input contract. No trial writes.";
        return JsonSerializer.SerializeToElement(root);
    }

    // The default catalog indexes edit operations. Focused help keeps the
    // complete branches, and request validation still uses the typed contracts.
    public static JsonElement PublishOperationIndex(JsonElement input)
    {
        var root = JsonNode.Parse(input.GetRawText())!.AsObject();
        var operations = root["properties"]!["operations"]!.AsObject();
        var names = operations["items"]!["oneOf"]!.AsArray()
            .Select(branch => branch!["properties"]!["operation"]!["const"]!.GetValue<string>())
            .Distinct(StringComparer.Ordinal);
        operations["items"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["operation"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray(names.Select(name => (JsonNode?)JsonValue.Create(name)).ToArray())
                }
            },
            ["required"] = new JsonArray("operation"),
            ["additionalProperties"] = true
        };
        operations["description"] = "Ordered operations. Add the fields from each exact variant's focused input contract directly to its item. Follow the branch's batching and recovery rules.";
        return JsonSerializer.SerializeToElement(root);
    }

    // Some MCP clients replace a root oneOf/anyOf without top-level properties by
    // an empty object and then drop every argument. Publish the union of the
    // alternatives: fields shared by all branches stay required, differing
    // constraints are relaxed and branch values join one enum, so every request
    // accepted before remains acceptable. The bridge still validates the exact branch.
    public static JsonElement PublishFlatInput(JsonElement input)
    {
        var root = JsonNode.Parse(input.GetRawText())!.AsObject();
        if (root["properties"] is JsonObject || (root["oneOf"] is not JsonArray && root["anyOf"] is not JsonArray)) return input;
        var notes = new List<string>();
        var flat = Merge(Alternatives(root, notes).ToArray());
        flat["type"] = "object";
        if (notes.Count > 0) flat["description"] = string.Join(" ", notes);
        else flat.Remove("description");
        return JsonSerializer.SerializeToElement(flat);
    }

    private static IEnumerable<JsonObject> Alternatives(JsonObject node, List<string> notes)
    {
        if (node["description"] is JsonValue note) notes.Add(note.GetValue<string>());
        var alternatives = node["oneOf"] as JsonArray ?? node["anyOf"] as JsonArray;
        if (alternatives is null) { yield return node; yield break; }
        foreach (var alternative in alternatives.OfType<JsonObject>())
        {
            var branch = (JsonObject)alternative.DeepClone();
            // Fields declared beside the alternatives apply to each of them.
            if (node["properties"] is JsonObject shared)
            {
                var properties = branch["properties"] as JsonObject ?? new JsonObject();
                foreach (var pair in shared) if (!properties.ContainsKey(pair.Key)) properties[pair.Key] = pair.Value?.DeepClone();
                branch["properties"] = properties;
            }
            if (node["required"] is JsonArray sharedRequired)
            {
                var required = branch["required"] as JsonArray ?? new JsonArray();
                foreach (var name in sharedRequired.Select(value => value!.GetValue<string>()))
                    if (!required.Any(value => value!.GetValue<string>() == name)) required.Add(name);
                branch["required"] = required;
            }
            foreach (var leaf in Alternatives(branch, notes)) yield return leaf;
        }
    }

    private static JsonObject Merge(IReadOnlyList<JsonObject> schemas)
    {
        if (schemas.All(schema => JsonNode.DeepEquals(schema, schemas[0]))) return (JsonObject)schemas[0].DeepClone();
        var merged = new JsonObject();
        foreach (var pair in schemas[0])
            if (!MergedKeywords.Contains(pair.Key) &&
                schemas.All(schema => schema.TryGetPropertyValue(pair.Key, out var value) && JsonNode.DeepEquals(value, pair.Value)))
                merged[pair.Key] = pair.Value?.DeepClone();
        if (schemas.Select(schema => schema["description"]).FirstOrDefault(value => value is not null) is { } description)
            merged["description"] = description.DeepClone();
        if (schemas.All(schema => schema["enum"] is JsonArray || schema.ContainsKey("const")))
        {
            var values = new JsonArray();
            foreach (var value in schemas.SelectMany(schema => schema["enum"] is JsonArray list ? list.ToArray() : [schema["const"]]))
                if (!values.Any(existing => JsonNode.DeepEquals(existing, value))) values.Add(value?.DeepClone());
            merged["enum"] = values;
        }
        if (schemas.All(schema => schema["items"] is JsonObject))
            merged["items"] = Merge(schemas.Select(schema => (JsonObject)schema["items"]!).ToArray());
        if (schemas.All(schema => schema["properties"] is JsonObject))
        {
            var properties = new JsonObject();
            foreach (string name in schemas.SelectMany(schema => ((JsonObject)schema["properties"]!).Select(pair => pair.Key)).Distinct(StringComparer.Ordinal))
                properties[name] = Merge(schemas.Select(schema => schema["properties"]![name]).OfType<JsonObject>().ToArray());
            merged["properties"] = properties;
            var required = schemas
                .Select(schema => (schema["required"] as JsonArray ?? []).Select(value => value!.GetValue<string>()))
                .Aggregate((left, right) => left.Intersect(right, StringComparer.Ordinal).ToArray());
            if (required.Any()) merged["required"] = new JsonArray(required.Select(name => (JsonNode?)name).ToArray());
            if (schemas.All(schema => schema["additionalProperties"] is JsonValue value && value.GetValueKind() == JsonValueKind.False))
                merged["additionalProperties"] = false;
        }
        return merged;
    }

    // Merged separately above; conditional and alternative keywords are left to the bridge validator.
    private static readonly HashSet<string> MergedKeywords =
        ["description", "enum", "const", "items", "properties", "required", "additionalProperties",
         "oneOf", "anyOf", "allOf", "if", "then", "else", "not", "prefixItems"];

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
