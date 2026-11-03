using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using NanoCAD.McpServer;
using Result = NanoCAD.Contracts.Result;

Environment.SetEnvironmentVariable("NANOCAD_MCP_RESPONSE_MODE","structured");
var filters = new List<string>();
for (int argument = 0; argument < args.Length; argument += 2)
{
    if (args[argument] != "--filter" || argument + 1 >= args.Length || string.IsNullOrWhiteSpace(args[argument + 1]))
    { Console.Error.WriteLine("Usage: NanoCAD.Tests [--filter <test-name substring>] ..."); return 2; }
    filters.Add(args[argument + 1]);
}
var testRoot = Path.GetFullPath(Path.Combine("artifacts", "tests", Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(testRoot);
Environment.SetEnvironmentVariable("NANOCAD_MCP_HOME", testRoot);
var outcomes = new List<object>();
int failures = 0, skipped = 0;
async Task Test(string name, Func<Task> test)
{
    if (filters.Count > 0 && !filters.Any(filter => name.Contains(filter, StringComparison.OrdinalIgnoreCase))) { skipped++; return; }
    try { await test(); Console.WriteLine("PASS " + name); outcomes.Add(new { name, passed = true }); }
    catch (Exception e) { failures++; Console.WriteLine("FAIL " + name + ": " + e); outcomes.Add(new { name, passed = false, error = e.ToString() }); }
}
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
void Domain(string code, Action action)
{
    try { action(); throw new Exception("Expected " + code); }
    catch (DomainException e) { Check(e.Error.code == code, $"Expected {code}, got {e.Error.code}"); }
}
async Task Throws<T>(Func<Task> action) where T : Exception
{
    try { await action(); throw new Exception("Expected " + typeof(T).Name); } catch (T) { }
}
await Test("published input schemas expose geometry without references", () =>
{
    foreach (var tool in ToolCatalog.Tools)
        Check(!tool.InputSchema.GetRawText().Contains("\"$ref\""), tool.Name + " contains unresolved client references");
    var create = ToolCatalog.Tools.Single(t => t.Name == "nanocad.create_entities").InputSchema;
    var branches = create.GetProperty("properties").GetProperty("entities").GetProperty("items").GetProperty("oneOf");
    var line = branches.EnumerateArray().Single(b => b.GetProperty("properties").GetProperty("type").GetProperty("const").GetString() == "Line");
    Check(line.GetProperty("properties").GetProperty("start").GetProperty("items").GetProperty("type").GetString() == "number", "Line coordinates hidden");
    return Task.CompletedTask;
});
Request RequestOf(string op = "get_document", object? arguments = null, int timeout = 10000) =>
    new(Wire.Version, Guid.NewGuid().ToString("D"), op, timeout, Wire.Element(arguments ?? new { }));
var documentId = Guid.NewGuid().ToString("D");
var instanceId = Guid.NewGuid().ToString("D");
EntitySummary Summary(string h) => new(new(instanceId, documentId, h), "Line", "test.Line", new("0", "10"), "model", "supported");
await BasicEditingBlockTests.Run(Test);
await SettingsEditingBlockTests.Run(Test);
await BasicEditing334Tests.Run(Test);
await CountCapacity357Tests.Run(Test);
await PublishedDiscriminator412Tests.Run(Test);
await ModelSketchSelection412Tests.Run(Test);
await NativeSmooth445Tests.Run(Test);
await SplineConstraint445Tests.Run(Test);
await NativeSketchCurves445Tests.Run(Test);
await ProfileConsumers446Tests.Run(Test);
await CurvedProfile445Tests.Run(Test);
await OperationInterface432Tests.Run(Test);
await ReadPreparation433Tests.Run(Test);
await EraseAssessment444Tests.Run(Test);
await ModelProfileElementGeometry358Tests.Run(Test);
await BasicEditing335Tests.Run(Test);
await BasicEditing336Tests.Run(Test);
await BasicEditing337Tests.Run(Test);
await Test("Hatch Origin preserves unrelated typed metadata", () =>
{
    NanoCADBridge.CadHatchOriginXData348Checks.Run();
    return Task.CompletedTask;
});

await Test("MText decoding preserves whitespace and fails explicitly on unknown content", () =>
{
    Check(MTextContent.PlainText(@"First line\P{\C1;Red text}") == "First line\nRed text", "fixture text/paragraph");
    Check(MTextContent.PlainText(@"  {\H2x;A\LB\l}\P\P C  ") == "  AB\n\n C  ", "spaces and empty paragraphs");
    Check(MTextContent.PlainText(@"\{a\} \\P \~ \U+0416") == ("{a} \\P \u00a0 " + (char)0x0416), "escaped formatting/Unicode/NBSP"); // Numeric code points retain technical Unicode glyph coverage.
    Check(MTextContent.PlainText(@"\fArial|b0|i0;A^IB^JC^M 1^ 2") == "A\tB\nC 1^2", "font/caret encoding");
    foreach (string text in new[] { @"\S1/2;", "%<field>%", @"\Zunknown;", @"\C+1;lost", @"\H1..5;lost", @"\C1", "{unclosed", "extra}", @"tail\", "%%c", @"\U+ZZZZ" })
        Check(MTextContent.PlainText(text) == null, "unknown/malformed contents must be unavailable: " + text);
    Check(MTextContent.PlainText("") == "", "empty text differs from unavailable");
    Check(MTextContent.PlainText(new string('x', 16385)) == null, "text bound");
    using var fixture = JsonDocument.Parse(File.ReadAllText("tests/fixtures/phase1-readonly.expected.json"));
    var expected = fixture.RootElement.GetProperty("objects").EnumerateArray()
        .Single(item => item.GetProperty("name").GetString() == "mtext_formatting").GetProperty("expected");
    string? decoded = MTextContent.PlainText(expected.GetProperty("contents").GetString()!);
    Check(decoded == expected.GetProperty("plain_text").GetString(), "independent ezdxf fixture oracle");
    File.WriteAllText(Path.Combine(testRoot, "mtext-decoding.json"), JsonSerializer.Serialize(new {
        scope = "Pure C# decoder; no CAD runtime", passed = true, contents = expected.GetProperty("contents").GetString(), plain_text = decoded }));
    return Task.CompletedTask;
});

await Test("compiled bridge excludes unsupported getters and the dirtying explicit lock", () =>
{
    // Inspect compiled references without loading any CAD assembly into the test process.
    using var stream = File.OpenRead(Path.GetFullPath("src/NanoCADBridge/bin/Release/net6.0-windows/NanoCADBridge.dll"));
    using var pe = new PEReader(stream);
    var metadata = pe.GetMetadataReader();
    foreach (var handle in metadata.MemberReferences)
    {
        var member = metadata.GetMemberReference(handle);
        string name = metadata.GetString(member.Name);
        Check(name != "get_IsQuiescent", "Known NOT_IMPLEMENTED native getter was reintroduced in the bridge");
        // Phase 2 uses the native command's own lock. Explicit Read/Write locks
        // and manual undo markers are not part of the selected mechanism.
        Check(name != "LockDocument", "LockDocument(Read) dirties a clean drawing on the installed host");
        Check(name != "StartUndoRecord", "Database.StartUndoRecord does not provide the required native command boundary");
        if (member.Parent.Kind == HandleKind.TypeReference)
        {
            string owner = metadata.GetString(metadata.GetTypeReference((TypeReferenceHandle)member.Parent).Name);
            Check(!(owner == "Layout" && name == "Initialize"), "Layout.Initialize is an exception-only stub in installed hostdbmgd");
            Check(!(owner == "MText" && name == "get_Text"), "MText.Text loses whitespace on the installed host");
            if (owner == "DocumentCollection")
            {
                Check(name is not ("ExecuteInApplicationContext" or "get_IsApplicationContext"), "Installed document context API is an API-logger stub");
                if (name == "Open")
                {
                    var signature = metadata.GetBlobReader(member.Signature);
                    var header = signature.ReadSignatureHeader();
                    if (header.IsGeneric) _ = signature.ReadCompressedInteger();
                    Check(signature.ReadCompressedInteger() == 2, "Only Open(fileName, forReadOnly) has an implemented native body");
                }
            }
        }
    }
    return Task.CompletedTask;
});

await Test("uint64 handles, canonicalization, invalid forms", () =>
{
    Check(Validation.NormalizeHandle("0000aB") == "AB", "canonical handle");
    Check(Validation.NormalizeHandle("ffffffffffffffff") == "FFFFFFFFFFFFFFFF", "64-bit handle");
    var bits = ulong.Parse(Validation.NormalizeHandle("8000000000000001"), System.Globalization.NumberStyles.HexNumber);
    Check(unchecked((ulong)(long)bits) == bits, "signed SDK representation loses bits");
    foreach (var h in new[] { "0", "000", "0xA", "-1", " 1", "g", "10000000000000000", "" }) Domain("INVALID_HANDLE", () => Validation.NormalizeHandle(h));
    return Task.CompletedTask;
});
await Test("strict validation and duplicate normalized handles", () =>
{
    Domain("INVALID_ARGUMENT", () => Validation.Parse("inspect_objects", Wire.Element(new { document_id = documentId, handles = new[] { "01", "1" } })));
    foreach (var input in new object[] { new { document_id = documentId, page_size = "2" }, new { document_id = documentId, page_size = 1.2 },
        new { document_id = documentId, page_size = 2147483648L }, new { document_id = documentId, types = Array.Empty<string>() },
        new { document_id = documentId, space = "paper" }, new { document_id = documentId, expected_revision = "18446744073709551616" },
        new { document_id = documentId, run_command = "LINE" } }) Domain("INVALID_ARGUMENT", () => Validation.Parse("query_objects", Wire.Element(input)));
    using var duplicate = JsonDocument.Parse("{\"document_id\":\"" + documentId + "\",\"page_size\":1,\"page_size\":2}");
    Domain("INVALID_ARGUMENT", () => Validation.Parse("query_objects", duplicate.RootElement));
    Check(Validation.Parse("query_objects", Wire.Element(new { document_id = documentId, expected_revision = "0003" })).ExpectedRevision == "3", "revision normalization");
    return Task.CompletedTask;
});
await Test("framing fragmentation, EOF, length and depth bounds", async () =>
{
    var request = RequestOf();
    using var stream = new MemoryStream();
    await Framing.WriteAsync(stream, request, Wire.MaxRequestBytes, default);
    using var fragmented = new FragmentedStream(stream.ToArray());
    Check((await Framing.ReadAsync<Request>(fragmented, Wire.MaxRequestBytes, default)).requestId == request.requestId, "fragmented read");
    foreach (var size in new[] { -1, 0, Wire.MaxRequestBytes + 1 })
    {
        var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, size);
        await Throws<InvalidDataException>(() => Framing.ReadAsync<Request>(new MemoryStream(header), Wire.MaxRequestBytes, default));
    }
    await Throws<EndOfStreamException>(() => Framing.ReadAsync<Request>(new MemoryStream(stream.ToArray()[..^1]), Wire.MaxRequestBytes, default));
    var deep = Encoding.UTF8.GetBytes(new string('[', 40) + "0" + new string(']', 40));
    using var deepStream = new MemoryStream();
    var deepHeader = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(deepHeader, deep.Length);
    deepStream.Write(deepHeader); deepStream.Write(deep); deepStream.Position = 0;
    await Throws<JsonException>(() => Framing.ReadAsync<JsonElement>(deepStream, Wire.MaxRequestBytes, default));
});
await Test("pagination ordering, revisions, filters, tamper and expiry", () =>
{
    var now = DateTime.UtcNow;
    var snapshots = new Snapshots(() => now);
    var first = snapshots.First(documentId, "1", null, "filter", [Summary("100"), Summary("F"), Summary("20")], 1);
    Check(first.items.Single().@ref.handle == "F" && !first.complete && first.total_count == 3, "numeric ordering/count");
    var second = snapshots.Next(first.next_cursor!, documentId, "1", null, "filter", 2);
    Check(second.complete && second.items[0].@ref.handle == "20" && second.returned_count == 2, "continuation");
    Domain("STALE_DOCUMENT", () => snapshots.Next(first.next_cursor!, documentId, "2", null, "filter", 1));
    Domain("INVALID_CURSOR", () => snapshots.Next(first.next_cursor!, documentId, "1", null, "changed", 1));
    var altered = (first.next_cursor![0] == 'A' ? "B" : "A") + first.next_cursor[1..];
    Domain("INVALID_CURSOR", () => snapshots.Next(altered, documentId, "1", null, "filter", 1));
    now = now.AddSeconds(61);
    Domain("CURSOR_EXPIRED", () => snapshots.Next(first.next_cursor!, documentId, "1", null, "filter", 1));
    var selected = snapshots.First(documentId, "1", "1", "selection", [Summary("1"), Summary("2")], 1);
    Domain("STALE_SELECTION", () => snapshots.Next(selected.next_cursor!, documentId, "1", "2", "selection", 1));
    snapshots.First(documentId, "1", null, "a", [], 1);
    snapshots.First(documentId, "1", null, "b", [], 1);
    Domain("CURSOR_EXPIRED", () => snapshots.Next(selected.next_cursor!, documentId, "1", "1", "selection", 1));
    return Task.CompletedTask;
});
await Test("UI queue capacity, cancellation and no late execution", async () =>
{
    // Dedicated thread models a host UI; no CAD assembly is loaded by these tests.
    var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    new Thread(() =>
    {
        try
        {
            using var queue = new ReadQueue(Environment.CurrentManagedThreadId, 1);
            using var cancel = new CancellationTokenSource();
            var first = queue.SubmitAsync(RequestOf(), cancel.Token);
            Check(queue.SubmitAsync(RequestOf(), default).GetAwaiter().GetResult().error!.code == "BUSY", "queue bound");
            cancel.Cancel();
            try { first.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
            int calls = 0;
            queue.Pump((r, _) => { calls++; return Result.Ok(r.requestId, new { }); });
            Check(calls == 0, "cancelled queued job executed");
            var live = queue.SubmitAsync(RequestOf(), default);
            queue.Pump((r, _) => { calls++; return Result.Ok(r.requestId, new { }); });
            Check(live.GetAwaiter().GetResult().success && calls == 1, "live job failed");
            var expiry = queue.SubmitAsync(RequestOf(timeout: 20), default);
            Check(expiry.GetAwaiter().GetResult().error!.code == "UI_DISPATCH_UNAVAILABLE", "queue timeout");
            queue.Pump((r, _) => { calls++; return Result.Ok(r.requestId, new { }); });
            Check(calls == 1, "expired job executed");
            queue.Dispose();
            Check(queue.SubmitAsync(RequestOf(), default).GetAwaiter().GetResult().error!.code == "BRIDGE_UNAVAILABLE", "post-shutdown enqueue");
            done.SetResult();
        }
        catch (Exception e) { done.SetException(e); }
    }) { IsBackground = true }.Start();
    await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
});
await Test("no-bridge diagnostics and read-only tool annotations", async () =>
{
    var service = new ToolService();
    var status = await service.CallAsync("get_status", null, default);
    Check(status.IsError == false && !status.StructuredContent!.Value.GetProperty("data").GetProperty("available").GetBoolean(), "no-bridge status");
    var document = await service.CallAsync("get_document", null, default);
    Check(document.IsError == true && document.StructuredContent!.Value.GetProperty("error").GetProperty("code").GetString() == "BRIDGE_UNAVAILABLE", "no-bridge document error");
    Check(ToolCatalog.Tools.Count == 22 && ToolCatalog.Tools.Count(t => t.Annotations!.ReadOnlyHint == true) == 16, "read tool count");
    Check(ToolCatalog.Tools.Select(t => t.Name).Order().SequenceEqual(ToolCatalog.OperationNames.Append("get_guidance").Select(op => "nanocad." + op).Order()), "catalog matches approved wire allowlist");
    Check(ToolCatalog.Tools.Where(t => t.Annotations!.ReadOnlyHint == true).Select(t => t.Name).Order().SequenceEqual(Wire.ReadOperations.Append("capture_view").Append("get_guidance").Select(op => "nanocad." + op).Order()), "read annotations match native and server-local read allowlist");
    Check(ToolCatalog.Tools.Single(t => t.Name == "nanocad.create_entities").Annotations!.ReadOnlyHint == false, "creation must be annotated as a write");
    var modifyTool = ToolCatalog.Tools.Single(t => t.Name == "nanocad.modify_entities");
    Check(modifyTool.Annotations!.ReadOnlyHint == false && modifyTool.Annotations.DestructiveHint == true && modifyTool.Annotations.IdempotentHint == true, "modification annotations");
    bool Closed(JsonElement schema) => schema.TryGetProperty("additionalProperties", out var additional) && additional.ValueKind == JsonValueKind.False
        || schema.TryGetProperty("oneOf", out var variants) && variants.EnumerateArray().All(Closed);
    Check(ToolCatalog.Tools.All(t => Closed(t.InputSchema)), "strict schemas including each document action");
});

var descriptor = Discovery.Describe(instanceId, "test-transport-only");
var cancelObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var slowStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
using (var listener = new BridgeListener(descriptor, async (request, token) =>
{
    if (request.operation == "modify_structure")
    {
        var args = StructureWriteValidation.Parse(request.arguments);
        return Result.Ok(request.requestId, args.Operations[0] is DefineBlockEdit or InsertBlockEdit ? BlockWriteTests.ProtocolData(args) :
            args.Operations[0] is CreateTextStyleEdit ? StyleWriteTests.ProtocolData(args) :
            args.Operations[0] is CreateDimensionStyleEdit ? DimensionStyleWriteTests.ProtocolData(args) :
            args.Operations[0] is CreateLinetypeEdit ? LinetypeWriteTests.ProtocolData(args) :
            args.Operations[0] is CreateViewportEdit ? ViewportCreateTests.ProtocolData(args) : args.Operations[0] is CreateLayoutEdit ? LayoutCreateTests.ProtocolData(args) : args.Operations[0] is SetViewportViewEdit ? ViewportWriteTests.ProtocolData(args) : StructureWriteTests.ProtocolData(args), new(instanceId,documentId,"4",DateTime.UtcNow.ToString("O")));
    }
    if (request.operation is "get_structure" or "inspect_block")
        return Result.Ok(request.requestId, request.operation == "get_structure" && request.arguments.GetProperty("section").GetString() == "text_styles"
            ? StyleReadTests.ProtocolData(instanceId, documentId)
            : request.operation == "get_structure" && request.arguments.GetProperty("section").GetString() == "dimension_styles"
                ? DimensionStyleReadTests.ProtocolData(instanceId, documentId)
                : request.operation == "get_structure" && request.arguments.GetProperty("section").GetString() == "linetypes"
                    ? LinetypeReadTests.ProtocolData(instanceId, documentId)
                : request.operation == "get_structure" && request.arguments.GetProperty("section").GetString() is "layouts" or "viewports"
                    ? LayoutReadTests.ProtocolData(request.arguments.GetProperty("section").GetString()!, instanceId, documentId)
                : Phase4Tests.ProtocolData(request.operation, instanceId, documentId), new(instanceId, documentId, "3", DateTime.UtcNow.ToString("O")));
    if (request.operation == "modify_entities")
    {
        var modify = ModifyValidation.Parse(request.arguments);
        var source = new EntityRef(instanceId, modify.DocumentId, "CA"); var copy = new EntityRef(instanceId, modify.DocumentId, "CB");
        return Result.Ok(request.requestId, new ModifiedEntities(modify.OperationId, "committed", false,
            [source], [], [copy], [new CopyMapping(1, source, copy)], "single_command"),
            new(instanceId, modify.DocumentId, "3", DateTime.UtcNow.ToString("O")));
    }
    if (request.operation == "create_entities")
    {
        // Protocol fixture only: no native CAD objects are created here.
        var create = CreateValidation.Parse(request.arguments);
        return Result.Ok(request.requestId, new CreatedEntities(create.OperationId, "committed", false,
            [new EntityRef(instanceId, create.DocumentId, "CA")], "single_command"),
            new(instanceId, create.DocumentId, "2", DateTime.UtcNow.ToString("O")));
    }
    if (request.operation == "get_documents")
        return Result.Ok(request.requestId, new DocumentList(instanceId, documentId, new[] { DocumentTests.Info(documentId) }));
    if (request.operation == "manage_document")
    {
        var action = DocumentValidation.Parse(request.arguments);
        bool written = action.Action is "save" or "export_dxf";
        return Result.Ok(request.requestId, new DocumentOperationResult(action.OperationId, "completed", false, action.Action,
            DocumentTests.Info(documentId), written ? action.Path : null, written ? 1024L : null, false), new(instanceId, documentId, "2", DateTime.UtcNow.ToString("O")));
    }
    if (request.operation == "get_document")
    {
        slowStarted.TrySetResult();
        try { await Task.Delay(30000, token); }
        catch (OperationCanceledException) { cancelObserved.TrySetResult(); throw; }
    }
    return Result.Ok(request.requestId, new { bridge_instance_id = instanceId, test_only = true });
}, new JsonLog("test-bridge")))
{
    await Test("Windows secured pipe roundtrip, peer identity and discovery", async () =>
    {
        Check(Discovery.List().Any(d => d.bridge_instance_id == instanceId), "discovery missing");
        var response = await new BridgeClient().CallAsync(descriptor, RequestOf("get_status"), default);
        Check(response.success, "pipe roundtrip");
        await Throws<DomainException>(() => new BridgeClient().CallAsync(descriptor with { process_start_utc_ticks = 1 }, RequestOf("get_status"), default));
    });
    await Test("IPC cancellation propagates after disconnect", async () =>
    {
        using var cancel = new CancellationTokenSource();
        var pending = new BridgeClient().CallAsync(descriptor, RequestOf(), cancel.Token);
        await slowStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancel.Cancel();
        await Throws<OperationCanceledException>(() => pending);
        await cancelObserved.Task.WaitAsync(TimeSpan.FromSeconds(3));
    });
    await Test("IPC rejects arbitrary commands outside semantic allowlist", async () =>
    {
        var response = await new BridgeClient().CallAsync(descriptor, RequestOf("run_command"), default);
        Check(!response.success && response.error!.code == "INVALID_ARGUMENT", "write/command accepted");
    });
    foreach (var protocolVersion in new[] { "2025-11-25", "2026-07-28" })
    await Test($"MCP {protocolVersion} discovery and tool calls through stdio/pipe", async () =>
    {
        string dotnet = Path.GetFullPath(".tools/dotnet/dotnet.exe");
        string server = Path.GetFullPath("src/NanoCAD.McpServer/bin/Release/net10.0-windows/NanoCAD.McpServer.dll");
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            // SDK 2.2.0 wraps Windows commands in cmd.exe. A bare executable in its own cwd
            // avoids cmd.exe stripping quotes around an executable path containing spaces.
            Name = "nanocad-test", Command = "dotnet.exe", WorkingDirectory = Path.GetDirectoryName(dotnet), Arguments = [server],
            EnvironmentVariables = new Dictionary<string, string?> { ["NANOCAD_MCP_HOME"] = testRoot }
        });
        using var connectDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var client = await McpClient.CreateAsync(transport, new McpClientOptions { ProtocolVersion = protocolVersion }, cancellationToken: connectDeadline.Token);
        Check(client.NegotiatedProtocolVersion == protocolVersion, "protocol negotiation");
        var tools = await client.ListToolsAsync();
        Check(tools.Count == 22, "stdio tool count");
        var response = await client.CallToolAsync("nanocad.get_status", new Dictionary<string, object?>());
        Check(response.IsError == false && response.StructuredContent!.Value.GetProperty("data").GetProperty("available").GetBoolean(), "full MCP to IPC result");
        var invalid = await client.CallToolAsync("nanocad.inspect_objects", new Dictionary<string, object?> { ["document_id"] = documentId, ["handles"] = new[] { "0" } });
        Check(invalid.IsError == true, "invalid handle must fail as tool error");
        File.WriteAllText(Path.Combine(testRoot, "tools-list.json"), JsonSerializer.Serialize(ToolCatalog.Tools, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(testRoot, $"mcp-status-{protocolVersion}.json"), response.StructuredContent!.Value.GetRawText());
        var createInput = new Dictionary<string, object?>
        {
            ["bridge_instance_id"] = instanceId, ["document_id"] = documentId, ["expected_revision"] = "1",
            ["operation_id"] = Guid.NewGuid().ToString("D"), ["units"] = "Millimeters",
            ["entities"] = new[] { new { type = "Line", start = new[] { 0, 0 }, end = new[] { 20, 0 } } }
        };
        var created = await client.CallToolAsync("nanocad.create_entities", createInput);
        Check(created.IsError == false && created.StructuredContent!.Value.GetProperty("data").GetProperty("created")[0].GetProperty("handle").GetString() == "CA", "create through MCP stdio/pipe");
        File.WriteAllText(Path.Combine(testRoot, $"mcp-create-input-{protocolVersion}.json"), JsonSerializer.Serialize(createInput));
        File.WriteAllText(Path.Combine(testRoot, $"mcp-create-result-{protocolVersion}.json"), created.StructuredContent!.Value.GetRawText());
        createInput.Remove("operation_id");
        var invalidCreate = await client.CallToolAsync("nanocad.create_entities", createInput);
        Check(invalidCreate.IsError == true && invalidCreate.StructuredContent!.Value.GetProperty("error").GetProperty("code").GetString() == "INVALID_ARGUMENT", "create without operation_id must fail");
        File.WriteAllText(Path.Combine(testRoot, $"mcp-create-error-{protocolVersion}.json"), invalidCreate.StructuredContent!.Value.GetRawText());
        var modifyInput = new Dictionary<string, object?>
        {
            ["bridge_instance_id"] = instanceId, ["document_id"] = documentId, ["expected_revision"] = "2",
            ["operation_id"] = Guid.NewGuid().ToString("D"), ["units"] = "Millimeters",
            ["operations"] = new object[] {
                new { operation = "move", handles = new[] { "CA" }, displacement = new[] { 10, 0 } },
                new { operation = "copy", handles = new[] { "CA" }, displacement = new[] { 0, 20 } } }
        };
        var modified = await client.CallToolAsync("nanocad.modify_entities", modifyInput);
        Check(modified.IsError == false && modified.StructuredContent!.Value.GetProperty("data").GetProperty("copies")[0].GetProperty("created").GetProperty("handle").GetString() == "CB", "modify through full MCP/IPC");
        File.WriteAllText(Path.Combine(testRoot, $"mcp-modify-input-{protocolVersion}.json"), JsonSerializer.Serialize(modifyInput));
        File.WriteAllText(Path.Combine(testRoot, $"mcp-modify-result-{protocolVersion}.json"), modified.StructuredContent!.Value.GetRawText());
        modifyInput.Remove("operation_id");
        var invalidModify = await client.CallToolAsync("nanocad.modify_entities", modifyInput);
        Check(invalidModify.IsError == true, "modify without operation_id accepted");
        File.WriteAllText(Path.Combine(testRoot, $"mcp-modify-error-{protocolVersion}.json"), invalidModify.StructuredContent!.Value.GetRawText());
        var structureWriteInput = StructureWriteTests.ProtocolInput(instanceId,documentId);
        var structureWrite = await client.CallToolAsync("nanocad.modify_structure", structureWriteInput);
        Check(structureWrite.IsError == false && structureWrite.StructuredContent!.Value.GetProperty("data").GetProperty("changes").GetArrayLength() == 3, "typed structural write MCP roundtrip");
        File.WriteAllText(Path.Combine(testRoot,$"mcp-modify_structure-input-{protocolVersion}.json"),JsonSerializer.Serialize(structureWriteInput));
        File.WriteAllText(Path.Combine(testRoot,$"mcp-modify_structure-result-{protocolVersion}.json"),structureWrite.StructuredContent!.Value.GetRawText());
        structureWriteInput["save_after"]=true;
        var rejectedStructureWrite=await client.CallToolAsync("nanocad.modify_structure",structureWriteInput);
        Check(rejectedStructureWrite.IsError==true,"structural hidden save accepted");
        File.WriteAllText(Path.Combine(testRoot,$"mcp-modify_structure-error-{protocolVersion}.json"),rejectedStructureWrite.StructuredContent!.Value.GetRawText());
        foreach(bool insert in new[] {false,true})
        {
            string blockOp=insert?"insert_block":"define_block";
            var blockInput=BlockWriteTests.ProtocolInput(instanceId,documentId,insert);
            var blockResult=await client.CallToolAsync("nanocad.modify_structure",blockInput);
            var change=blockResult.StructuredContent!.Value.GetProperty("data").GetProperty("changes")[0];
            Check(blockResult.IsError==false && change.GetProperty("children").GetArrayLength()==(insert?2:7) &&
                change.GetProperty("children")[insert?0:5].GetProperty("tag").GetString()=="NAME", "block children MCP/IPC roundtrip");
            File.WriteAllText(Path.Combine(testRoot,$"mcp-{blockOp}-input-{protocolVersion}.json"),JsonSerializer.Serialize(blockInput));
            File.WriteAllText(Path.Combine(testRoot,$"mcp-{blockOp}-result-{protocolVersion}.json"),blockResult.StructuredContent!.Value.GetRawText());
            blockInput["load_file"]=@"C:\unrequested.dwg";
            var rejectedBlock=await client.CallToolAsync("nanocad.modify_structure",blockInput);
            Check(rejectedBlock.IsError==true,"hidden block file access accepted");
            File.WriteAllText(Path.Combine(testRoot,$"mcp-{blockOp}-error-{protocolVersion}.json"),rejectedBlock.StructuredContent!.Value.GetRawText());
        }
        {
        var layoutInput = LayoutCreateTests.ProtocolInput(instanceId, documentId);
        var layoutResult = await client.CallToolAsync("nanocad.modify_structure", layoutInput);
        var layoutChange = layoutResult.StructuredContent!.Value.GetProperty("data").GetProperty("changes")[0];
        Check(layoutResult.IsError == false && layoutChange.GetProperty("kind").GetString() == "layout" && layoutChange.GetProperty("created").GetBoolean(), "layout creation MCP/IPC roundtrip");
        File.WriteAllText(Path.Combine(testRoot, $"mcp-create_layout-input-{protocolVersion}.json"), JsonSerializer.Serialize(layoutInput));
        File.WriteAllText(Path.Combine(testRoot, $"mcp-create_layout-result-{protocolVersion}.json"), layoutResult.StructuredContent!.Value.GetRawText());
        layoutInput["activate_layout"] = true;
        var invalidLayout = await client.CallToolAsync("nanocad.modify_structure", layoutInput);
        Check(invalidLayout.IsError == true, "hidden layout context change accepted");
        File.WriteAllText(Path.Combine(testRoot, $"mcp-create_layout-error-{protocolVersion}.json"), invalidLayout.StructuredContent!.Value.GetRawText());
        }
        {
        var input = ViewportCreateTests.ProtocolInput(instanceId, documentId);
        var result = await client.CallToolAsync("nanocad.modify_structure", input);
        var change = result.StructuredContent!.Value.GetProperty("data").GetProperty("changes")[0];
        Check(result.IsError == false && change.GetProperty("kind").GetString() == "viewport" && change.GetProperty("created").GetBoolean(), "viewport creation MCP/IPC roundtrip");
        File.WriteAllText(Path.Combine(testRoot, $"mcp-create_viewport-input-{protocolVersion}.json"), JsonSerializer.Serialize(input));
        File.WriteAllText(Path.Combine(testRoot, $"mcp-create_viewport-result-{protocolVersion}.json"), result.StructuredContent!.Value.GetRawText());
        input["activate_layout"] = true;
        var rejected = await client.CallToolAsync("nanocad.modify_structure", input);
        Check(rejected.IsError == true, "hidden viewport activation accepted");
        File.WriteAllText(Path.Combine(testRoot, $"mcp-create_viewport-error-{protocolVersion}.json"), rejected.StructuredContent!.Value.GetRawText());
        }
        var viewportInput = ViewportWriteTests.ProtocolInput(instanceId, documentId);
        var viewportResult = await client.CallToolAsync("nanocad.modify_structure", viewportInput);
        var viewportChange = viewportResult.StructuredContent!.Value.GetProperty("data").GetProperty("changes")[0];
        Check(viewportResult.IsError == false && viewportChange.GetProperty("kind").GetString() == "viewport" && !viewportChange.GetProperty("created").GetBoolean(), "viewport edit MCP/IPC roundtrip");
        File.WriteAllText(Path.Combine(testRoot, $"mcp-set_viewport_view-input-{protocolVersion}.json"), JsonSerializer.Serialize(viewportInput));
        File.WriteAllText(Path.Combine(testRoot, $"mcp-set_viewport_view-result-{protocolVersion}.json"), viewportResult.StructuredContent!.Value.GetRawText());
        viewportInput["activate_layout"] = true;
        var invalidViewport = await client.CallToolAsync("nanocad.modify_structure", viewportInput);
        Check(invalidViewport.IsError == true, "hidden viewport context change accepted");
        File.WriteAllText(Path.Combine(testRoot, $"mcp-set_viewport_view-error-{protocolVersion}.json"), invalidViewport.StructuredContent!.Value.GetRawText());
        var linetypeWriteInput = LinetypeWriteTests.ProtocolInput(instanceId, documentId);
        var linetypeWriteResult = await client.CallToolAsync("nanocad.modify_structure", linetypeWriteInput);
        Check(linetypeWriteResult.IsError == false && linetypeWriteResult.StructuredContent!.Value.GetProperty("data").GetProperty("changes")[0].GetProperty("kind").GetString() == "linetype", "linetype MCP roundtrip");
        File.WriteAllText(Path.Combine(testRoot, $"mcp-create_linetype-input-{protocolVersion}.json"), JsonSerializer.Serialize(linetypeWriteInput));
        File.WriteAllText(Path.Combine(testRoot, $"mcp-create_linetype-result-{protocolVersion}.json"), linetypeWriteResult.StructuredContent!.Value.GetRawText());
        linetypeWriteInput["load_file"] = "unrequested.lin";
        var rejectedLinetype = await client.CallToolAsync("nanocad.modify_structure", linetypeWriteInput);
        Check(rejectedLinetype.IsError == true, "hidden linetype file loading accepted");
        File.WriteAllText(Path.Combine(testRoot, $"mcp-create_linetype-error-{protocolVersion}.json"), rejectedLinetype.StructuredContent!.Value.GetRawText());
        var dimensionWriteInput = DimensionStyleWriteTests.ProtocolInput(instanceId, documentId);
        var dimensionWriteResult = await client.CallToolAsync("nanocad.modify_structure", dimensionWriteInput);
        Check(dimensionWriteResult.IsError == false && dimensionWriteResult.StructuredContent!.Value.GetProperty("data").GetProperty("changes")[0].GetProperty("kind").GetString() == "dimension_style", "dimension creation MCP/IPC roundtrip");
        File.WriteAllText(Path.Combine(testRoot, $"mcp-create_dimension_style-input-{protocolVersion}.json"), JsonSerializer.Serialize(dimensionWriteInput));
        File.WriteAllText(Path.Combine(testRoot, $"mcp-create_dimension_style-result-{protocolVersion}.json"), dimensionWriteResult.StructuredContent!.Value.GetRawText());
        dimensionWriteInput["set_current"] = true;
        var invalidDimensionWrite = await client.CallToolAsync("nanocad.modify_structure", dimensionWriteInput);
        Check(invalidDimensionWrite.IsError == true, "hidden current dimension style edit accepted");
        File.WriteAllText(Path.Combine(testRoot, $"mcp-create_dimension_style-error-{protocolVersion}.json"), invalidDimensionWrite.StructuredContent!.Value.GetRawText());
        var styleInput = StyleWriteTests.ProtocolInput(instanceId, documentId);
        var styleResult = await client.CallToolAsync("nanocad.modify_structure", styleInput);
        Check(styleResult.IsError == false && styleResult.StructuredContent!.Value.GetProperty("data").GetProperty("changes")[0].GetProperty("kind").GetString() == "text_style", "style creation MCP/IPC roundtrip");
        File.WriteAllText(Path.Combine(testRoot, $"mcp-create_text_style-input-{protocolVersion}.json"), JsonSerializer.Serialize(styleInput));
        File.WriteAllText(Path.Combine(testRoot, $"mcp-create_text_style-result-{protocolVersion}.json"), styleResult.StructuredContent!.Value.GetRawText());
        styleInput["font_file"] = "unrequested.ttf";
        var invalidStyle = await client.CallToolAsync("nanocad.modify_structure", styleInput);
        Check(invalidStyle.IsError == true, "style hidden font path accepted");
        File.WriteAllText(Path.Combine(testRoot, $"mcp-create_text_style-error-{protocolVersion}.json"), invalidStyle.StructuredContent!.Value.GetRawText());
        var styleReadInput = new Dictionary<string, object?> { ["bridge_instance_id"] = instanceId, ["document_id"] = documentId, ["section"] = "text_styles" };
        var styleReadResult = await client.CallToolAsync("nanocad.get_structure", styleReadInput);
        var styleProperties = styleReadResult.StructuredContent!.Value.GetProperty("data").GetProperty("items")[0].GetProperty("properties");
        Check(styleReadResult.IsError == false && styleProperties.GetProperty("base_eligibility").GetProperty("eligible").GetBoolean() && styleProperties.GetProperty("annotative_native_value").GetInt32() == 2, "style metadata MCP/IPC roundtrip");
        File.WriteAllText(Path.Combine(testRoot, $"mcp-text_styles-input-{protocolVersion}.json"), JsonSerializer.Serialize(styleReadInput));
        File.WriteAllText(Path.Combine(testRoot, $"mcp-text_styles-result-{protocolVersion}.json"), styleReadResult.StructuredContent!.Value.GetRawText());
        styleReadInput["set_current"] = true;
        var invalidStyleRead = await client.CallToolAsync("nanocad.get_structure", styleReadInput);
        Check(invalidStyleRead.IsError == true, "style read hidden current-style mutation accepted");
        File.WriteAllText(Path.Combine(testRoot, $"mcp-text_styles-error-{protocolVersion}.json"), invalidStyleRead.StructuredContent!.Value.GetRawText());
        var documentList = await client.CallToolAsync("nanocad.get_documents", new Dictionary<string, object?> { ["bridge_instance_id"] = instanceId });
        foreach (string layoutSection in new[] { "layouts", "viewports" })
        {
            var layoutInput = new Dictionary<string, object?> { ["bridge_instance_id"] = instanceId, ["document_id"] = documentId, ["section"] = layoutSection };
            var layoutResult = await client.CallToolAsync("nanocad.get_structure", layoutInput);
            Check(layoutResult.IsError == false, "layout metadata MCP/IPC roundtrip");
            File.WriteAllText(Path.Combine(testRoot, $"mcp-{layoutSection}-input-{protocolVersion}.json"), JsonSerializer.Serialize(layoutInput));
            File.WriteAllText(Path.Combine(testRoot, $"mcp-{layoutSection}-result-{protocolVersion}.json"), layoutResult.StructuredContent!.Value.GetRawText());
            layoutInput["activate"] = true;
            var invalidLayout = await client.CallToolAsync("nanocad.get_structure", layoutInput);
            Check(invalidLayout.IsError == true, "hidden layout activation accepted");
            File.WriteAllText(Path.Combine(testRoot, $"mcp-{layoutSection}-error-{protocolVersion}.json"), invalidLayout.StructuredContent!.Value.GetRawText());
        }
        var linetypeInput = new Dictionary<string, object?> { ["bridge_instance_id"] = instanceId, ["document_id"] = documentId, ["section"] = "linetypes" };
        var linetypeResult = await client.CallToolAsync("nanocad.get_structure", linetypeInput);
        var lineProperties = linetypeResult.StructuredContent!.Value.GetProperty("data").GetProperty("items")[0].GetProperty("properties");
        Check(!linetypeResult.IsError.GetValueOrDefault() && lineProperties.GetProperty("elements")[1].GetProperty("text").GetString() == "GAS", "linetype detailed metadata MCP roundtrip");
        File.WriteAllText(Path.Combine(testRoot, $"mcp-linetypes-input-{protocolVersion}.json"), JsonSerializer.Serialize(linetypeInput));
        File.WriteAllText(Path.Combine(testRoot, $"mcp-linetypes-result-{protocolVersion}.json"), linetypeResult.StructuredContent!.Value.GetRawText());
        linetypeInput["load_file"] = "unrequested.lin";
        var invalidLinetype = await client.CallToolAsync("nanocad.get_structure", linetypeInput);
        Check(invalidLinetype.IsError == true, "linetype read hidden file load accepted");
        File.WriteAllText(Path.Combine(testRoot, $"mcp-linetypes-error-{protocolVersion}.json"), invalidLinetype.StructuredContent!.Value.GetRawText());
        var dimensionInput = new Dictionary<string, object?> { ["bridge_instance_id"] = instanceId, ["document_id"] = documentId, ["section"] = "dimension_styles" };
        var dimensionResult = await client.CallToolAsync("nanocad.get_structure", dimensionInput);
        var dimensionProperties = dimensionResult.StructuredContent!.Value.GetProperty("data").GetProperty("items")[0].GetProperty("properties");
        Check(dimensionResult.IsError == false && dimensionProperties.GetProperty("variables").GetProperty("dimgap").GetDouble() == -0.625 &&
            dimensionProperties.GetProperty("references").GetProperty("text_style").GetProperty("ref").GetProperty("document_id").GetString() == documentId,
            "dimension metadata MCP/IPC roundtrip");
        File.WriteAllText(Path.Combine(testRoot, $"mcp-dimension_styles-input-{protocolVersion}.json"), JsonSerializer.Serialize(dimensionInput));
        File.WriteAllText(Path.Combine(testRoot, $"mcp-dimension_styles-result-{protocolVersion}.json"), dimensionResult.StructuredContent!.Value.GetRawText());
        dimensionInput["set_current"] = true;
        var invalidDimension = await client.CallToolAsync("nanocad.get_structure", dimensionInput);
        Check(invalidDimension.IsError == true, "dimension read must reject implicit current-style mutation");
        File.WriteAllText(Path.Combine(testRoot, $"mcp-dimension_styles-error-{protocolVersion}.json"), invalidDimension.StructuredContent!.Value.GetRawText());
        Check(documentList.IsError == false && documentList.StructuredContent!.Value.GetProperty("data").GetProperty("documents").GetArrayLength() == 1, "document list MCP roundtrip");
        File.WriteAllText(Path.Combine(testRoot, $"mcp-get_documents-result-{protocolVersion}.json"), documentList.StructuredContent!.Value.GetRawText());
        foreach (string action in new[] { "open", "activate", "save", "export_dxf", "activate_layout" })
        {
            var documentInput = new Dictionary<string, object?> { ["bridge_instance_id"] = instanceId, ["operation_id"] = Guid.NewGuid().ToString("D"), ["action"] = action };
            if (action != "open") { documentInput["document_id"] = documentId; documentInput["expected_revision"] = "1"; }
            if (action is not ("activate" or "activate_layout")) documentInput["path"] = action == "save" ? @"C:\copy.dwg" : @"C:\copy.dxf";
            if (action == "activate_layout") documentInput["layout_handle"] = "1E";
            var documentAction = await client.CallToolAsync("nanocad.manage_document", documentInput);
            Check(documentAction.IsError == false && documentAction.StructuredContent!.Value.GetProperty("data").GetProperty("action").GetString() == action, "document action MCP roundtrip");
            File.WriteAllText(Path.Combine(testRoot, $"mcp-manage-{action}-input-{protocolVersion}.json"), JsonSerializer.Serialize(documentInput));
            File.WriteAllText(Path.Combine(testRoot, $"mcp-manage-{action}-result-{protocolVersion}.json"), documentAction.StructuredContent!.Value.GetRawText());
            documentInput["close_after"] = true;
            var invalidDocument = await client.CallToolAsync("nanocad.manage_document", documentInput);
            Check(invalidDocument.IsError == true, "document unexpected close field accepted");
            File.WriteAllText(Path.Combine(testRoot, $"mcp-manage-{action}-error-{protocolVersion}.json"), invalidDocument.StructuredContent!.Value.GetRawText());
        }
        foreach (string operation in new[] { "get_structure", "inspect_block" })
        {
            var structuralInput = new Dictionary<string, object?> { ["bridge_instance_id"] = instanceId, ["document_id"] = documentId };
            structuralInput.Add(operation == "get_structure" ? "section" : "handle", operation == "get_structure" ? "layers" : "AB");
            var structural = await client.CallToolAsync("nanocad." + operation, structuralInput);
            Check(structural.IsError == false && structural.StructuredContent!.Value.GetProperty("data").GetProperty("complete").GetBoolean(), "structural MCP roundtrip");
            File.WriteAllText(Path.Combine(testRoot, $"mcp-{operation}-input-{protocolVersion}.json"), JsonSerializer.Serialize(structuralInput));
            File.WriteAllText(Path.Combine(testRoot, $"mcp-{operation}-result-{protocolVersion}.json"), structural.StructuredContent!.Value.GetRawText());
            structuralInput.Add("command", "_OPEN");
            var rejected = await client.CallToolAsync("nanocad." + operation, structuralInput);
            Check(rejected.IsError == true, "unknown structural field accepted");
            File.WriteAllText(Path.Combine(testRoot, $"mcp-{operation}-error-{protocolVersion}.json"), rejected.StructuredContent!.Value.GetRawText());
        }
    });
}
await Phase2Tests.Run(Test);
await LongOperationTests.Run(Test);
await Phase3Tests.Run(Test);
await TextEditTests.Run(Test);
await TableTextEditTests.Run(Test);
await TableSizeEditTests.Run(Test);
await TableCellLayout340Tests.Run(Test);
await TableGridLayout341Tests.Run(Test);
await DimensionEditing341Tests.Run(Test);
await NativeContourOffset341Tests.Run(Test);
await NativeCurveInspection342Tests.Run(Test);
await CurveCopyGeometry417Tests.Run(Test);
await NativeContourFinishing342Tests.Run(Test);
await IndependentContourCorners343Tests.Run(Test);
await TableFormula343Tests.Run(Test);
await TableFormula350Tests.Run(Test);
await NativeAnnotationMetadata358Tests.Run(Test);
await TableTopology354Tests.Run(Test);
await ContourSize344Tests.Run(Test);
await NativeStretch345Tests.Run(Test);
await ModelFeatureReorder345Tests.Run(Test);
await DrawingViewLifecycle345Tests.Run(Test);
await ModelProfileLifecycle340Tests.Run(Test);
await ModelParametric377Tests.Run(Test);
await IndependentFilletGeometry349Tests.Run(Test);
await Basic340IntegrationTests.Run(Test);
await DimensionLineEditTests.Run(Test);
await BlockMoveTests.Run(Test);
await BlockScaleTests.Run(Test);
await BlockEraseTests.Run(Test);
await BlockPropertiesReadTests.Run(Test);
await MTextFormattingReadTests.Run(Test);
await TextFormat442Tests.Run(Test);
await ConstructionCurves443Tests.Run(Test);
await HatchSpline443Tests.Run(Test);
await MLeaderInstance442Tests.Run(Test);
await BlockRootPropertyTests.Run(Test);
await HatchPatternEditTests.Run(Test);
await LayerRenameTests.Run(Test);
await ServerStartupTests.Run(Test);
await MLeaderTextEditTests.Run(Test);
await MTextCreateTests.Run(Test);
await MTextEditTests.Run(Test);
await DimensionTextEditTests.Run(Test);
await EllipseReadTests.Run(Test);
await EllipseCreateTests.Run(Test);
await SplineCreateTests.Run(Test);
await LinearSplineMetricTests.Run(Test);
await LinearSplineAreaTests.Run(Test);
await VisualCaptureTests.Run(Test);
await CameraViewTests.Run(Test);
await BlockCurveWriteTests.Run(Test);
await CurvedEditTests.Run(Test);
await SplineReadTests.Run(Test);
await HatchPatternTests.Run(Test);
await HatchRegionTests.Run(Test);
await WriteSpaceTests.Run(Test);
await Phase4Tests.Run(Test);
await DocumentTests.Run(Test);
await StructureWriteTests.Run(Test);
await BlockWriteTests.Run(Test);
await StyleWriteTests.Run(Test);
await FontCoverageTests.Run(Test);
await ModelManufacturingTests.Run(Test);
await StyleReadTests.Run(Test);
await DimensionStyleReadTests.Run(Test);
await DimensionStyleWriteTests.Run(Test);
await LinetypeReadTests.Run(Test);
await LayoutReadTests.Run(Test);
await ViewportWriteTests.Run(Test);
await LayoutCreateTests.Run(Test);
await ViewportCreateTests.Run(Test);
await LayoutActivationTests.Run(Test);
await DimensionGeometryTests.Run(Test);
await DimensionCreateTests.Run(Test);
await AnnotationTests.Run(Test);
await GeometryAnalysisTests.Run(Test);
await CurveDistanceTests.Run(Test);
await FiniteSegmentDistanceTests.Run(Test);
await FiniteArcDistanceTests.Run(Test);
await WriteRecoveryTests.Run(Test);
await CurveRelationsTests.Run(Test);
await PointContainmentTests.Run(Test);
await StateInvalidationTests.Run(Test);
await SolidReadTests.Run(Test);
await ModelHistoryTests.Run(Test);
await ModelEditingBatchTests.Run(Test);
await ModelExtrusionTests.Run(Test);
await ContourEditingTests.Run(Test);
await CameraFramingTests.Run(Test);
await ModelFeatureTests.Run(Test);
await ModelPatternTests.Run(Test);
await ModelPattern439Tests.Run(Test);
await ModelShellTests.Run(Test);
await ModelFeatureSuppressionTests.Run(Test);
await ModelRebuild419Tests.Run(Test);
await ModelArithmeticTests.Run(Test);
await SketchConstraintTests.Run(Test);
await AngularOrdinateCreateTests.Run(Test);
await HatchBoundaryReadTests.Run(Test);
await PointCloudReadTests.Run(Test);
await ModelVariablesTests.Run(Test);
await ModelParameterTests.Run(Test);
await ModelProfileRebind430Tests.Run(Test);
await ModelSketchSupportRecovery440Tests.Run(Test);
await ModelSketchConstraintRemoval430Tests.Run(Test);
await ModelSketchVisibility436Tests.Run(Test);
await PolylineEditing431Tests.Run(Test);
await DrawingConstraints431Tests.Run(Test);
await MLeaderTopology431Tests.Run(Test);
await DrawingGroups431Tests.Run(Test);
await AssociativeArray431Tests.Run(Test);
await CurveCreation441Tests.Run(Test);
await Wipeout431Tests.Run(Test);
await SketchConstraintsTests.Run(Test);
await DocumentConstraintsTests.Run(Test);
await DrawingCircleTests.Run(Test);
await DrawingProfileTests.Run(Test);
await DrawingArcTests.Run(Test);
await ModelPolycurveTests.Run(Test);
await DrawingWorkplaneTests.Run(Test);
await ModelSketchEditTests.Run(Test);
await SketchGeometryComparisonTests.Run(Test);
await DrawingRevolutionTests.Run(Test);
await DrawingArcRevolutionTests.Run(Test);
await CurveShapeEditingTests.Run(Test);
await PolylineTopologyTests.Run(Test);
await ContourAssemblyTests.Run(Test);
await PartialRevolutionTests.Run(Test);
await ExtendedCurveEditTests.Run(Test);
await ModelPlacementTests.Run(Test);
await DrawingAngularTests.Run(Test);
await DrawingRemovalTests.Run(Test);
await DrawingVariableTests.Run(Test);
await ModelFeatureSequenceTests.Run(Test);
await NativeParametric410Tests.Run(Test);
await ModelSketchBodyReactor410Tests.Run(Test);
await ModelFormingTerminal410Tests.Run(Test);
await DrawingViewsTests.Run(Test);
await DrawingViewCreateTests.Run(Test);
await ModelScaleTests.Run(Test);
await SolidSliceTests.Run(Test);
await SectionViewTests.Run(Test);
await DrawingViewBreakTests.Run(Test);
await SolidCreateTests.Run(Test);
await ExtrusionTests.Run(Test);
await RevolveSweepTests.Run(Test);
await LoftTests.Run(Test);
await SolidTransformCopyTests.Run(Test);
await SolidTopologyTests.Run(Test);
await DiscoveryRetentionTests.Run(Test);
await DrilledSolidCopyTests.Run(Test);
await SolidFinishingCopyTests.Run(Test);
await SolidMachiningEditTests.Run(Test);
await ModelEdgeFeatureTests.Run(Test);
await SolidBooleanTests.Run(Test);
await ViewportMviewTests.Run(Test);
await BasicModelingTests.Run(Test);
await LayoutReadAdapterTests.Run(Test);
await LinetypeWriteTests.Run(Test);
await LinetypeReadAdapterTests.Run(Test);
await DimensionReadAdapterTests.Run(Test);

await Test("descriptor removed on shutdown", () =>
{
    Check(!Discovery.List().Any(d => d.bridge_instance_id == instanceId), "stale live descriptor remained");
    return Task.CompletedTask;
});

if (outcomes.Count == 0) { failures++; Console.Error.WriteLine("No tests matched the requested filter."); }
File.WriteAllText(Path.Combine(testRoot, "results.json"), JsonSerializer.Serialize(new { utc = DateTime.UtcNow.ToString("O"), failures,
    partial_run = filters.Count > 0, filters, skipped,
    scope = "No CAD runtime: contract, queue, Windows IPC, discovery, MCP stdio", outcomes }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{Math.Max(0, outcomes.Count - failures)}/{outcomes.Count} passed. Evidence: {testRoot}");
return failures == 0 ? 0 : 1;

sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
{
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        base.ReadAsync(buffer[..Math.Min(3, buffer.Length)], cancellationToken);
}

