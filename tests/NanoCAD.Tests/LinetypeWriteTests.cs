using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using NanoCAD.McpServer;

internal static class LinetypeWriteTests
{
    private static void Check(bool value, string why) { if (!value) throw new Exception(why); }
    private static void Domain(string code, Action action)
    { try { action(); throw new Exception("Expected " + code); } catch (DomainException e) { Check(e.Error.code == code, e.Error.code); } }
    private static JsonObject Input(double[]? pattern = null) => new() { ["operation"] = "create_linetype", ["name"] = "MCP_Dash", ["description"] = "Dot — gap", ["pattern"] = JsonSerializer.SerializeToNode(pattern ?? new[] { 5d, -1, 0, -1 }) };
    private static CreateLinetypeEdit Parse(JsonObject value) => LinetypeWriteValidation.ParseCreate(Wire.Element(value));
    public static Dictionary<string, object?> ProtocolInput(string bridge, string document)
    { var input = StructureWriteTests.ProtocolInput(bridge, document); input["operations"] = new[] { Input() }; return input; }
    public static ModifiedStructure ProtocolData(StructureWriteArguments args) => new(args.OperationId, "committed", false,
        new[] { new StructureChange(0, "create_linetype", new(args.BridgeId, args.DocumentId, "FC"), "linetype", ((CreateLinetypeEdit)args.Operations[0]).Name, true) }, "single_command");
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("simple linetypes preserve dash dot gap intent and calculate the full period", () =>
        {
            var examples = new[] { Input(), Input(new[] { 5d, -2 }), Input(new[] { 0d, -2 }), Input(Enumerable.Range(0, 64).Select(i => i % 2 == 0 ? .5 : -.5).ToArray()) };
            foreach (var value in examples)
            {
                var operation = Parse(value); var expected = LinetypeWriteValidation.Expected(operation);
                Check(expected.pattern_length == operation.Pattern.Sum(Math.Abs) && expected.dash_count == operation.Pattern.Length, "derived period/count");
                Check(expected.elements.Select(e => e.length).SequenceEqual(operation.Pattern), "order/raw lengths");
                Check(expected.elements.All(e => e.text == "" && e.shape_number == 0 && e.style.state == "null"), "no complex pattern data");
            }
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!, "linetype-write-examples.json"),
                JsonSerializer.Serialize(new { schema = LinetypeWriteSchema.Create(), inputs = examples }, Wire.Json));
            return Task.CompletedTask;
        });
        await test("simple linetype input rejects semantic numeric loss and unsupported patterns", () =>
        {
            foreach (var pattern in new[] { Array.Empty<double>(), new[] { 1d }, new[] { 1d, -1, 0 }, new[] { -1d, -1 }, new[] { 1d, 0 }, new[] { 1d, 1 }, new[] { 1e-10, -1d }, new[] { 1d, -1e-10 }, new[] { 1e6, -1d }, Enumerable.Repeat(1d, 66).ToArray() })
                Domain("INVALID_ARGUMENT", () => Parse(Input(pattern)));
            foreach (string number in new[] { "null", "true", "\"1\"", "1e999", "1e-999", "-1e-999" })
            {
                using var raw = JsonDocument.Parse("{\"operation\":\"create_linetype\",\"name\":\"Test\",\"description\":\"\",\"pattern\":[" + number + ",-1]}");
                Domain("INVALID_ARGUMENT", () => LinetypeWriteValidation.ParseCreate(raw.RootElement));
            }
            foreach (double number in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                Domain("INVALID_ARGUMENT", () => LinetypeWriteValidation.Validate(new("Test", "", new[] { number, -1d })));
            return Task.CompletedTask;
        });
        await test("linetype write rejects reserved names external data and malformed Unicode", () =>
        {
            foreach (string name in new[] { "ByLayer", "BYBLOCK", "continuous", "A|B", "*X", " bad", "bad ", "", new string('A', 256) })
            { var input = Input(); input["name"] = name; Domain("INVALID_ARGUMENT", () => Parse(input)); }
            foreach (string field in new[] { "file", "font", "text", "shape_number", "set_current", "pattern_length", "overwrite" })
            { var input = Input(); input[field] = "hidden"; Domain("INVALID_ARGUMENT", () => Parse(input)); }
            foreach (string description in new[] { "x\n", new string('X', 1025), "\ud800" })
                Domain("INVALID_ARGUMENT", () => LinetypeWriteValidation.Validate(new("Good", description, new[] { 1d, -1 })));
            using var raw = JsonDocument.Parse("{\"operation\":\"create_linetype\",\"name\":\"A\",\"name\":\"B\",\"description\":\"\",\"pattern\":[1,-1]}");
            Domain("INVALID_ARGUMENT", () => LinetypeWriteValidation.ParseCreate(raw.RootElement));
            return Task.CompletedTask;
        });
        await test("linetypes share aggregate object budget and reject duplicate table names", () =>
        {
            var item = Parse(Input());
            var types = Enumerable.Range(0, 16).Select(i => (StructureEdit)(item with { Name = "L" + i })).ToArray();
            Check(LinetypeWriteValidation.ValidateBatch(types) == 16, "16 types");
            Check(LinetypeWriteValidation.ValidateBatch(types.Append(item with{Name="Next"}))==17,"no arbitrary linetype batch ceiling");
            Domain("INVALID_OPERATION_ORDER", () => LinetypeWriteValidation.ValidateBatch(new[] { item, item with { Name = item.Name.ToLowerInvariant() } }));
            var definitions = Enumerable.Range(0, 4).Select(i => (StructureEdit)new DefineBlockEdit("B" + i, new(0, 0),
                Enumerable.Range(0, 100).Select(j => (EntitySpec)new LineSpec(new(0, j), new(1, j), "0")).ToArray(),
                Enumerable.Range(0, 16).Select(j => new BlockAttributeSpec("A" + j, "", new(0, j), 2, "Standard", 0)).ToArray()));
            var full = definitions.Concat(types).Concat(Enumerable.Range(0, 28).Select(i => new CreateLayerEdit("N" + i, new(null, null, null, null, null, null, null)))).ToArray();
            Check(LinetypeWriteValidation.ValidateBatch(full) == 512, "512 combined objects");
            Check(LinetypeWriteValidation.ValidateBatch(full.Append(new CreateLayerEdit("Extra", new(null, null, null, null, null, null, null))))==513,"no arbitrary combined object ceiling");
            return Task.CompletedTask;
        });
        await test("native pattern verification detects injected complex metadata and wrong geometry", () =>
        {
            var expected = LinetypeWriteValidation.Expected(Parse(Input()));
            foreach (var altered in new[] { expected with { pattern_length = 999 }, expected with { is_dependent = true }, expected with { has_fields = true }, expected with { has_xdata = true }, expected with { has_extension_dictionary = true }, expected with { is_scaled_to_fit = true } })
                Domain("API_EXCEPTION", () => LinetypeWriteValidation.RequireSame(expected, altered));
            foreach (var element in new[] { expected.elements[0] with { length = 6 }, expected.elements[0] with { text = "X" }, expected.elements[0] with { shape_number = 1 }, expected.elements[0] with { offset = new[] { 1d, 0 } }, expected.elements[0] with { scale = 1 }, expected.elements[0] with { rotation = 1 }, expected.elements[0] with { is_ucs_oriented = true } })
            { var elements = expected.elements.ToArray(); elements[0] = element; Domain("API_EXCEPTION", () => LinetypeWriteValidation.RequireSame(expected, expected with { elements = elements })); }
            return Task.CompletedTask;
        });
        await test("linetype operation replay preserves committed identity and rejects changed pattern intent", () =>
        {
            var input = ProtocolInput("11111111-1111-4111-8111-111111111111", "22222222-2222-4222-8222-222222222222");
            var request = new Request(Wire.Version, Guid.NewGuid().ToString("D"), "modify_structure", 10000, Wire.Element(input));
            var args = StructureWriteValidation.Parse(request.arguments); Check(args.Operations.Single() is CreateLinetypeEdit, "route");
            using var queue = new WriteQueue(Environment.CurrentManagedThreadId);
            var (job, _) = queue.Enqueue(request); queue.Take(); Check(job.TryStart(), "start");
            job.BeginCommit(); var result = Result.Ok(request.requestId, ProtocolData(args)); job.RecordCommit(result); job.Complete(result);
            var (same, repeated) = queue.Enqueue(request with { requestId = Guid.NewGuid().ToString("D") });
            var replay = (ModifiedStructure)WriteJob.ForCaller(same.Completion.Result, "replay", repeated).data!;
            Check(replay.replayed && replay.changes[0].@ref.handle == "FC" && replay.changes[0].kind == "linetype" && queue.Take() == null, "historical result");
            foreach (string field in new[] { "name", "description", "pattern" })
            {
                var changed = JsonNode.Parse(request.arguments.GetRawText())!;
                changed["operations"]![0]![field] = field == "pattern" ? JsonSerializer.SerializeToNode(new[] { 3d, -1 }) : JsonValue.Create("Changed");
                Domain("OPERATION_ID_CONFLICT", () => queue.Enqueue(request with { arguments = Wire.Element(changed) }));
            }
            return Task.CompletedTask;
        });
    }
}
