using HostMgd.ApplicationServices;
using NanoCAD.Contracts;
using Application = HostMgd.ApplicationServices.Application;

namespace NanoCADBridge;

internal static class HostReadGate
{
    private static readonly HostWindowState Window = new(Environment.ProcessId);
    public static bool IsWindowAvailable() => Window.Capture().available;
    // UI thread only. IsQuiescent exists in metadata but nanoCAD 26 raises
    // NOT_IMPLEMENTED for its getter. CMDACTIVE is documented in the SDK guide
    // (including bit 8 for dialogs) and passed the installed-host probe.
    public static bool IsBusy(Document document)
    {
        // Native Ctrl+O may leave CMDACTIVE=0 and CommandInProgress empty
        // while disabling the main frame. Reject this state before CAD reads.
        if (!IsWindowAvailable()) return true;
        object value = Application.GetSystemVariable("CMDACTIVE");
        int active = value switch
        {
            int number => number,
            short number => number,
            _ => throw new DomainException("API_EXCEPTION", "Unexpected CMDACTIVE value; native reads are disabled.")
        };
        return active != 0 || !string.IsNullOrEmpty(document.CommandInProgress);
    }

    public static void RequireImpliedSelection()
    {
        object value = Application.GetSystemVariable("PICKFIRST");
        int mode = value switch
        {
            short number => number,
            int number => number,
            _ => throw new DomainException("API_EXCEPTION", "Unexpected PICKFIRST value; selection cannot be read reliably.")
        };
        if (mode == 0)
            throw new DomainException("SELECTION_MODE_UNSUPPORTED", "Implied selection is disabled (PICKFIRST=0).", false,
                new { system_variable = "PICKFIRST", value = mode });
        if (mode != 1)
            throw new DomainException("API_EXCEPTION", "Unexpected PICKFIRST mode; selection cannot be read reliably.");
    }
}
