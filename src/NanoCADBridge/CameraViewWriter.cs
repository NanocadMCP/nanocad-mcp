using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.Geometry;
using Teigha.DatabaseServices;
using Multicad.ApplicationServices;
using Multicad.DatabaseServices;
using Application = HostMgd.ApplicationServices.Application;

namespace NanoCADBridge;

// Camera-only UI work uses the existing write ledger, without emitting a CAD
// command, opening database objects ForWrite or creating a graphics device.
internal sealed class CameraViewWriter
{
    private readonly string _bridge;
    private readonly DocumentRegistry _documents;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    // VisualStyleId can belong to the host's view database, not the drawing.
    // Retain the exact native ID inside the bounded document/lifetime snapshot;
    // never reconstruct it by looking up its handle in the drawing database.
    private sealed record Snapshot(CameraView View, ObjectId Space, ObjectId Style, Multicad.McObjectId SheetId,
        Multicad.McObjectId ViewDocumentId, Multicad.McObjectId VisualStyle, ViewRenderModesEnum Mode,
        NativeCameraRenderer370.Snapshot? Renderer);
    private readonly Dictionary<string, (string Document, Snapshot Camera)> _saved = new(StringComparer.Ordinal);
    internal bool Available => true;
    internal CameraViewWriter(string bridge, DocumentRegistry documents) { _bridge = bridge; _documents = documents; }
    private CameraView Read(DocumentState state) => CameraViewReader.Read(_bridge, state).view ?? throw new DomainException("VIEW_UNAVAILABLE", "Use an unclipped orthographic Model view or the Paper sheet itself; an entered Paper model viewport requires its viewport adapter.");
    private ReadContext Context(DocumentState state) => new(_bridge, state.Id, state.Revision, DateTime.UtcNow.ToString("O"));
    private static bool Matches(CameraView expected, CameraView actual)
    {
        if (CameraChangeValidation.SameState(expected, actual)) return true;
        var n = new Vector3d(expected.direction_wcs[0], expected.direction_wcs[1], expected.direction_wcs[2]);
        var basis = Matrix3d.PlaneToWorld(n);
        var x = Vector3d.XAxis.TransformBy(basis); var y = Vector3d.YAxis.TransformBy(basis);
        return CameraChangeValidation.SameUntwistedOrthographicProjection(expected, actual, new(x.X, x.Y, x.Z), new(y.X, y.Y, y.Z));
    }
    private static bool DirectionMatches(CameraView expected, CameraView actual)
    {
        if (actual.projection != "orthographic" || actual.front_clip_enabled || actual.back_clip_enabled) return false;
        var a = new Vector3d(expected.direction_wcs[0], expected.direction_wcs[1], expected.direction_wcs[2]).GetNormal();
        var b = new Vector3d(actual.direction_wcs[0], actual.direction_wcs[1], actual.direction_wcs[2]).GetNormal();
        return a.DotProduct(b) > 1 - 1e-6;
    }
    private DocumentState Preflight(CameraChangeArguments a)
    {
        if (Environment.CurrentManagedThreadId != _thread) throw new DomainException("UI_THREAD_MISMATCH", "Camera access requires the host UI thread.");
        if (a.BridgeId != _bridge) throw new DomainException("BRIDGE_UNAVAILABLE", "Camera request targets another bridge lifetime.");
        if (!HostReadGate.IsWindowAvailable()) throw new DomainException("HOST_BUSY", "Close the modal dialog before changing the view.", true);
        var document = Application.DocumentManager.MdiActiveDocument ?? throw new DomainException("NO_DOCUMENT", "No active document.");
        if (HostReadGate.IsBusy(document)) throw new DomainException("HOST_BUSY", "Finish the active command before changing the view.", true);
        var state = _documents.Get(document);
        if (state.Id != a.DocumentId) throw new DomainException(_documents.IsOpen(a.DocumentId) ? "DOCUMENT_NOT_ACTIVE" : "DOCUMENT_CLOSED", "The requested document is not active.");
        if (state.Revision != a.ExpectedRevision) throw new DomainException("STALE_DOCUMENT", "Read the current document before changing the view.", true);
        return state;
    }
    private Snapshot Capture(DocumentState state)
    {
        var view = Read(state);
        using var native = state.Document.Editor.GetCurrentView();
        if (!native.ObjectId.IsNull || !native.IsNewObject || !native.AutoDelete)
            throw new DomainException("VIEW_UNAVAILABLE", "Expected an owned camera snapshot.");
        var sheet = McDocumentsManager.GetActiveSheet(); var active = sheet.ActiveView;
        if (active.VisualStyle.IsNull) throw new DomainException("VIEW_STYLE_UNAVAILABLE", "The original native visual style cannot be retained for restoration.");
        var renderer = state.Document.Database.TileMode && (int)state.Document.Database.Insunits == 4 ? NativeCameraRenderer370.Capture(active) : null;
        return new(view, state.Document.Database.CurrentSpaceId, native.VisualStyleId, sheet.ID, active.DocID, active.VisualStyle, active.ViewRenderMode, renderer);
    }
    private static void RequireScope(DocumentState state, Snapshot snapshot)
    {
        if (Application.DocumentManager.MdiActiveDocument != state.Document || state.Document.Database.CurrentSpaceId != snapshot.Space)
            throw new DomainException("CAMERA_CONTEXT_CHANGED", "Camera snapshot belongs to a different active document/native space.");
        object number = Application.GetSystemVariable("CVPORT");
        int viewport = number switch { int n => n, short n => n, _ => -1 };
        var id = state.Document.Editor.CurrentViewportObjectId;
        if (viewport != snapshot.View.viewport_number || (id.IsNull ? null : id.Handle.ToString()) != snapshot.View.viewport_handle)
            throw new DomainException("CAMERA_CONTEXT_CHANGED", "The native viewport changed while applying the camera.");
        var sheet = McDocumentsManager.GetActiveSheet();
        if (sheet.ID != snapshot.SheetId || sheet.ActiveView.DocID != snapshot.ViewDocumentId || snapshot.Renderer is { } renderer &&
            sheet.ActiveView.Handle.ToInt64().ToString("X") != renderer.State.view_window_handle)
            throw new DomainException("CAMERA_CONTEXT_CHANGED", "The native sheet/view identity changed while applying the camera.");
        if (snapshot.Renderer is { } rendererScope) NativeCameraRenderer370.RequireBinding(sheet.ActiveView, rendererScope);
    }
    private static bool Apply(DocumentState state, Snapshot snapshot, out NativeCameraRenderer370.Snapshot? rendererAfter, Action? beforeSet = null)
    {
        rendererAfter = null; var desired = snapshot.View;
        RequireScope(state, snapshot);
        using var native = state.Document.Editor.GetCurrentView();
        if (!native.ObjectId.IsNull || !native.IsNewObject || !native.AutoDelete || native.PerspectiveEnabled || native.FrontClipEnabled || native.BackClipEnabled)
            throw new DomainException("VIEW_UNAVAILABLE", "Camera record identity or projection changed.");
        native.Target = new Point3d(desired.target_wcs[0], desired.target_wcs[1], desired.target_wcs[2]);
        native.ViewDirection = new Vector3d(desired.direction_wcs[0], desired.direction_wcs[1], desired.direction_wcs[2]);
        native.CenterPoint = new Point2d(desired.center_view[0], desired.center_view[1]);
        native.Width = desired.width; native.Height = desired.height; native.ViewTwist = desired.twist_radians;
        native.FrontClipDistance = desired.front_clip_distance; native.BackClipDistance = desired.back_clip_distance;
        if (!Enum.IsDefined(typeof(ViewRenderModesEnum), snapshot.Mode))
            throw new DomainException("VIEW_STYLE_UNAVAILABLE", "The recorded render mode cannot be restored.");
        if (native.VisualStyleId != snapshot.Style) native.VisualStyleId = snapshot.Style;
        beforeSet?.Invoke();
        state.Document.Editor.SetCurrentView(native);
        RequireScope(state, snapshot);
        var active = McDocumentsManager.GetActiveSheet().ActiveView;
        if (active.ViewRenderMode != snapshot.Mode) active.ViewRenderMode = snapshot.Mode;
        // A requested shading mode picks its native style. A stored snapshot
        // instead restores the exact original style, including custom settings.
        if (!snapshot.VisualStyle.IsNull && active.VisualStyle != snapshot.VisualStyle) active.VisualStyle = snapshot.VisualStyle;
        if (snapshot.Renderer is { } renderer) NativeCameraRenderer370.Apply(active, renderer, () => RequireScope(state, snapshot));
        // SetCurrentView can update camera metadata while an opened DWG keeps
        // an unpainted graphics surface. Ask the host to regenerate the actual
        // viewport before capture_view observes pixels.
        state.Document.Editor.Regen();
        RequireScope(state, snapshot);
        // The editor view record can be current while the native sheet keeps
        // its previous graphics. Request rendering on that exact existing
        // native view; no window activation, input, new device or delay.
        active = McDocumentsManager.GetActiveSheet().ActiveView;
        active.Regenerate(false);
        bool refreshed = active.Update();
        if (snapshot.Renderer is { } expectedRenderer) rendererAfter = NativeCameraRenderer370.Verify(active, expectedRenderer);
        RequireScope(state, snapshot);
        active = McDocumentsManager.GetActiveSheet().ActiveView;
        if (active.ViewRenderMode != snapshot.Mode || !snapshot.VisualStyle.IsNull && active.VisualStyle != snapshot.VisualStyle)
            throw new DomainException("VIEW_STYLE_UNAVAILABLE", "The native display style did not retain the requested state.");
        using var applied = state.Document.Editor.GetCurrentView();
        if (!applied.ObjectId.IsNull || !applied.IsNewObject || !applied.AutoDelete)
            throw new DomainException("VIEW_UNAVAILABLE", "The applied camera record identity changed.");
        return refreshed;
    }
    internal (ReadContext Context, object Plan) Preview(CameraChangeArguments args, Action check)
    {
        check();
        var state = Preflight(args);
        var before = Capture(state);
        var desired = Plan(args, state, before, check, out var warning);
        return (Context(state), new { view = desired.View, warning });
    }

