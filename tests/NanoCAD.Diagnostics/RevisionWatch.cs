using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Client;

internal static class RevisionWatch
{
    // A bounded read-only test runner. Keeps a cursor fresh before a user's
    // edit, then tests it immediately, independently of chat response latency.
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 5) throw new ArgumentException("--watch-revision <dotnet> <server.dll> <bridge UUID> <document UUID> <result.json>");
        string output = Path.GetFullPath(args[4]);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "nanocad-revision-check", Command = Path.GetFileName(args[0]),
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(args[0])), Arguments = [Path.GetFullPath(args[1])]
        });
        void Save(object value) => File.WriteAllText(output, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        try
        {
            await using var client = await McpClient.CreateAsync(transport, cancellationToken: deadline.Token);
            async Task<JsonElement> Call(string name, Dictionary<string, object?> input)
            {
                var response = await client.CallToolAsync("nanocad." + name, input, cancellationToken: deadline.Token);
                return JsonSerializer.SerializeToElement(response.StructuredContent);
            }
            Dictionary<string, object?> DocumentArgs() => new() { ["bridge_instance_id"] = args[2] };
            Dictionary<string, object?> PageArgs() => new() { ["bridge_instance_id"] = args[2], ["document_id"] = args[3], ["page_size"] = 3 };
            bool Success(JsonElement value) => value.GetProperty("success").GetBoolean();
            string? Error(JsonElement value) => value.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
                ? error.GetProperty("code").GetString() : null;
            string Revision(JsonElement value) => value.GetProperty("context").GetProperty("revision").GetString()!;
            void RequireTarget(JsonElement value)
            {
                if (value.GetProperty("context").GetProperty("document_id").GetString() != args[3])
                    throw new InvalidOperationException("The active document changed; watcher stopped.");
                if (!string.Equals(value.GetProperty("data").GetProperty("path").GetString(),
                    @"C:\NanoCAD MCP\tests\fixtures\phase1-readonly.dxf", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Revision watcher is restricted to the acceptance fixture.");
            }
            var first = await Call("get_document", DocumentArgs());
            if (!Success(first)) throw new InvalidOperationException("Initial read failed: " + Error(first));
            RequireTarget(first);
            string baselineRevision = Revision(first);
            var pageArgs = PageArgs(); pageArgs["expected_revision"] = baselineRevision;
            var page = await Call("query_objects", pageArgs);
            if (!Success(page) || page.GetProperty("data").GetProperty("next_cursor").ValueKind != JsonValueKind.String)
                throw new InvalidOperationException("Cannot establish a paginated baseline: " + Error(page));
            var age = Stopwatch.StartNew();
            Save(new { status = "ready", bridge_instance_id = args[2], document_id = args[3], baseline_revision = baselineRevision });
            Console.WriteLine("WATCH_READY: a user edit/Undo can now be performed; no CAD actions are sent by this runner.");
            while (true)
            {
                await Task.Delay(500, deadline.Token);
                var current = await Call("get_document", DocumentArgs());
                if (!Success(current))
                {
                    if (Error(current) is "HOST_BUSY" or "UI_DISPATCH_UNAVAILABLE") continue;
                    throw new InvalidOperationException("Read failed while waiting: " + Error(current));
                }
                RequireTarget(current);
                if (Revision(current) != baselineRevision)
                {
                    var continuation = PageArgs(); continuation["cursor"] = page.GetProperty("data").GetProperty("next_cursor").GetString();
                    var result = await Call("query_objects", continuation);
                    bool passed = Error(result) == "STALE_DOCUMENT" && age.Elapsed < TimeSpan.FromSeconds(60);
                    Save(new { status = passed ? "passed" : "failed", baseline = page, changed_document = current,
                        cursor_age_ms = age.Elapsed.TotalMilliseconds, continuation_result = result,
                        scope = "Actual MCP cursor rejected immediately after native revision change; runner never edits the drawing" });
                    Console.WriteLine(passed ? "PASS native stale cursor" : "FAIL native stale cursor: " + Error(result));
                    return passed ? 0 : 1;
                }
                if (age.Elapsed > TimeSpan.FromSeconds(10))
                {
                    var refreshed = await Call("query_objects", pageArgs);
                    if (Success(refreshed)) { page = refreshed; age.Restart(); }
                    else if (Error(refreshed) != "STALE_DOCUMENT") throw new InvalidOperationException("Cursor refresh failed: " + Error(refreshed));
                }
            }
        }
        catch (Exception error)
        {
            Save(new { status = "failed", error = error.ToString(), scope = "Read-only watcher; stopped without CAD mutations" });
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }
}
