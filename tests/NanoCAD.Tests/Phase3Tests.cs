using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using NanoCAD.McpServer;

internal static class Phase3Tests
{
    private static readonly string Bridge = Guid.NewGuid().ToString("D"), Document = Guid.NewGuid().ToString("D");
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Domain(string code, Action action)
    {
        try { action(); throw new Exception("Expected " + code); }
        catch (DomainException error) { Check(error.Error.code == code, "Unexpected error: " + error.Error.code); }
    }
    private static object Move(string[]? handles = null, double x = 10) => new { operation = "move", handles = handles ?? new[] { "A" }, displacement = new[] { x, 0.0 } };
    private static object Copy(string[]? handles = null) => new { operation = "copy", handles = handles ?? new[] { "A" }, displacement = new[] { 0, 20 } };
    private static Request Request(object[]? edits = null, string? operationId = null, string revision = "1") =>
        new(Wire.Version, Guid.NewGuid().ToString("D"), "modify_entities", 10000, Wire.Element(new
        { bridge_instance_id = Bridge, document_id = Document, operation_id = operationId ?? Guid.NewGuid().ToString("D"), expected_revision = revision, units = "Millimeters", operations = edits ?? new[] { Copy() } }));
    private static Result Success(WriteJob job)
    {
        var source = new EntityRef(Bridge, Document, "A"); var copy = new EntityRef(Bridge, Document, "B");
        return Result.Ok(job.OriginalRequestId, new ModifiedEntities(job.OperationId, "committed", false,
            Array.Empty<EntityRef>(), Array.Empty<EntityRef>(), new[] { copy }, new[] { new CopyMapping(0, source, copy) }, "single_command"), new(Bridge, Document, "2", DateTime.UtcNow.ToString("O")));
    }
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("modify validation: all six operations and ordered source semantics", () =>
        {
            object[] edits = { Move(), new { operation = "rotate", handles = new[] { "A" }, center = new[] { 0, 0 }, angle = Math.PI / 2 },
                new { operation = "scale", handles = new[] { "A" }, center = new[] { 1, 1 }, factor = 2 }, Copy(),
                new { operation = "set_properties", handles = new[] { "A" }, properties = new { layer = "TARGET", color_aci = 1, linetype = "Continuous", lineweight_hundredth_mm = 35 } },
                new { operation = "erase", handles = new[] { "A" } } };
            var parsed = ModifyValidation.Parse(Request(edits).arguments);
            Check(parsed.Operations.Select(e => e.Operation).SequenceEqual(new[] { "move", "rotate", "scale", "copy", "set_properties", "erase" }), "ordered parser lost edit kind");
            Domain("INVALID_OPERATION_ORDER", () => ModifyValidation.Parse(Request(new object[] { edits[^1], Copy() }).arguments));
            Domain("INVALID_OPERATION_ORDER", () => ModifyValidation.Parse(Request(new object[] { edits[^1], edits[^1] }).arguments));
            Check(ModifyValidation.Parse(Request(new object[] { Copy(), edits[^1] }).arguments).Operations.Length == 2, "copy before erase should be legal");
            return Task.CompletedTask;
        });
        await test("modify fingerprint: normalized handles, operation order and every edit parameter", () =>
        {
            var request = Request(new[] { Move(new[] { "000a" }, -0.0) });
            var parsed = ModifyValidation.Parse(request.arguments);
            var normalized = ModifyValidation.Parse(Request(new[] { Move(new[] { "A" }, 0) }, parsed.OperationId, "0001").arguments);
            Check(parsed.Fingerprint() == normalized.Fingerprint(), "handle/zero/revision normalization");
            Check(parsed.Fingerprint() != ModifyValidation.Parse(Request(new[] { Move(new[] { "A" }, 1) }, parsed.OperationId).arguments).Fingerprint(), "displacement lost");
            var ordered = Request(new[] { Move(), Copy() }); var order = ModifyValidation.Parse(ordered.arguments);
            Check(order.Fingerprint() != ModifyValidation.Parse(Request(new[] { Copy(), Move() }, order.OperationId).arguments).Fingerprint(), "edit order lost");
            foreach (var pair in new (object First, object Second)[] {
                (new { operation="rotate", handles=new[]{"A"}, center=new[]{0,0}, angle=1 }, new { operation="rotate", handles=new[]{"A"}, center=new[]{0,0}, angle=2 }),
                (new { operation="scale", handles=new[]{"A"}, center=new[]{0,0}, factor=2 }, new { operation="scale", handles=new[]{"A"}, center=new[]{1,0}, factor=2 }),
                (new { operation="set_properties", handles=new[]{"A"}, properties=new{color_aci=1} }, new { operation="set_properties", handles=new[]{"A"}, properties=new{color_aci=2} }) })
                Check(ModifyValidation.Parse(Request(new[] { pair.First }).arguments).Fingerprint() != ModifyValidation.Parse(Request(new[] { pair.Second }).arguments).Fingerprint(), "derived edit field was lost");
            return Task.CompletedTask;
        });
        await test("modify rejects ambiguous geometry, handles, properties and raw fields", () =>
        {
            foreach (var edit in new object[] {
                new { operation="scale", handles=new[]{"A"}, center=new[]{0,0}, factor=0 },
                new { operation="scale", handles=new[]{"A"}, center=new[]{0,0}, factor=-1 },
                new { operation="scale", handles=new[]{"A"}, center=new[]{0,0}, factor=1e7 },
                new { operation="move", handles=new[]{"A"}, displacement=new[]{0,0,1} },
                new { operation="rotate", handles=new[]{"A"}, center=new[]{0,0}, angle=20 } })
                Domain("INVALID_GEOMETRY", () => ModifyValidation.Parse(Request(new[] { edit }).arguments));
            Domain("INVALID_ARGUMENT", () => ModifyValidation.Parse(Request(new[] { Move(new[] { "a", "0A" }) }).arguments));
            Domain("INVALID_HANDLE", () => ModifyValidation.Parse(Request(new[] { Move(new[] { "0" }) }).arguments));
            foreach (var properties in new object[] { new { }, new { color_aci = 257 }, new { color_aci = 1.5 }, new { lineweight_hundredth_mm = 12 }, new { layer = "" }, new { linetype = (string?)null }, new { rotation = 1 } })
                Domain("INVALID_ARGUMENT", () => ModifyValidation.Parse(Request(new[] { new { operation = "set_properties", handles = new[] { "A" }, properties } }).arguments));
            var original = Request(new[] { Move() });
            foreach (string raw in new[] {
                original.arguments.GetRawText().Replace("\"operation\":\"move\"", "\"operation\":\"move\",\"displacement\":[1,1]"),
                original.arguments.GetRawText().Replace("\"operations\":", "\"command\":\"_ERASE\",\"operations\":"),
                original.arguments.GetRawText().Replace("[10,0]", "[1e999,0]") })
            {
                using var json = JsonDocument.Parse(raw);
                try { ModifyValidation.Parse(json.RootElement); throw new Exception("Malformed payload accepted"); } catch (DomainException) { }
            }
            return Task.CompletedTask;
        });
        await test("modify bounded batch and independent copy budget", () =>
        {
            Domain("INVALID_ARGUMENT", () => ModifyValidation.Parse(Request(Array.Empty<object>()).arguments));
            Domain("INVALID_ARGUMENT", () => ModifyValidation.Parse(Request(Enumerable.Repeat(Move(), 101).ToArray()).arguments));
            var hundred = Enumerable.Range(1, 100).Select(i => i.ToString("X")).ToArray();
            Domain("INVALID_ARGUMENT", () => ModifyValidation.Parse(Request(new[] { Move(hundred), Move(new[] { "100" }) }).arguments));
            Domain("INVALID_ARGUMENT", () => ModifyValidation.Parse(Request(Enumerable.Repeat(Move(hundred), 11).ToArray()).arguments));
            Domain("INVALID_ARGUMENT", () => ModifyValidation.Parse(Request(new[] { Copy(hundred), Copy() }).arguments));
            Check(ModifyValidation.Parse(Request(new[] { Copy(hundred) }).arguments).Operations.Length == 1, "boundary copy budget rejected");
            return Task.CompletedTask;
        });
        await test("create and modify share a non-evicting operation_id namespace and FIFO", () =>
        {
            using var queue = new WriteQueue(Environment.CurrentManagedThreadId);
            var modify = Request(); var parsed = ModifyValidation.Parse(modify.arguments);
            var create = new Request(Wire.Version, Guid.NewGuid().ToString("D"), "create_entities", 10000, Wire.Element(new
            { bridge_instance_id = Bridge, document_id = Document, operation_id = parsed.OperationId, expected_revision = "1", units = "Millimeters",
                entities = new[] { new { type = "Line", start = new[] { 0, 0 }, end = new[] { 1, 0 } } } }));
            queue.Enqueue(modify);
            Domain("OPERATION_ID_CONFLICT", () => queue.Enqueue(create));
            var second = Request(); queue.Enqueue(second);
            Check(queue.Take()!.OriginalRequestId == modify.requestId && queue.Take()!.OriginalRequestId == second.requestId, "write kinds do not share serial FIFO");
            using var other = new WriteQueue(Environment.CurrentManagedThreadId); other.Enqueue(create);
            Domain("OPERATION_ID_CONFLICT", () => other.Enqueue(modify));
            return Task.CompletedTask;
        });
        await test("modify copy: cancelled queued work and committed replay after disconnect/Undo", () =>
        {
            using var queue = new WriteQueue(Environment.CurrentManagedThreadId);
            using var cancelled = new CancellationTokenSource();
            var abandoned = queue.SubmitAsync(Request(), cancelled.Token); cancelled.Cancel();
            try { abandoned.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
            Check(queue.Take() == null, "cancelled edit reached native execution");
            using var disconnected = new CancellationTokenSource(); var request = Request();
            var owner = queue.SubmitAsync(request, disconnected.Token); var job = queue.Take()!;
            Check(job.Operation == "modify_entities" && job.TryStart(), "modify dispatch kind");
            job.BeginCommit(); var committed = Success(job); job.RecordCommit(committed); disconnected.Cancel(); job.Complete(committed);
            try { owner.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
            var replay = queue.SubmitAsync(request with { requestId = Guid.NewGuid().ToString("D") }, default).GetAwaiter().GetResult();
            Check(replay.success && replay.data is ModifiedEntities { replayed: true } result && result.created.Single().handle == "B" && result.copies.Single().source.handle == "A", "copy mapping/replay lost");
            Check(replay.context == committed.context && queue.Take() == null, "historical replay changed context or rescheduled work");
            return Task.CompletedTask;
        });
        await test("modify invalid input never reaches IPC; post-send reply loss remains unknown", async () =>
        {
            int calls = 0; var descriptor = Discovery.Describe(Bridge, "phase3-fake-transport-no-CAD");
            using var listener = new BridgeListener(descriptor, (request, _) =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult(Result.Ok("mismatched-id", new { }));
            }, new JsonLog("phase3-test-bridge"));
            var input = Request().arguments.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
            input.Remove("expected_revision");
            var bad = await new ToolService().CallAsync("modify_entities", input, default);
            Check(bad.IsError == true && calls == 0, "invalid modification reached host");
            var request = Request();
            try { await new BridgeClient().CallAsync(descriptor, request, default); throw new Exception("Lost write identity accepted"); }
            catch (DomainException e) { Check(e.Error.code == "WRITE_OUTCOME_UNKNOWN" && e.Error.details is WriteErrorDetails { outcome: "unknown" }, "modification classified as safe retry"); }
            Check(calls == 1, "transport did not send request");
        });
    }
}