    private Snapshot Plan(CameraChangeArguments args, DocumentState state, Snapshot before, Action check, out string? framingWarning)
    {
        if (before.View.camera_token != args.ExpectedCameraToken) throw new DomainException("CAMERA_CONTEXT_CHANGED", "The camera changed since it was inspected.", true);
        Snapshot desired; framingWarning = null;
        if (args.Action == "restore")
        {
            if (!_saved.TryGetValue(args.RestoreToken!, out var saved) || saved.Document != state.Id || saved.Camera.Space != before.Space || saved.Camera.View.viewport_number != before.View.viewport_number || saved.Camera.View.viewport_handle != before.View.viewport_handle)
                throw new DomainException("CAMERA_SNAPSHOT_UNAVAILABLE", "The restore token does not belong to this document, viewport and bridge lifetime.");
            desired = saved.Camera;
            if (Math.Abs(desired.View.width / desired.View.height - before.View.width / before.View.height) > 1e-10 * Math.Max(1, before.View.width / before.View.height))
                throw new DomainException("CAMERA_VIEWPORT_CHANGED", "The viewport aspect ratio changed since this snapshot; restoration was not started.");
        }
        else
        {
            if (!state.Document.Database.TileMode && (args.Preset != "top" || args.DisplayStyle != "preserve"))
                throw new DomainException("INVALID_ARGUMENT", "Observe the Paper sheet using preset top with its existing display style; 3D directions/shading belong to Model.");
            desired = before with { View = before.View with { direction_wcs = CameraChangeValidation.Direction(args.Preset!), twist_radians = 0 } };
            if (args.FrameHandles != null)
            {
                try { desired = desired with { View = CameraObjectFraming.Frame(state, desired.View, args.FrameHandles, args.FramePadding, check) }; }
                catch (DomainException e) when (e.Error.code == "FRAME_BOUNDS_UNAVAILABLE")
                { framingWarning = "FRAMING_BOUNDS_UNAVAILABLE: Preset applied without object framing; inspect the rendered view and reframe after bounds become available."; }
            }
            if (args.DisplayStyle == "shaded_edges") desired = desired with { View = desired.View with { render_mode = nameof(ViewRenderModesEnum.FaceNEdgeFine) }, Mode = ViewRenderModesEnum.FaceNEdgeFine, VisualStyle = Multicad.McObjectId.Null };
            if (args.DisplayStyle == "wireframe_2d") desired = desired with { View = desired.View with { render_mode = nameof(ViewRenderModesEnum.Optimized2d) }, Mode = ViewRenderModesEnum.Optimized2d, VisualStyle = Multicad.McObjectId.Null };
            if (before.Renderer is { } renderer) desired = desired with { Renderer = NativeCameraRenderer370.Plan(renderer, desired.View) };
        }
        return desired;
    }

