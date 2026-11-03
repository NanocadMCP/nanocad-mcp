using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using HostMgd.ApplicationServices;
using Teigha.DatabaseServices;
using Teigha.Runtime;
using Application = HostMgd.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(NanoCADBridge.ReadProbe.Entry))]
[assembly: CommandClass(typeof(NanoCADBridge.ReadProbe.Entry))]

namespace NanoCADBridge.ReadProbe;

// Separate, manually loaded diagnostic assembly. It never creates a drawing,
// saves, closes, resets DBMOD, changes sysvars or runs an external command.
public sealed class Entry : IExtensionApplication
{
    private const string Fixture = @"C:\NanoCAD MCP\tests\fixtures\phase1-readonly.dxf";
    private static Entry? _current;
    private System.Windows.Forms.Timer? _timer;
    private readonly List<object> _observations = new();
    private readonly Stopwatch _wait = new();
    private string _run = "";
    private string _stage = "idle";
    private int _uiThread;
    private bool _running;

    public void Initialize() { _current = this; _uiThread = Environment.CurrentManagedThreadId; Arm(); }
    public void Terminate() { Stop(); if (_current == this) _current = null; }

    [CommandMethod("NCMCP_READPROBE", CommandFlags.NoUndoMarker | CommandFlags.NoHistory)]
    public void RestartProbe() => _current?.Arm();

    private void Arm()
    {
        Stop(); _observations.Clear(); _run = Guid.NewGuid().ToString("D"); _wait.Restart();
        Write("waiting_for_clean_fixture");
        _timer = new System.Windows.Forms.Timer { Interval = 100 };
        _timer.Tick += Tick; _timer.Start();
    }
    private void Stop()
    {
        if (_timer == null) return;
        _timer.Stop(); _timer.Tick -= Tick; _timer.Dispose(); _timer = null;
    }
    private static int Dbmod() => Convert.ToInt32(Application.GetSystemVariable("DBMOD"));
    private void Tick(object? sender, EventArgs args)
    {
        if (_running) return;
        _running = true;
        try
        {
            if (Environment.CurrentManagedThreadId != _uiThread) throw new InvalidOperationException("UI thread mismatch.");
            if (_wait.Elapsed > TimeSpan.FromMinutes(3)) { Stop(); Write("clean_fixture_timeout"); return; }
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null || !string.Equals(Path.GetFullPath(doc.Name), Fixture, StringComparison.OrdinalIgnoreCase)) return;
            if (!string.IsNullOrEmpty(doc.CommandInProgress) || Convert.ToInt32(Application.GetSystemVariable("CMDACTIVE")) != 0) return;
            if (Dbmod() != 0) return;
            Stop();
            Run(doc);
            Write("completed");
        }
        catch (System.Exception error) { Stop(); Write("failed", error.ToString()); }
        finally { _running = false; }
    }

    private void Run(Document doc)
    {
        Step("dbmod_control", Dbmod);
        Step("audit_pickfirst", () => Application.GetSystemVariable("PICKFIRST"));
        Step("audit_select_implied", () => doc.Editor.SelectImplied().Status.ToString());
        DocumentLock? documentLock = null;
        Transaction? transaction = null;
        try
        {
            documentLock = Step("read_lock_acquire", () => doc.LockDocument(DocumentLockMode.Read, null, null, false));
            var db = Step("document_database", () => doc.Database);
            var manager = Step("transaction_manager", () => db.TransactionManager);
            transaction = Step("start_transaction", () => manager.StartTransaction());
            var blockTableId = Step("block_table_id", () => db.BlockTableId);
            var table = Step("open_block_table_for_read", () => (BlockTable)transaction.GetObject(blockTableId, OpenMode.ForRead));
            var modelId = Step("model_space_id", () => table[BlockTableRecord.ModelSpace]);
            Step("document_name", () => doc.Name);
            Step("is_named_drawing", () => doc.IsNamedDrawing);
            Step("is_read_only", () => doc.IsReadOnly);
            Step("insunits", () => db.Insunits.ToString());
            Step("current_space_id", () => db.CurrentSpaceId == modelId);
            Step("fingerprint_guid", () => db.FingerprintGuid);
        }
        finally
        {
            try
            {
                if (transaction != null) Step("transaction_dispose_without_commit", () => { transaction.Dispose(); return true; });
            }
            finally
            {
                if (documentLock != null) Step("read_lock_dispose", () => { documentLock.Dispose(); return true; });
            }
        }
        Step("audit_select_implied_after", () => doc.Editor.SelectImplied().Status.ToString());
    }

    private T Step<T>(string stage, Func<T> operation)
    {
        _stage = stage;
        int before = Dbmod();
        Trace(new { stage, state = "before", dbmod = before });
        T value = operation();
        int after = Dbmod();
        var item = new { stage, before, after, changed = before != after };
        _observations.Add(item); Trace(new { stage, state = "after", dbmod = after });
        return value;
    }
    private static string DirectoryPath => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
    private void Trace(object value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { utc = DateTime.UtcNow.ToString("O"), run = _run,
            pid = Environment.ProcessId, thread = Environment.CurrentManagedThreadId, data = value });
        using var stream = new FileStream(Path.Combine(DirectoryPath, "read-probe-trace.jsonl"), FileMode.Append, FileAccess.Write, FileShare.Read);
        if (stream.Length > 1024 * 1024) throw new InvalidOperationException("Read probe trace limit reached.");
        stream.Write(bytes); stream.WriteByte((byte)'\n'); stream.Flush(true);
    }
    private void Write(string status, string? error = null)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { utc = DateTime.UtcNow.ToString("O"), run = _run,
            pid = Environment.ProcessId, status, stage = _stage, error, observations = _observations,
            scope = "Fixed fixture only; staged read DBMOD diagnostics; no reset or CAD writes" }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllBytes(Path.Combine(DirectoryPath, "read-probe-result.json"), bytes);
    }
}
