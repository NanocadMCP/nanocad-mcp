using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Multicad.DatabaseServices;
using Teigha.Geometry;
using Application = HostMgd.ApplicationServices.Application;
using NativePoint = Multicad.Geometry.Point3d;

namespace NanoCADBridge;

// Internal read-only IPC endpoint. The bridge runs in nanoCAD's interactive
// desktop, while the MCP server may run in an isolated window station.
internal static class DesktopCaptureBridge
{
    // The IPC listener never accepts this operation. Only capture_window queues it.
    internal const string ViewportTargetRead = "internal_capture_viewport_target";
    private sealed record ViewportTarget(IntPtr Window, int Width, int Height, int ViewportNumber,
        string NativeDocument, string DocumentId, string Revision, RegionProjection? Region);
    private sealed record RegionProjection(CapturePixelRect Crop, (double, double) Origin, (double, double) X,
        (double, double) Y, (double, double) Far);

    internal static Result ReadViewportTarget(Request request, DocumentRegistry documents)
    {
        var args = VisualCaptureBridgeArguments.Parse(request.arguments);
        var document = Application.DocumentManager.MdiActiveDocument
            ?? throw new DomainException("NO_DOCUMENT", "No active drawing is open.");
        if (HostReadGate.IsBusy(document))
            throw new DomainException("HOST_BUSY", "The active drawing is busy.", true);

        var state = documents.Get(document);
        string revision = state.Revision;
        // MAPI IMcsNativeView::getViewHWND is the window where the native drawing is rendered.
        // Compare its client size to the active viewport's SCREENSIZE in the WGC worker.
        var sheet = McDocumentsManager.GetActiveSheet();
        var view = sheet.ActiveView;
        object number = Application.GetSystemVariable("CVPORT");
        int viewport = number switch { int n => n, short n => n, _ => 0 };
        if (viewport <= 0 || view.Handle == IntPtr.Zero || view.DocID.IsNull ||
            Application.GetSystemVariable("SCREENSIZE") is not Point2d size ||
            !double.IsFinite(size.X) || !double.IsFinite(size.Y) || size.X < 1 || size.Y < 1 || size.X > int.MaxValue || size.Y > int.MaxValue ||
            size.X != Math.Truncate(size.X) || size.Y != Math.Truncate(size.Y))
            throw new DomainException("CAPTURE_VIEWPORT_UNAVAILABLE", "Native drawing window or viewport pixel dimensions are unavailable.");

        RegionProjection? region = null;
        int pixelWidth = (int)size.X, pixelHeight = (int)size.Y;
        if (args.Bounds is { } bounds)
        {
            if (!document.Database.TileMode || (int)document.Database.Insunits != 4)
                throw new DomainException("CAPTURE_BOUNDS_UNAVAILABLE", "WCS bounds capture requires Model space with millimetre drawing units.");
            var camera = CameraViewReader.Read(args.BridgeId, state).view;
            if (camera == null)
                throw new DomainException("CAPTURE_BOUNDS_UNAVAILABLE", "WCS bounds capture requires an unclipped orthographic Model view.");
            if (camera.camera_token != args.CameraToken)
                throw new DomainException("CAMERA_CONTEXT_CHANGED", "The camera changed before the native bounds projection.", true);
            // The native window DC may use logical pixels; WGC uses physical pixels.
            if (!view.MatrWorldToDC(out var matrix))
                throw new DomainException("CAPTURE_BOUNDS_UNAVAILABLE", "The native renderer did not supply its orthographic pixel projection.");
            var mapping = DesktopViewportPixels.Read(view.Handle, pixelWidth, pixelHeight);
            pixelWidth = mapping.Width; pixelHeight = mapping.Height;
            (double, double) Project(double x, double y)
            {
                var pixel = new NativePoint(x, y, bounds.Z).TransformBy(matrix);
                return (pixel.X * mapping.Scale, pixel.Y * mapping.Scale);
            }
            var origin = Project(bounds.MinX, bounds.MinY);
            var x = Project(bounds.MaxX, bounds.MinY);
            var y = Project(bounds.MinX, bounds.MaxY);
            var far = Project(bounds.MaxX, bounds.MaxY);
            region = new(CapturePixelRect.Enclose(origin, x, y, far, pixelWidth, pixelHeight), origin, x, y, far);
        }
        var target = new ViewportTarget(view.Handle, pixelWidth, pixelHeight, viewport,
            view.DocID.ToString(), state.Id, revision, region);
        if (!ReferenceEquals(Application.DocumentManager.MdiActiveDocument, document) || state.Revision != revision)
            throw new DomainException("CAPTURE_CONTEXT_CHANGED", "The document changed during viewport observation.", true);
        return Result.Ok(request.requestId, target);
    }

