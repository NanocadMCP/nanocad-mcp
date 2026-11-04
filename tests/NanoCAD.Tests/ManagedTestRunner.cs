using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using System.Xml.Linq;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

internal sealed class ManagedTestRunner
{
    private sealed record Outcome(string name, bool passed, string? error, bool skipped, double seconds, string? reason);
    private readonly List<Outcome> outcomes = [];
    private readonly List<string> filters = [];
    private HashSet<string>? failedNames;
    private readonly string? junit;
    private readonly string? json;
    private readonly bool managedOnly;
    private int filtered;

    public string Root { get; } = Path.GetFullPath(Path.Combine("artifacts", "tests", Guid.NewGuid().ToString("N")));

    public ManagedTestRunner(string[] args)
    {
        for (int index = 0; index < args.Length; index++)
        {
            string option = args[index];
            if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
                throw new ArgumentException("Each test option requires a value.");
            string value = args[index];
            switch (option)
            {
                case "--filter": filters.Add(value); break;
                case "--junit": junit = Path.GetFullPath(value); break;
                case "--json": json = Path.GetFullPath(value); break;
                case "--require-standard-windows-user": RequireStandardWindowsUser(value); break;
                case "--require-elevated-windows-owner" when value == "administrators": RequireElevatedWindowsOwner(); break;
                case "--suite" when value is "all" or "managed": managedOnly = value == "managed"; break;
                case "--rerun-failed":
                    using (var previous = JsonDocument.Parse(File.ReadAllText(value)))
                        failedNames = previous.RootElement.GetProperty("outcomes").EnumerateArray()
                            .Where(item => !item.GetProperty("passed").GetBoolean())
                            .Select(item => item.GetProperty("name").GetString()!)
                            .ToHashSet(StringComparer.Ordinal);
                    break;
                default: throw new ArgumentException("Unknown test option or suite: " + option);
            }
        }
        Directory.CreateDirectory(Root);
        Environment.SetEnvironmentVariable("NANOCAD_MCP_HOME", Root);
    }

    private static void RequireStandardWindowsUser(string expectedSid)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        if (identity.User?.Value != expectedSid || !Equals(identity.Owner, identity.User) ||
            identity.Groups == null || identity.Groups.Contains(administrators))
            throw new ArgumentException("The CI test process requires the requested standard Windows user with its user SID as token owner.");
        Console.WriteLine("Verified standard Windows test token and user SID owner.");
    }

    private static void RequireElevatedWindowsOwner()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        if (identity.Owner != administrators || identity.User == administrators ||
            !new WindowsPrincipal(identity).IsInRole(administrators))
            throw new ArgumentException("Elevated IPC tests require an enabled Administrators group as the token owner, distinct from the user's SID.");
        Console.WriteLine("Verified elevated Windows test token with Administrators as the default owner.");
    }

    public Task Run(string name, Func<Task> test) => Execute(name, test, false);
    public Task RunHostMetadata(string name, Func<Task> test) => Execute(name, test, true);

    private async Task Execute(string name, Func<Task> test, bool hostMetadata)
    {
        if ((filters.Count > 0 && !filters.Any(filter => name.Contains(filter, StringComparison.OrdinalIgnoreCase))) ||
            (failedNames != null && !failedNames.Contains(name)))
        {
            filtered++;
            return;
        }
        if (managedOnly && hostMetadata)
        {
            const string reason = "Requires a locally built nanoCAD bridge or installed host metadata; excluded from the managed CI suite.";
            outcomes.Add(new(name, true, null, true, 0, reason));
            Console.WriteLine("SKIP " + name + ": " + reason);
            return;
        }
        var timer = Stopwatch.StartNew();
        try
        {
            await test();
            outcomes.Add(new(name, true, null, false, timer.Elapsed.TotalSeconds, null));
            Console.WriteLine("PASS " + name);
        }
        catch (Exception error)
        {
            outcomes.Add(new(name, false, error.ToString(), false, timer.Elapsed.TotalSeconds, null));
            Console.WriteLine("FAIL " + name + ": " + error);
        }
    }

    public int Complete()
    {
        if (outcomes.Count == 0)
            outcomes.Add(new("test selection", false, "No tests matched the requested selection.", false, 0, null));
        int failures = outcomes.Count(item => !item.passed);
        int skipped = outcomes.Count(item => item.skipped);
        string results = JsonSerializer.Serialize(new
        {
            utc = DateTime.UtcNow.ToString("O"),
            failures,
            suite = managedOnly ? "managed" : "all",
            partial_run = filters.Count > 0 || failedNames != null,
            filters,
            filtered,
            skipped,
            scope = "No CAD execution: contracts, queue, Windows IPC, discovery, MCP stdio; optional compiled host metadata",
            outcomes
        }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(Root, "results.json"), results);
        if (json != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(json)!);
            File.WriteAllText(json, results);
        }
        if (junit != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(junit)!);
            var suite = new XElement("testsuite", new XAttribute("name", "NanoCAD.Managed"),
                new XAttribute("tests", outcomes.Count), new XAttribute("failures", failures), new XAttribute("skipped", skipped));
            foreach (var item in outcomes)
            {
                var entry = new XElement("testcase", new XAttribute("name", item.name), new XAttribute("classname", "NanoCAD.Tests"),
                    new XAttribute("time", item.seconds.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)));
                if (item.skipped) entry.Add(new XElement("skipped", new XAttribute("message", item.reason!)));
                else if (!item.passed) entry.Add(new XElement("failure", item.error));
                suite.Add(entry);
            }
            new XDocument(suite).Save(junit);
        }
        Console.WriteLine($"{outcomes.Count - skipped - failures}/{outcomes.Count - skipped} passed; {skipped} host-metadata tests skipped. Evidence: {Root}");
        return failures == 0 ? 0 : 1;
    }
}

