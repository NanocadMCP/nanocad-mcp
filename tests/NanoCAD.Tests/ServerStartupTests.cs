using System.Text.Json;
using NanoCAD.McpServer;

internal static class ServerStartupTests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        string root = Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!;
        await test("server startup failure exits normally with bounded stderr and clean stdout", async () =>
        {
            string blocked = Path.Combine(root, "blocked-log-home"); File.WriteAllText(blocked, "owned startup failure fixture");
            using var process = TestEnvironment.StartServer(blocked); var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
                string message = await stderr;
                if (process.ExitCode != 3 || await stdout != "" || message.Length > 1024 || message.Contains(blocked) || message.Contains("Unhandled exception")) throw new Exception("Startup failure escaped its process boundary.");
                using var parsed = JsonDocument.Parse(message);
                if (parsed.RootElement.GetProperty("server_error").GetString() != "MCP_SERVER_FAILED" || parsed.RootElement.GetProperty("phase").GetString() != "initialization") throw new Exception("Wrong startup diagnostic.");
                File.WriteAllText(Path.Combine(root, "server-startup-failure.json"), message);
            }
            finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
        });
        foreach (string protocol in new[] { "2025-11-25", "2026-07-28" })
            await test("server initialize guidance matches actual core capabilities " + protocol, async () =>
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await using var server = new TestMcpServer(Path.Combine(root, "startup-" + protocol));
                // The SDK negotiates the appropriate handshake. New session protocols
                // are not sent through the legacy initialize request.
                await using var client = await server.ConnectAsync(protocol, deadline.Token);
                string instructions = client.ServerInstructions ?? throw new Exception("No server instructions.");
                if (instructions != ServerGuidance.Instructions) throw new Exception("Current guidance was not delivered intact in the real MCP handshake.");
                if (client.NegotiatedProtocolVersion != protocol ||
                    !instructions.Contains("Work as an engineer") || !instructions.Contains("manufacture") ||
                    !instructions.Contains("Never create disposable examples") || !instructions.Contains("capture_view") ||
                    !instructions.Contains("exact same-ID/payload replay") || !instructions.Contains("specialized operation expresses the required result") ||
                    !instructions.Contains("EVERY supplied dimension") || !instructions.Contains("ALL supplied views and sections") ||
                    !instructions.Contains("fillets, chamfers, threads") || !instructions.Contains("edit that same document in place"))
                    throw new Exception("Engineering or safe-operation guidance missing from handshake.");
                File.WriteAllText(Path.Combine(root, "server-initialize-" + protocol + ".json"), JsonSerializer.Serialize(new { protocol = client.NegotiatedProtocolVersion, instructions }));
            });
    }
}
