using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;
using Application = HostMgd.ApplicationServices.Application;

namespace NanoCADBridge;

internal static class OffscreenDrawingCapture
{
    internal const string Operation = "internal_capture_drawing_layout";
    static string Ticks() => (Stopwatch.GetTimestamp() * (TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency))
        .ToString("0", CultureInfo.InvariantCulture);
    static string? HandleOf(ObjectId id) => id.IsNull ? null : id.Handle.ToString();

    internal static Result Read(Request request, string instance, DocumentRegistry documents, CancellationToken cancellation)
    {
        var args = VisualCaptureBridgeArguments.Parse(request.arguments);
        if (!args.NativeGraphics || args.BridgeId != instance)
            throw new DomainException("BRIDGE_UNAVAILABLE", "The capture source or running instance differs from the request.");
        var document = Application.DocumentManager.MdiActiveDocument ?? throw new DomainException("NO_DOCUMENT", "No drawing is active.");
        if (HostReadGate.IsBusy(document)) throw new DomainException("HOST_BUSY", "The active drawing is busy.", true);
        var state = documents.Get(document);
        var revision = state.Revision;
        var camera = CameraViewReader.Read(instance, state);
        var view = camera.view;
        if (view == null || view.camera_token != args.CameraToken)
            throw new DomainException("CAMERA_CONTEXT_CHANGED", "The active camera differs from the requested view.", true);
        bool fromFile = args.SelectedSource == VisualCaptureBridgeArguments.SavedDrawing;
        if (fromFile && !document.IsNamedDrawing)
            throw new DomainException("SAVED_DRAWING_UNAVAILABLE", "This drawing has no saved DWG. Use current_drawing to observe it.");

        var db = document.Database;
        int Dirty() => Convert.ToInt32(Application.GetSystemVariable("DBMOD"));
        string? Layout() => Application.GetSystemVariable("CTAB")?.ToString();
        string SourceStamp() => JsonSerializer.Serialize(new
        {
            db = db.UnmanagedObject.ToInt64(),
            working = HostApplicationServices.WorkingDatabase?.UnmanagedObject.ToInt64(),
            document.Name,
            space = HandleOf(db.CurrentSpaceId),
            layout = Layout(),
            viewport = Application.GetSystemVariable("CVPORT"),
            db.FingerprintGuid,
            db.VersionGuid,
            db.Tduupdate,
            handseed = db.Handseed.ToString(),
            modified = Dirty(),
            db.Insunits,
            db.Measurement,
            tablestyle = HandleOf(db.Tablestyle),
            mleaderstyle = HandleOf(db.MLeaderstyle),
            annotation_scale = db.Cannoscale?.Name,
            db.LayerEval,
            db.LayerNotify
        });

        try
        {
            using var saved = fromFile ? SavedDwgInput.Acquire(document.Name,
                LocalSecurity.SecureDirectory(Path.Combine(Discovery.Root, "drawing-inputs")), cancellation) : null;
            using var events = new DrawingCaptureEvents(db);
            var stamp = SourceStamp();
            if (!fromFile)
            {
                var protectedDimension = DrawingClonePreflight.Find(db, cancellation);
                if (document != Application.DocumentManager.MdiActiveDocument || revision != state.Revision ||
                    events.HeaderConflict != null || stamp != SourceStamp() ||
                    view.camera_token != CameraViewReader.Read(instance, state).view?.camera_token)
                    throw new DomainException("CAPTURE_CONTEXT_CHANGED", "The active drawing changed during the pre-clone check.", true,
                        new { clone_started = false, revision_before = revision, revision_after = state.Revision, events.FirstSourceChange, events.FirstSourceHandle });
                if (protectedDimension != null) throw protectedDimension.Refusal();
            }
            var started = Ticks();
            var png = DrawingLayoutRenderer.Read(document, saved, args.MaxEdge, view.render_mode, camera.render_camera, events, cancellation, out var references);
            var finished = Ticks();
            if (png.Length > 600 * 1024)
                throw new DomainException("CAPTURE_IMAGE_LIMIT", "The native bitmap exceeds the inline PNG budget. Request a smaller max_edge.");
            cancellation.ThrowIfCancellationRequested();
            saved?.VerifySavedBasis(cancellation);
            var finalView = CameraViewReader.Read(instance, state).view;
            bool sameDocument = document == Application.DocumentManager.MdiActiveDocument;
            bool sameStamp = sameDocument && stamp == SourceStamp();
            var observedRevision = state.Revision;
            bool unchanged = sameDocument && sameStamp && revision == observedRevision && finalView?.camera_token == view.camera_token;
            if (!events.Finish(unchanged))
            {
                bool sameCamera = finalView != null &&
                    JsonSerializer.Serialize(finalView with { camera_token = "" }) == JsonSerializer.Serialize(view with { camera_token = "" });
                throw new DomainException("CAPTURE_CONTEXT_CHANGED", "The source drawing, camera or observed database changed during rendering.", true,
                    new
                    {
                        same_document = sameDocument,
                        same_drawing_state = sameStamp,
                        same_camera_geometry = sameCamera,
                        revision_before = revision,
                        revision_observed = observedRevision,
                        revision_after = state.Revision,
                        source_object_change = events.FirstSourceChange,
                        source_object_handle = events.FirstSourceHandle,
                        event_attribution_failure = events.HeaderConflict
                    });
            }
            var observation = new
            {
                source_kind = args.SelectedSource,
                renderer = "native_layout_bitmap",
                document_id = state.Id,
                drawing_path = document.IsNamedDrawing ? document.Name : null,
                revision,
                layout = Layout(),
                space = HandleOf(db.CurrentSpaceId),
                camera = view,
                render_camera = camera.render_camera,
                camera_source = camera.render_camera == null ? "editor_view" : "native_model_view",
                dirty_flags = Dirty(),
                rendered_bytes_sha256 = saved?.Sha256,
                rendered_bytes_length = saved?.Length,
                palette = "native_dark_palette",
                referenced_definitions = references
            };
            int width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));
            int height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));
            return Result.Ok(request.requestId, new
            {
                capture_id = args.CaptureId,
                process_id = Environment.ProcessId,
                source = args.SelectedSource,
                offscreen_observation = observation,
                viewport_only = true,
                capture_document_id = state.Id,
                capture_revision = revision,
                capture_camera_token = view.camera_token,
                width,
                height,
                source_width = width,
                source_height = height,
                capture_started_system_relative_ticks = started,
                frame_system_relative_ticks = finished,
                png_base64 = Convert.ToBase64String(png)
            });
        }
        catch (IOException e)
        {
            throw new DomainException("SAVED_DRAWING_UNAVAILABLE", "The saved input could not be read or changed while rendering.", true,
                new { io_error = e.HResult & 0xffff });
        }
        catch (UnauthorizedAccessException)
        {
            throw new DomainException("SAVED_DRAWING_UNAVAILABLE", "Access to the saved DWG or private drawing input directory was denied.");
        }
        catch (InvalidOperationException e)
        {
            throw new DomainException("DRAWING_RENDER_UNAVAILABLE", e.Message);
        }
        catch (Teigha.Runtime.Exception e)
        {
            throw new DomainException("DRAWING_RENDER_UNAVAILABLE", "The native layout renderer could not form this drawing view.",
                false, new { native_status = e.ErrorStatus.ToString() });
        }
    }
}
