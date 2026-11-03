using System.Text.Json;
using ModelContextProtocol.Client;
using NanoCAD.Contracts;

internal static class DocumentFixture
{
    public const string ScratchRoot = @"C:\NanoCAD MCP\artifacts\phase4-documents-candidate\native-files\";
    public static bool IsScratch(string? path) => path != null && Path.GetFullPath(path).StartsWith(ScratchRoot, StringComparison.OrdinalIgnoreCase);
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 4) throw new ArgumentException("Usage: --document-fixture <dotnet.exe> <server.dll> <saved-request.json> <result.json>");
        string requestPath = Path.GetFullPath(args[2]), output = Path.GetFullPath(args[3]);
        if (File.Exists(output) || string.Equals(requestPath, output, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Use a fresh result path and preserve the saved request for replay.");
        FixturePendingWait.RequireFreshPrefix(output);
        using var json = JsonDocument.Parse(File.ReadAllText(requestPath));
        var parsed = DocumentValidation.Parse(json.RootElement);
        if (parsed.Path != null && !IsScratch(parsed.Path)) throw new ArgumentException("Document acceptance paths must be inside the dedicated native-files directory.");
        var input = json.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone());
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "nanocad-document-fixture", Command = Path.GetFileName(args[0]), WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(args[0])),
            Arguments = [Path.GetFullPath(args[1])], StandardErrorLines = line => Console.Error.WriteLine("MCP server: " + line)
        });
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: deadline.Token);
        async Task<JsonElement> Call(string name,Dictionary<string,object?> values,CancellationToken cancel)
        {
            var response=await client.CallToolAsync("nanocad."+name,values,cancellationToken:cancel);
            return JsonSerializer.SerializeToElement(response.StructuredContent);
        }
        var lifetime=await FixturePendingWait.CaptureLifetime(Call,parsed.BridgeId,output,deadline.Token);
        if (parsed.Action is not ("new" or "open"))
        {
            var snapshot = await client.CallToolAsync("nanocad.get_documents", new Dictionary<string, object?> { ["bridge_instance_id"] = parsed.BridgeId }, cancellationToken: deadline.Token);
            if (snapshot.IsError == true || snapshot.StructuredContent is not { } documents) throw new InvalidOperationException("Could not identify scratch documents; no mutation sent.");
            var target = documents.GetProperty("data").GetProperty("documents").EnumerateArray()
                .SingleOrDefault(d => d.GetProperty("document_id").GetString() == parsed.DocumentId);
            bool ownedNew = false;
            string ownership = Path.Combine(Path.GetDirectoryName(output)!, "owned-new-document.json");
            if (target.ValueKind != JsonValueKind.Undefined && target.GetProperty("path").ValueKind == JsonValueKind.Null && File.Exists(ownership))
            {
                using var proof = JsonDocument.Parse(File.ReadAllText(ownership));
                ownedNew = proof.RootElement.GetProperty("bridge_instance_id").GetString() == parsed.BridgeId &&
                    proof.RootElement.GetProperty("document_id").GetString() == parsed.DocumentId &&
                    target.GetProperty("is_named").GetBoolean() == false && parsed.Action == "save" && IsScratch(parsed.Path);
            }
            if (target.ValueKind == JsonValueKind.Undefined || (!ownedNew && !IsScratch(target.GetProperty("path").GetString())))
                throw new InvalidOperationException("Target is not an identified scratch document; no mutation sent.");
            FixturePendingWait.SaveNew(output+".preflight.json",documents);
        }
        // The preflight timer is removed BEFORE submission. A live pending
        // operation stays with this client; only the original saved ID is read.
        deadline.CancelAfter(Timeout.InfiniteTimeSpan);
        deadline.Token.ThrowIfCancellationRequested();
        var created=await FixturePendingWait.Await(Call,"manage_document",input,parsed.BridgeId,lifetime,output);
        if (parsed.Action == "new" && created.GetProperty("success").GetBoolean())
        {
            var document = created.GetProperty("data").GetProperty("document");
            string ownership = Path.Combine(Path.GetDirectoryName(output)!, "owned-new-document.json");
            var proof = JsonSerializer.Serialize(new { bridge_instance_id = parsed.BridgeId, document_id = document.GetProperty("document_id").GetString() });
            if (File.Exists(ownership))
            {if(File.ReadAllText(ownership)!=proof)throw new InvalidOperationException("Ownership evidence already belongs to another new document.");}
            else FixturePendingWait.SaveNew(ownership,proof);
        }
        return created.GetProperty("success").GetBoolean()?0:1;
    }
}
