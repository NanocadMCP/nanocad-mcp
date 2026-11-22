using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.Ipc;

internal static class DiscoveryRetentionTests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("discovery finds a live bridge beyond 64 stale lifetime descriptors", () =>
        {
            string prior = Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!;
            try
            {
                Environment.SetEnvironmentVariable("NANOCAD_MCP_HOME", Path.Combine(prior, "stale-discovery"));
                Directory.CreateDirectory(Discovery.Instances);
                for (int i = 0; i < 100; i++)
                {
                    string id = Guid.NewGuid().ToString("D");
                    var stale = Discovery.Describe(id, "test") with { process_start_utc_ticks = 1 };
                    File.WriteAllText(Path.Combine(Discovery.Instances, id + ".json"), JsonSerializer.Serialize(stale, Wire.Json));
                }
                // Choose the actual 81st entry; no filesystem sorting assumption.
                string path = Directory.EnumerateFiles(Discovery.Instances, "*.json").ElementAt(80);
                var live = Discovery.Describe(Path.GetFileNameWithoutExtension(path), "test");
                File.WriteAllText(path, JsonSerializer.Serialize(live, Wire.Json));
                var found = Discovery.List();
                if (found.Length != 1 || found[0] != live) throw new Exception("Live bridge hidden by stale discovery entries.");
                if (Directory.GetFiles(Discovery.Instances, "*.json").Length != 100) throw new Exception("Read-only discovery removed files.");
                string foreignName = Path.Combine(Discovery.Instances, "unrelated.json");
                File.WriteAllText(foreignName, JsonSerializer.Serialize(live with { process_start_utc_ticks = 1 }, Wire.Json));
                var other = Discovery.Describe(Guid.NewGuid().ToString("D"), "test") with { session_id = live.session_id + 1, process_start_utc_ticks = 1 };
                string otherPath = Path.Combine(Discovery.Instances, other.bridge_instance_id + ".json");
                File.WriteAllText(otherPath, JsonSerializer.Serialize(other, Wire.Json));
                string malformed = Path.Combine(Discovery.Instances, Guid.NewGuid() + ".json");
                File.WriteAllText(malformed, "{");
                string retiredId = Guid.NewGuid().ToString("D");
                var retired = Discovery.Describe(retiredId, "test") with { process_id = int.MaxValue, pipe_name = $"NanoCADMcp-{int.MaxValue}-{retiredId}" };
                string retiredPath = Path.Combine(Discovery.Instances, retiredId + ".json");
                File.WriteAllText(retiredPath, JsonSerializer.Serialize(retired, Wire.Json));
                var fresh = Discovery.Describe(Guid.NewGuid().ToString("D"), "test");
                string published = Discovery.Publish(fresh);
                var remaining = Directory.GetFiles(Discovery.Instances, "*.json").ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (!remaining.SetEquals(new[] { path, published, foreignName, otherPath, malformed }) || File.Exists(retiredPath))
                    throw new Exception("Startup cleanup must remove only proven retired descriptors in the same session.");
            }
            finally { Environment.SetEnvironmentVariable("NANOCAD_MCP_HOME", prior); }
            return Task.CompletedTask;
        });
        await test("routine log slot rotation preserves legacy files and concurrent writes", async () =>
        {
            string prior = Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!;
            string root = Path.Combine(prior, "log-slots");
            Environment.SetEnvironmentVariable("NANOCAD_MCP_HOME", root);
            try
            {
                var logs = Enumerable.Range(0, 8).Select(_ => new JsonLog("server")).ToArray();
                var prefix = Path.Combine(root, "logs", "server-slot0.jsonl");
                var legacy = Path.Combine(root, "logs", "server-123.jsonl");
                File.WriteAllText(legacy, "saved evidence");
                File.WriteAllText(prefix, new string('a', 2 * 1024 * 1024));
                File.WriteAllText(prefix + ".1", "previous");
                using (var occupied = new FileStream(prefix + ".lock", FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read))
                    logs[0].Write("test", "alternate-slot", "ok");
                await Task.WhenAll(logs.Select((log, index) => Task.Run(() =>
                {
                    for (int n = 0; n < 25; n++) log.Write("test", $"{index}-{n}", "ok");
                })));
                var records = Directory.GetFiles(Path.GetDirectoryName(prefix)!, "server-slot*.jsonl")
                    .SelectMany(File.ReadLines).Select(line => JsonSerializer.Deserialize<JsonElement>(line)).ToArray();
                if (File.ReadAllText(legacy) != "saved evidence" || File.ReadAllText(prefix + ".2") != "previous" ||
                    new FileInfo(prefix + ".1").Length != 2 * 1024 * 1024 || records.Length != 201 ||
                    records.Select(x => x.GetProperty("request_id").GetString()).Distinct().Count() != records.Length ||
                    records.Any(x => x.GetProperty("process_id").GetInt32() != Environment.ProcessId))
                    throw new Exception("Lost log records, rotation history or process identity.");
                // A read-only destination or an unavailable diagnostic directory must leave CAD calls usable.
                string locked = Path.Combine(root, "logs", "server-slot0.jsonl");
                File.SetAttributes(locked, FileAttributes.ReadOnly);
                try { logs[0].Write("test", "no-write-access", "ok"); }
                finally { File.SetAttributes(locked, FileAttributes.Normal); }
                string logUnavailable = Directory.CreateDirectory(Path.Combine(root, "log-unavailable")).FullName;
                File.WriteAllText(Path.Combine(logUnavailable, "logs"), "Only diagnostic storage is unavailable.");
                Environment.SetEnvironmentVariable("NANOCAD_MCP_HOME", logUnavailable);
                new JsonLog("server").Write("test", "no-directory", "ok");
            }
            finally { Environment.SetEnvironmentVariable("NANOCAD_MCP_HOME", prior); }
        });
    }
}
