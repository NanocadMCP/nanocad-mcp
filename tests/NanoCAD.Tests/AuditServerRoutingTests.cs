using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
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
                var items = published.GetProperty("properties").GetProperty(field).GetProperty("items");
                var branches = items.GetProperty("oneOf");
                foreach (var group in branches.EnumerateArray().GroupBy(Key))
                {
                    var focused = AuditServerContractSupport.Input(operation, group.Key);
                    var fullBranch = focused.GetProperty("properties").GetProperty(field).GetProperty("items");
                    var publicBranch = group.Count() == 1 ? AuditServerContractSupport.RestoreSharedItemProperties(items, group.Single())
                        : JsonSerializer.SerializeToElement(new { oneOf = group.Select(branch => AuditServerContractSupport.RestoreSharedItemProperties(items, branch)).ToArray() });
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
        await test("server default catalog indexes edit variants while retaining focused and structured contracts", () =>
        {
            var listTools = typeof(ToolCatalog).Assembly.GetType("NanoCAD.McpServer.McpPresentation")!
                .GetMethod("ListTools", BindingFlags.Public | BindingFlags.Static)!;
            const string responseMode = "NANOCAD_MCP_RESPONSE_MODE";
            string? previousMode = Environment.GetEnvironmentVariable(responseMode);
            try
            {
                Environment.SetEnvironmentVariable(responseMode, "structured");
                var full = (IList<Tool>)listTools.Invoke(null, null)!;
                Environment.SetEnvironmentVariable(responseMode, null);
                var compact = (IList<Tool>)listTools.Invoke(null, null)!;
                foreach (var tool in full)
                {
                    var published = compact.Single(candidate => candidate.Name == tool.Name);
                    var canonical = ToolCatalog.Tools.Single(candidate => candidate.Name == tool.Name);
                    if ((canonical.InputSchema.TryGetProperty("oneOf", out _) || canonical.InputSchema.TryGetProperty("anyOf", out _)) &&
                        !canonical.InputSchema.TryGetProperty("properties", out _))
                    {
                        RequireFlatUnion(tool.Name, canonical.InputSchema, tool.InputSchema);
                        RequireFlatUnion(tool.Name, canonical.InputSchema, published.InputSchema);
                    }
                    if (published.OutputSchema is not null || !Nullable.Equals(tool.OutputSchema, canonical.OutputSchema))
                        throw new Exception("Input publication changed response mode contracts: " + tool.Name);
                    if (tool.Name == "nanocad.create_entities")
                    {
                        var restored = AuditServerContractSupport.RestoreCreationKindAlternatives(tool.InputSchema, published.InputSchema);
                        if (!JsonNode.DeepEquals(JsonNode.Parse(tool.InputSchema.GetRawText()), JsonNode.Parse(restored.GetRawText())))
                            throw new Exception("Default creation kind sharing changed fields outside the exact alternatives.");
                        continue;
                    }
                    if (tool.Name is not ("nanocad.modify_entities" or "nanocad.modify_structure"))
                    {
                        if (!JsonElement.DeepEquals(tool.InputSchema, published.InputSchema))
                            throw new Exception("Default projection changed an unrelated tool: " + tool.Name);
                        continue;
                    }
                    string operation = tool.Name["nanocad.".Length..];
                    var items = published.InputSchema.GetProperty("properties").GetProperty("operations").GetProperty("items");
                    var publishedNames = items.GetProperty("properties").GetProperty("operation").GetProperty("enum");
                    var focusedNames = AuditServerContractSupport.Input(operation).GetProperty("variants");
                    if (!JsonElement.DeepEquals(publishedNames, focusedNames) || !items.GetProperty("additionalProperties").GetBoolean() ||
                        items.GetProperty("type").GetString() != "object" || items.GetProperty("required")[0].GetString() != "operation" ||
                        !published.Description!.Contains("operation=" + operation, StringComparison.Ordinal) ||
                        !JsonNode.DeepEquals(EditEnvelope(tool.InputSchema), EditEnvelope(published.InputSchema)))
                        throw new Exception("Default edit index changed the request envelope or focused discovery: " + operation);
                }
                var move = AuditServerContractSupport.Branch("modify_entities", "move");
                var copyViewport = AuditServerContractSupport.Branch("modify_structure", "copy_viewport");
                if (!move.GetProperty("properties").TryGetProperty("displacement", out _) ||
                    !copyViewport.GetProperty("properties").TryGetProperty("layout_handle", out _))
                    throw new Exception("Focused edit contracts lost variant-specific fields.");
            }
            finally { Environment.SetEnvironmentVariable(responseMode, previousMode); }
            return Task.CompletedTask;

            static JsonObject EditEnvelope(JsonElement input)
            {
                var schema = JsonNode.Parse(input.GetRawText())!.AsObject();
                var operations = schema["properties"]!["operations"]!.AsObject();
                operations.Remove("items");
                operations.Remove("description");
                return schema;
            }
        });
        await test("server catalog publishes root alternatives as callable top-level fields", () =>
        {
            using var input = JsonDocument.Parse("""
                {"type":"object","oneOf":[
                  {"type":"object","additionalProperties":false,"required":["id","mode","handles"],"properties":{
                    "id":{"type":"string"},"mode":{"type":"string","enum":["a","b"]},
                    "handles":{"type":"array","minItems":1,"uniqueItems":true,"items":{"type":"string"}}}},
                  {"type":"object","description":"Second branch prose.","oneOf":[
                    {"type":"object","additionalProperties":false,"required":["id","mode","point"],"properties":{
                      "id":{"type":"string"},"mode":{"const":"c"},"point":{"type":"array","items":{"type":"number"}},
                      "handles":{"type":"array","minItems":2,"maxItems":2,"uniqueItems":true,"items":{"type":"string"}}}}]}
                ]}
                """);
            var method = typeof(ToolCatalog).Assembly.GetType("NanoCAD.McpServer.SchemaCompactor")!
                .GetMethod("PublishFlatInput", BindingFlags.Public | BindingFlags.Static)!;
            var output = (JsonElement)method.Invoke(null, [input.RootElement])!;
            RequireFlatUnion("synthetic", input.RootElement, output);
            var properties = output.GetProperty("properties");
            var handles = properties.GetProperty("handles");
            if (!properties.GetProperty("mode").GetProperty("enum").EnumerateArray().Select(value => value.GetString()).SequenceEqual(["a", "b", "c"]) ||
                handles.TryGetProperty("minItems", out _) || handles.TryGetProperty("maxItems", out _) || !handles.GetProperty("uniqueItems").GetBoolean() ||
                !output.GetProperty("required").EnumerateArray().Select(value => value.GetString()).SequenceEqual(["id", "mode"]) ||
                output.GetProperty("additionalProperties").GetBoolean())
                throw new Exception("Flat publication did not relax differing constraints or join branch values.");

            using var open = JsonDocument.Parse("""
                {"anyOf":[
                  {"type":"object","additionalProperties":false,"required":["mode","point"],"properties":{"mode":{"const":"one"},"point":{"type":"array"}}},
                  {"type":"object","required":["mode"],"properties":{"mode":{"const":"two"}}}
                ]}
                """);
            var openOutput = (JsonElement)method.Invoke(null, [open.RootElement])!;
            RequireFlatUnion("open synthetic", open.RootElement, openOutput);
            if (openOutput.GetProperty("properties").GetProperty("point").EnumerateObject().Any() ||
                openOutput.TryGetProperty("additionalProperties", out _))
                throw new Exception("Flat publication over-constrained an open alternative.");
            return Task.CompletedTask;
        });

        // The top-level object advertises every argument, and all exact constraints remain enforceable.
        static void RequireFlatUnion(string name, JsonElement full, JsonElement published)
        {
            if (published.GetProperty("type").GetString() != "object" || published.GetProperty("properties").ValueKind != JsonValueKind.Object ||
                published.TryGetProperty("oneOf", out _) || published.TryGetProperty("anyOf", out _))
                throw new Exception("Root alternatives were not flattened into top-level fields: " + name);
            if (published.GetProperty("allOf").GetArrayLength() != 1 ||
                !JsonNode.DeepEquals(WithoutDescriptions(published.GetProperty("allOf")[0]), WithoutDescriptions(full)))
                throw new Exception("Projection changed the exact alternative constraints: " + name);
            var properties = published.GetProperty("properties");
            var required = published.TryGetProperty("required", out var list) ? list.EnumerateArray().Select(value => value.GetString()!).ToArray() : [];
            foreach (var (fields, needed) in Leaves(full, new Dictionary<string, JsonElement>(), []))
            {
                if (required.Except(needed).Any()) throw new Exception("Flat publication requires a field one alternative omits: " + name);
                foreach (var (field, schema) in fields)
                {
                    if (!properties.TryGetProperty(field, out var flat)) throw new Exception($"Flat publication lost {name}.{field}.");
                    var values = schema.TryGetProperty("const", out var single) ? new[] { single }
                        : schema.TryGetProperty("enum", out var many) ? many.EnumerateArray().ToArray() : [];
                    if (flat.TryGetProperty("enum", out var allowed) && values.Any(value => !allowed.EnumerateArray().Any(candidate => JsonElement.DeepEquals(candidate, value))))
                        throw new Exception($"Flat publication rejects a branch value of {name}.{field}.");
                }
            }

            static IEnumerable<(Dictionary<string, JsonElement> Fields, string[] Required)> Leaves(JsonElement node, Dictionary<string, JsonElement> fields, string[] required)
            {
                fields = new Dictionary<string, JsonElement>(fields);
                if (node.TryGetProperty("properties", out var own)) foreach (var field in own.EnumerateObject()) fields[field.Name] = field.Value;
                if (node.TryGetProperty("required", out var needed)) required = required.Concat(needed.EnumerateArray().Select(value => value.GetString()!)).ToArray();
                if (!node.TryGetProperty("oneOf", out var alternatives) && !node.TryGetProperty("anyOf", out alternatives))
                {
                    yield return (fields, required);
                    yield break;
                }
                foreach (var alternative in alternatives.EnumerateArray())
                    foreach (var leaf in Leaves(alternative, fields, required)) yield return leaf;
            }
        }
        await test("server guidance shows exact variant inputs without repeating the common write envelope", async () =>
        {
            foreach (var (operation, variant, field) in new[]
                { ("create_entities", "Hatch", "entities"), ("modify_entities", "move", "operations"), ("modify_structure", "create_viewport", "operations") })
            {
                var request = new Dictionary<string, JsonElement>
                {
                    ["topic"] = JsonSerializer.SerializeToElement("schema"),
                    ["operation"] = JsonSerializer.SerializeToElement(operation),
                    ["variant"] = JsonSerializer.SerializeToElement(variant)
                };
                var full = JsonSerializer.Deserialize<JsonElement>(await ReadGuidance(request));
                request["include_envelope"] = JsonSerializer.SerializeToElement(false);
                var focused = JsonSerializer.Deserialize<JsonElement>(await ReadGuidance(request));
                if (focused.GetProperty("tool").GetString() != "nanocad." + operation ||
                    focused.GetProperty("array_field").GetString() != field || focused.GetProperty("variant").GetString() != variant ||
                    !JsonElement.DeepEquals(focused.GetProperty("item_schema"), full.GetProperty("properties").GetProperty(field).GetProperty("items")) ||
                    !focused.GetProperty("envelope").GetString()!.Contains("operation=" + operation, StringComparison.Ordinal) ||
                    focused.TryGetProperty("properties", out _) || !full.GetProperty("properties").TryGetProperty("operation_id", out _))
                    throw new Exception("Focused guidance omitted fields or repeated the request envelope: " + variant);

                request["topic"] = JsonSerializer.SerializeToElement("operation");
                request.Remove("include_envelope");
                string operatingHelp = await ReadGuidance(request);
                var contract = JsonSerializer.Deserialize<JsonElement>(operatingHelp[(operatingHelp.IndexOf('\n') + 1)..]);
                if (!JsonElement.DeepEquals(contract, focused))
                    throw new Exception("Operation help and focused schema disagree: " + variant);

                request["include_envelope"] = JsonSerializer.SerializeToElement(true);
                operatingHelp = await ReadGuidance(request);
                contract = JsonSerializer.Deserialize<JsonElement>(operatingHelp[(operatingHelp.IndexOf('\n') + 1)..]);
                if (!JsonElement.DeepEquals(contract, full))
                    throw new Exception("An explicit request for the full envelope changed the schema: " + variant);
            }

            var invalid = new Dictionary<string, JsonElement>
            {
                ["topic"] = JsonSerializer.SerializeToElement("schema"),
                ["operation"] = JsonSerializer.SerializeToElement("modify_entities"),
                ["include_envelope"] = JsonSerializer.SerializeToElement(false)
            };
            await RejectEnvelope(invalid, "without an exact variant");
            invalid["variant"] = JsonSerializer.SerializeToElement("move");
            invalid["include_envelope"] = JsonSerializer.SerializeToElement("false");
            await RejectEnvelope(invalid, "with a non-Boolean value");

            static async Task RejectEnvelope(Dictionary<string, JsonElement> request, string description)
            {
                try { await ReadGuidance(request); }
                catch (McpProtocolException) { return; }
                throw new Exception("Guidance accepted the envelope option " + description + ".");
            }
        });
        await test("server command tree classifies drawing-driven solids and model rebuild as 3D routes", async () =>
        {
            var planar = await Variants("2d", "modify_entities");
            var direct = await Variants("direct3d", "modify_entities");
            var model = await Variants("parametric", "modify_entities");
            string[] retained = ["extrude_drawing_circle", "extrude_drawing_profile", "revolve_drawing_profile", "rebuild_model"];
            if (planar.Overlaps(retained.Append("extrude_faces")) || !planar.Contains("trim_curve") ||
                direct.Overlaps(retained) || !direct.Contains("extrude_faces") || !model.IsSupersetOf(retained) ||
                !(await Variants("documentation", "create_entities")).Contains("projected_aligned"))
                throw new Exception("Command discovery lost a route or advertised a 3D edit under 2D.");

            static async Task<HashSet<string>> Variants(string section, string operation)
            {
                var args = new Dictionary<string, JsonElement>
                {
                    ["topic"] = JsonSerializer.SerializeToElement("commands"),
                    ["section"] = JsonSerializer.SerializeToElement(section)
                };
                var category = JsonSerializer.Deserialize<JsonElement>(await ReadGuidance(args));
                return category.GetProperty("routes").EnumerateArray()
                    .Single(route => route.GetProperty("tool").GetString() == "nanocad." + operation)
                    .GetProperty("variants").EnumerateArray().Select(value => value.GetString()!).ToHashSet(StringComparer.Ordinal);
            }
        });
        await test("server historical result declares the optional qualified write revision", () =>
        {
            var properties = AuditServerContractSupport.Output("get_operation_result").GetProperty("properties");
            var context = properties.GetProperty("context");
            var historical = properties.GetProperty("receipt").GetProperty("properties").GetProperty("historical_context");
            foreach (var shape in new[] { context, historical }.Select(value => value.GetProperty("anyOf")[0]))
                if (shape.GetProperty("properties").GetProperty("write_revision").GetProperty("type").GetString() != "string" ||
                    shape.GetProperty("required").EnumerateArray().Any(field => field.GetString() == "write_revision"))
                    throw new Exception("The ledger result requires a qualified revision that older contexts cannot supply.");
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

    private static async Task<string> ReadGuidance(Dictionary<string, JsonElement> arguments)
    {
        var response = await new ToolService().CallAsync("get_guidance", arguments, default);
        if (response.IsError == true) throw new Exception("Public guidance request failed.");
        return response.Content.OfType<TextContentBlock>().Single().Text;
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
