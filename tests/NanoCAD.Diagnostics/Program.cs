using System.Text.Json;
using ModelContextProtocol.Client;

Environment.SetEnvironmentVariable("NANOCAD_MCP_RESPONSE_MODE","structured");

try { return await RunAsync(args); }
catch (Exception error)
{
    // A failed helper must exit normally, never open a Windows fatal-error
    // dialog. No automatic retry, especially if a write may have been sent.
    Console.Error.WriteLine(JsonSerializer.Serialize(new
    {
        diagnostic_error = error is OperationCanceledException ? "DIAGNOSTIC_TIMEOUT" : "DIAGNOSTIC_FAILED",
        error_type = error.GetType().Name, message = error.Message,
        recovery = args.FirstOrDefault() is "--create-fixture" or "--long-create-fixture" or "--modify-fixture" or "--structure-fixture" or "--document-fixture" or "--model-fixture" or "--view-fixture" ? "Use the exact saved request to retrieve a possibly submitted write in its original bridge lifetime; never generate a new operation_id." : null
    }));
    return 3;
}

static async Task<int> RunAsync(string[] args)
{
if (args.Length > 0 && args[0] == "--document-fixture")
    return await DocumentFixture.RunAsync(args.Skip(1).ToArray());
if (args.Length > 0 && args[0] == "--create-fixture")
    return await FixtureWrite.RunAsync(args.Skip(1).ToArray(), "create_entities");
if (args.Length > 0 && args[0] == "--long-create-fixture")
    return await FixtureWrite.RunAsync(args.Skip(1).ToArray(), "create_entities", awaitPending: true);
if (args.Length > 0 && args[0] == "--modify-fixture")
    return await FixtureWrite.RunAsync(args.Skip(1).ToArray(), "modify_entities");
if (args.Length > 0 && args[0] == "--structure-fixture")
    return await FixtureWrite.RunAsync(args.Skip(1).ToArray(), "modify_structure");
if (args.Length > 0 && args[0] == "--model-fixture")
    return await FixtureWrite.RunAsync(args.Skip(1).ToArray(), "set_model_parameter");
if (args.Length > 0 && args[0] == "--view-fixture")
    return await FixtureWrite.RunAsync(args.Skip(1).ToArray(), "set_view");
if (args.Length > 0 && args[0] == "--watch-revision")
    return await RevisionWatch.RunAsync(args.Skip(1).ToArray());
if (args.Length == 3 && args[0] == "--window-state")
{
    int pid = int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
    using var cadProcess = System.Diagnostics.Process.GetProcessById(pid);
    if (!string.Equals(cadProcess.ProcessName, "nCad", StringComparison.OrdinalIgnoreCase))
        throw new ArgumentException("Window diagnostic is restricted to a nanoCAD process.");
    var snapshot = new NanoCADBridge.HostWindowState(pid).Capture();
    string evidence = JsonSerializer.Serialize(new { utc = DateTime.UtcNow.ToString("O"), pid, snapshot }, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(args[2], evidence); Console.WriteLine(evidence); return 0;
}

// One real MCP read per invocation; never sends CAD commands or loads a plugin.
if (args.Length < 3 || args.Length > 5)
{
    Console.Error.WriteLine("Usage: NanoCAD.Diagnostics <dotnet.exe> <server.dll> <operation> [arguments.json] [result.json]");
    return 2;
}
string operation = args[2].StartsWith("nanocad.", StringComparison.Ordinal) ? args[2] : "nanocad." + args[2];
if (!NanoCAD.Contracts.Wire.ReadOperations.Select(name => "nanocad." + name).Contains(operation))
    throw new ArgumentException("Only semantic read operations are allowed by this diagnostic mode.");
var input = args.Length >= 4
    ? JsonSerializer.Deserialize<Dictionary<string, object?>>(File.ReadAllText(args[3]))!
    : new Dictionary<string, object?>();
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
var transport = new StdioClientTransport(new StdioClientTransportOptions
{
    Name = "nanocad-diagnostics", Command = Path.GetFileName(args[0]),
    WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(args[0])), Arguments = [Path.GetFullPath(args[1])],
    StandardErrorLines = line => Console.Error.WriteLine("MCP server: " + line)
});
await using var client = await McpClient.CreateAsync(transport, cancellationToken: deadline.Token);

var response = await client.CallToolAsync(operation, input, cancellationToken: deadline.Token);
string json = JsonSerializer.Serialize(response.StructuredContent, new JsonSerializerOptions { WriteIndented = true });
if (args.Length == 5) File.WriteAllText(args[4], json);
Console.WriteLine(json);
return response.IsError == true ? 1 : 0;
}