    internal Result Execute(WriteJob job)
    {
        DocumentState? state = null; Snapshot? before = null; string? restore = null; bool started = false;
        try
        {
            var args = (CameraChangeArguments)job.Arguments;
            job.CheckCancellation(); state = Preflight(args); before = Capture(state);
            var desired = Plan(args, state, before, job.CheckCancellation, out var framingWarning);
            job.CheckCancellation(); if (!job.TryStart()) return job.Error("CANCELLED", "Camera operation was cancelled before execution.", "not_started");
            restore = Guid.NewGuid().ToString("D"); _saved.Add(restore, (state.Id, before));
            bool refreshed = Apply(state, desired, out var rendererReadback, () => { job.CheckCancellation(); job.BeginCommit(); started = true; });
            var after = Read(state);
            var expected = args.Action == "preset" && args.DisplayStyle != "preserve" ? desired.View with { visual_style_handle = after.visual_style_handle } : desired.View;
            RequireScope(state, desired);
            if (expected.viewport_number != after.viewport_number || expected.viewport_handle != after.viewport_handle || expected.render_mode != after.render_mode || expected.visual_style_handle != after.visual_style_handle)
                throw new DomainException("CAMERA_VERIFY_FAILED", "Camera viewport identity or display style differs from the requested state.");
            bool exact = Matches(expected, after);
            if (!exact && (args.Action != "preset" || !DirectionMatches(expected, after)))
                throw new DomainException("CAMERA_VERIFY_FAILED", "The native view does not match the requested projection.", details: new { desired = expected, actual = after });
#if CAMERA_CHANGE_FAILURE_PROBE
            CameraChangeFailureProbe.Observe(_bridge,state,args);
#endif
            var warnings = new List<string>();
            if (!refreshed) warnings.Add("CAMERA_REFRESH_UNCONFIRMED: Camera metadata changed, but the native view did not confirm its update. Verify the requested contents in an MCP image.");
            if (args.FrameHandles != null) warnings.Add(framingWarning ?? "DISPLAY_FRAMING_BOUNDS: Native axis-aligned bounds are used only to frame the image; they are not exact profile/volume or visibility measurements. Inspect the rendered result.");
            if (!exact) warnings.Add("CAMERA_READBACK_DIFFERENCE: The host retained the requested direction but normalized other view metadata. Inspect an MCP image before judging framing.");
            if (desired.Renderer == null) warnings.Add("CAMERA_NATIVE_RENDERER_UNOBSERVED: Editor camera changed; direct native renderer synchronization is scoped to Millimeters Model. Verify actual contents in an MCP image.");
            var rendererAfter = rendererReadback?.State;
            if (rendererAfter?.requested_projection_matches == false) warnings.Add("CAMERA_NATIVE_RENDERER_READBACK_DIFFERENCE: The actual native view normalized the requested centre/direction/pixel scale. Actual renderer data is retained; inspect the grouped image before judging framing.");
            string cameraScope = state.Document.Database.TileMode ? "orthographic_model_camera" : "orthographic_paper_camera";
            var result = Result.Ok(job.OriginalRequestId, new CameraChangeResult(args.OperationId, "committed", false, before.View, after, restore,
                args.FrameHandles == null || framingWarning != null ? cameraScope + "_only" : cameraScope + "_with_selected_object_framing", false, before.Renderer?.State, rendererAfter,
                rendererAfter == null ? "editor_view_only; native_renderer_unobserved" : "existing_native_view_camera_applied_and_read; projection_comparison_reported; pixels_not_observed"), Context(state), warnings.ToArray());
            job.RecordCommit(result); return result;
        }
        catch (Exception error)
        {
            if (job.CommittedResult is { } committed) return committed;
            string code = error is DomainException domain ? domain.Error.code : error is OperationCanceledException ? "CANCELLED" : "CAMERA_CHANGE_FAILED";
            var cause = error is DomainException d ? new { message = d.Error.message, details = d.Error.details } : new { message = error.Message, details = (object?)null };
            if (!started) { if (restore != null) _saved.Remove(restore); return job.Error(code, "Camera change was refused before applying a view.", "not_started", state == null ? null : Context(state), cause); }
            try
            {
                if (state == null || before == null || Application.DocumentManager.MdiActiveDocument != state.Document) throw new InvalidOperationException("Cannot restore camera in a changed document.");
                Apply(state, before, out var restoredRenderer);
                if (before.Renderer != null && restoredRenderer?.State.requested_projection_matches != true) throw new InvalidOperationException("Native renderer restoration differs; no verified rollback claim.");
                if (!Matches(before.View, Read(state))) throw new InvalidOperationException("Camera restoration differs.");
                if (restore != null) _saved.Remove(restore);
                return job.Error(code, "Camera change failed; the previous camera state was restored and verified.", "rolled_back", Context(state), cause);
            }
            catch (Exception rollback)
            {
                return job.Error("WRITE_OUTCOME_UNKNOWN", "Camera restoration could not be verified. Read the current camera before another view change; this operation remains unknown.", "unknown", state == null ? null : Context(state), new { error = cause, rollback = rollback.Message });
            }
        }
    }
}
