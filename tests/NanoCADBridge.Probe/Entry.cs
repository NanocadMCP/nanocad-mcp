using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using HostMgd.ApplicationServices;
using Teigha.DatabaseServices;
using Teigha.Runtime;
using Application = HostMgd.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(NanoCADBridge.Probe.Entry))]

namespace NanoCADBridge.Probe;

/// <summary>One-shot read-only host/context probe. No MCP or drawing commands.</summary>
public sealed class Entry : IExtensionApplication
{
    private int _initializeThread;
    private bool _running;
    private string _stage = "initialize";
    private readonly Stopwatch _lifetime = Stopwatch.StartNew();
    private int _idleAttempts;
    private readonly string _run = Guid.NewGuid().ToString("N");
    private System.Windows.Forms.Timer? _timer;

    public void Initialize()
    {
        _initializeThread = Environment.CurrentManagedThreadId;
        Record("initialize", "entered");
        Write(new { status = "waiting_for_ui_timer", initialize_thread = _initializeThread });
        Record("start_ui_timer", "before");
        _timer = new System.Windows.Forms.Timer { Interval = 100 };
        _timer.Tick += OnIdle;
        _timer.Start();
        Record("start_ui_timer", "after");
    }

    public void Terminate()
    {
        Record("terminate", "before");
        StopTimer();
        Record("terminate", "after");
    }

    private void OnIdle(object? sender, EventArgs args)
    {
        if (_running) return;
        _running = true;
        try
        {
            if (++_idleAttempts > 300 || _lifetime.Elapsed > TimeSpan.FromSeconds(90))
                throw new TimeoutException("No idle document became available within the probe budget.");
            Record("ui_timer", "entered");
            if (Environment.CurrentManagedThreadId != _initializeThread)
                throw new InvalidOperationException("Timer did not run on the Initialize thread.");
            // nanoCAD 26 reports NOT_IMPLEMENTED for get_IsQuiescent. Never invoke it.
            var manager = Step("document_manager", () => Application.DocumentManager);
            var doc = Step("active_document", () => manager.MdiActiveDocument);
            if (doc == null) { Record("active_document", "none"); return; }
            var command = Step("command_in_progress", () => doc.CommandInProgress);
            var active = Step("cmdactive", () => Convert.ToInt32(Application.GetSystemVariable("CMDACTIVE")));
            Record("busy_state", "value", new { command, cmdactive = active });
            if (!string.IsNullOrEmpty(command) || active != 0) return;
            var watch = Stopwatch.StartNew();
            string name;
            int count = 0;
            using (Step("read_lock", () => doc.LockDocument(DocumentLockMode.Read, null, null, false)))
            {
                var database = Step("database", () => doc.Database);
                var transactions = Step("transaction_manager", () => database.TransactionManager);
                using (var transaction = Step("start_transaction", () => transactions.StartTransaction()))
                {
                    name = Step("document_name", () => doc.Name);
                    var blockTableId = Step("block_table_id", () => database.BlockTableId);
                    var table = Step("open_block_table", () => (BlockTable)transaction.GetObject(blockTableId, OpenMode.ForRead));
                    var modelId = Step("model_space_id", () => table[BlockTableRecord.ModelSpace]);
                    var space = Step("open_model_space", () => (BlockTableRecord)transaction.GetObject(modelId, OpenMode.ForRead));
                    Record("enumerate_model_space", "before");
                    foreach (ObjectId id in space)
                    {
                        if (!Step("object_is_erased", () => id.IsErased)) count++;
                        if (count >= 1000) break;
                    }
                    Record("enumerate_model_space", "after", new { count });
                    Record("dispose_transaction", "before");
                }
                Record("dispose_transaction", "after");
                Record("dispose_lock", "before");
            }
            Record("dispose_lock", "after");
            Record("stop_ui_timer", "before");
            StopTimer();
            Record("stop_ui_timer", "after");
            Write(new
            {
                status = "passed",
                initialize_thread = _initializeThread,
                idle_thread = Environment.CurrentManagedThreadId,
                dispatcher = "System.Windows.Forms.Timer",
                document_name = name,
                model_space_count_up_to_1000 = count,
                elapsed_ms = watch.ElapsedMilliseconds,
                document_lock = "Read",
                transaction = "ForRead/disposed",
                hostmgd = typeof(Document).Assembly.Location,
                hostdbmgd = typeof(Database).Assembly.Location
            });
            Record("probe", "passed");
        }
        catch (System.Exception error)
        {
            Record(_stage, "failed", new { error = error.ToString() });
            Write(new { status = "failed", stage = _stage, error = error.ToString(), initialize_thread = _initializeThread,
                idle_thread = Environment.CurrentManagedThreadId });
            StopTimer();
        }
        finally { _running = false; }
    }

    private void StopTimer()
    {
        if (_timer == null) return;
        _timer.Stop();
        _timer.Tick -= OnIdle;
        _timer.Dispose();
        _timer = null;
    }

    private T Step<T>(string stage, Func<T> read)
    {
        Record(stage, "before");
        T result = read();
        Record(stage, "after");
        return result;
    }

    private void Record(string stage, string status, object? data = null)
    {
        _stage = stage;
        var directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var line = JsonSerializer.SerializeToUtf8Bytes(new { utc = DateTime.UtcNow.ToString("O"), run = _run,
            pid = Environment.ProcessId, thread = Environment.CurrentManagedThreadId, stage, status, data });
        using var stream = new FileStream(Path.Combine(directory, "probe-trace.jsonl"), FileMode.Append,
            FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
        stream.Write(line);
        stream.WriteByte((byte)'\n');
        stream.Flush(true);
    }

    private void Write(object value)
    {
        var directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var path = Path.Combine(directory, "probe-result.json");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { run = _run, pid = Environment.ProcessId,
            utc = DateTime.UtcNow.ToString("O"), result = value }, new JsonSerializerOptions { WriteIndented = true });
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
        stream.Write(bytes);
        stream.Flush(true);
    }
}
