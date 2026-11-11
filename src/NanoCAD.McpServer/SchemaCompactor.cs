using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

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
        foreach (var branch in branches)
            RemoveDescriptions(branch?.AsObject() ?? throw new InvalidOperationException("Invalid published branch."));
        if (operation == "create_entities")
        {
            HoistRepeatedEntityProperties(items, branches);
            ShareNativeSnapValidation(items, branches);
        }
        string discriminator = operation == "create_entities" ? "kind or type" : "operation";
        items["description"] = "Shared item properties and patterns retain all inline validation. Before writing, read " +
            $"get_guidance(topic=operation, operation={operation}, variant=exact {discriminator} value) for field meanings and operating limits.";
        return JsonSerializer.SerializeToElement(root);
    }

    private static void HoistRepeatedEntityProperties(JsonObject items, JsonArray branches)
    {
        // Closed alternatives still declare their allowed fields. A sibling
        // properties schema can therefore validate identical occurrences once
        // without admitting a field to any other alternative.
        if (items.ContainsKey("properties") || branches.Count < 2 ||
            branches.Any(branch => branch is not JsonObject value || !HasClosedProperties(value) ||
                value["properties"] is not JsonObject)) return;
        var shared = new JsonObject();
        var groups = branches.OfType<JsonObject>().SelectMany(branch => branch["properties"]!.AsObject())
            .GroupBy(pair => pair.Key, StringComparer.Ordinal)
            .OrderByDescending(group => group.Key.EndsWith("_snap", StringComparison.Ordinal));
        foreach (var group in groups)
        {
            var occurrences = group.ToArray();
            if (occurrences.Length < 2 || occurrences[0].Value is not JsonObject schema ||
                occurrences.Any(pair => !JsonNode.DeepEquals(pair.Value, schema))) continue;
            var declaration = new JsonObject();
            if (schema["type"] is { } type) declaration["type"] = type.DeepClone();
            int saving = (occurrences.Length - 1) * schema.ToJsonString().Length -
                occurrences.Length * declaration.ToJsonString().Length - JsonValue.Create(group.Key)!.ToJsonString().Length - 2;
            if (saving <= "\"properties\":{},".Length) continue;
            shared[group.Key] = schema.DeepClone();
            foreach (var branch in branches.OfType<JsonObject>())
                if (branch["properties"]!.AsObject().ContainsKey(group.Key))
                    branch["properties"]![group.Key] = declaration.DeepClone();
        }
        if (shared.Count > 0) items["properties"] = shared;
    }

    private static void ShareNativeSnapValidation(JsonObject items, JsonArray branches)
    {
        if (items.ContainsKey("patternProperties") || branches.Any(branch => branch is not JsonObject value ||
            !HasClosedProperties(value) || value["properties"] is not JsonObject)) return;
        var sharedProperties = items["properties"] as JsonObject ?? [];
        foreach (var field in branches.OfType<JsonObject>().SelectMany(branch => branch["properties"]!.AsObject())
            .Where(field => field.Key.EndsWith("_snap", StringComparison.Ordinal) && sharedProperties.ContainsKey(field.Key)))
        {
            var declaration = new JsonObject();
            if (sharedProperties[field.Key]?["type"] is { } type) declaration["type"] = type.DeepClone();
            if (!JsonNode.DeepEquals(field.Value, declaration)) return;
        }
        var fields = sharedProperties.Concat(branches.OfType<JsonObject>()
                .SelectMany(branch => branch["properties"]!.AsObject()).Where(field => !sharedProperties.ContainsKey(field.Key)))
            .Where(field => field.Key.EndsWith("_snap", StringComparison.Ordinal)).ToArray();
        if (fields.Length < 2 || fields[0].Value is not JsonObject first ||
            first["properties"] is not JsonObject || first["allOf"] is not JsonArray ||
            fields.Any(field => !JsonNode.DeepEquals(field.Value, first))) return;
        var sample = fields[0].Value!.AsObject();
        var patterns = new JsonObject();
        foreach (var field in sample["properties"]!.AsObject())
            if (field.Value is JsonObject leaf && leaf["pattern"] is { } pattern)
                patterns[field.Key] = new JsonObject { ["pattern"] = pattern.DeepClone() };
        var validation = new JsonObject
        {
            ["allOf"] = sample["allOf"]!.DeepClone(),
            ["properties"] = patterns
        };
        foreach (var field in fields)
        {
            var schema = field.Value!.AsObject();
            schema.Remove("allOf");
            foreach (var pattern in patterns) schema["properties"]![pattern.Key]!.AsObject().Remove("pattern");
        }
        string names = string.Join("|", fields.Select(field => field.Key).Distinct(StringComparer.Ordinal).Select(Regex.Escape));
        // Field names/types, required base fields and closure remain at each
        // occurrence. Only identical mode conditions and leaf regexes move.
        items["patternProperties"] = new JsonObject { ["^(?:" + names + ")$"] = validation };
    }

    // Only the default catalog merges otherwise identical creation variants.
    // A required, distinct constant discriminator makes their alternatives
    // disjoint; an enum therefore preserves their exact oneOf validation.
    // Focused and structured contracts retain the individual constant branches.
    public static JsonElement ShareCreationKindAlternatives(JsonElement input)
    {
        var root = JsonNode.Parse(input.GetRawText())!.AsObject();
        if (root["properties"]?["entities"]?["items"]?["oneOf"] is not JsonArray branches) return input;
        var candidates = new List<(int Index, string Kind, JsonObject Shape)>();
        for (int index = 0; index < branches.Count; index++)
        {
            if (branches[index] is not JsonObject branch || !HasClosedProperties(branch) ||
                branch["properties"] is not JsonObject fields || fields["kind"] is not JsonObject discriminator ||
                discriminator["const"] is not JsonValue value || !value.TryGetValue<string>(out var kind) ||
                discriminator.ContainsKey("enum") || branch["required"] is not JsonArray required ||
                !required.Any(field => field is JsonValue name && name.TryGetValue<string>(out var text) && text == "kind")) continue;
            var shape = (JsonObject)branch.DeepClone();
            shape["properties"]!["kind"]!.AsObject().Remove("const");
            candidates.Add((index, kind, shape));
        }
        var groups = new Dictionary<int, string[]>();
        var consumed = new HashSet<int>();
        foreach (var candidate in candidates)
        {
            if (consumed.Contains(candidate.Index)) continue;
            var same = candidates.Where(other => !consumed.Contains(other.Index) && JsonNode.DeepEquals(candidate.Shape, other.Shape)).ToArray();
            // Duplicate constants previously matched two oneOf alternatives;
            // merging them would incorrectly admit that discriminator value.
            if (same.Length < 2 || same.Select(other => other.Kind).Distinct(StringComparer.Ordinal).Count() != same.Length) continue;
            groups.Add(candidate.Index, same.Select(other => other.Kind).ToArray());
            foreach (var item in same) consumed.Add(item.Index);
        }
        if (groups.Count == 0) return input;
        var merged = new JsonArray();
        for (int index = 0; index < branches.Count; index++)
        {
            if (groups.TryGetValue(index, out var group))
            {
                var branch = branches[index]!.DeepClone().AsObject();
                var discriminator = branch["properties"]!["kind"]!.AsObject();
                discriminator.Remove("const");
                discriminator["enum"] = new JsonArray(group.Select(kind => (JsonNode?)JsonValue.Create(kind)).ToArray());
                merged.Add(branch);
            }
            else if (!consumed.Contains(index)) merged.Add(branches[index]?.DeepClone());
        }
        root["properties"]!["entities"]!["items"]!["oneOf"] = merged;
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
        operations["description"] = "Ordered operations. Add the fields from each variant's focused item schema directly to its item.";
        return JsonSerializer.SerializeToElement(root);
    }

    // Some MCP clients discover arguments from top-level properties only.
    // Expose the union of the fields for discovery. Keep the exact alternatives
    // as a separate constraint, including their required and forbidden fields.
    public static JsonElement PublishFlatInput(JsonElement input)
    {
        var root = JsonNode.Parse(input.GetRawText())!.AsObject();
        if (root["properties"] is JsonObject || (root["oneOf"] is not JsonArray && root["anyOf"] is not JsonArray)) return input;
        var flat = Merge(Alternatives(root).ToArray());
        flat["type"] = "object";
        if (root["description"] is { } description) flat["description"] = description.DeepClone();
        else flat.Remove("description");
        RemoveDescriptions(root);
        flat["allOf"] = new JsonArray(root);
        return JsonSerializer.SerializeToElement(flat);
    }

    private static void RemoveDescriptions(JsonObject schema)
    {
        schema.Remove("description");
        foreach (var child in Children(schema)) RemoveDescriptions(child);
    }

    private static IEnumerable<JsonObject> Alternatives(JsonObject node)
    {
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
            foreach (var leaf in Alternatives(branch)) yield return leaf;
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
        if (schemas.Any(schema => schema["properties"] is JsonObject))
        {
            var properties = new JsonObject();
            foreach (string name in schemas.SelectMany(schema => (schema["properties"] as JsonObject ?? []).Select(pair => pair.Key)).Distinct(StringComparer.Ordinal))
            {
                var known = schemas.Select(schema => schema["properties"]?[name]).OfType<JsonObject>().ToArray();
                bool unrestricted = schemas.Any(schema => schema["properties"]?[name] is not JsonObject && !HasClosedProperties(schema));
                properties[name] = unrestricted || known.Length == 0 ? new JsonObject() : Merge(known);
            }
            merged["properties"] = properties;
            var required = schemas
                .Select(schema => (schema["required"] as JsonArray ?? []).Select(value => value!.GetValue<string>()))
                .Aggregate((left, right) => left.Intersect(right, StringComparer.Ordinal).ToArray());
            if (required.Any()) merged["required"] = new JsonArray(required.Select(name => (JsonNode?)name).ToArray());
            if (schemas.All(HasClosedProperties))
                merged["additionalProperties"] = false;
        }
        return merged;
    }

    private static bool HasClosedProperties(JsonObject schema) =>
        schema["additionalProperties"] is JsonValue value && value.GetValueKind() == JsonValueKind.False &&
        !schema.ContainsKey("patternProperties");

    // Merged separately above. The exact schema retains every original condition.
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
