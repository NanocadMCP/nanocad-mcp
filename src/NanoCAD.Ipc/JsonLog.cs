using System.Text.Json;
using NanoCAD.Contracts;

namespace NanoCAD.Ipc;

public sealed class JsonLog
{
    private readonly string _path;
    private readonly object _gate = new();
    public JsonLog(string component)
    {
        LocalSecurity.SecureDirectory(Discovery.Root);
        _path = Path.Combine(LocalSecurity.SecureDirectory(Path.Combine(Discovery.Root, "logs")), $"{component}-{Environment.ProcessId}.jsonl");
    }
    // Deliberately no request arguments, DWG paths, text values or native stacks in routine logs.
    public void Write(string operation, string requestId, string outcome, long elapsedMs = 0, string? transaction = null, string? operationId = null)
    {
        lock (_gate)
        {
            try
            {
                if (File.Exists(_path) && new FileInfo(_path).Length > 2 * 1024 * 1024)
                {
                    File.Move(_path + ".1", _path + ".2", true); // handled below if no previous file
                }
            }
            catch (IOException) { }
            try
            {
                if (File.Exists(_path) && new FileInfo(_path).Length > 2 * 1024 * 1024) File.Move(_path, _path + ".1", true);
                File.AppendAllText(_path, JsonSerializer.Serialize(new { utc = DateTime.UtcNow.ToString("O"), operation,
                    request_id = requestId, operation_id = operationId, outcome, elapsed_ms = elapsedMs, transaction }, Wire.Json) + Environment.NewLine);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* Logging cannot break a CAD read. */ }
        }
    }
}