    private static async Task<ViewportTarget> GetViewportTarget(ReadQueue queue, JsonElement arguments, CancellationToken cancellation)
    {
        var result = await queue.SubmitAsync(new Request(Wire.Version, Guid.NewGuid().ToString("D"),
            ViewportTargetRead, 2000, arguments), cancellation);
        if (result.error is { } error)
            throw new DomainException(error.code, error.message, error.retryable, error.details);
        return result.data as ViewportTarget
            ?? throw new DomainException("CAPTURE_VIEWPORT_UNAVAILABLE", "The native viewport target was not returned.");
    }

    private static IntPtr MainWindow()
    {
        using var process=Process.GetCurrentProcess();
        IntPtr handle=process.MainWindowHandle;
        GetWindowThreadProcessId(handle,out uint owner);
        if(handle==IntPtr.Zero||!IsWindow(handle)||owner!=(uint)process.Id||!IsWindowVisible(handle)||IsIconic(handle))
            throw new DomainException("CAPTURE_WINDOW_UNAVAILABLE","The nanoCAD main window is unavailable in its desktop session.");
        if(!IsWindowEnabled(handle))throw new DomainException("HOST_BUSY","Close the modal dialog before requesting a document image.",true);
        return handle;
    }

    internal static async Task<Result> CaptureAsync(Request request,Descriptor descriptor,ReadQueue queue,CancellationToken cancellation)
    {
        var args=VisualCaptureBridgeArguments.Parse(request.arguments);
        if(args.BridgeId!=descriptor.bridge_instance_id)throw new DomainException("BRIDGE_UNAVAILABLE","Capture targets another bridge lifetime.");
        var window=MainWindow();
        var viewport = args.RequiresViewport ? await GetViewportTarget(queue, request.arguments, cancellation) : null;
        var crop = viewport?.Region?.Crop;
        string root=LocalSecurity.SecureDirectory(Path.Combine(Discovery.Root,"visual-captures"));
        string directory=Path.Combine(root,args.InlineImage ? Guid.NewGuid().ToString("N") : args.CaptureId);
        if(args.InlineImage)LocalSecurity.SecureDirectory(directory);
        try
        {
        if(!Directory.Exists(directory))throw new DomainException("CAPTURE_UNAVAILABLE","The server did not prepare this capture token.");
        LocalSecurity.SecureDirectory(directory);
        string framePath=Path.Combine(directory,"frame.png");
        if(File.Exists(framePath))throw new DomainException("CAPTURE_UNAVAILABLE","This capture token already has an image.");
        string assembly=typeof(Entry).Assembly.Location;
        string worker=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(assembly)!,"..","server","NanoCAD.VisualWorker.exe"));
        if(!File.Exists(worker))throw new DomainException("CAPTURE_UNAVAILABLE","The packaged desktop visual worker is unavailable.");
        var start=new ProcessStartInfo(worker){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=directory,
            RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var value in new[]{"--capture",descriptor.process_id.ToString(CultureInfo.InvariantCulture),
            descriptor.process_start_utc_ticks.ToString(CultureInfo.InvariantCulture),window.ToInt64().ToString(CultureInfo.InvariantCulture),
            framePath,"--max-edge",args.MaxEdge.ToString(CultureInfo.InvariantCulture)})start.ArgumentList.Add(value);
        if (viewport != null)
            foreach (var value in new[] { "--viewport", viewport.Window.ToInt64().ToString(CultureInfo.InvariantCulture),
                viewport.Width.ToString(CultureInfo.InvariantCulture), viewport.Height.ToString(CultureInfo.InvariantCulture) })
                start.ArgumentList.Add(value);
        if (crop != null)
        {
            start.ArgumentList.Add("--region");
            foreach (var edge in new[] { crop.left, crop.top, crop.right, crop.bottom })
                start.ArgumentList.Add(edge.ToString(CultureInfo.InvariantCulture));
        }
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        string output;
        Process child;
        try{child=Process.Start(start)??throw new DomainException("CAPTURE_UNAVAILABLE","Desktop visual worker could not start.");}
        catch(System.ComponentModel.Win32Exception){throw new DomainException("CAPTURE_UNAVAILABLE","Desktop visual worker could not start.");}
        using(child)
        {
            var stdout=child.StandardOutput.ReadToEndAsync();
            var stderr=child.StandardError.ReadToEndAsync();
            try
            {
                await child.WaitForExitAsync(deadline.Token);
                output=await stdout;_ = await stderr;
                if (child.ExitCode == 4 && viewport != null)
                {
                    string? reason = null;
                    try
                    {
                        using var failure = JsonDocument.Parse(output);
                        if (failure.RootElement.TryGetProperty("reason", out var value) && value.ValueKind == JsonValueKind.String)
                            reason = value.GetString();
                    }
                    catch (JsonException) { }
                    throw new DomainException(crop == null ? "CAPTURE_VIEWPORT_UNAVAILABLE" : "CAPTURE_BOUNDS_UNAVAILABLE",
                        "The active viewport could not be matched to the captured window.", false, new { reason });
                }
                if(child.ExitCode!=0||output.Length>8192)
                    throw new DomainException("CAPTURE_FAILED","Windows did not provide a usable window image.");
            }
            catch(OperationCanceledException) when(!cancellation.IsCancellationRequested)
            {throw new DomainException("CAPTURE_TIMEOUT","Desktop capture deadline expired; only its visual worker was stopped.",true);}
            finally {if(!child.HasExited){child.Kill();await child.WaitForExitAsync(CancellationToken.None);}}
        }
        JsonDocument parsed;
        try{parsed=JsonDocument.Parse(output);}
        catch(JsonException){throw new DomainException("CAPTURE_FAILED","Desktop visual worker returned invalid metadata.");}
        using(parsed)
        {
        var frame=parsed.RootElement;
        if(!frame.GetProperty("captured").GetBoolean()||frame.GetProperty("pid").GetInt32()!=descriptor.process_id)
            throw new DomainException("CAPTURE_FAILED","Desktop worker identity or result differs.");
        if (viewport != null)
        {
            int sourceWidth = crop == null ? viewport.Width : crop.right - crop.left;
            int sourceHeight = crop == null ? viewport.Height : crop.bottom - crop.top;
            if (!frame.TryGetProperty("viewport_only", out var applied) || applied.ValueKind != JsonValueKind.True ||
                frame.GetProperty("source_width").GetInt32() != sourceWidth ||
                frame.GetProperty("source_height").GetInt32() != sourceHeight)
                throw new DomainException("CAPTURE_VIEWPORT_UNAVAILABLE", "The visual worker did not confirm the native viewport crop.");
            if (crop != null && (!frame.TryGetProperty("crop_pixels", out var appliedCrop) || CapturePixelRect.Read(appliedCrop) != crop))
                throw new DomainException("CAPTURE_BOUNDS_UNAVAILABLE", "The visual worker did not confirm the requested bounds crop.");
            if (await GetViewportTarget(queue, request.arguments, cancellation) != viewport)
                throw new DomainException("CAPTURE_CONTEXT_CHANGED", "The active native viewport changed during capture.", true);
        }
        var info=new FileInfo(framePath);
        if(!info.Exists||info.Length>600*1024||(info.Attributes&FileAttributes.ReparsePoint)!=0)
            throw new DomainException("CAPTURE_IMAGE_LIMIT","Desktop PNG exceeds600KiB; request a smaller max_edge.");
        if(MainWindow()!=window)throw new DomainException("CAPTURE_WINDOW_UNAVAILABLE","The nanoCAD main window changed during capture.");
        return Result.Ok(request.requestId,new{
            capture_id=args.CaptureId,process_id=descriptor.process_id,
            viewport_only=viewport!=null, capture_document_id=viewport?.DocumentId, capture_revision=viewport?.Revision,
            capture_bounds=args.Bounds?.ToWire(), crop_pixels=crop, viewport_width=viewport?.Width, viewport_height=viewport?.Height,
            png_base64=args.InlineImage ? Convert.ToBase64String(await File.ReadAllBytesAsync(framePath,cancellation)) : null,
            width=frame.GetProperty("width").GetInt32(),height=frame.GetProperty("height").GetInt32(),
            source_width=frame.GetProperty("source_width").GetInt32(),source_height=frame.GetProperty("source_height").GetInt32(),
            frame_system_relative_ticks=frame.GetProperty("frame_system_relative_ticks").GetString(),
            capture_started_system_relative_ticks=frame.GetProperty("capture_started_system_relative_ticks").GetString()
        });
        }
        }
        finally
        {
            if(args.InlineImage)
                try { File.Delete(Path.Combine(directory,"frame.png")); Directory.Delete(directory); }
                catch(Exception e) when(e is IOException or UnauthorizedAccessException) { }
        }
    }

    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool IsIconic(IntPtr window);
}
