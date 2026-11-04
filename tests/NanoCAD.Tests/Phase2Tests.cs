using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using NanoCAD.McpServer;

internal static class Phase2Tests
{
    private static readonly string Bridge = Guid.NewGuid().ToString("D"), Document = Guid.NewGuid().ToString("D");
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Domain(string code, Action action)
    {
        try { action(); throw new Exception("Expected " + code); }
        catch (DomainException error) { Check(error.Error.code == code, "Unexpected error: " + error.Error.code); }
    }
    private static object Line(double end = 20) => new { type = "Line", start = new[] { 0.0, 0.0 }, end = new[] { end, 0.0 } };
    private static Request Request(string? id = null, object[]? entities = null, string revision = "1", int timeout = 10000) =>
        new(Wire.Version, Guid.NewGuid().ToString("D"), "create_entities", timeout, Wire.Element(new
        {
            bridge_instance_id = Bridge,
            document_id = Document,
            expected_revision = revision,
            operation_id = id ?? Guid.NewGuid().ToString("D"),
            units = "Millimeters",
            entities = entities ?? new[] { Line() }
        }));
    private static Result Success(WriteJob job) => Result.Ok(job.OriginalRequestId,
        new CreatedEntities(job.OperationId, "committed", false, new[] { new EntityRef(Bridge, Document, "AB") }, "single_command"),
        new(Bridge, Document, "2", DateTime.UtcNow.ToString("O")));
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("create validation: five types, canonical fingerprint and geometry bounds", () =>
        {
            object[] entities =
            {
                Line(), new { type = "Circle", center = new[] { 30, 10 }, radius = 5 },
                new { type = "Arc", center = new[] { 50, 10 }, radius = 5, start_angle = 0, sweep_angle = Math.PI / 2 },
                new { type = "Polyline", closed = true, vertices = new[] {
                    new { point = new[] { 0, 20 }, bulge = 0.0 }, new { point = new[] { 20, 20 }, bulge = .25 },
                    new { point = new[] { 20, 35 }, bulge = 0.0 }, new { point = new[] { 0, 35 }, bulge = 0.0 } } },
                new { type = "Text", text = "MCP Phase 2: test", position = new[] { 0, 45 }, height = 2.5 }
            };
            var parsed = CreateValidation.Parse(Request(entities: entities).arguments);
            Check(parsed.Entities.Length == 5 && parsed.Entities[4] is TextSpec { TextStyle: "Standard" }, "type/default parsing");
            var original = Request(); var args = CreateValidation.Parse(original.arguments);
            var explicitDefaults = Request(args.OperationId, new object[] { new { type = "Line", layer = "0", start = new[] { -0.0, 0.0 }, end = new[] { 20.0, 0.0 } } }, "0001");
            Check(args.Fingerprint() == CreateValidation.Parse(explicitDefaults.arguments).Fingerprint(), "defaults/-0/revision normalization");
            Check(args.Fingerprint() != CreateValidation.Parse(Request(args.OperationId, new[] { Line(21) }).arguments).Fingerprint(), "geometry was lost from fingerprint");
            Check(parsed.Fingerprint() != CreateValidation.Parse(Request(entities: entities.Reverse().ToArray()).arguments).Fingerprint(), "entity order must be significant");
            foreach (var invalid in new object[] {
                new { type = "Line", start = new[] { 0, 0 }, end = new[] { 0, 0 } },
                new { type = "Circle", center = new[] { 0, 0 }, radius = -1 },
                new { type = "Arc", center = new[] { 0, 0 }, radius = 1, start_angle = 0, sweep_angle = Math.PI * 2 },
                new { type = "Line", start = new[] { 0, 0, 1 }, end = new[] { 1, 1 } },
                new { type = "Text", text = "x", position = new[] { 0, 0 }, height = 0 },
                new { type = "Polyline", closed = true, vertices = new[] { new { point = new[] { 0, 0 } }, new { point = new[] { 1, 1 } }, new { point = new[] { 2, 2 } } } },
                new { type = "Polyline", vertices = new[] { new { point = new[] { 0, 0 }, bulge = 0 }, new { point = new[] { 1, 1 }, bulge = 1 } } }
            }) Domain("INVALID_GEOMETRY", () => CreateValidation.Parse(Request(entities: new[] { invalid }).arguments));
            Domain("INVALID_ARGUMENT", () => CreateValidation.Parse(Request(entities: Array.Empty<object>()).arguments));
            Check(CreateValidation.Parse(Request(entities: Enumerable.Repeat(Line(), 101).ToArray()).arguments).Entities.Length == 101, "Former count ceiling must not reject a request within the byte budget");
            Domain("INVALID_ARGUMENT", () => CreateValidation.Parse(Request(entities: new object[] { new { type = "Text", text = "%<field>%", position = new[] { 0, 0 }, height = 1 } }).arguments));
            foreach (string raw in new[] {
                original.arguments.GetRawText().Replace("\"Millimeters\"", "\"Meters\""),
                original.arguments.GetRawText().Replace("\"entities\":", "\"operation_id\":\"" + args.OperationId + "\",\"entities\":"),
                original.arguments.GetRawText().Replace("\"type\":\"Line\"", "\"type\":\"Line\",\"start\":[1,1]"),
                original.arguments.GetRawText().Replace("[20,0]", "[1e999,0]")
            })
            {
                using var json = JsonDocument.Parse(raw);
                try { CreateValidation.Parse(json.RootElement); throw new Exception("Invalid create payload accepted"); }
                catch (DomainException) { }
            }
            return Task.CompletedTask;
        });
        await test("write journal: concurrent duplicate reservation and normalized payload conflicts", async () =>
        {
            using var queue = new WriteQueue(Environment.CurrentManagedThreadId);
            var request = Request();
            var reservations = await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => Task.Run(() => queue.Enqueue(request))));
            Check(reservations.Count(r => !r.Replay) == 1 && reservations.All(r => ReferenceEquals(r.Job, reservations[0].Job)), "duplicate scheduling");
            var id = reservations[0].Job.OperationId;
            Domain("OPERATION_ID_CONFLICT", () => queue.Enqueue(Request(id, new[] { Line(21) })));
            Domain("OPERATION_ID_CONFLICT", () => queue.Enqueue(Request(id, revision: "2")));
            Check(queue.Enqueue(Request(id, revision: "01")).Replay && queue.Count == 1 && queue.LedgerCount == 1, "canonical replay");
        });
        await test("write journal: pre-start cancellation never executes later", () =>
        {
            using var queue = new WriteQueue(Environment.CurrentManagedThreadId);
            using var cancel = new CancellationTokenSource(); var request = Request();
            var pending = queue.SubmitAsync(request, cancel.Token); cancel.Cancel();
            try { pending.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
            Check(queue.Take() == null, "cancelled job reached native execution");
            var replay = queue.SubmitAsync(request with { requestId = Guid.NewGuid().ToString("D") }, default).GetAwaiter().GetResult();
            Check(!replay.success && replay.error!.details is WriteErrorDetails { outcome: "not_started", replayed: true }, "cancel replay outcome");
            return Task.CompletedTask;
        });
        await test("write journal: pending native command cannot execute an expired operation", () =>
        {
            using var queue = new WriteQueue(Environment.CurrentManagedThreadId);
            using var cancel = new CancellationTokenSource(); var request = Request();
            var pending = queue.SubmitAsync(request, cancel.Token);
            var job = queue.Take()!; cancel.Cancel();
            try { pending.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
            Check(!job.TryStart() && job.Phase == WritePhase.Finished, "late callback executed cancelled payload");
            return Task.CompletedTask;
        });
        await test("write journal: cancellation before commit requires rollback", () =>
        {
            using var queue = new WriteQueue(Environment.CurrentManagedThreadId);
            using var cancel = new CancellationTokenSource(); var request = Request();
            var pending = queue.SubmitAsync(request, cancel.Token);
            var job = queue.Take()!; Check(job.TryStart(), "start"); cancel.Cancel();
            try { job.BeginCommit(); throw new Exception("Cancelled job crossed Commit"); } catch (OperationCanceledException) { }
            job.Complete(job.Error("CANCELLED", "rolled back in native adapter", "rolled_back"));
            try { pending.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
            var replay = queue.SubmitAsync(request, default).GetAwaiter().GetResult();
            Check(replay.error!.details is WriteErrorDetails { outcome: "rolled_back", replayed: true }, "rollback must be durable");
            return Task.CompletedTask;
        });
        await test("write journal: commit survives disconnect, replay and later state changes", () =>
        {
            using var queue = new WriteQueue(Environment.CurrentManagedThreadId);
            using var cancel = new CancellationTokenSource(); var request = Request();
            var pending = queue.SubmitAsync(request, cancel.Token);
            var job = queue.Take()!; Check(job.TryStart(), "start"); job.BeginCommit();
            job.RecordCommit(Success(job)); cancel.Cancel();
            try { pending.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
            var inFlightReplay = queue.SubmitAsync(request, default).GetAwaiter().GetResult();
            Check(inFlightReplay.success && inFlightReplay.warnings.Length == 1, "recorded commit unavailable before end event");
            job.Complete(Success(job) with { context = new(Bridge, Document, "3", DateTime.UtcNow.ToString("O")) });
            var callerId = Guid.NewGuid().ToString("D");
            var replay = queue.SubmitAsync(request with { requestId = callerId }, default).GetAwaiter().GetResult();
            Check(replay.request_id == callerId && replay.context!.revision == "3" && replay.data is CreatedEntities { replayed: true }, "replay identity/context");
            Check(queue.Take() == null && queue.LedgerCount == 1, "replay recreated entities");
            Domain("OPERATION_ID_CONFLICT", () => queue.Enqueue(Request(job.OperationId, revision: "99")));
            return Task.CompletedTask;
        });
        await test("write journal: duplicate client's cancellation does not cancel owner", () =>
        {
            using var queue = new WriteQueue(Environment.CurrentManagedThreadId);
            var request = Request(); var owner = queue.SubmitAsync(request, default);
            using var cancelReplay = new CancellationTokenSource();
            var duplicate = queue.SubmitAsync(request, cancelReplay.Token); cancelReplay.Cancel();
            try { duplicate.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
            var job = queue.Take()!; Check(job.TryStart(), "duplicate cancelled owner"); job.BeginCommit(); job.RecordCommit(Success(job)); job.Complete(Success(job));
            Check(owner.GetAwaiter().GetResult().success, "owner failed");
            return Task.CompletedTask;
        });
        await test("write journal: capacity never evicts IDs and stop retains recorded commit", () =>
        {
            using var queue = new WriteQueue(Environment.CurrentManagedThreadId, 1);
            var request = Request(); var reservation = queue.Enqueue(request);
            Domain("BUSY", () => queue.Enqueue(Request()));
            var job = queue.Take()!; Check(job.TryStart(), "start"); job.BeginCommit(); job.RecordCommit(Success(job));
            var second = queue.Enqueue(Request());
            Domain("BUSY", () => queue.Enqueue(Request()));
            Check(queue.Enqueue(request).Replay, "busy queue refused known ID");
            queue.Dispose();
            Check(job.Completion.GetAwaiter().GetResult().success, "stop lost commit");
            Check(second.Job.Completion.GetAwaiter().GetResult().error!.details is WriteErrorDetails { outcome: "not_started" }, "stop allowed queued write");
            return Task.CompletedTask;
        });
        await test("write journal: long sessions retain early replay without lifetime operation ceiling", () =>
        {
            using var queue = new WriteQueue(Environment.CurrentManagedThreadId);
            var first = Request();
            WriteJob? original = null;
            for (int i = 0; i < 600; i++)
            {
                var reservation = queue.Enqueue(i == 0 ? first : Request());
                var job = queue.Take()!;
                if (i == 0) original = job;
                Check(job.TryStart(), "start"); job.BeginCommit(); job.RecordCommit(Success(job)); job.Complete(Success(job));
            }
            var replay = queue.Enqueue(first);
            Check(queue.LedgerCount == 600 && queue.Count == 0, "lifetime ceiling or replay enqueued again");
            Check(replay.Replay && ReferenceEquals(replay.Job, original) && replay.Job.Completion.Result.success, "early committed result was lost");
            return Task.CompletedTask;
        });
        await test("create tool validates before IPC and secured transport preserves write outcome", async () =>
        {
            int calls = 0;
            var descriptor = Discovery.Describe(Bridge, "phase2-fake-transport-no-CAD");
            using var listener = new BridgeListener(descriptor, (request, _) =>
            {
                Interlocked.Increment(ref calls);
                var parsed = CreateValidation.Parse(request.arguments);
                return Task.FromResult(Result.Ok(request.requestId,
                    new CreatedEntities(parsed.OperationId, "committed", false, new[] { new EntityRef(Bridge, Document, "CA") }, "single_command"),
                    new(Bridge, Document, "2", DateTime.UtcNow.ToString("O"))));
            }, new JsonLog("phase2-test-bridge"));
            var service = new ToolService(); var input = Request().arguments;
            var dictionary = input.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
            var response = await service.CallAsync("create_entities", dictionary, default);
            Check(response.IsError == false && calls == 1 && response.StructuredContent!.Value.GetProperty("data").GetProperty("created")[0].GetProperty("handle").GetString() == "CA", "write roundtrip");
            dictionary.Remove("operation_id");
            var invalid = await service.CallAsync("create_entities", dictionary, default);
            Check(invalid.IsError == true && calls == 1, "invalid request reached host");
        });
        await test("write transport: timeout or mismatched reply after send has unknown outcome", async () =>
        {
            var descriptor = Discovery.Describe(Bridge, "phase2-fake-transport-no-CAD");
            int received = 0;
            using var listener = new BridgeListener(descriptor, async (request, token) =>
            {
                Interlocked.Increment(ref received);
                if (request.timeoutMs < 1000) await Task.Delay(10000, token);
                return Result.Ok("wrong-response-id", new { });
            }, new JsonLog("phase2-test-transport-loss"));
            foreach (var timeout in new[] { 500, 10000 })
            {
                var request = Request(timeout: timeout);
                try
                {
                    await new BridgeClient().CallAsync(descriptor, request, default);
                    throw new Exception("Transport ambiguity reported as success");
                }
                catch (DomainException error)
                {
                    Check(error.Error.code == "WRITE_OUTCOME_UNKNOWN" && !error.Error.retryable &&
                        error.Error.details is WriteErrorDetails { outcome: "unknown" } details &&
                        details.operation_id == CreateValidation.Parse(request.arguments).OperationId, "lost write identity/outcome");
                }
            }
            Check(received == 2, "test must reach the peer before failing");
        });
    }
}
