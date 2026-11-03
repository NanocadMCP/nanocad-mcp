using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.Ipc;

namespace NanoCAD.McpServer;

public static class VisualCaptureService
{
    public const int MaxPngBytes=600*1024;
    private static readonly SemaphoreSlim Gate=new(1,1);
    // Only a hint about observation quality. Identical pixels after a camera
    // change cannot prove that the CAD view has rendered the new state.
    private sealed record CaptureFingerprint(string Bridge,string Document,string Camera,string Sha256);
    private static CaptureFingerprint? _lastCapture;
    public static void RequireContext(VisualCaptureArguments args,JsonElement data)
    {
        if(data.GetProperty("document_id").GetString()!=args.DocumentId)throw new DomainException("DOCUMENT_NOT_ACTIVE","The requested document is not active; capture was refused.");
        if(data.GetProperty("revision").GetString()!=args.Revision)throw new DomainException("REVISION_MISMATCH","Read the current document revision before requesting an image.");
        if(args.CameraToken!=null && (!data.TryGetProperty("view",out var view) || view.ValueKind!=JsonValueKind.Object ||
            !view.TryGetProperty("camera_token",out var token) || token.GetString()!=args.CameraToken))
            throw new DomainException("CAMERA_CONTEXT_CHANGED","The requested camera context is no longer current or cannot be verified.",true);
    }
    public static void RequireUnchanged(JsonElement before,JsonElement after)
    {
        if(!JsonElement.DeepEquals(before,after))throw new DomainException("CAPTURE_CONTEXT_CHANGED","The document context changed during capture; the image was discarded.",true);
    }
    public static void RequirePng(byte[] png,int width,int height,int maxEdge)
    {
        ReadOnlySpan<byte> signature=[137,80,78,71,13,10,26,10];
        if(png.Length<33||png.Length>MaxPngBytes||!png.AsSpan(0,8).SequenceEqual(signature)||!png.AsSpan(12,4).SequenceEqual("IHDR"u8)||
            BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16,4))!=width||BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20,4))!=height||
            width<1||height<1||width>maxEdge||height>maxEdge)
            throw new DomainException("CAPTURE_IMAGE_LIMIT","PNG is invalid or exceeds the image budget; request a smaller max_edge.");
    }
    public static byte[] DecodeImage(JsonElement remote,int width,int height,int maxEdge)
    {
        if(!remote.TryGetProperty("png_base64",out var value)||value.ValueKind!=JsonValueKind.String)
            throw new DomainException("CAPTURE_UNAVAILABLE","The loaded bridge does not provide inline images. Update the bridge and restart nanoCAD after saving drawings.");
        string encoded=value.GetString()!;
        if(encoded.Length>((MaxPngBytes+2)/3)*4)throw new DomainException("CAPTURE_IMAGE_LIMIT","Encoded image exceeds the capture budget.");
        byte[] png;
        try{png=Convert.FromBase64String(encoded);}
        catch(FormatException){throw new DomainException("CAPTURE_FAILED","The bridge returned an invalid image encoding.");}
        RequirePng(png,width,height,maxEdge);
        return png;
    }
    private static IntPtr Target(Descriptor descriptor,IntPtr expected=default)
    {
        using var p=Process.GetProcessById(descriptor.process_id);
        if(p.HasExited||p.StartTime.ToUniversalTime().Ticks!=descriptor.process_start_utc_ticks||p.SessionId!=descriptor.session_id||
            !string.Equals(p.ProcessName,"nCad",StringComparison.OrdinalIgnoreCase))throw new DomainException("BRIDGE_UNAVAILABLE","nanoCAD process identity changed.");
        var handle=p.MainWindowHandle;
        GetWindowThreadProcessId(handle,out uint owner);
        if(handle==IntPtr.Zero||!IsWindow(handle)||owner!=(uint)descriptor.process_id||expected!=IntPtr.Zero&&handle!=expected||!IsWindowVisible(handle)||IsIconic(handle))
            throw new DomainException("CAPTURE_WINDOW_UNAVAILABLE","The expected nanoCAD main window is unavailable.");
        if(!IsWindowEnabled(handle))throw new DomainException("HOST_BUSY","Close the modal dialog before requesting a document image.",true);
        return handle;
    }
    public static async Task<(Result Result,byte[] Image)> CaptureAsync(BridgeClient client,string id,JsonElement input,CancellationToken cancellation)
    {
        var args=VisualCaptureSchema.Parse(input);
        if(!await Gate.WaitAsync(0,cancellation))throw new DomainException("CAPTURE_IN_PROGRESS","Another image capture is in progress.",true);
        string captureId=Guid.NewGuid().ToString("N");
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellation);deadline.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            var descriptor=Discovery.Choose(args.BridgeId);
            async Task<Result> Context()
            {
                var response=await client.CallAsync(descriptor,new Request(Wire.Version,Guid.NewGuid().ToString("D"),"get_document",2000,Wire.Element(new{bridge_instance_id=args.BridgeId,include_view=true})),deadline.Token);
                if(!response.success)throw new DomainException(response.error!.code,response.error.message,response.error.retryable,response.error.details);
                return response;
            }
            var before=await Context();var beforeData=Wire.Element(before.data!);RequireContext(args,beforeData);
            IntPtr window=IntPtr.Zero;
            try{window=Target(descriptor);}
            catch(DomainException e) when(e.Error.code=="CAPTURE_WINDOW_UNAVAILABLE")
            {
                // An isolated MCP process can share the bridge's Windows
                // session and token while still having a different desktop.
                // The bridge can launch the same WGC worker in nanoCAD's own
                // desktop; it never sends input or CAD commands.
            }
            string started=DateTime.UtcNow.ToString("O");
            JsonElement frame;
            // An MCP server may discover the host HWND while its own desktop
            // still cannot render the CAD graphics surface. The bridge lives
            // in nanoCAD's desktop, so use its read-only WGC worker in both
            // cases. Keep the server-side window identity check when possible.
            var response=await client.CallAsync(descriptor,new Request(Wire.Version,Guid.NewGuid().ToString("D"),"capture_window",9000,
                Wire.Element(new{bridge_instance_id=args.BridgeId,capture_id=captureId,max_edge=args.MaxEdge,inline_image=true})),deadline.Token);
            if(!response.success)throw new DomainException(response.error!.code,response.error.message,response.error.retryable,response.error.details);
            var remote=Wire.Element(response.data!);
            if(remote.GetProperty("capture_id").GetString()!=captureId||remote.GetProperty("process_id").GetInt32()!=descriptor.process_id)
                throw new DomainException("CAPTURE_FAILED","Bridge capture token or process identity differs.");
            frame=Wire.Element(new{captured=true,pid=descriptor.process_id,
                width=remote.GetProperty("width").GetInt32(),height=remote.GetProperty("height").GetInt32(),
                source_width=remote.GetProperty("source_width").GetInt32(),source_height=remote.GetProperty("source_height").GetInt32(),
                frame_system_relative_ticks=remote.GetProperty("frame_system_relative_ticks").GetString(),
                capture_started_system_relative_ticks=remote.GetProperty("capture_started_system_relative_ticks").GetString()});
            if(!frame.GetProperty("captured").GetBoolean()||frame.GetProperty("pid").GetInt32()!=descriptor.process_id)throw new DomainException("CAPTURE_FAILED","Visual worker identity or result differs.");
            int width=frame.GetProperty("width").GetInt32(),height=frame.GetProperty("height").GetInt32();
            string timestamp=frame.GetProperty("frame_system_relative_ticks").GetString()!,captureStart=frame.GetProperty("capture_started_system_relative_ticks").GetString()!;
            if(!long.TryParse(timestamp,out long frameTicks)||!long.TryParse(captureStart,out long startedTicks)||frameTicks<startedTicks)throw new DomainException("CAPTURE_STALE_FRAME","Frame predates this capture session.");
            byte[] png=DecodeImage(remote,width,height,args.MaxEdge);
            if(window!=IntPtr.Zero)Target(descriptor,window);
            var after=await Context();var afterData=Wire.Element(after.data!);RequireUnchanged(beforeData,afterData);
            if(window!=IntPtr.Zero)Target(descriptor,window);
            bool cameraVerified=afterData.TryGetProperty("view",out var camera) && camera.ValueKind==JsonValueKind.Object;
            string digest=Convert.ToHexString(SHA256.HashData(png));
            string cameraToken=cameraVerified&&camera.TryGetProperty("camera_token",out var token)&&token.ValueKind==JsonValueKind.String
                ?token.GetString()??"": "";
            var previous=_lastCapture;
            bool identicalAfterCameraChange=previous!=null&&previous.Bridge==args.BridgeId&&previous.Document==args.DocumentId&&
                previous.Camera.Length>0&&cameraToken.Length>0&&previous.Camera!=cameraToken&&previous.Sha256==digest;
            _lastCapture=new(args.BridgeId,args.DocumentId,cameraToken,digest);
            var data=new {frame_id=id,source="windows_graphics_capture",mime_type="image/png",width,height,
                source_width=frame.GetProperty("source_width").GetInt32(),source_height=frame.GetProperty("source_height").GetInt32(),
                png_sha256=digest,process_id=descriptor.process_id,process_start_utc_ticks=descriptor.process_start_utc_ticks.ToString(CultureInfo.InvariantCulture),
                started_at_utc=started,completed_at_utc=DateTime.UtcNow.ToString("O"),frame_system_relative_ticks=timestamp,capture_started_system_relative_ticks=captureStart,
                document=afterData,document_metadata_unchanged=true,camera_consistency=cameraVerified?"camera_metadata_unchanged":"unverified",scope="main_window_observation; camera metadata comparison is not an atomic model-render synchronization barrier"};
            var warnings=identicalAfterCameraChange
                ?new[]{"Visual observation only: verify dimensions and write outcomes with exact APIs; separate dialogs/menus may be absent.",
                    "CAPTURE_IDENTICAL_AFTER_CAMERA_CHANGE: Pixels match the previous frame despite a different camera token. Inspect the actual image; rendering may not have updated. This does not imply a CAD write failure."}
                :new[]{"Visual observation only: verify dimensions and write outcomes with exact APIs; separate dialogs/menus may be absent."};
            return (Result.Ok(id,data,after.context,warnings),png);
        }
        catch(OperationCanceledException) when(!cancellation.IsCancellationRequested){throw new DomainException("CAPTURE_TIMEOUT","Capture deadline expired; only the visual worker was stopped.",true);}
        catch(InvalidOperationException){throw new DomainException("CAPTURE_FAILED","Window capture or its context could not be verified.");}
        finally
        {
            Gate.Release();
        }
    }
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool IsIconic(IntPtr window);
}
