using System.Diagnostics;
using HostMgd.ApplicationServices;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.Runtime;
using Application = HostMgd.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(NanoCADBridge.Entry))]
[assembly: CommandClass(typeof(NanoCADBridge.Entry))]

namespace NanoCADBridge;

public sealed class Entry : IExtensionApplication
{
    private static Entry? _current;
    private ReadQueue? _queue;
    private WriteQueue? _writeQueue;
    private WriteDispatcher? _writes;
    private BridgeListener? _listener;
    private DocumentRegistry? _documents;
    private CadReader? _reader;
    private JsonLog? _log;
    private NativeAccessibilityGuard? _accessibilityGuard;
    private Descriptor? _descriptor;
    private bool _pumping;
    private long _heartbeat;
    private int _uiThread;
    private volatile bool _uiVerified;
    private volatile bool _ready;
    private System.Windows.Forms.Timer? _uiTimer;

    public void Initialize()
    {
        try
        {
            _uiThread = Environment.CurrentManagedThreadId;
            _current = this;
            _log = new("bridge");
            _accessibilityGuard = new(_log);
            _log.Write("initialize", "", "describe_before");
            var instance = Guid.NewGuid().ToString("D");
            _descriptor = Discovery.Describe(instance, FileVersionInfo.GetVersionInfo(typeof(Document).Assembly.Location).FileVersion ?? "unknown");
            _log.Write("initialize", "", "document_registry_before");
            _documents = new();
            _log.Write("initialize", "", "document_registry_after");
            _queue = new(_uiThread);
            _reader = new(instance, _documents, _log);
            _writeQueue = new(_uiThread);
            _writes = new(_writeQueue, new CadWriter(instance, _documents, _log), new CadDocuments(instance, _documents), new CameraViewWriter(instance, _documents), _log);
            _log.Write("initialize", "", "subscribe_idle_before");
            Application.Idle += OnIdle;
            _log.Write("initialize", "", "subscribe_idle_after");
            // Idle alone does not wake when an IPC request arrives in an inactive
            // host window. A WinForms timer posts on the Initialize/UI thread;
            // it is not a System.Threading.Timer and never invokes CAD on a worker.
            _uiTimer = new System.Windows.Forms.Timer { Interval = 100 };
            _uiTimer.Tick += OnIdle;
            _uiTimer.Start();
            _log.Write("initialize", "", "ui_timer_started");
            _listener = new(_descriptor, HandleAsync, _log);
            _log.Write("initialize", "", "listener_started");
        }
        catch (System.Exception e)
        {
            _log?.Write("initialize", "", e.GetType().Name);
            Terminate();
            throw;
        }
    }
    private Task<Result> HandleAsync(Request request, CancellationToken cancellationToken)
    {
        // Runs on IPC threads: only cached values and DTOs are accessed here.
        var queue = _queue;
        if (queue == null) return Task.FromResult(Result.Fail(request.requestId, new("BRIDGE_UNAVAILABLE", "Bridge is stopping.", true)));
        if (request.operation == "get_status")
        {
            long heartbeat = Interlocked.Read(ref _heartbeat);
            return Task.FromResult(Result.Ok(request.requestId, new
            {
                bridge_instance_id = _descriptor!.bridge_instance_id,
                host_version = _descriptor.host_version,
                bridge_version = _descriptor.bridge_version,
                read_only = false,
                ui_thread_verified = _uiVerified,
                ready_at_last_idle = _ready,
                ui_heartbeat_age_ms = heartbeat == 0 ? (double?)null : (DateTime.UtcNow.Ticks - heartbeat) / (double)TimeSpan.TicksPerMillisecond,
                queued_reads = queue.Count,
                supported_tools = Wire.Operations,
                queued_writes = _writeQueue?.Count,
                operation_ledger_count = _writeQueue?.LedgerCount,
                active_writes = _writeQueue?.ActiveExecutions,
                write_response_wait_ms = WriteQueue.ResponseWaitMilliseconds,
                write_execution_budget_ms = WriteQueue.ExecutionBudgetMilliseconds,
                long_write_policy = "running_work_survives_response_timeout; exact_replay_only; cooperative_execution_deadline",
                write_dispatch_pending = _writes?.Pending,
                write_dispatch_available = _writes?.Available,
                camera_write_available = _writes?.CameraAvailable,
                write_policy = "native_command_transaction",
                write_acceptance = "operation_specific_not_blanket_rollback_guarantee",
                failed_write_undo_history = "may_contain_empty_step",
                detailed_types = new[] { "Line", "Circle", "Arc", "Ellipse", "Spline", "Polyline", "Polyline2d", "Polyline3d", "Point", "Ray", "Xline", "Text", "MText", "Dimension", "MLeader", "Table", "Hatch", "Wipeout", "Solid3d" },
                read_policy = "active_ui_for_cad_reads_with_window_guard; get_status_and_get_operation_result_use_cached_state_without_CAD_UI",
                revision_policy = "session_monotonic_objects_commands_document_activation_system_variables",
                runtime_acceptance = "operation_specific_see_capabilities_and_warnings",
                disabled_write_capabilities = _writes?.Available == false ? new[] { "document_writes", "camera_writes" } : Array.Empty<string>(),
                rollback_not_guaranteed_capabilities = new[] { "create_viewport", "create_associative_hatch", "block_attribute_transforms" },
                geometry_analysis = new
                {
                    types = new[] { "Line", "Circle", "Arc", "Ellipse", "Spline", "Polyline", "Solid3d" },
                    modes = new[] { "metrics", "intersections", "distance", "relations", "containment", "solid_containment", "solid_interference", "solid_distance", "face_distance", "wall_thickness", "cylinder_diameter", "axis_axis_distance", "axis_plane_distance" },
                    tolerance_policy = "native_kernel",
                    overlap_classification = true,
                    relations_scope = "WCS_XY_Z0_or_explicit_workplane_finite_Line_Circle_Arc_Polyline",
                    containment = true,
                    containment_scope = "single_Circle_or_simple_closed_straight_Polyline_at_least_3_vertices_WCS_XY_Z0_or_explicit_workplane",
                    containment_method = "native_bounds_then_brep_face",
                    solid_measurement_scope = "finite trimmed planar straight-edged faces; whole-body modes require a complete planar straight-edged boundary",
                    axis_measurement_scope = "native circular cylinder diameters and infinite cylinder axes or supporting planes; native vector tolerance; no finite face clearance",
                    wall_thickness_scope = "one selected opposed planar material chord at the requested face-interior point; not global minimum thickness"
                },
                export_dxf_source_state = "may_mark_modified_known_issue"
            }));
        }
        if (request.operation == "capture_window")
            return DesktopCaptureBridge.CaptureAsync(request, _descriptor!, queue, cancellationToken);
        if (request.operation == "get_operation_result")
            return _writeQueue?.ReadOperationAsync(request, cancellationToken) ?? Task.FromResult(Result.Fail(request.requestId, new("BRIDGE_UNAVAILABLE", "Bridge is stopping.")));
        if (Wire.WriteOperations.Contains(request.operation))
            return _writeQueue?.SubmitAsync(request, cancellationToken) ?? Task.FromResult(Result.Fail(request.requestId, new("BRIDGE_UNAVAILABLE", "Bridge is stopping.")));
        return queue.SubmitAsync(request, cancellationToken);
    }
    [CommandMethod(WriteDispatcher.CommandName, CommandFlags.Modal | CommandFlags.NoHistory)]
    public void Apply2D()
    {
        var current = _current;
        try
        {
            (current?._log ?? new JsonLog("bridge")).Write("native_command", "",
            current == null ? "command_entry_without_current" : current._writes == null ? "command_entry_without_dispatcher" : "command_entry",
            transaction: WriteDispatcher.CommandName);
        }
        catch (System.Exception) { /* A missing logger must not hide or change command execution. */ }
        current?._writes?.ExecuteCommand(Application.DocumentManager.MdiActiveDocument);
    }
#if HATCH_UNDO_PROBE
    [CommandMethod(HatchUndoProbe.CommandName, CommandFlags.Modal | CommandFlags.NoHistory)]
    public void ProbeHatchUndo() => HatchUndoProbe.Execute(_current?._descriptor,_current?._documents);
#endif
#if BLOCK_POSITION_PROBE
    [CommandMethod(BlockPositionProbe.CommandName, CommandFlags.Modal | CommandFlags.NoHistory)]
    public void ProbeBlockPosition() => BlockPositionProbe.Execute(_current?._descriptor,_current?._documents);
#endif
#if DIMENSION_PLACEMENT_PROBE
    [CommandMethod(DimensionPlacementProbe.CommandName, CommandFlags.Modal | CommandFlags.NoHistory)]
    public void ProbeDimensionPlacement() => DimensionPlacementProbe.Execute(_current?._descriptor,_current?._documents);
#endif
#if CORE_PARAMETER_PROBE
    [CommandMethod(CoreParameterProbe.CommandName, CommandFlags.Modal | CommandFlags.NoHistory)]
    public void ProbeCoreParameter() => CoreParameterProbe.Execute(_current?._descriptor,_current?._documents);
#endif
#if PARAMETRIC_SKETCH_PROBE
    [CommandMethod(ParametricSketchProbe.CommandName, CommandFlags.Modal | CommandFlags.NoHistory)]
    public void ProbeParametricSketch() => ParametricSketchProbe.Execute(_current?._descriptor,_current?._documents);
#endif
#if DOCUMENT_CONSTRAINTS_PROBE
    [CommandMethod(DocumentConstraintsProbe.CommandName, CommandFlags.Modal | CommandFlags.NoHistory)]
    public void ProbeDocumentConstraints() => DocumentConstraintsProbe.Execute(_current?._descriptor,_current?._documents);
#endif
#if DOCUMENT_DRIVEN_MODEL_PROBE
    [CommandMethod(DocumentDrivenModelProbe.CommandName, CommandFlags.Modal | CommandFlags.NoHistory)]
    public void ProbeDocumentDrivenModel() => DocumentDrivenModelProbe.Execute(_current?._descriptor,_current?._documents);
#endif
#if STATE_IMPORT_PROBE
    [CommandMethod(StateImportProbe.CommandName, CommandFlags.Modal | CommandFlags.NoHistory)]
    public void ProbeStateImport() => StateImportProbe.Execute(_current?._descriptor,_current?._documents);
#endif
#if MULTICAD_TRANSACTION_PROBE
    [CommandMethod(ModelTransactionProbe.CommandName, CommandFlags.Modal | CommandFlags.NoHistory)]
    public void ProbeModelTransaction() => ModelTransactionProbe.Execute(_current?._descriptor);
#endif
#if DRAWING_VIEW_FIXTURE
    [CommandMethod(DrawingViewFixture.CommandName, CommandFlags.Modal | CommandFlags.NoHistory)]
    public void SeedDrawingViews() => DrawingViewFixture.Execute(_current?._descriptor);
#endif
#if DRAWING_VIEW_WRITE_PROBE
    [CommandMethod(DrawingViewWriteProbe.CommandName, CommandFlags.Modal | CommandFlags.NoHistory)]
    public void ProbeDrawingViewWrite() => DrawingViewWriteProbe.Execute(_current?._descriptor);
#endif
#if SHEET_METAL_FIXTURE
    [CommandMethod(SheetMetalFixture.CommandName, CommandFlags.Modal | CommandFlags.NoHistory)]
    public void SeedSheetMetal() => SheetMetalFixture.Execute(_current?._descriptor);
#endif
#if POINT_CLOUD_FIXTURE
    [CommandMethod(PointCloudFixture.CommandName, CommandFlags.Modal | CommandFlags.NoHistory)]
    public void SeedPointCloud() => PointCloudFixture.Execute(_current?._descriptor);
#endif
    private void OnIdle(object? sender, EventArgs args)
    {
        if (_pumping || _queue == null || _writes?.DocumentOperationRunning == true) return;
        _pumping = true;
        try
        {
            if (Environment.CurrentManagedThreadId != _uiThread)
            {
                _log?.Write("idle", "", "UI_THREAD_MISMATCH");
                Terminate();
                return;
            }
            _uiVerified = true;
            _accessibilityGuard?.RetryPending();
            Interlocked.Exchange(ref _heartbeat, DateTime.UtcNow.Ticks);
            // Check the native frame before touching document state while a
            // modal dialog is open. Pump still runs so queued reads fail busy.
            if (!HostReadGate.IsWindowAvailable()) _ready = false;
            else
            {
                var document = Application.DocumentManager.MdiActiveDocument;
                _ready = document != null && !HostReadGate.IsBusy(document);
            }
            // Both paths are synchronous on this UI thread. Busy native
            // commands are rejected by the read gate; a queued write must not
            // indefinitely prevent diagnostics/read responses.
#if STATE_IMPORT_PROBE
            if (StateImportProbe.Pending) { _ready = false; return; }
#endif
#if MULTICAD_TRANSACTION_PROBE
            if (ModelTransactionProbe.Pending) { _ready = false; return; }
#endif
#if DRAWING_VIEW_FIXTURE
            if (DrawingViewFixture.Pending) { _ready = false; return; }
#endif
#if DRAWING_VIEW_WRITE_PROBE
            if (DrawingViewWriteProbe.Pending) { _ready = false; return; }
#endif
#if SHEET_METAL_FIXTURE
            if (SheetMetalFixture.Pending) { _ready = false; return; }
#endif
#if POINT_CLOUD_FIXTURE
            if (PointCloudFixture.Pending) { _ready = false; return; }
#endif
#if CORE_PARAMETER_PROBE
            if (CoreParameterProbe.Pending) { _ready = false; return; }
#endif
#if PARAMETRIC_SKETCH_PROBE
            if (ParametricSketchProbe.Pending) { _ready = false; return; }
#endif
#if DOCUMENT_CONSTRAINTS_PROBE
            if (DocumentConstraintsProbe.Pending) { _ready = false; return; }
#endif
#if DOCUMENT_DRIVEN_MODEL_PROBE
            if (DocumentDrivenModelProbe.Pending) { _ready = false; return; }
#endif
#if DIMENSION_PLACEMENT_PROBE
            if (DimensionPlacementProbe.Pending) { _ready = false; return; }
#endif
#if HATCH_UNDO_PROBE
            if (HatchUndoProbe.Pending) { _ready = false; return; }
#endif
#if BLOCK_POSITION_PROBE
            if (BlockPositionProbe.Pending) { _ready = false; return; }
#endif
            _queue.Pump((request, cancellation) => request.operation == WritePreviewRequest.Route
                ? _writes!.Preview(request, cancellation) : _reader!.Execute(request, cancellation));
            _writes?.Pump();
#if HATCH_UNDO_PROBE
            if (_ready && _queue.Count == 0 && _writeQueue?.Count == 0 && _writes?.Pending == false)
                HatchUndoProbe.Pump(_descriptor!,_documents!);
#endif
#if BLOCK_POSITION_PROBE
            if (_ready && _queue.Count == 0 && _writeQueue?.Count == 0 && _writes?.Pending == false)
                BlockPositionProbe.Pump(_descriptor!,_documents!);
#endif
#if DIMENSION_PLACEMENT_PROBE
            if (_ready && _queue.Count == 0 && _writeQueue?.Count == 0 && _writes?.Pending == false)
                DimensionPlacementProbe.Pump(_descriptor!,_documents!);
#endif
#if CORE_PARAMETER_PROBE
            if (_ready && _queue.Count == 0 && _writeQueue?.Count == 0 && _writes?.Pending == false)
                CoreParameterProbe.Pump(_descriptor!,_documents!);
#endif
#if PARAMETRIC_SKETCH_PROBE
            if (_ready && _queue.Count == 0 && _writeQueue?.Count == 0 && _writes?.Pending == false)
                ParametricSketchProbe.Pump(_descriptor!,_documents!);
#endif
#if DOCUMENT_CONSTRAINTS_PROBE
            if (_ready && _queue.Count == 0 && _writeQueue?.Count == 0 && _writes?.Pending == false)
                DocumentConstraintsProbe.Pump(_descriptor!,_documents!);
#endif
#if DOCUMENT_DRIVEN_MODEL_PROBE
            if (_ready && _queue.Count == 0 && _writeQueue?.Count == 0 && _writes?.Pending == false)
                DocumentDrivenModelProbe.Pump(_descriptor!,_documents!);
#endif
#if SOLID_TOPOLOGY_PROBE
            if (_ready && _queue.Count == 0 && _writeQueue?.Count == 0 && _writes?.Pending == false)
                SolidTopologyProbe.Pump(_descriptor!,_documents!);
#endif
#if STATE_IMPORT_PROBE
            if (_ready && _queue.Count == 0 && _writeQueue?.Count == 0 && _writes?.Pending == false)
                StateImportProbe.Pump(_descriptor!,_documents!);
#endif
#if MULTICAD_TRANSACTION_PROBE
            if (_ready && _queue.Count == 0 && _writeQueue?.Count == 0 && _writes?.Pending == false)
                ModelTransactionProbe.Pump(_descriptor!);
#endif
#if DRAWING_VIEW_FIXTURE
            if (_ready && _queue.Count == 0 && _writeQueue?.Count == 0 && _writes?.Pending == false)
                DrawingViewFixture.Pump(_descriptor!);
#endif
#if DRAWING_VIEW_WRITE_PROBE
            if (_ready && _queue.Count == 0 && _writeQueue?.Count == 0 && _writes?.Pending == false)
                DrawingViewWriteProbe.Pump(_descriptor!);
#endif
#if SHEET_METAL_FIXTURE
            if (_ready && _queue.Count == 0 && _writeQueue?.Count == 0 && _writes?.Pending == false)
                SheetMetalFixture.Pump(_descriptor!);
#endif
#if POINT_CLOUD_FIXTURE
            if (_ready && _queue.Count == 0 && _writeQueue?.Count == 0 && _writes?.Pending == false)
                PointCloudFixture.Pump(_descriptor!);
#endif
        }
        catch (System.Exception e) { _ready = false; _log?.Write("idle", "", e.GetType().Name); }
        finally { _pumping = false; }
    }
    public void Terminate()
    {
        if (_uiTimer != null)
        {
            _uiTimer.Stop();
            _uiTimer.Tick -= OnIdle;
            _uiTimer.Dispose();
            _uiTimer = null;
        }
        Application.Idle -= OnIdle;
        _listener?.Dispose(); _listener = null;
        _writes?.Dispose(); _writes = null;
        _writeQueue?.Dispose(); _writeQueue = null;
        _queue?.Dispose(); _queue = null;
        _documents?.Dispose(); _documents = null;
        _reader = null;
        _accessibilityGuard?.Dispose(); _accessibilityGuard = null;
        if (_current == this) _current = null;
    }
}


