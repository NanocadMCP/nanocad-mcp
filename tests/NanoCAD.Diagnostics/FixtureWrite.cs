using System.Text.Json;
using ModelContextProtocol.Client;
using NanoCAD.Contracts;

internal static class FixtureWrite
{
    public static async Task<int> RunAsync(string[] args, string operation, bool awaitPending = false)
    {
        if (args.Length != 4)
            throw new ArgumentException("Usage: --create-fixture/--modify-fixture/--structure-fixture/--view-fixture <dotnet.exe> <server.dll> <existing-request.json> <result.json>");
        string requestPath = Path.GetFullPath(args[2]), resultPath = Path.GetFullPath(args[3]);
        if (File.Exists(resultPath) || string.Equals(requestPath, resultPath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Keep the exact request and previous evidence for replay; result must use a fresh file.");
        FixturePendingWait.RequireFreshPrefix(resultPath);
        using var json = JsonDocument.Parse(File.ReadAllText(requestPath));
        var parsed = WriteValidation.Parse(operation, json.RootElement);
        string documentId=parsed switch
        {
            IWriteArguments write=>write.DocumentId,
            CameraChangeArguments camera=>camera.DocumentId,
            _=>throw new ArgumentException("This helper accepts geometry/structure/model or display-only camera operations.")
        };
        // A request must already exist on disk, including its operation_id.
        // Pending mode retrieves the same reserved operation; never invents a new ID.
        var input = json.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone());
        // Legacy awaitPending argument remains compatible, but every actual
        // pending write now waits. This timer bounds connection/preflight only.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "nanocad-fixture-write", Command = Path.GetFileName(args[0]),
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(args[0])), Arguments = [Path.GetFullPath(args[1])],
            StandardErrorLines = line => Console.Error.WriteLine("MCP server: " + line)
        });
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: deadline.Token);
        async Task<JsonElement> Call(string name,Dictionary<string,object?> values,CancellationToken cancel)
        {
            var response=await client.CallToolAsync("nanocad."+name,values,cancellationToken:cancel);
            return JsonSerializer.SerializeToElement(response.StructuredContent);
        }
        var lifetime=await FixturePendingWait.CaptureLifetime(Call,parsed.BridgeId,resultPath,deadline.Token);
        var current = await client.CallToolAsync("nanocad.get_document",
            new Dictionary<string, object?> { ["bridge_instance_id"] = parsed.BridgeId }, cancellationToken: deadline.Token);
        if (current.IsError == true || current.StructuredContent is not { } document)
            throw new InvalidOperationException("Fixture read failed; no write was sent.");
        var data = document.GetProperty("data");
        string? path = data.GetProperty("path").GetString();
        string fixture = operation switch
        {
            "modify_structure" => @"C:\NanoCAD MCP\tests\fixtures\phase4-structure-write.dxf",
            "modify_entities" => @"C:\NanoCAD MCP\tests\fixtures\phase3-edit-probe.dxf",
            _ => @"C:\NanoCAD MCP\tests\fixtures\phase2-write-probe.dxf"
        };
        if (path == null || !(string.Equals(Path.GetFullPath(path), fixture, StringComparison.OrdinalIgnoreCase) || DocumentFixture.IsScratch(path)) ||
            data.GetProperty("document_id").GetString() != documentId ||
            document.GetProperty("context").GetProperty("bridge_instance_id").GetString() != parsed.BridgeId ||
            (parsed is IWriteArguments && data.GetProperty("units").GetProperty("name").GetString() != "Millimeters"))
            throw new InvalidOperationException("Only the identified active fixture/scratch drawing is allowed; geometry writes additionally require Millimeters. No write was sent.");
        FixturePendingWait.SaveNew(resultPath+".preflight.json",document);
        Console.WriteLine(JsonSerializer.Serialize(new{pending_wait="all ordinary/long fixture writes",legacy_long_mode=awaitPending}));
        deadline.CancelAfter(Timeout.InfiniteTimeSpan);
        deadline.Token.ThrowIfCancellationRequested();
        var terminal=await FixturePendingWait.Await(Call,operation,input,parsed.BridgeId,lifetime,resultPath);
        return terminal.GetProperty("success").GetBoolean()?0:1;
    }
}
