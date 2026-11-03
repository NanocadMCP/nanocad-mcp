using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using HostMgd.ApplicationServices;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;
using Application = HostMgd.ApplicationServices.Application;
#if WRITE_PROBE_CANCEL_FAILURE
using ProbeEntry = NanoCADBridge.WriteProbeCancelFailure.Entry;
#elif WRITE_PROBE_TRANSACTION_MARKER
using ProbeEntry = NanoCADBridge.WriteProbeTransactionMarker.Entry;
#elif WRITE_PROBE_FAILURE_NO_MARKER
using ProbeEntry = NanoCADBridge.WriteProbeFailureNoMarker.Entry;
#elif WRITE_PROBE_COMMAND_NO_LOCK
using ProbeEntry = NanoCADBridge.WriteProbeCommandNoLock.Entry;
#elif WRITE_PROBE_COMMAND_CONTEXT
using ProbeEntry = NanoCADBridge.WriteProbeCommandContext.Entry;
#elif WRITE_PROBE_DISTINCT_COMMAND
using ProbeEntry = NanoCADBridge.WriteProbeLock2.Entry;
#else
using ProbeEntry = NanoCADBridge.WriteProbe.Entry;
#endif

[assembly: ExtensionApplication(typeof(ProbeEntry))]
[assembly: CommandClass(typeof(ProbeEntry))]

#if WRITE_PROBE_CANCEL_FAILURE
namespace NanoCADBridge.WriteProbeCancelFailure;
#elif WRITE_PROBE_TRANSACTION_MARKER
namespace NanoCADBridge.WriteProbeTransactionMarker;
#elif WRITE_PROBE_FAILURE_NO_MARKER
namespace NanoCADBridge.WriteProbeFailureNoMarker;
#elif WRITE_PROBE_COMMAND_NO_LOCK
namespace NanoCADBridge.WriteProbeCommandNoLock;
#elif WRITE_PROBE_COMMAND_CONTEXT
namespace NanoCADBridge.WriteProbeCommandContext;
#elif WRITE_PROBE_DISTINCT_COMMAND
namespace NanoCADBridge.WriteProbeLock2;
#else
namespace NanoCADBridge.WriteProbe;
#endif

// Opt-in native experiment, not an MCP write capability. Policy selects either
// a modeless callback or a native command. Fixed scratch path, one prior Line, no save/close/Undo,
// no external commands, and no retries after the first write attempt.
public sealed class Entry : IExtensionApplication
{
#if WRITE_PROBE_LOCK_ONLY
#if WRITE_PROBE_CANCEL_FAILURE
    private const string CommandName = "NCMCP_WRITEPROBE_CMD5";
#elif WRITE_PROBE_TRANSACTION_MARKER
    private const string CommandName = "NCMCP_WRITEPROBE_CMD4";
#elif WRITE_PROBE_FAILURE_NO_MARKER
    private const string CommandName = "NCMCP_WRITEPROBE_CMD3";
#elif WRITE_PROBE_COMMAND_NO_LOCK
    private const string CommandName = "NCMCP_WRITEPROBE_CMD2";
#elif WRITE_PROBE_COMMAND_CONTEXT
    private const string CommandName = "NCMCP_WRITEPROBE_CMD";
#elif WRITE_PROBE_DISTINCT_COMMAND
    private const string CommandName = "NCMCP_WRITEPROBE_LOCK2";
#else
    private const string CommandName = "NCMCP_WRITEPROBE_LOCK";
#endif
#if WRITE_PROBE_CANCEL_FAILURE
    private const string UndoPolicy = "native_command_context_transaction_failure_as_user_break";
#elif WRITE_PROBE_TRANSACTION_MARKER
    private const string UndoPolicy = "native_no_marker_command_start_undo_record_inside_transaction";
#elif WRITE_PROBE_FAILURE_NO_MARKER
    private const string UndoPolicy = "native_command_context_transaction_no_explicit_lock_failure_no_marker";
#elif WRITE_PROBE_COMMAND_NO_LOCK
    private const string UndoPolicy = "native_command_context_transaction_no_explicit_lock";
#elif WRITE_PROBE_COMMAND_CONTEXT
    private const string UndoPolicy = "native_command_context_write_lock_transaction";
#else
    private const string UndoPolicy = "write_lock_and_transaction_only";
#endif
#else
    private const string CommandName = "NCMCP_WRITEPROBE";
    private const string UndoPolicy = "write_lock_start_undo_record_transaction";
#endif
    private const string Fixture = @"C:\NanoCAD MCP\tests\fixtures\phase2-write-probe.dxf";
    private static Entry? _current;
    private readonly HostWindowState _window = new(Environment.ProcessId);
    private readonly string _run = Guid.NewGuid().ToString("D");
    private readonly List<object> _observations = new();
    private readonly List<string> _committed = new(), _rolledBack = new();
    private System.Windows.Forms.Timer? _timer;
    private Stopwatch? _wait;
    private int _uiThread;
    private bool _running, _attempted;
    private string _stage = "loaded";
    private Frame? _baseline, _afterCommit, _afterRollback;
    private Document? _targetDocument;
#if WRITE_PROBE_COMMAND_CONTEXT
    private ObjectId _probeLayer, _probeStyle;
    private bool _failureAttempted;
#endif

