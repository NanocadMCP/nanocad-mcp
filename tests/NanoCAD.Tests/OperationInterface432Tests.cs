using System.Text;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Result = NanoCAD.Contracts.Result;

internal static class OperationInterface432Tests
{
    private static readonly string Bridge = Guid.NewGuid().ToString("D"), Document = Guid.NewGuid().ToString("D");
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Domain(string code, Action action)
    {
        try { action(); throw new Exception("Expected " + code); }
        catch (DomainException error) { Check(error.Error.code == code, "Unexpected error: " + error.Error.code); }
    }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); throw new Exception("Expected " + typeof(T).Name); }
        catch (T) { }
    }
    private static Request Create(string? operationId = null, double end = 20, string revision = "1") =>
        new(Wire.Version, Guid.NewGuid().ToString("D"), "create_entities", 10000, Wire.Element(new
        {
            bridge_instance_id = Bridge, document_id = Document, expected_revision = revision,
            operation_id = operationId ?? Guid.NewGuid().ToString("D"), units = "Millimeters",
            entities = new[] { new { type = "Line", start = new[] { 0d, 0d }, end = new[] { end, 0d } } }
        }));
    private static Request Modify() => new(Wire.Version, Guid.NewGuid().ToString("D"), "modify_entities", 10000, Wire.Element(new
    {
        bridge_instance_id = Bridge, document_id = Document, expected_revision = "1", operation_id = Guid.NewGuid().ToString("D"),
        units = "Millimeters", operations = new[] { new { operation = "copy", handles = new[] { "FFFEEE" }, displacement = new[] { 0d, 20d } } }
    }));
    private static Request Read(string operationId, string mode = "result", string? cursor = null, int pageSize = 100, int waitMs = 0, string? bridge = null)
    {
        var input = new Dictionary<string, object> { ["bridge_instance_id"] = bridge ?? Bridge, ["operation_id"] = operationId,
            ["wait_ms"] = waitMs, ["mode"] = mode, ["page_size"] = pageSize };
        if (cursor != null) input.Add("cursor", cursor);
        return new(Wire.Version, Guid.NewGuid().ToString("D"), "get_operation_result", 10000, Wire.Element(input));
    }
    private static OperationResultLookup432 Lookup(WriteQueue queue, Request request)
    {
        var result = queue.ReadOperationAsync(request, default).GetAwaiter().GetResult();
        Check(result.success && result.request_id == request.requestId && result.context == null, "Lookup must have its own response identity and no fresh document context.");
        return result.data as OperationResultLookup432 ?? throw new Exception("Missing lookup DTO.");
    }
    private static ReadContext Context(string revision = "2") => new(Bridge, Document, revision, "2026-10-19T12:00:00.0000000Z");
    private static EntityRef Ref(string handle) => new(Bridge, Document, handle);
    private static Result Success(WriteJob job, int count = 1, string[]? warnings = null) => Result.Ok(job.OriginalRequestId,
        new CreatedEntities(job.OperationId, "committed", false, Enumerable.Range(1, count).Select(i => Ref(i.ToString("X"))).ToArray(), "single_command"),
        Context(), warnings ?? new[] { "Original native warning." });
    private static WriteJob Start(WriteQueue queue, Request request)
    {
        var reservation = queue.Enqueue(request); Check(!reservation.Replay, "Expected a new operation.");
        var job = queue.Take()!; Check(ReferenceEquals(job, reservation.Job) && job.TryStart(), "Native-free dispatcher did not start the reserved job.");
        return job;
    }
    private static int IpcBytes(Result value) => JsonSerializer.SerializeToUtf8Bytes(value, Wire.Json).Length;
    // This is the same public MCP shape as ToolService.Wrap; test both response modes.
    private static CallToolResult Wrapped(Result value, bool structured)
    {
        var json = Wire.Element(value);
        return new() { IsError = !value.success, StructuredContent = structured ? json : null,
            Content = new List<ContentBlock> { new TextContentBlock { Text = json.GetRawText() } } };
    }
    private static int McpBytes(Result value, bool structured) => JsonSerializer.SerializeToUtf8Bytes(Wrapped(value, structured)).Length;

    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("block432: lookup parser is strict and has no document or page-count requirement", () =>
        {
            var operation = Guid.NewGuid().ToString("D");
            var minimal = Wire.Element(new { bridge_instance_id = Bridge.ToUpperInvariant(), operation_id = operation.ToUpperInvariant() });
            var parsed = OperationResultArguments432.Parse(minimal);
            Check(parsed.BridgeId == Bridge && parsed.OperationId == operation && parsed.WaitMs == 2000 && parsed.Mode == "result" && parsed.PageSize == 100, "Lookup defaults/UUID normalization changed.");
            Check(OperationResultArguments432.Parse(Read(operation, pageSize: int.MaxValue, waitMs: 6000).arguments).PageSize == int.MaxValue, "An artificial page-count ceiling was added.");
            foreach (var invalid in new object[]
            {
                new { operation_id = operation }, new { bridge_instance_id = Bridge, operation_id = Guid.Empty.ToString("D") },
                new { bridge_instance_id = Bridge, operation_id = "not-a-uuid" }, new { bridge_instance_id = Bridge, operation_id = operation, document_id = Document },
                new { bridge_instance_id = Bridge, operation_id = operation, expected_revision = "1" },
                new { bridge_instance_id = Bridge, operation_id = operation, wait_ms = -1 }, new { bridge_instance_id = Bridge, operation_id = operation, wait_ms = 6001 },
                new { bridge_instance_id = Bridge, operation_id = operation, wait_ms = 1.5 }, new { bridge_instance_id = Bridge, operation_id = operation, page_size = 0 },
                new { bridge_instance_id = Bridge, operation_id = operation, page_size = 2147483648L }, new { bridge_instance_id = Bridge, operation_id = operation, mode = "current" },
                new { bridge_instance_id = Bridge, operation_id = operation, cursor = "cursor-with-result-mode" }
            }) Domain("INVALID_ARGUMENT", () => OperationResultArguments432.Parse(Wire.Element(invalid)));
            using var duplicate = JsonDocument.Parse("{\"bridge_instance_id\":\"" + Bridge + "\",\"operation_id\":\"" + operation + "\",\"wait_ms\":0,\"wait_ms\":1}");
            Domain("INVALID_ARGUMENT", () => OperationResultArguments432.Parse(duplicate.RootElement));
            return Task.CompletedTask;
        });

        await test("block432: reader cancellation and timeout do not cancel queued or running writes", () =>
        {
            foreach (bool running in new[] { false, true })
            {
                using var queue = new WriteQueue(Environment.CurrentManagedThreadId);
                var request = Create(); var job = queue.Enqueue(request).Job;
                if (running) { Check(ReferenceEquals(queue.Take(), job) && job.TryStart(), "Start failed."); }
                using var readerCancellation = new CancellationTokenSource();
                var reader = queue.ReadOperationAsync(Read(job.OperationId, waitMs: 6000), readerCancellation.Token);
                Check(!reader.IsCompleted, "Reader did not wait for the existing job."); readerCancellation.Cancel();
                Throws<OperationCanceledException>(() => reader.GetAwaiter().GetResult());
                Check(queue.LedgerCount == 1 && queue.Count == (running ? 0 : 1), "Reader changed queue ownership or reservations.");
                if (!running) Check(ReferenceEquals(queue.Take(), job) && job.TryStart(), "Cancelled reader prevented a queued writer from starting.");
                job.CheckCancellation();
                var timed = Lookup(queue, Read(job.OperationId, waitMs: 1));
                Check(timed is { phase: "running", job_finished: false, commit_recorded: false, outcome: "pending", recorded_result: null }, "Reader timeout changed or invented an outcome.");
                job.CheckCancellation(); job.BeginCommit(); var result = Success(job); job.RecordCommit(result); job.Complete(result);
                Check(Lookup(queue, Read(job.OperationId)).outcome == "committed", "Writer did not finish after reader cancellation.");
            }
            return Task.CompletedTask;
        });

        await test("block432: pending committed and final lookup states retain historical results after payload release", () =>
        {
            using var queue = new WriteQueue(Environment.CurrentManagedThreadId);
            var request = Create(); var job = queue.Enqueue(request).Job;
            Check(Lookup(queue, Read(job.OperationId)) is { phase: "queued", job_finished: false, commit_recorded: false, outcome: "pending" }, "Queued state changed.");
            Check(ReferenceEquals(queue.Take(), job) && job.TryStart(), "Start failed."); job.BeginCommit();
            var committed = Success(job); job.RecordCommit(committed);
            var unfinished = Lookup(queue, Read(job.OperationId));
            Check(unfinished is { phase: "committing", job_finished: false, commit_recorded: true, outcome: "committed", recorded_result_included: true } &&
                ReferenceEquals(unfinished.recorded_result, committed) && unfinished.original_request_id == request.requestId && unfinished.operation == request.operation,
                "Recorded commit was lost or incorrectly called final.");
            var final = committed with { context = Context("3"), warnings = new[] { "Final historical warning." } }; job.Complete(final);
            Throws<InvalidOperationException>(() => { _ = job.Arguments; });
            var finished = Lookup(queue, Read(job.OperationId));
            var finishedResult = finished.recorded_result ?? throw new Exception("Missing final historical result.");
            Check(finished is { phase: "finished", job_finished: true, commit_recorded: true, outcome: "committed" } && ReferenceEquals(finishedResult, final) &&
                finishedResult.request_id == request.requestId && finishedResult.context!.revision == "3" && finished.result_fingerprint != unfinished.result_fingerprint,
                "Final result was reconstructed from arguments or fresh state.");
            Check(queue.LedgerCount == 1 && queue.Count == 0, "Lookup scheduled another operation.");

            using var documents = new WriteQueue(Environment.CurrentManagedThreadId);
            var documentRequest = new Request(Wire.Version, Guid.NewGuid().ToString("D"), "manage_document", 10000,
                Wire.Element(new { bridge_instance_id = Bridge, operation_id = Guid.NewGuid().ToString("D"), action = "new" }));
            var documentJob = Start(documents, documentRequest);
            var completed = Result.Ok(documentJob.OriginalRequestId, new DocumentOperationResult(documentJob.OperationId, "completed", false, "new",
                new(Document, "Historical document", null, false, false, true, "1"), null, null, false), Context("1"));
            documentJob.Complete(completed);
            Check(Lookup(documents, Read(documentJob.OperationId)) is { job_finished: true, commit_recorded: false, outcome: "completed", recorded_result: not null }, "Completed document result was mislabeled as committed or pending.");
            return Task.CompletedTask;
        });

        await test("block432: missing identity bridge mismatch and stopped jobs never imply rollback or permission to repeat", () =>
        {
            using var queue = new WriteQueue(Environment.CurrentManagedThreadId);
            var missing = Lookup(queue, Read(Guid.NewGuid().ToString("D")));
            Check(missing is { found: false, outcome: "unknown", recorded_result: null } && queue.LedgerCount == 0 && queue.Count == 0, "Unknown lookup reserved a write or invented an outcome.");
            var job = Start(queue, Create());
            Domain("BRIDGE_UNAVAILABLE", () => queue.ReadOperationAsync(Read(job.OperationId, bridge: Guid.NewGuid().ToString("D")), default).GetAwaiter().GetResult());
            queue.Dispose();
            var stopped = Lookup(queue, Read(job.OperationId));
            Check(stopped is { found: true, job_finished: true, commit_recorded: false, outcome: "unknown" } &&
                stopped.recorded_result?.error?.details is WriteErrorDetails { outcome: "unknown" }, "Stopping fabricated native completion or rollback.");
            using var queued = new WriteQueue(Environment.CurrentManagedThreadId); var queuedJob = queued.Enqueue(Create()).Job; queued.Dispose();
            Check(Lookup(queued, Read(queuedJob.OperationId)).outcome == "not_started", "Recorded not_started result was lost.");
            return Task.CompletedTask;
        });

        await test("block432: reference pages recover every returned observation with role and provenance", () =>
        {
            using var queue = new WriteQueue(Environment.CurrentManagedThreadId); var job = Start(queue, Modify());
            var created = Enumerable.Range(16, 1600).Select(i => Ref(i.ToString("X"))).ToArray();
            var recorded = Result.Ok(job.OriginalRequestId, new ModifiedEntities(job.OperationId, "committed", false,
                new[] { Ref("D1") }, new[] { Ref("D2") }, created, new[] { new CopyMapping(0, Ref("A1"), created[0]) }, "single_command"), Context());
            job.BeginCommit(); job.RecordCommit(recorded); job.Complete(recorded);
            var observed = new List<OperationReference432>(); var cursors = new HashSet<string>(StringComparer.Ordinal); string? cursor = null, fingerprint = null; int pages = 0;
            do
            {
                var request = Read(job.OperationId, "references", cursor, int.MaxValue); var response = queue.ReadOperationAsync(request, default).GetAwaiter().GetResult();
                Check(IpcBytes(response) <= Wire.PayloadBudget, "Reference lookup exceeded IPC bytes.");
                var lookup = (OperationResultLookup432)response.data!; var page = lookup.references_page!;
                Check(lookup.recorded_result == null && !lookup.recorded_result_included && page.total_count == created.Length + 4 && page.returned_count == page.items.Length &&
                    JsonSerializer.SerializeToUtf8Bytes(page, Wire.Json).Length <= Wire.PayloadBudget / 4, "Reference page is not honest or byte bounded.");
                fingerprint ??= lookup.result_fingerprint; Check(lookup.result_fingerprint == fingerprint, "Stable result changed fingerprint between pages.");
                observed.AddRange(page.items); pages++; Check(page.complete == (page.next_cursor == null), "Reference page cursor/completeness disagree.");
                cursor = page.next_cursor; if (cursor != null) Check(cursors.Add(cursor) && page.returned_count > 0, "Reference page did not make progress.");
            } while (cursor != null);
            Check(pages > 1 && observed.Count == created.Length + 4 && observed.Select(i => i.provenance).Distinct().Count() == observed.Count, "Reference pages lost or duplicated observations.");
            Check(observed.Take(2).Select(i => i.role).SequenceEqual(new[] { "modified", "erased" }) &&
                observed.Skip(2).Take(created.Length).Select(i => i.@ref).SequenceEqual(created) &&
                observed.Skip(2).Take(created.Length).Select(i => i.provenance).SequenceEqual(Enumerable.Range(0, created.Length).Select(i => "/data/created/" + i)) &&
                observed[^2] is { role: "source", provenance: "/data/copies/0/source" } && observed[^2].@ref.handle == "A1" &&
                observed.All(i => i.@ref.handle != "FFFEEE"), "Output roles/provenance were replaced with selected request targets.");
            return Task.CompletedTask;
        });

        await test("block432: warning pages losslessly reassemble oversized Unicode text and empty warnings", () =>
        {
            using var queue = new WriteQueue(Environment.CurrentManagedThreadId); var job = Start(queue, Create());
            string[] warnings = { "", "prefix\r\n" + string.Concat(Enumerable.Repeat(("🧭" + (char)0x0416 + "\\\"<>\r\n"), 12000)) + "end", "last warning" }; // Numeric code points retain technical Unicode glyph coverage.
            var recorded = Success(job, warnings: warnings); job.BeginCommit(); job.RecordCommit(recorded); job.Complete(recorded);
            var restored = warnings.Select(_ => new StringBuilder()).ToArray(); var completed = new bool[warnings.Length]; var cursors = new HashSet<string>(); string? cursor = null; int pages = 0;
            do
            {
                var request = Read(job.OperationId, "warnings", cursor, int.MaxValue); var response = queue.ReadOperationAsync(request, default).GetAwaiter().GetResult();
                var lookup = (OperationResultLookup432)response.data!; var page = lookup.warnings_page!;
                Check(IpcBytes(response) <= Wire.PayloadBudget && JsonSerializer.SerializeToUtf8Bytes(page, Wire.Json).Length <= Wire.PayloadBudget / 4 &&
                    page.total_count == warnings.Length && page.returned_count == page.items.Length, "Warning page is not byte bounded or loses its original count.");
                foreach (var fragment in page.items)
                {
                    int index = fragment.warning_index;
                    Check(!completed[index] && fragment.text_offset == restored[index].Length && fragment.total_characters == warnings[index].Length, "Warning fragments overlap, skip text, or change source length.");
                    Check(fragment.text.Length == 0 || !char.IsLowSurrogate(fragment.text[0]) && !char.IsHighSurrogate(fragment.text[^1]), "Warning fragment split a Unicode surrogate pair.");
                    restored[index].Append(fragment.text); completed[index] = fragment.complete;
                    Check(fragment.complete == (restored[index].Length == warnings[index].Length), "Warning fragment completeness is incorrect.");
                }
                pages++; cursor = page.next_cursor; Check(page.complete == (cursor == null), "Warning page cursor/completeness disagree.");
                if (cursor != null) Check(cursors.Add(cursor) && page.returned_count > 0, "Warning page did not make progress.");
            } while (cursor != null);
            Check(pages > 1 && completed.All(value => value) && restored.Select(value => value.ToString()).SequenceEqual(warnings), "Original Unicode warnings could not be reconstructed exactly.");
            return Task.CompletedTask;
        });

        await test("block432: page cursors bind mode operation and exact recorded result", () =>
        {
            using var queue = new WriteQueue(Environment.CurrentManagedThreadId); var job = Start(queue, Create()); job.BeginCommit();
            var committed = Success(job, 2); job.RecordCommit(committed);
            var first = Lookup(queue, Read(job.OperationId, "references", pageSize: 1)); var cursor = first.references_page!.next_cursor!;
            Check(!first.job_finished && first.commit_recorded && cursor.Length > 0, "Expected a page over an unfinished recorded commit.");
            Domain("INVALID_CURSOR", () => Lookup(queue, Read(job.OperationId, "warnings", cursor)));
            Domain("INVALID_CURSOR", () => Lookup(queue, Read(job.OperationId, "references", "not-base64")));
            var other = Start(queue, Create()); other.BeginCommit(); var otherResult = Success(other, 2); other.RecordCommit(otherResult); other.Complete(otherResult);
            Domain("INVALID_CURSOR", () => Lookup(queue, Read(other.OperationId, "references", cursor)));
            job.Complete(committed with { context = Context("3") });
            Domain("INVALID_CURSOR", () => Lookup(queue, Read(job.OperationId, "references", cursor)));
            Check(Lookup(queue, Read(job.OperationId, "references", pageSize: 1)).result_fingerprint != first.result_fingerprint, "Changed final result reused an earlier fingerprint.");
            return Task.CompletedTask;
        });

        await test("block432: lookup preserves exact replay and conflicting-payload rejection", () =>
        {
            using var queue = new WriteQueue(Environment.CurrentManagedThreadId); var request = Create(); var job = Start(queue, request);
            job.BeginCommit(); var original = Success(job); job.RecordCommit(original); job.Complete(original);
            _ = Lookup(queue, Read(job.OperationId));
            var replayRequest = request with { requestId = Guid.NewGuid().ToString("D") };
            var replay = queue.SubmitAsync(replayRequest, default).GetAwaiter().GetResult();
            Check(replay.success && replay.request_id == replayRequest.requestId && replay.data is CreatedEntities { replayed: true } &&
                replay.context == original.context && replay.warnings.SequenceEqual(original.warnings), "Lookup altered replay outcome or caller identity.");
            Check(queue.Enqueue(Create(job.OperationId, revision: "01")).Replay, "Canonical equivalent replay stopped working.");
            Domain("OPERATION_ID_CONFLICT", () => queue.Enqueue(Create(job.OperationId, end: 21)));
            Check(queue.LedgerCount == 1 && queue.Count == 0 && ReferenceEquals(Lookup(queue, Read(job.OperationId)).recorded_result, original), "Replay or conflict replaced the original ledger result.");
            return Task.CompletedTask;
        });

        await test("block432: fitting responses stay identical and oversized outcomes survive IPC and MCP budgets", () =>
        {
            var operationId = Guid.NewGuid().ToString("D"); var context = Context();
            var small = Result.Ok(Guid.NewGuid().ToString("D"), new CreatedEntities(operationId, "committed", false, new[] { Ref("A") }, "single_command"), context, "Keep this warning.");
            foreach (var measure in new Func<Result, int>[] { IpcBytes, value => McpBytes(value, false), value => McpBytes(value, true) })
            {
                Check(WriteResponseBudget432.TryFit(small, "create_entities", Wire.PayloadBudget, measure, out var unchanged, Bridge) && ReferenceEquals(small, unchanged), "Fitting response was rewritten or bloated with a receipt.");
                Check(!Wire.Element(unchanged).TryGetProperty("receipt", out _), "Old fitting result shape changed.");
            }
            var references = Enumerable.Range(1, 10000).Select(i => Ref(i.ToString("X"))).ToArray();
            var committed = small with { data = new CreatedEntities(operationId, "committed", false, references, "single_command") };
            var awaitingCommand = committed with { warnings = new[] { "COMMAND_COMPLETION_PENDING: commit is recorded; refresh document state before a new write.", new string((char)0x0416, 30000) } }; // Numeric code points retain technical Unicode glyph coverage.
            bool waitingIpcFits = WriteResponseBudget432.TryFit(awaitingCommand, "create_entities", Wire.PayloadBudget, IpcBytes, out var waitingIpc, Bridge);
            var waitingWire = JsonSerializer.Deserialize<Result>(JsonSerializer.SerializeToUtf8Bytes(waitingIpc, Wire.Json), Wire.Json)!;
            bool waitingMcpFits = WriteResponseBudget432.TryFit(waitingWire, "create_entities", Wire.PayloadBudget, value => McpBytes(value, true), out var waitingMcp, Bridge);
            Check(waitingIpcFits && waitingMcpFits && !ReferenceEquals(waitingIpc, awaitingCommand) && !ReferenceEquals(waitingMcp, waitingWire) &&
                waitingIpc.receipt is { status: "committed", outcome: "committed", next_action: "wait_for_result" } &&
                waitingMcp.receipt is { status: "committed", outcome: "committed", next_action: "wait_for_result" } &&
                Wire.Element(waitingMcp.data!).GetProperty("status").GetString() == "committed" && McpBytes(waitingMcp, true) <= Wire.PayloadBudget,
                "Committed result awaiting command completion lost its status or wait action across IPC then MCP compaction.");
            var completed = Result.Ok(Guid.NewGuid().ToString("D"), new DocumentOperationResult(operationId, "completed", false, "new",
                new(Document, "Document", null, false, false, true, "2"), null, null, false), context, new string((char)0x0416, 220000), "Original document warning."); // Numeric code points retain technical Unicode glyph coverage.
            var unknown = Result.Fail(Guid.NewGuid().ToString("D"), new("NATIVE_CLEANUP_UNKNOWN", "Original cleanup diagnostic.", false,
                new WriteErrorDetails(operationId, "unknown", false, "unknown", new { native_evidence = new string((char)0x0416, 220000) })), context) with { warnings = new[] { "Original unknown-outcome warning." } }; // Numeric code points retain technical Unicode glyph coverage.
            foreach (var item in new[] { (Result: committed, Operation: "create_entities", Outcome: "committed"), (Result: completed, Operation: "manage_document", Outcome: "completed"), (Result: unknown, Operation: "modify_entities", Outcome: "unknown") })
            {
                string before = Wire.Element(item.Result).GetRawText();
                foreach (bool structured in new[] { false, true })
                {
                    foreach (bool ipc in new[] { true, false })
                    {
                        int budget = ipc ? Wire.PayloadBudget : Wire.MaxResponseBytes - 16384;
                        Func<Result, int> measure = ipc ? IpcBytes : value => McpBytes(value, structured);
                        Check(measure(item.Result) > budget, "Oversized fixture did not exercise the selected boundary.");
                        Check(WriteResponseBudget432.TryFit(item.Result, item.Operation, budget, measure, out var fitted, Bridge) && measure(fitted) <= budget, "Outcome receipt did not fit actual serialized bytes.");
                        var receipt = fitted.receipt ?? throw new Exception("Oversized write lost its receipt.");
                        Check(fitted.success == item.Result.success && fitted.context == context && receipt is { details_omitted: true } && receipt.operation_id == operationId &&
                            receipt.operation == item.Operation && receipt.outcome == item.Outcome && receipt.historical_context == context &&
                            receipt.original_warning_count == item.Result.warnings.Length && receipt.lookup is { tool: "get_operation_result" } &&
                            receipt.lookup.bridge_instance_id == Bridge && receipt.lookup.operation_id == operationId, "Receipt lost write identity, outcome or lookup context.");
                        if (item.Result.success) Check(Wire.Element(fitted.data!).GetProperty("status").GetString() == item.Outcome, "A known success became a generic response-limit failure.");
                        else
                        {
                            var fittedError = fitted.error ?? throw new Exception("Compaction discarded the original error."); var originalError = item.Result.error!;
                            Check(fittedError.code == originalError.code && fittedError.message == originalError.message && receipt.original_error_code == originalError.code &&
                                Wire.Element(fittedError.details!).GetProperty("outcome").GetString() == "unknown", "Unknown error code/message/outcome was replaced by response-limit advice.");
                        }
                        if (receipt.warnings_complete) Check(fitted.warnings.SequenceEqual(item.Result.warnings), "Complete warnings changed during compaction.");
                        else Check(fitted.warnings.Contains(WriteResponseBudget432.PagedWarning), "Omitted warnings lack explicit paging guidance.");
                        var wrapped = Wrapped(fitted, structured); Check(wrapped.IsError == !item.Result.success, "MCP wrapper changed success/error classification.");
                    }
                }
                Check(Wire.Element(item.Result).GetRawText() == before, "Presentation compaction mutated the original ledger result.");
            }
            return Task.CompletedTask;
        });

        await test("block432: lookup compaction preserves page cursors and handles near-limit JSON depth", () =>
        {
            using var queue = new WriteQueue(Environment.CurrentManagedThreadId); var job = Start(queue, Create());
            var original = Success(job, 7000, new[] { string.Concat(Enumerable.Repeat(("🧭" + (char)0x0416), 50000)), "Last original warning." }); // Numeric code points retain technical Unicode glyph coverage.
            job.BeginCommit(); job.RecordCommit(original); job.Complete(original);
            var lookupResult = queue.ReadOperationAsync(Read(job.OperationId, pageSize: 1), default).GetAwaiter().GetResult();
            var before = (OperationResultLookup432)lookupResult.data!;
            foreach (bool serialized in new[] { false, true })
            {
                var input = serialized ? lookupResult with { data = Wire.Element(before) } : lookupResult;
                Check(WriteResponseBudget432.TryFit(input, "get_operation_result", Wire.PayloadBudget, IpcBytes, out var fitted, Bridge), "Oversized historical lookup did not fit.");
                var after = WriteResponseBudget432.Lookup(fitted)!;
                Check(after is { recorded_result: null, recorded_result_included: false } && after.operation_id == before.operation_id && after.phase == before.phase &&
                    after.job_finished == before.job_finished && after.commit_recorded == before.commit_recorded && after.outcome == before.outcome && after.result_fingerprint == before.result_fingerprint &&
                    Wire.Element(after.references_page!).GetRawText() == Wire.Element(before.references_page!).GetRawText() &&
                    Wire.Element(after.warnings_page!).GetRawText() == Wire.Element(before.warnings_page!).GetRawText(), "Lookup compaction discarded or changed retained reference/warning pages.");
            }

            Result? deepest = null;
            for (int depth = 0; depth < 40; depth++)
            {
                using var document = JsonDocument.Parse(new string('[', depth) + "0" + new string(']', depth), new JsonDocumentOptions { MaxDepth = 128 });
                var candidate = Result.Ok(Guid.NewGuid().ToString("D"), new { operation_id = job.OperationId, status = "committed", native_detail = document.RootElement.Clone() }, Context());
                try { _ = IpcBytes(candidate); deepest = candidate; } catch (JsonException) { break; }
            }
            Check(deepest != null, "No serializable deep result fixture.");
            var deepLookup = OperationResult432.Recorded(Guid.NewGuid().ToString("D"), OperationResultArguments432.Parse(Read(job.OperationId).arguments),
                "create_entities", deepest!.request_id, "finished", true, true, deepest);
            Throws<JsonException>(() => { _ = IpcBytes(deepLookup); });
            Check(WriteResponseBudget432.TryFit(deepLookup, "get_operation_result", Wire.PayloadBudget, IpcBytes, out var compactDeep, Bridge) &&
                WriteResponseBudget432.Lookup(compactDeep) is { outcome: "committed", job_finished: true, commit_recorded: true, recorded_result_included: false, recorded_result: null },
                "Nested depth failure lost a known historical result instead of omitting only nested details.");
            return Task.CompletedTask;
        });
    }
}
