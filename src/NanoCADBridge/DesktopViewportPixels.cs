using System.Runtime.InteropServices;
using NanoCAD.Contracts;

namespace NanoCADBridge;

internal static class DesktopViewportPixels
{
    private static readonly IntPtr PerMonitorAwareV2 = new(-4);

    internal static CaptureViewportPixels Read(IntPtr window, int viewportWidth, int viewportHeight)
    {
        if (GetWindowThreadProcessId(window, out uint pid) == 0 || pid != (uint)Environment.ProcessId)
            throw Unavailable();
        var logical = ClientSize(window, GetWindowDpiAwarenessContext(window));
        var physical = ClientSize(window, PerMonitorAwareV2);
        if (logical != ClientSize(window, GetWindowDpiAwarenessContext(window)) ||
            GetWindowThreadProcessId(window, out pid) == 0 || pid != (uint)Environment.ProcessId)
            throw Unavailable();
        return CaptureViewportPixels.Bind(viewportWidth, viewportHeight, logical.Width, logical.Height,
            physical.Width, physical.Height);
    }

    private static (int Width, int Height) ClientSize(IntPtr window, IntPtr awareness)
    {
        IntPtr previous = awareness != IntPtr.Zero ? SetThreadDpiAwarenessContext(awareness) : IntPtr.Zero;
        if (previous == IntPtr.Zero) throw Unavailable();
        try
        {
            if (!GetClientRect(window, out var rect) || rect.Left != 0 || rect.Top != 0) throw Unavailable();
            if (MapWindowPoints(window, IntPtr.Zero, ref rect, 2) == 0 && Marshal.GetLastPInvokeError() != 0)
                throw Unavailable();
            long width = (long)rect.Right - rect.Left, height = (long)rect.Bottom - rect.Top;
            if (width is < 1 or > int.MaxValue || height is < 1 or > int.MaxValue) throw Unavailable();
            return ((int)width, (int)height);
        }
        finally
        {
            if (SetThreadDpiAwarenessContext(previous) == IntPtr.Zero) throw Unavailable();
        }
    }

    private static DomainException Unavailable() => new("CAPTURE_BOUNDS_UNAVAILABLE",
        "The native drawing window's logical and physical pixel mapping is unavailable.");

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr awareness);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int MapWindowPoints(IntPtr window, IntPtr target, ref Rect rect, uint count);
}