    public void Initialize()
    {
        _current = this; _uiThread = Environment.CurrentManagedThreadId;
        Write("loaded_waiting_for_" + CommandName);
    }
    public void Terminate()
    {
        Stop();
#if WRITE_PROBE_CANCEL_FAILURE
        ObserveCommands(false);
#endif
        if (_current == this) _current = null;
    }

#if WRITE_PROBE_COMMAND_CONTEXT
#if WRITE_PROBE_TRANSACTION_MARKER
    [CommandMethod(CommandName, CommandFlags.Modal | CommandFlags.NoHistory | CommandFlags.NoUndoMarker)]
#else
    [CommandMethod(CommandName, CommandFlags.Modal | CommandFlags.NoHistory)]
#endif
    public void CreateCommand() => _current?.CreateInCommandContext();

#if WRITE_PROBE_TRANSACTION_MARKER
    // Same flags and undo-record placement for success and failure; only the
    // injected exception before Commit differs in this experiment.
    [CommandMethod(CommandName + "FAIL", CommandFlags.Modal | CommandFlags.NoHistory | CommandFlags.NoUndoMarker)]
#elif WRITE_PROBE_FAILURE_NO_MARKER
    // Diagnostic only: this command is guaranteed to roll back. A general write
    // command cannot choose its flags based on an outcome not yet known.
    [CommandMethod(CommandName + "FAIL", CommandFlags.Modal | CommandFlags.NoHistory | CommandFlags.NoUndoMarker)]
#else
    [CommandMethod(CommandName + "FAIL", CommandFlags.Modal | CommandFlags.NoHistory)]
#endif
    public void FailureCommand() => _current?.FailInCommandContext();

