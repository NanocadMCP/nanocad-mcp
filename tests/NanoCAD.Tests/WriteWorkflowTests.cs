using System.Text.Json;
using TextContentBlock = ModelContextProtocol.Protocol.TextContentBlock;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using NanoCAD.McpServer;

internal static class WriteWorkflowTests
{
    private const string Bridge = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string Document = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
    private const string Other = "cccccccc-cccc-cccc-cccc-cccccccccccc";
    private const string Operation = "dddddddd-dddd-dddd-dddd-dddddddddddd";
    private static readonly ReadContext Current = new(Bridge, Document, "42", "");
    private static readonly Descriptor Local = new(Wire.Version, Bridge, "pipe", 1, 0, 1, "test", "test", "");

    internal static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("write workflow keeps exact replay identity with optional units and flags", async () =>
        {
            var implicitUnits = await Bind(new Dictionary<string, object>
            {
                ["bridge_instance_id"] = Bridge, ["document_id"] = Document, ["expected_revision"] = "42", ["operation_id"] = Operation,
                ["validate_only"] = true, ["entities"] = new[] { new { type = "Line", start = new[] { 0, 0 }, end = new[] { 5, 0 } } }
            });
            var explicitUnits = await Bind(new Dictionary<string, object>
            {
                ["bridge_instance_id"] = Bridge, ["document_id"] = Document, ["expected_revision"] = "42", ["operation_id"] = Operation,
                ["return_state"] = true, ["units"] = "Millimeters", ["entities"] = new[] { new { type = "Line", start = new[] { 0, 0 }, end = new[] { 5, 0 } } }
            });
            var parsed = WriteValidation.Parse("create_entities", explicitUnits.Arguments);
            var preview = Wire.Element(new WritePreviewRequest("create_entities", implicitUnits.Arguments));
            Check(implicitUnits.ValidateOnly && !implicitUnits.ReturnState && explicitUnits.ReturnState &&
                  Wire.ValidateBridge(WritePreviewRequest.Route, preview) == Bridge &&
                  WritePreviewRequest.Parse(preview).Fingerprint() == parsed.Fingerprint(), "Preview/observation flags and omitted units must preserve write intent.");
        });
        await test("write workflow resolves only a revision-bound unique active document", async () =>
        {
            var args = new Dictionary<string, object>
            {
                ["expected_revision"] = Current.write_revision!, ["operation_id"] = Operation,
                ["entities"] = new[] { new { type = "Point", position = new[] { 1, 2, 0 } } }
            };
            var bound = await Bind(args);
            Check(bound.Arguments.GetProperty("document_id").GetString() == Document &&
                  bound.Arguments.GetProperty("bridge_instance_id").GetString() == Bridge &&
                  bound.Arguments.GetProperty("expected_revision").GetString() == "42", "Qualified revision must bind the same drawing.");
            await Reject("STALE_DOCUMENT", () => Bind(args, new(Bridge, Other, [new(Other, "other", null, false, false, true, "42")])));
            await Reject("AMBIGUOUS_DOCUMENT", () => Bind(args, new(Bridge, Document,
                [new(Document, "a", null, false, false, true, "42"), new(Other, "b", null, false, false, false, "42")])));
            await Reject("STALE_DOCUMENT", () => Bind(args, descriptor: Local with { bridge_instance_id = Other }));
            await Reject("AMBIGUOUS_INSTANCE", () => WriteRequestOptions.BindAsync("create_entities", Wire.Element(args),
                _ => throw new DomainException("AMBIGUOUS_INSTANCE", "Choose an instance."), (_, _) => throw new Exception("Do not inspect an ambiguous bridge."), default));
            args["expected_revision"] = "42";
            await Reject("INVALID_ARGUMENT", () => Bind(args));
        });
        await test("write workflow returns verified surviving object geometry and rejects stale observations", async () =>
        {
            var created = new EntityRef(Bridge, Document, "A2");
            var erased = new EntityRef(Bridge, Document, "A1");
            var receipt = Result.Ok(Operation, new ModifiedEntities(Operation,"committed",false,[],[erased],[created],[],"single_command"),Current);
            Task<Result> Read(string tool, object arguments, CancellationToken _)
            {
                var input = Wire.Element(arguments);
                Check(tool == "inspect_objects" && input.GetProperty("handles")[0].GetString() == "A2" &&
                      input.GetProperty("handles").GetArrayLength() == 1 && input.GetProperty("expected_revision").GetString() == "42", "Read only the surviving output at the write revision.");
                return Task.FromResult(Result.Ok(Operation,new { items = new[] { new { success = true, entity = new
                { @ref = created, type = "Line", geometry = new { native_curve = new { curve_token = new string('A',64) }, start = new[] { 0,0,0 }, end = new[] { 10,0,0 } } } } } },Current));
            }
            var current = await WriteResultState.ReadAsync("modify_entities", receipt, Read, default);
            Check(current is { status: "observed", observations: not null } && Wire.Element(current.observations).GetRawText().Contains("curve_token"), "Return the fresh editing token.");
            var stale = await WriteResultState.ReadAsync("modify_entities", receipt,
                async (tool,input,token) => (await Read(tool,input,token)) with { context = Current with { revision = "43" } }, default);
            Check(stale is { status: "unavailable", observations: null } && receipt.success, "Stale observation cannot change the write outcome.");
            var pending = Result.Fail(Operation,new("WRITE_OUTCOME_PENDING","Still running."),Current);
            Check(await WriteResultState.ReadAsync("modify_entities", pending, (_,_,_) => throw new Exception("Pending write read"), default) == null,
                "Pending must not initiate a result read.");
            var earlyReceipt = receipt with { warnings = ["COMMAND_COMPLETION_PENDING: commit is recorded."] };
            Check(await WriteResultState.ReadAsync("modify_entities", earlyReceipt,
                (_,_,_) => throw new Exception("Native command has not ended."), default) is { reason: "COMMAND_COMPLETION_PENDING" },
                "An early commit receipt must wait for command completion before a result read.");
        });
        await test("write workflow transports preview on the read route and observes only committed outputs", async () =>
        {
            var requests = new System.Collections.Concurrent.ConcurrentQueue<Request>();
            var output = new EntityRef(Bridge, Document, "A2");
            using var listener = new BridgeListener(Discovery.Describe(Bridge, "write-workflow-test"), (request, _) =>
            {
                requests.Enqueue(request);
                return Task.FromResult(request.operation switch
                {
                    WritePreviewRequest.Route => Result.Ok(request.requestId,
                        new WritePreviewResult("create_entities", "validated", false, "input_and_available_native_preflight"), Current),
                    "create_entities" => Result.Ok(request.requestId,
                        new { status = "committed", operation_id = Operation, created = new[] { output } }, Current with { revision = "43" }),
                    "inspect_objects" => Result.Ok(request.requestId, new { items = new[] { new
                    {
                        success = true, entity = new { @ref = output, type = "Point", geometry = new { position = new[] { 1, 2, 0 } } }
                    } } }, Current with { revision = "43" }),
                    _ => throw new Exception("Unexpected IPC operation: " + request.operation)
                });
            }, new JsonLog("write-workflow-test"));
            var tool = new ToolService();
            var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Wire.Element(new
            {
                bridge_instance_id = Bridge, document_id = Document, expected_revision = "42", operation_id = Operation,
                entities = new[] { new { type = "Point", position = new[] { 1, 2, 0 } } }, validate_only = true
            }).GetRawText())!;
            var planned = await tool.CallAsync("create_entities", args, default);
            using (var result = JsonDocument.Parse(planned.Content.OfType<TextContentBlock>().Single().Text))
                Check(!result.RootElement.GetProperty("data").GetProperty("document_changed").GetBoolean(), "Preview claims a document change.");
            Check(requests.TryDequeue(out var previewRequest) && previewRequest.operation == WritePreviewRequest.Route &&
                  WritePreviewRequest.Parse(previewRequest.arguments).OperationId == Operation && requests.IsEmpty,
                "A preview must not enqueue the normal write or issue any follow-up.");
            args.Remove("validate_only");
            args.Add("return_state", Wire.Element(true));
            var written = await tool.CallAsync("create_entities", args, default);
            using (var result = JsonDocument.Parse(written.Content.OfType<TextContentBlock>().Single().Text))
            {
                Check(result.RootElement.GetProperty("data").GetProperty("status").GetString() == "committed" &&
                      result.RootElement.GetProperty("write_state").GetProperty("status").GetString() == "observed", "Write result or current state lost in MCP.");
            }
            Check(requests.TryDequeue(out var write) && write.operation == "create_entities" && !write.arguments.TryGetProperty("return_state", out _) &&
                  requests.TryDequeue(out var inspect) && inspect.operation == "inspect_objects" &&
                  inspect.arguments.GetProperty("expected_revision").GetString() == "43" && requests.IsEmpty, "Observation must use the committed revision.");
        });
        await test("write workflow errors carry a field path and allowed values", async () =>
        {
            var details = InputError.Complete(new("INVALID_ARGUMENT", "$.operations[2].pick_point: Expected point.", false,
                new { path = "$.operations[2].pick_point" })).details;
            Check(Wire.Element(details!).GetProperty("rule").GetString() == "Expected point.", "Preserve parser path and expose its rule.");
            try { await Bind(new Dictionary<string, object> { ["validate_only"] = "yes" }); }
            catch (DomainException failure)
            {
                var error = Wire.Element(failure.Error.details!);
                Check(error.GetProperty("path").GetString() == "$.validate_only" && error.GetProperty("allowed").GetArrayLength() == 2, "Boolean error allowed values.");
                return;
            }
            throw new Exception("Accepted invalid validate_only flag.");
        });
    }

    private static Task<WriteRequestOptions> Bind(Dictionary<string, object> args, DocumentList? documents = null, Descriptor? descriptor = null) =>
        WriteRequestOptions.BindAsync("create_entities", Wire.Element(args), _ => descriptor ?? Local,
            (_, _) => Task.FromResult(documents ?? new DocumentList(Bridge, Document, [new(Document, "only", null, false, false, true, "42")])), default);

    private static async Task Reject(string code, Func<Task<WriteRequestOptions>> action)
    {
        try { await action(); }
        catch (DomainException error) when (error.Error.code == code) { return; }
        throw new Exception("Expected " + code);
    }

    private static void Check(bool condition, string detail) { if (!condition) throw new Exception(detail); }
}
