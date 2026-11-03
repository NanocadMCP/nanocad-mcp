using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using NanoCAD.McpServer;

internal static class DocumentTests
{
    private static readonly string Bridge = Guid.NewGuid().ToString("D"), Doc = Guid.NewGuid().ToString("D");
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static void Domain(string code, Action action)
    { try { action(); throw new Exception("Expected " + code); } catch (DomainException e) { Check(e.Error.code == code, e.Error.code); } }
    private static Request Open(string? id = null, string path = @"C:\Projects\Test Ω 1.dxf") => new(Wire.Version, Guid.NewGuid().ToString("D"), "manage_document", 10000,
        Wire.Element(new { bridge_instance_id = Bridge, operation_id = id ?? Guid.NewGuid().ToString("D"), action = "open", path }));
    public static DocumentInfo Info(string document) => new(document, "Test Ω 1.dxf", @"C:\Projects\Test Ω 1.dxf", true, false, true, "2");
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("new document uses native defaults without a path and replays only once", () =>
        {
            var fields = new Dictionary<string, object?> { ["bridge_instance_id"] = Bridge, ["operation_id"] = Guid.NewGuid().ToString("D"), ["action"] = "new" };
            var args = DocumentValidation.Parse(Wire.Element(fields));
            Check(args.Path == null && args.DocumentId == null && args.ExpectedRevision == null && !args.Overwrite && !args.ReadOnly, "new must leave defaults to the host");
            foreach (string field in new[] { "path", "template", "document_id", "expected_revision", "units", "read_only", "overwrite" })
            {
                fields[field] = "unrequested";
                Domain("INVALID_ARGUMENT", () => DocumentValidation.Parse(Wire.Element(fields)));
                fields.Remove(field);
            }
            using var queue = new WriteQueue(Environment.CurrentManagedThreadId);
            var request = new Request(Wire.Version, Guid.NewGuid().ToString("D"), "manage_document", 10000, Wire.Element(fields));
            var (job, _) = queue.Enqueue(request); queue.Take(); Check(job.TryStart(), "new start"); job.BeginCommit();
            var info = new DocumentInfo(Doc, "Drawing1.dwg", null, false, false, true, "0");
            var result = Result.Ok(request.requestId, new DocumentOperationResult(job.OperationId, "completed", false, "new", info, null, null, false));
            job.RecordCommit(result); job.Complete(result);
            var (same, replay) = queue.Enqueue(request with { requestId = Guid.NewGuid().ToString("D") });
            Check(replay && ReferenceEquals(job, same) && queue.Take() == null, "new must not create duplicate tabs on replay");
            fields["action"] = "open"; fields["path"] = @"C:\different.dwg";
            Domain("OPERATION_ID_CONFLICT", () => queue.Enqueue(request with { arguments = Wire.Element(fields) }));
            return Task.CompletedTask;
        });
        await test("DWG save preflight blocks linetype counts known to lose data on native roundtrip", () =>
        {
            foreach (int count in new[] { 0, 2, 64, 255 }) DwgSaveValidation.RequireLinetypeElementCount(count);
            foreach (int count in new[] { 256, 257, int.MaxValue }) Domain("UNSUPPORTED_DWG_CONTENT", () => DwgSaveValidation.RequireLinetypeElementCount(count));
            Domain("OBJECT_READ_FAILED", () => DwgSaveValidation.RequireLinetypeElementCount(-1));
            return Task.CompletedTask;
        });
        await test("file preflight preserves existing content and rejects read-only/missing destinations", () =>
        {
            string root = Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!, "file-policy");
            Directory.CreateDirectory(root);
            string existing = Path.Combine(root, "existing.dwg");
            File.WriteAllText(existing, "existing file must survive preflight");
            Domain("FILE_EXISTS", () => DocumentFilePolicy.Check(existing, false, false));
            Check(File.ReadAllText(existing) == "existing file must survive preflight", "existing file changed");
            File.SetAttributes(existing, FileAttributes.ReadOnly);
            try
            {
                Domain("FILE_READ_ONLY", () => DocumentFilePolicy.Check(existing, false, true));
                DocumentFilePolicy.Check(existing, true, false);
            }
            finally { File.SetAttributes(existing, FileAttributes.Normal); }
            string absent = Path.Combine(root, "absent.dwg");
            DocumentFilePolicy.Check(absent, false, false);
            Check(!File.Exists(absent), "preflight created a file");
            Domain("FILE_NOT_FOUND", () => DocumentFilePolicy.Check(absent, true, false));
            Domain("DIRECTORY_NOT_FOUND", () => DocumentFilePolicy.Check(Path.Combine(root, "missing", "target.dwg"), false, false));
            Check(File.ReadAllText(existing) == "existing file must survive preflight", "rejected write changed content");
            return Task.CompletedTask;
        });
        await test("document paths reject devices, traversal, ADS, UNC and invalid formats", () =>
        {
            var accepted = DocumentValidation.Parse(Open().arguments);
            Check(accepted.Path == @"C:\Projects\Test Ω 1.dxf" && !accepted.Overwrite && !accepted.ReadOnly, "Unicode path/defaults");
            foreach (string path in new[] { @"C:relative.dwg", @"\\server\share\a.dwg", @"\\?\C:\a.dwg", @"C:\a.dwg:stream", @"C:\dir\..\a.dwg", @"C:\CON.dwg", @"C:\a.\b.dwg", @"C:\COM1\a.dwg" })
                Domain("INVALID_PATH", () => DocumentValidation.Parse(Open(path: path).arguments));
            Domain("UNSUPPORTED_FORMAT", () => DocumentValidation.Parse(Open(path: @"C:\a.scr").arguments));
            Check(DocumentValidation.Parse(Open(path: "c:/Projects/Test Ω 1.dxf").arguments).Fingerprint() == accepted.Fingerprint(), "equivalent local paths produce distinct fingerprint");
            return Task.CompletedTask;
        });
        await test("document actions cannot smuggle save/close/commands or omit target revisions", () =>
        {
            foreach (string action in new[] { "activate", "save", "export_dxf" })
            {
                var input = new Dictionary<string, object?> { ["bridge_instance_id"] = Bridge, ["operation_id"] = Guid.NewGuid().ToString("D"),
                    ["action"] = action, ["document_id"] = Doc, ["expected_revision"] = "18446744073709551615" };
                if (action == "export_dxf") input["path"] = @"C:\copy.dxf";
                Check(DocumentValidation.Parse(Wire.Element(input)).Action == action, "valid action rejected");
                input.Remove("expected_revision");
                Domain("INVALID_ARGUMENT", () => DocumentValidation.Parse(Wire.Element(input)));
            }
            using var duplicate = JsonDocument.Parse(Open().arguments.GetRawText().TrimEnd('}') + ",\"action\":\"save\"}");
            Domain("INVALID_ARGUMENT", () => DocumentValidation.Parse(duplicate.RootElement));
            foreach (string field in new[] { "command", "overwrite", "document_id" })
            {
                var fields = JsonSerializer.Deserialize<Dictionary<string, object?>>(Open().arguments.GetRawText())!; fields[field] = "unrequested";
                Domain("INVALID_ARGUMENT", () => DocumentValidation.Parse(Wire.Element(fields)));
            }
            Domain("INVALID_ARGUMENT", () => DocumentValidation.Parse(Wire.Element(new { bridge_instance_id = Bridge, operation_id = Guid.NewGuid(), action = "close" })));
            return Task.CompletedTask;
        });
        await test("document mutations share the geometry ledger and replay completed results without reexecution", () =>
        {
            using var queue = new WriteQueue(Environment.CurrentManagedThreadId);
            var request = Open(); var parsed = DocumentValidation.Parse(request.arguments);
            var (job, replay) = queue.Enqueue(request);
            Check(!replay && ReferenceEquals(queue.Take(), job) && job.TryStart(), "document queue dispatch");
            job.BeginCommit();
            var result = Result.Ok(request.requestId, new DocumentOperationResult(job.OperationId, "completed", false, "open", Info(Doc), null, null, false));
            job.RecordCommit(result); job.Complete(result);
            var (same, repeated) = queue.Enqueue(request with { requestId = Guid.NewGuid().ToString("D") });
            Check(repeated && ReferenceEquals(same, job) && queue.Take() == null, "open repeated by replay");
            var historical = WriteJob.ForCaller(same.Completion.Result, "another-request", true);
            Check(historical.data is DocumentOperationResult { replayed: true, action: "open" }, "replay annotation");
            Domain("OPERATION_ID_CONFLICT", () => queue.Enqueue(Open(parsed.OperationId, @"C:\different.dwg")));
            var create = new Request(Wire.Version, Guid.NewGuid().ToString("D"), "create_entities", 10000, Wire.Element(new
            { bridge_instance_id = Bridge, document_id = Doc, expected_revision = "1", operation_id = parsed.OperationId, units = "Millimeters", entities = new[] { new { type = "Line", start = new[] { 0, 0 }, end = new[] { 1, 0 } } } }));
            Domain("OPERATION_ID_CONFLICT", () => queue.Enqueue(create));
            return Task.CompletedTask;
        });
        await test("cancelled application-context document callbacks cannot start late", () =>
        {
            using var queue = new WriteQueue(Environment.CurrentManagedThreadId);
            using var cancel = new CancellationTokenSource();
            var (job, _) = queue.Enqueue(Open(), cancel.Token);
            Check(ReferenceEquals(queue.Take(), job), "not scheduled");
            cancel.Cancel();
            Check(!job.TryStart() && job.Completion.IsCompleted && job.Completion.Result.error!.code == "CANCELLED", "late callback can still open a file");
            return Task.CompletedTask;
        });
        await test("unknown native document outcomes stay in ledger and are not repeated", () =>
        {
            using var queue = new WriteQueue(Environment.CurrentManagedThreadId);
            var request = Open(); var (job, _) = queue.Enqueue(request);
            queue.Take(); Check(job.TryStart(), "not running"); job.BeginCommit();
            job.Complete(job.Error("DOCUMENT_OUTCOME_UNKNOWN", "Native call failed after boundary", "unknown"));
            var (same, replay) = queue.Enqueue(request);
            Check(replay && ReferenceEquals(same, job) && queue.Take() == null && !same.Completion.Result.success, "unknown outcome was rescheduled");
            return Task.CompletedTask;
        });
        await test("document invalid arguments are rejected by MCP before IPC", async () =>
        {
            int calls = 0;
            using var listener = new BridgeListener(Discovery.Describe(Bridge, "document-test-no-CAD"), (request, _) =>
            { calls++; return Task.FromResult(Result.Ok(request.requestId, new { })); }, new JsonLog("document-test"));
            var input = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Open().arguments.GetRawText())!;
            input["path"] = Wire.Element(@"C:\a.dwg:ads");
            var response = await new ToolService().CallAsync("manage_document", input, default);
            Check(response.IsError == true && calls == 0, "invalid path crossed IPC boundary");
        });
    }
}