    private Document CommandDocument()
    {
        if (Environment.CurrentManagedThreadId != _uiThread || !_window.Capture().available)
            throw new InvalidOperationException("Command UI context is unavailable.");
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc == null || !string.Equals(Path.GetFullPath(doc.Name), Fixture, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Activate the dedicated scratch drawing.");
        return doc;
    }
    private void CreateInCommandContext()
    {
        if (_attempted) { Trace("repeat_refused"); return; }
        try
        {
            var doc = CommandDocument(); _targetDocument = doc;
#if WRITE_PROBE_CANCEL_FAILURE
            ObserveCommands(true);
#endif
            Run(doc, deferRollback: true);
        }
        catch (System.Exception error) { Write("command_create_failed", error.ToString()); throw; }
    }
    private void FailInCommandContext()
    {
        if (_failureAttempted) { Trace("repeat_failure_refused"); return; }
        try
        {
            var doc = CommandDocument();
            if (!_attempted || !ReferenceEquals(doc, _targetDocument) || _afterCommit == null)
                throw new InvalidOperationException("Run the create probe in this document first.");
            if (JsonSerializer.Serialize(ReadFrame(doc).entities) != JsonSerializer.Serialize(_afterCommit.entities))
                throw new InvalidOperationException("Scratch geometry changed after the create command.");
            _failureAttempted = true;
            RunRollback(doc, _probeLayer, _probeStyle);
            Write("completed_awaiting_native_undo_redo");
        }
        catch (System.Exception error) { Write("command_failure_probe_failed", error.ToString()); throw; }
        // Let the native command framework observe a managed command failure
        // after transaction rollback and verification have finished.
#if WRITE_PROBE_CANCEL_FAILURE
        Trace("throw_native_user_break_after_verified_rollback");
        throw new Teigha.Runtime.Exception(ErrorStatus.UserBreak);
#else
        throw new InvalidOperationException("Intentional rollback probe: no additional objects retained.");
#endif
    }
#if WRITE_PROBE_CANCEL_FAILURE
    private bool _observingCommands;
    private void ObserveCommands(bool enable)
    {
        if (_targetDocument == null || _observingCommands == enable) return;
        if (enable)
        {
            _targetDocument.CommandEnded += CommandEnded;
            _targetDocument.CommandCancelled += CommandCancelled;
            _targetDocument.CommandFailed += CommandFailed;
        }
        else
        {
            _targetDocument.CommandEnded -= CommandEnded;
            _targetDocument.CommandCancelled -= CommandCancelled;
            _targetDocument.CommandFailed -= CommandFailed;
        }
        _observingCommands = enable;
    }
    private void CommandEnded(object? sender, CommandEventArgs args) => ObserveOutcome("ended", args);
    private void CommandCancelled(object? sender, CommandEventArgs args) => ObserveOutcome("cancelled", args);
    private void CommandFailed(object? sender, CommandEventArgs args) => ObserveOutcome("failed", args);
    private void ObserveOutcome(string outcome, CommandEventArgs args)
    {
        // Only file logging: do not read/write CAD state from lifecycle callbacks.
        if (!string.Equals(args.GlobalCommandName, CommandName, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(args.GlobalCommandName, CommandName + "FAIL", StringComparison.OrdinalIgnoreCase)) return;
        try { Trace("host_command_" + outcome + "_" + args.GlobalCommandName); }
        catch (System.Exception error) { Debug.WriteLine("Probe lifecycle log failed: " + error); }
    }
#endif
#else
    [CommandMethod(CommandName, CommandFlags.NoUndoMarker | CommandFlags.NoHistory)]
    public void ArmCommand() => _current?.Arm();
#endif

    private void Arm()
    {
        if (_attempted) { Trace("repeat_refused"); return; }
        Stop();
        var target = Application.DocumentManager.MdiActiveDocument;
        if (target == null || !string.Equals(Path.GetFullPath(target.Name), Fixture, StringComparison.OrdinalIgnoreCase))
        {
            Write("rejected_before_write", "Activate the dedicated phase2-write-probe.dxf before running the command.");
            return;
        }
        _targetDocument = target;
        _wait = Stopwatch.StartNew(); Write("waiting_for_idle_scratch_document");
        _timer = new System.Windows.Forms.Timer { Interval = 100 };
        _timer.Tick += Tick; _timer.Start();
    }
    private void Stop()
    {
        if (_timer == null) return;
        _timer.Stop(); _timer.Tick -= Tick; _timer.Dispose(); _timer = null;
    }
    private void Tick(object? sender, EventArgs args)
    {
        if (_running) return;
        _running = true;
        try
        {
            if (Environment.CurrentManagedThreadId != _uiThread) throw new InvalidOperationException("UI thread mismatch.");
            if (_wait!.Elapsed > TimeSpan.FromMinutes(3)) { Stop(); Write("timeout_no_write"); return; }
            if (!_window.Capture().available) return;
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null || !ReferenceEquals(doc, _targetDocument) ||
                !string.Equals(Path.GetFullPath(doc.Name), Fixture, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The armed scratch document is no longer active; no write was started.");
            if (!string.IsNullOrEmpty(doc.CommandInProgress) || Convert.ToInt32(Application.GetSystemVariable("CMDACTIVE")) != 0) return;
            Stop(); Run(doc); Write("completed_awaiting_native_undo_redo");
        }
        catch (System.Exception error) { Stop(); Write(_attempted ? "failed_after_write_attempt" : "rejected_before_write", error.ToString()); }
        finally { _running = false; }
    }

    private void Run(Document doc, bool deferRollback = false)
    {
        if (doc.IsReadOnly) throw new InvalidOperationException("Scratch document is read-only.");
        var db = doc.Database;
        if ((int)db.Insunits != 4) throw new InvalidOperationException("Scratch drawing must use millimeters.");
        _baseline = ReadFrame(doc);
        if (_baseline.entities.Length != 1 || _baseline.entities[0].type != "Line")
            throw new InvalidOperationException("Scratch drawing must contain exactly one prior Line for Undo isolation.");
        ObjectId layer, style;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (db.CurrentSpaceId != bt[BlockTableRecord.ModelSpace]) throw new InvalidOperationException("Model Space must be active.");
            var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            layer = layers["0"];
            var layerRecord = (LayerTableRecord)tr.GetObject(layer, OpenMode.ForRead);
            if (layerRecord.IsLocked || layerRecord.IsFrozen || layerRecord.IsOff) throw new InvalidOperationException("Layer 0 must be available.");
            var styles = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            style = styles["Standard"];
        }
#if WRITE_PROBE_COMMAND_CONTEXT
        _probeLayer = layer; _probeStyle = style;
#endif
        _attempted = true;
        Write("write_attempt_started");
        ExecuteBatch(doc, layer, style, rollback: false);
        _afterCommit = ReadFrame(doc); Write("commit_observed");
        if (_afterCommit.entities.Length != 6 || !_committed.All(h => _afterCommit.entities.Any(e => e.handle == h)))
            throw new InvalidOperationException("Commit did not yield exactly five new entities.");
        if (deferRollback) { Write("commit_observed_awaiting_failure_command"); return; }
        RunRollback(doc, layer, style);
    }

    private void RunRollback(Document doc, ObjectId layer, ObjectId style)
    {
        ExecuteBatch(doc, layer, style, rollback: true);
        _afterRollback = ReadFrame(doc);
        if (JsonSerializer.Serialize(_afterCommit!.entities) != JsonSerializer.Serialize(_afterRollback.entities))
            throw new InvalidOperationException("Rollback changed the committed geometry.");
        if (_rolledBack.Any(h => _afterRollback.entities.Any(e => e.handle == h)))
            throw new InvalidOperationException("A rolled-back entity remains in Model Space.");
        Trace("rollback_geometry_verified");
    }

    private void ExecuteBatch(Document doc, ObjectId layer, ObjectId style, bool rollback)
    {
        string prefix = rollback ? "rollback" : "commit";
        var db = doc.Database;
        var entities = new List<Entity>();
        var pendingHandles = new List<string>();
        try
        {
            if (rollback)
            {
                entities.Add(new Line(new Point3d(100, 0, 0), new Point3d(110, 0, 0)));
                entities.Add(new Circle(new Point3d(120, 0, 0), Vector3d.ZAxis, 2));
            }
            else
            {
                entities.Add(new Line(new Point3d(0, 0, 0), new Point3d(20, 0, 0)));
                entities.Add(new Circle(new Point3d(30, 10, 0), Vector3d.ZAxis, 5));
                entities.Add(new Arc(new Point3d(50, 10, 0), Vector3d.ZAxis, 5, 0, Math.PI / 2));
                var pl = new Polyline(4); entities.Add(pl);
                pl.AddVertexAt(0, new Point2d(0, 20), 0, 0, 0);
                pl.AddVertexAt(1, new Point2d(20, 20), 0.25, 0, 0);
                pl.AddVertexAt(2, new Point2d(20, 35), 0, 0, 0);
                pl.AddVertexAt(3, new Point2d(0, 35), 0, 0, 0);
                pl.Closed = true;
                var text = new DBText(); entities.Add(text);
                text.TextString = "MCP Phase 2: test"; text.Position = new Point3d(0, 45, 0);
                text.Height = 2.5; text.TextStyleId = style; text.Normal = Vector3d.ZAxis;
            }
            foreach (var entity in entities) entity.LayerId = layer;
            using (AcquireWriteLock(doc, prefix))
            {
#if WRITE_PROBE_TRANSACTION_MARKER
                Trace(prefix + "_undo_record_deferred_until_transaction");
#elif WRITE_PROBE_LOCK_ONLY
                Trace(prefix + "_no_explicit_undo_record");
#else
                Trace(prefix + "_start_undo_record_before"); db.StartUndoRecord(); Trace(prefix + "_start_undo_record_after");
#endif
                Trace(prefix + "_transaction_before");
                try
                {
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        Trace(prefix + "_transaction_after");
#if WRITE_PROBE_TRANSACTION_MARKER
                        // Test whether Abort also discards this record boundary.
                        // Do not assume that transaction rollback owns Undo state.
                        Trace(prefix + "_transaction_start_undo_record_before");
                        db.StartUndoRecord();
                        Trace(prefix + "_transaction_start_undo_record_after");
#endif
                        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        var model = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                        for (int i = 0; i < entities.Count; i++)
                        {
                            Trace(prefix + "_append_" + i + "_before");
                            model.AppendEntity(entities[i]); tr.AddNewlyCreatedDBObject(entities[i], true);
                            pendingHandles.Add(entities[i].Handle.ToString());
                            Trace(prefix + "_append_" + i + "_after");
                        }
                        if (rollback) { Trace("injected_failure_before_commit"); throw new InjectedFailure(); }
                        Trace("commit_before"); tr.Commit();
                        _committed.AddRange(pendingHandles);
                        Trace("commit_after");
                    }
                }
                catch (InjectedFailure) when (rollback)
                {
                    _rolledBack.AddRange(pendingHandles);
                    Trace("rollback_transaction_disposed_without_commit");
                }
                Trace(prefix + "_transaction_disposed");
            }
#if WRITE_PROBE_COMMAND_NO_LOCK
            Trace(prefix + "_native_command_transaction_disposed");
#else
            Trace(prefix + "_write_lock_disposed");
#endif
        }
        finally { foreach (var entity in entities) entity.Dispose(); }
    }

    private DocumentLock? AcquireWriteLock(Document doc, string prefix)
    {
#if WRITE_PROBE_COMMAND_NO_LOCK
#if !WRITE_PROBE_COMMAND_CONTEXT
#error Skipping explicit document locking requires native command context.
#endif
        // The modal command owns the active document context. Follow the SDK
        // command example: do not take an additional modeless DocumentLock.
        if (!ReferenceEquals(doc, CommandDocument()))
            throw new InvalidOperationException("Native command document changed before write.");
        Trace(prefix + "_native_command_no_explicit_write_lock");
        return null;
#else
        Trace(prefix + "_write_lock_before");
        var docLock = doc.LockDocument(DocumentLockMode.Write, null, null, false);
        try { Trace(prefix + "_write_lock_after"); return docLock; }
        catch { docLock.Dispose(); throw; }
#endif
    }

    private sealed class InjectedFailure : System.Exception { }
    private sealed record Row(string handle, string type, object geometry);
    private sealed record Frame(int dbmod, Row[] entities);
    private static double[] Point(Point3d p) => new[] { p.X, p.Y, p.Z };
    private static Frame ReadFrame(Document doc)
    {
        var rows = new List<Row>(); var db = doc.Database;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var model = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            foreach (ObjectId id in model)
            {
                if (id.IsErased || id.IsEffectivelyErased) continue;
                if (rows.Count >= 20) throw new InvalidOperationException("Unexpected scratch contents.");
                var e = (Entity)tr.GetObject(id, OpenMode.ForRead);
                object geometry = e switch
                {
                    Line line => new { start = Point(line.StartPoint), end = Point(line.EndPoint) },
                    Circle circle => new { center = Point(circle.Center), radius = circle.Radius },
                    Arc arc => new { center = Point(arc.Center), radius = arc.Radius, start_angle = arc.StartAngle, end_angle = arc.EndAngle },
                    Polyline pl => new { closed = pl.Closed, vertices = Enumerable.Range(0, pl.NumberOfVertices).Select(i => new { point = Point(pl.GetPoint3dAt(i)), bulge = pl.GetBulgeAt(i) }).ToArray() },
                    DBText text => new { text = text.TextString, position = Point(text.Position), height = text.Height },
                    _ => throw new InvalidOperationException("Unexpected scratch entity type.")
                };
                rows.Add(new(e.Handle.ToString(), e is DBText ? "Text" : e.GetType().Name, geometry));
            }
        }
        return new(Convert.ToInt32(Application.GetSystemVariable("DBMOD")), rows.OrderBy(r => r.handle, StringComparer.Ordinal).ToArray());
    }