internal static class TestEnvironment
{
    public static string ServerAssembly => typeof(NanoCAD.McpServer.ToolService).Assembly.Location;

    public static Process StartServer(string home, bool compactCatalog = false)
    {
        var info = new ProcessStartInfo(DotnetHost)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        info.ArgumentList.Add(ServerAssembly);
        info.Environment["NANOCAD_MCP_HOME"] = home;
        if (compactCatalog)
            info.Environment.Remove("NANOCAD_MCP_RESPONSE_MODE");
        return Process.Start(info) ?? throw new InvalidOperationException("MCP test server did not start.");
    }

    public static string DotnetHost
    {
        get
        {
            foreach (string? candidate in new[]
            {
                Environment.GetEnvironmentVariable("DOTNET_HOST_PATH"),
                Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet" ? Environment.ProcessPath : null,
                Path.GetFullPath(".tools/dotnet/dotnet.exe"),
                Path.Combine(Environment.GetEnvironmentVariable("DOTNET_ROOT") ?? "", "dotnet.exe")
            })
                if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate)) return Path.GetFullPath(candidate);
            foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                string candidate = Path.Combine(folder, "dotnet.exe");
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
            throw new FileNotFoundException("Set DOTNET_HOST_PATH to the .NET SDK host used to build the managed tests.");
        }
    }
}

internal sealed class TestMcpServer : IAsyncDisposable
{
    private readonly Process process;
    private readonly Task<string> stderr;

    public TestMcpServer(string home)
    {
        process = TestEnvironment.StartServer(home);
        stderr = process.StandardError.ReadToEndAsync();
    }

    public async Task<McpClient> ConnectAsync(string protocolVersion, CancellationToken cancellationToken)
    {
        // Direct process startup honors the resolved host path, including spaces.
        var transport = new StreamClientTransport(process.StandardInput.BaseStream, process.StandardOutput.BaseStream);
        try
        {
            return await McpClient.CreateAsync(transport, new McpClientOptions { ProtocolVersion = protocolVersion }, cancellationToken: cancellationToken);
        }
        catch (Exception error) when (process.HasExited)
        {
            throw new InvalidOperationException($"MCP test server exited (0x{process.ExitCode:X8}): {await stderr}", error);
        }
    }

    public async ValueTask DisposeAsync()
    {
        using (process)
        {
            process.StandardInput.BaseStream.Dispose();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (TimeoutException)
            {
                if (!process.HasExited) process.Kill();
                await process.WaitForExitAsync();
            }
            await stderr;
        }
    }
}
