using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NanoCADBridge;

// Passive Win32 state check; no window activation, messages or input. Used on
// the CAD UI thread. The diagnostic runner links this exact implementation.
internal sealed class HostWindowState
{
    private readonly int _processId;
    private IntPtr _handle;
    public HostWindowState(int processId) => _processId = processId;
    internal sealed record Snapshot(long main_window_handle, int owner_process_id, bool is_window, bool enabled, bool available);

    public Snapshot Capture()
    {
        if (_handle == IntPtr.Zero || !Native.IsWindow(_handle))
        {
            using var process = Process.GetProcessById(_processId);
            _handle = process.MainWindowHandle;
        }
        bool valid = _handle != IntPtr.Zero && Native.IsWindow(_handle);
        uint owner = 0;
        if (valid) Native.GetWindowThreadProcessId(_handle, out owner);
        bool enabled = valid && Native.IsWindowEnabled(_handle);
        return new(_handle.ToInt64(), unchecked((int)owner), valid, enabled, valid && owner == (uint)_processId && enabled);
    }

    private static class Native
    {
        [DllImport("user32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowEnabled(IntPtr window);
        [DllImport("user32.dll", ExactSpelling = true)]
        internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    }
}