    private static string OutputDirectory => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
    private void Trace(string stage)
    {
        _stage = stage;
        var entry = new { utc = DateTime.UtcNow.ToString("O"), run = _run, pid = Environment.ProcessId,
            thread = Environment.CurrentManagedThreadId, stage };
        _observations.Add(entry);
        using var stream = new FileStream(Path.Combine(OutputDirectory, "write-probe-trace.jsonl"), FileMode.Append, FileAccess.Write, FileShare.Read);
        if (stream.Length > 1024 * 1024) throw new InvalidOperationException("Trace limit reached.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(entry); stream.Write(bytes); stream.WriteByte((byte)'\n'); stream.Flush(true);
    }
    private void Write(string status, string? error = null)
    {
        var result = new { utc = DateTime.UtcNow.ToString("O"), run = _run, pid = Environment.ProcessId,
            status, stage = _stage, error, fixture = Fixture, command = CommandName, undo_policy = UndoPolicy,
            baseline = _baseline, after_commit = _afterCommit,
            after_rollback = _afterRollback, committed_handles = _committed, rolled_back_handles = _rolledBack,
            observations = _observations, scope = "Fixed scratch document; opt-in transaction experiment with explicit undo_policy; no save, close, command dispatch or automatic Undo" };
        File.WriteAllBytes(Path.Combine(OutputDirectory, "write-probe-result.json"), JsonSerializer.SerializeToUtf8Bytes(result, new JsonSerializerOptions { WriteIndented = true }));
    }
}
