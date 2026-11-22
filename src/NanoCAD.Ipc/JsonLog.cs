using System.Text.Json;
using NanoCAD.Contracts;

namespace NanoCAD.Ipc;

public sealed class JsonLog
{
    private const long RotationBytes = 2 * 1024 * 1024;
    private readonly string? _prefix;
    private readonly object _gate = new();

    public JsonLog(string component)
    {
        try
        {
            LocalSecurity.SecureDirectory(Discovery.Root);
            string directory = LocalSecurity.SecureDirectory(Path.Combine(Discovery.Root, "logs"));
            _prefix = Path.Combine(directory, component);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* Unavailable diagnostic storage. */ }
    }

    // Stable slots are reused across process lifetimes. An exclusive writer lease
    // covers rotation and append, including when several clients start at once.
    private FileStream AcquireSlot(out string path)
    {
        for (int slot = 0; ; slot++)
        {
            path = $"{_prefix}-slot{slot}.jsonl";
            try { return new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read); }
            catch (IOException e) when ((e.HResult & 0xffff) == 32) { } // ERROR_SHARING_VIOLATION
        }
    }

    // Deliberately no request arguments, DWG paths, text values or native stacks in routine logs.
    public void Write(string operation, string requestId, string outcome, long elapsedMs = 0, string? transaction = null, string? operationId = null)
    {
        if (_prefix == null) return;
        lock (_gate)
        {
            try
            {
                using var lease = AcquireSlot(out string path);
                var file = new FileInfo(path);
                if (file.Exists && file.Length >= RotationBytes)
                {
                    if (File.Exists(path + ".1")) File.Move(path + ".1", path + ".2", true);
                    File.Move(path, path + ".1", true);
                }
                File.AppendAllText(path, JsonSerializer.Serialize(new { utc = DateTime.UtcNow.ToString("O"),
                    process_id = Environment.ProcessId, operation, request_id = requestId, operation_id = operationId,
                    outcome, elapsed_ms = elapsedMs, transaction }, Wire.Json) + Environment.NewLine);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* Routine logging is best effort. */ }
        }
    }
}
