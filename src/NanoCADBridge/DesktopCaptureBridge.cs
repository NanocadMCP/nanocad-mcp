using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.Ipc;

namespace NanoCADBridge;

// Internal read-only IPC endpoint. The bridge runs in nanoCAD's interactive
// desktop, while the MCP server may run in an isolated window station.
internal static class DesktopCaptureBridge
{
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

    internal static async Task<Result> CaptureAsync(Request request,Descriptor descriptor,CancellationToken cancellation)
    {
        var args=VisualCaptureBridgeArguments.Parse(request.arguments);
        if(args.BridgeId!=descriptor.bridge_instance_id)throw new DomainException("BRIDGE_UNAVAILABLE","Capture targets another bridge lifetime.");
        var window=MainWindow();
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
        var info=new FileInfo(framePath);
        if(!info.Exists||info.Length>600*1024||(info.Attributes&FileAttributes.ReparsePoint)!=0)
            throw new DomainException("CAPTURE_IMAGE_LIMIT","Desktop PNG exceeds600KiB; request a smaller max_edge.");
        if(MainWindow()!=window)throw new DomainException("CAPTURE_WINDOW_UNAVAILABLE","The nanoCAD main window changed during capture.");
        return Result.Ok(request.requestId,new{
            capture_id=args.CaptureId,process_id=descriptor.process_id,
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
