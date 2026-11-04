using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.McpServer;

internal static class AuditServerRoutingTests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("server routing accepts only exact published tool names", () =>
        {
            var operations = ToolCatalog.OperationNames.Append("get_guidance").ToHashSet(StringComparer.Ordinal);
            var resolved = new HashSet<string>(StringComparer.Ordinal);
            foreach (var tool in ToolCatalog.Tools)
            {
                if (!ToolCatalog.TryResolveOperation(tool.Name, out string operation) ||
                    !operations.Contains(operation) || !resolved.Add(operation))
                    throw new Exception("Published tool has no unique operation route: " + tool.Name);
            }
            if (!resolved.SetEquals(operations))
                throw new Exception("The published catalog and operation routes differ.");
            foreach (string name in new[] { "", "get_status", "nanocad_get_status", "NANOCAD.get_status",
                "nanocad.get_status.extra", " nanocad.get_status", "nanocad.unknown" })
                if (ToolCatalog.TryResolveOperation(name, out _))
                    throw new Exception("Unpublished tool name was accepted: " + name);
            return Task.CompletedTask;
        });
        await test("server compact publication preserves complete focused variant validation", () =>
        {
            foreach (string operation in new[] { "create_entities", "modify_entities", "modify_structure" })
            {
                string field = operation == "create_entities" ? "entities" : "operations";
                var published = ToolCatalog.Tools.Single(tool => tool.Name == "nanocad." + operation).InputSchema;
                string Key(JsonElement branch)
                {
                    var properties = branch.GetProperty("properties");
                    return (operation == "create_entities"
                        ? properties.TryGetProperty("kind", out var kind) && kind.TryGetProperty("const", out var value)
                            ? value : properties.GetProperty("type").GetProperty("const")
                        : properties.GetProperty("operation").GetProperty("const")).GetString()!;
                }
                var branches = published.GetProperty("properties").GetProperty(field).GetProperty("items").GetProperty("oneOf");
                foreach (var group in branches.EnumerateArray().GroupBy(Key))
                {
                    var focused = AuditServerContractSupport.Input(operation, group.Key);
                    var fullBranch = focused.GetProperty("properties").GetProperty(field).GetProperty("items");
                    var publicBranch = group.Count() == 1 ? group.Single()
                        : JsonSerializer.SerializeToElement(new { oneOf = group.ToArray() });
                    if (!JsonNode.DeepEquals(WithoutDescriptions(fullBranch), WithoutDescriptions(publicBranch)))
                        throw new Exception("Compaction changed callable fields, defaults or validation: " + operation + "/" + group.Key);
                    if (publicBranch.GetRawText().Contains("\"$ref\"", StringComparison.Ordinal))
                        throw new Exception("Input compaction introduced unresolved references.");
                    foreach (var property in focused.GetProperty("properties").EnumerateObject().Where(property => property.Name != field))
                        if (!JsonElement.DeepEquals(property.Value, published.GetProperty("properties").GetProperty(property.Name)))
                            throw new Exception("Compaction changed common request fields: " + property.Name);
                }
            }
            return Task.CompletedTask;
        });
        await test("server prose compaction retains properties and literal values named description", () =>
        {
            using var input = JsonDocument.Parse("""
                {"type":"object","properties":{"operations":{"type":"array","items":{"oneOf":[
                  {"type":"object","description":"Move variant prose to help.","properties":{
                    "operation":{"const":"sample"},
                    "description":{"type":"string","description":"Move field prose to help.","default":"retained"},
                    "payload":{"type":"object","default":{"description":"literal data"}}
                  },"required":["operation","description"]}
                ]}}}}
                """);
            var method = typeof(ToolCatalog).Assembly.GetType("NanoCAD.McpServer.SchemaCompactor")!
                .GetMethod("PublishInput", BindingFlags.Public | BindingFlags.Static)!;
            var output = (JsonElement)method.Invoke(null, ["modify_entities", input.RootElement])!;
            var branch = output.GetProperty("properties").GetProperty("operations").GetProperty("items").GetProperty("oneOf")[0];
            var properties = branch.GetProperty("properties");
            if (branch.TryGetProperty("description", out _) || properties.GetProperty("description").TryGetProperty("description", out _) ||
                properties.GetProperty("description").GetProperty("default").GetString() != "retained" ||
                properties.GetProperty("payload").GetProperty("default").GetProperty("description").GetString() != "literal data" ||
                !branch.GetProperty("required").EnumerateArray().Any(value => value.GetString() == "description"))
                throw new Exception("Schema prose traversal removed callable fields, defaults or literal description data.");
            return Task.CompletedTask;
        });
    }

    internal static JsonNode WithoutDescriptions(JsonElement input)
    {
        var root = JsonNode.Parse(input.GetRawText())!;
        void Visit(JsonObject schema)
        {
            schema.Remove("description");
            foreach (var property in schema)
            {
                if (property.Key is "properties" or "patternProperties" or "$defs" or "definitions" or "dependentSchemas")
                {
                    foreach (var child in property.Value!.AsObject().Select(item => item.Value).OfType<JsonObject>()) Visit(child);
                }
                else if (property.Key is "items" or "additionalProperties" or "not" or "if" or "then" or "else" or "contains" or "propertyNames" or "unevaluatedProperties")
                {
                    if (property.Value is JsonObject child) Visit(child);
                }
                else if (property.Key is "oneOf" or "anyOf" or "allOf" or "prefixItems")
                {
                    foreach (var child in property.Value!.AsArray().OfType<JsonObject>()) Visit(child);
                }
            }
        }
        Visit(root.AsObject());
        return root;
    }
}
