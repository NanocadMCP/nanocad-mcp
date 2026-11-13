// Copyright 2026 Andrey Juliovich Camejo Severinov. Licensed under MIT.
using System.Runtime.InteropServices;
using NanoCAD.Ipc;

namespace NanoCADBridge;

// Starts on the host UI thread. The native module annotates only the affected
// MSAA value property and borrows the real frame through the existing SDK export.
internal sealed class NativeAccessibilityGuard : IDisposable
{
    private const uint Abi = 1;
    private const int Pending = unchecked((int)0x8000000A);
    private readonly JsonLog _log;
    private Api? _api;
    private bool _retryPending;
    private bool _startAttempted;
    private bool _disposed;

    [StructLayout(LayoutKind.Sequential)]
    private struct Report
    {
        public uint Size, Version, Phase;
        public int Status;
        public uint OwnerThreadId, AnnotatedWindowCount, CallbackCount,
            GuardedCallbackCount, WrongThreadCallbackCount, IdentityFailureCount;

        public static Report Create() => new() { Size = (uint)Marshal.SizeOf<Report>(), Version = Abi };
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint AbiVersion();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private delegate int StartNative(IntPtr frameProvider, [MarshalAs(UnmanagedType.LPWStr)] string logPath, ref Report report);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int StopNative(ref Report report);
    private sealed record Api(IntPtr Library, StartNative Start, StopNative Stop);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr GetModuleHandleW(string name);

    internal NativeAccessibilityGuard(JsonLog log)
    {
        _log = log;
        TryStart(allowRetry: true);
    }

    // A host that initializes managed extensions before creating its frame gets
    // one deferred attempt. Successful installation never polls or rescans here.
    internal void RetryPending()
    {
        if (!_retryPending || _disposed) return;
        _retryPending = false;
        TryStart(allowRetry: false);
    }

    private void TryStart(bool allowRetry)
    {
        try
        {
            var host = GetModuleHandleW("NrxDbGate.dll");
            if (host == IntPtr.Zero)
            {
                _retryPending = allowRetry;
                _log.Write("native_accessibility_guard", "", allowRetry ? "host_module_pending" : "host_module_unavailable");
                return;
            }
            var provider = NativeLibrary.GetExport(host, "?ncedGetNcadFrame@@YAPEAVCMDIFrameWnd@@XZ");
            var api = _api ??= Load();
            var report = Report.Create();
            _startAttempted = true;
            int status = api.Start(provider, Path.Combine(Discovery.Root, "logs", $"accessibility-{Environment.ProcessId}.log"), ref report);
            _retryPending = status == Pending && allowRetry;
            Log("start", status, report);
        }
        catch (Exception error) when (error is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException
            or IOException or UnauthorizedAccessException)
        {
            _log.Write("native_accessibility_guard", "", "unavailable_" + error.GetType().Name);
        }
    }

    private static Api Load()
    {
        string directory = Path.GetDirectoryName(typeof(NativeAccessibilityGuard).Assembly.Location)
            ?? throw new DllNotFoundException("The bridge directory is unavailable.");
        var library = NativeLibrary.Load(Path.Combine(directory, "NanoCAD.NativeAccessibility.dll"),
            typeof(NativeAccessibilityGuard).Assembly,
            DllImportSearchPath.UseDllDirectoryForDependencies | DllImportSearchPath.System32 | DllImportSearchPath.ApplicationDirectory);
        try
        {
            var version = Marshal.GetDelegateForFunctionPointer<AbiVersion>(NativeLibrary.GetExport(library, "NanoCAD_AccessibilityGuardAbiVersion"));
            if (version() != Abi || Marshal.SizeOf<Report>() != 40)
                throw new BadImageFormatException("The accessibility compatibility module has an incompatible ABI.");
            return new(library,
                Marshal.GetDelegateForFunctionPointer<StartNative>(NativeLibrary.GetExport(library, "NanoCAD_AccessibilityGuardStart")),
                Marshal.GetDelegateForFunctionPointer<StopNative>(NativeLibrary.GetExport(library, "NanoCAD_AccessibilityGuardStop")));
        }
        catch
        {
            // No native Start has run, so no callback or window can retain it.
            NativeLibrary.Free(library);
            throw;
        }
    }

    private void Log(string operation, int status, Report report) => _log.Write("native_accessibility_guard", "",
        $"{operation}_0x{unchecked((uint)status):X8}_phase{report.Phase}_owner{report.OwnerThreadId}" +
        $"_windows{report.AnnotatedWindowCount}_callbacks{report.CallbackCount}_guarded{report.GuardedCallbackCount}" +
        $"_wrong_thread{report.WrongThreadCallbackCount}_identity_failures{report.IdentityFailureCount}");

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _retryPending = false;
        if (_startAttempted && _api is { } api)
        {
            var report = Report.Create();
            Log("stop", api.Stop(ref report), report);
        }
        // Native Start pins this small DLL until process exit. Clear/unhook in
        // Stop disconnects our callbacks; externally retained COM references
        // must still be able to release their object after bridge termination.
    }
}
