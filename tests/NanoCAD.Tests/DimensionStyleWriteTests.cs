using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using NanoCAD.McpServer;

internal static class DimensionStyleWriteTests
{
    private static readonly EntityRef Owner = new("11111111-1111-4111-8111-111111111111", "22222222-2222-4222-8222-222222222222", "45");
    private static void Check(bool value, string why) { if (!value) throw new Exception(why); }
    private static void Domain(string code, Action action)
    { try { action(); throw new Exception("Expected " + code); } catch (DomainException e) { Check(e.Error.code == code, "Expected " + code + ", got " + e.Error.code); } }
    private static JsonObject Input() => new() { ["operation"] = "create_dimension_style", ["name"] = "MCP_Dimension", ["base_style_handle"] = "000af" };
    private static CreateDimensionStyleEdit Parse(JsonObject value) => DimensionStyleWriteValidation.ParseCreate(Wire.Element(value));
    public static Dictionary<string, object?> ProtocolInput(string bridge, string document)
    { var root = StructureWriteTests.ProtocolInput(bridge, document); root["operations"] = new[] { Input() }; return root; }
    public static ModifiedStructure ProtocolData(StructureWriteArguments args) => new(args.OperationId, "committed", false,
        new[] { new StructureChange(0, "create_dimension_style", new(args.BridgeId, args.DocumentId, "FB"), "dimension_style", ((CreateDimensionStyleEdit)args.Operations[0]).Name, true) }, "single_command");
    private static DimensionStyleProperties Example() => (DimensionStyleProperties)DimensionStyleReadTests.ProtocolData(Owner.bridge_instance_id, Owner.document_id).items[0].properties;
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("dimension style overrides inherit all other metadata and normalize handles", () =>
        {
            var value = Input(); var parsed = Parse(value); var source = Example();
            Check(parsed.BaseStyleHandle == "AF" && parsed.Properties == new DimensionStyleChanges(null, null, null), "normalized empty overrides");
            DimensionStyleWriteValidation.RequireSame(source, DimensionStyleWriteValidation.Expected("Base", Owner, source, parsed.Properties));
            var inputs = new List<JsonObject> { value.DeepClone().AsObject() };
            foreach (string key in new[] { "dimension_scale", "text_height", "arrow_size" })
            {
                foreach (double number in new[] { 1e-9, 4.25, 1e6 })
                {
                    value = Input(); value["properties"] = new JsonObject { [key] = number }; parsed = Parse(value); inputs.Add(value);
                    var expectedJson = JsonSerializer.SerializeToNode(source, Wire.Json)!.AsObject(); expectedJson[key] = number;
                    var actual = DimensionStyleWriteValidation.Expected("Base", Owner, source, parsed.Properties);
                    Check(JsonNode.DeepEquals(expectedJson, JsonSerializer.SerializeToNode(actual, Wire.Json)), "only requested scalar may change");
                }
            }
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!, "dimension-style-write-examples.json"),
                JsonSerializer.Serialize(new { schema = DimensionStyleWriteSchema.Create(), inputs }, Wire.Json));
            return Task.CompletedTask;
        });
        await test("dimension style parser rejects hidden edits, duplicate fields and numeric loss", () =>
        {
            foreach (string name in new[] { "", " bad", "bad ", "A|B", "Base$0", "A$123", "*X", new string('A', 256) })
            { var value = Input(); value["name"] = name; Domain("INVALID_ARGUMENT", () => Parse(value)); }
            foreach (string key in new[] { "dimension_scale", "text_height", "arrow_size" })
            foreach (string number in new[] { "null", "true", "\"2\"", "0", "-1", "1e-10", "1000001", "1e999", "1e-999" })
            {
                var value = Input(); value["properties"] = JsonNode.Parse("{\"" + key + "\":" + number + "}");
                Domain("INVALID_ARGUMENT", () => Parse(value));
            }
            foreach (string props in new[] { "{}", "null", "[]", "{\"dimension_scale\":1,\"dimension_scale\":2}", "{\"dimpost\":\"bad\"}", "{\"set_current\":true}" })
            {
                using var raw = JsonDocument.Parse("{\"operation\":\"create_dimension_style\",\"name\":\"New\",\"base_style_handle\":\"A\",\"properties\":" + props + "}");
                Domain("INVALID_ARGUMENT", () => DimensionStyleWriteValidation.ParseCreate(raw.RootElement));
            }
            foreach (string field in new[] { "set_current", "file", "dimension_type", "text_style_handle" })
            { var value = Input(); value[field] = "bad"; Domain("INVALID_ARGUMENT", () => Parse(value)); }
            using var unicode = JsonDocument.Parse("{\"operation\":\"create_dimension_style\",\"name\":\"\\uD800\",\"base_style_handle\":\"A\"}");
            Domain("INVALID_ARGUMENT", () => DimensionStyleWriteValidation.ParseCreate(unicode.RootElement));
            return Task.CompletedTask;
        });
        await test("dimension style bases retain unsupported metadata rather than stripping it", () =>
        {
            var s = Example();
            var bad = new[] { s with { is_dependent = true }, s with { has_xdata = true }, s with { has_fields = true }, s with { has_extension_dictionary = true },
                s with { annotative_state = "true", annotative_native_value = 0 }, s with { annotative_state = "unknown", annotative_native_value = 77 },
                s with { dimension_scale = 0 }, s with { text_height = 0 }, s with { arrow_size = 0 },
                s with { references = s.references with { text_style = new("text_style", "null", null, null, null) } },
                s with { references = s.references with { arrow = new("block", "unavailable", null, null, "wrong_owner") } },
                s with { colors = s.colors with { dimension = s.colors.dimension with { has_color_name = true, book_name = "Book", color_name = "Color" } } },
                s with { lineweights = s.lineweights with { dimension = new("unknown", 999) } } };
            foreach (var item in bad) Domain("UNSUPPORTED_STYLE_TYPE", () => DimensionStyleWriteValidation.Expected("Base", Owner, item, new(null, null, null)));
            Domain("UNSUPPORTED_STYLE_TYPE", () => DimensionStyleWriteValidation.Expected("Base$0", Owner, s with { family_name_candidate = true }, new(null, null, null)));
            Domain("OBJECT_READ_FAILED", () => DimensionStyleWriteValidation.Expected("Base", Owner,
                s with { references = s.references with { text_style = s.references.text_style with { @ref = Owner with { document_id = Guid.NewGuid().ToString("D") } } } }, new(null, null, null)));
            foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, 0, -1, 1e7 })
                Domain("INVALID_ARGUMENT", () => DimensionStyleWriteValidation.Expected("Base", Owner, s, new(invalid, null, null)));
            return Task.CompletedTask;
        });
        await test("dimension clone verification detects changes throughout every observed metadata group", () =>
        {
            var source = Example(); int mutations = 0;
            void Visit(JsonObject obj, Action<JsonObject> verify)
            {
                foreach (var pair in obj.ToArray())
                {
                    if (pair.Value is JsonObject child)
                        Visit(child, changed => { var copy = obj.DeepClone().AsObject(); copy[pair.Key] = changed; verify(copy); });
                    else if (pair.Value is JsonValue scalar)
                    {
                        var copy = obj.DeepClone().AsObject(); var element = JsonSerializer.SerializeToElement(scalar);
                        copy[pair.Key] = element.ValueKind switch { JsonValueKind.True => JsonValue.Create(false), JsonValueKind.False => JsonValue.Create(true),
                            JsonValueKind.Number => JsonValue.Create(element.GetDouble() + 1), _ => JsonValue.Create(element.GetString() + "X") };
                        verify(copy);
                    }
                }
            }
            Visit(JsonSerializer.SerializeToNode(source, Wire.Json)!.AsObject(), changed =>
            { mutations++; Domain("API_EXCEPTION", () => DimensionStyleWriteValidation.RequireSame(source, changed.Deserialize<DimensionStyleProperties>(Wire.Json)!)); });
            Check(mutations > 110, "insufficient metadata coverage");
            return Task.CompletedTask;
        });
        await test("dimension styles count toward combined atomic object budgets with separate table names", () =>
        {
            var template = Parse(Input());
            var dimensions = Enumerable.Range(0, 16).Select(i => (StructureEdit)(template with { Name = "D" + i })).ToArray();
            Check(DimensionStyleWriteValidation.ValidateBatch(dimensions) == 16, "16 styles");
            Check(DimensionStyleWriteValidation.ValidateBatch(dimensions.Append(template with{Name="Next"}))==17,"no arbitrary dimension style count ceiling");
            Domain("INVALID_OPERATION_ORDER", () => DimensionStyleWriteValidation.ValidateBatch(new[] { template, template with { Name = template.Name.ToLowerInvariant() } }));
            Check(DimensionStyleWriteValidation.ValidateBatch(new StructureEdit[] { template, new CreateTextStyleEdit(template.Name, "AF", new(null, null)) }) == 2, "separate namespaces");
            var full = Enumerable.Range(0, 4).Select(i => (StructureEdit)new DefineBlockEdit("B" + i, new(0, 0),
                Enumerable.Range(0, 100).Select(j => (EntitySpec)new LineSpec(new(0, j), new(1, j), "0")).ToArray(),
                Enumerable.Range(0, 16).Select(j => new BlockAttributeSpec("A" + j, "", new(0, j), 2, "Standard", 0)).ToArray()))
                .Concat(dimensions).Concat(Enumerable.Range(0, 28).Select(i => new CreateLayerEdit("L" + i, new(null, null, null, null, null, null, null)))).ToArray();
            Check(DimensionStyleWriteValidation.ValidateBatch(full) == 512, "512 objects");
            Check(DimensionStyleWriteValidation.ValidateBatch(full.Append(new CreateLayerEdit("Extra", new(null, null, null, null, null, null, null))))==513,"actual native object count");
            return Task.CompletedTask;
        });
        await test("dimension write fingerprints and replay bind all scalar intent to one result", () =>
        {
            var input = ProtocolInput(Owner.bridge_instance_id, Owner.document_id);
            var request = new Request(Wire.Version, Guid.NewGuid().ToString("D"), "modify_structure", 10000, Wire.Element(input));
            var args = StructureWriteValidation.Parse(request.arguments);
            Check(args.Operations.Single() is CreateDimensionStyleEdit, "typed routing");
            using var queue = new WriteQueue(Environment.CurrentManagedThreadId);
            var (job, _) = queue.Enqueue(request); queue.Take(); Check(job.TryStart(), "not started");
            job.BeginCommit(); var result = Result.Ok(request.requestId, ProtocolData(args)); job.RecordCommit(result); job.Complete(result);
            var (same, repeated) = queue.Enqueue(request with { requestId = Guid.NewGuid().ToString("D") });
            var replay = (ModifiedStructure)WriteJob.ForCaller(same.Completion.Result, "replay", repeated).data!;
            Check(replay.replayed && replay.changes[0].kind == "dimension_style" && replay.changes[0].@ref.handle == "FB" && queue.Take() == null, "replay changed identity or reexecuted");
            foreach (string key in new[] { "dimension_scale", "text_height", "arrow_size" })
            {
                var changed = JsonNode.Parse(request.arguments.GetRawText())!.AsObject(); changed["operations"]![0]!["properties"] = new JsonObject { [key] = 3 };
                Domain("OPERATION_ID_CONFLICT", () => queue.Enqueue(request with { arguments = Wire.Element(changed) }));
            }
            return Task.CompletedTask;
        });
    }
}
