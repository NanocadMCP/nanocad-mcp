using NanoCAD.Contracts;
using NanoCAD.Ipc;

namespace NanoCADBridge;

internal static class NativeTransactionCompletion
{
    // Finish the owned transaction once. Only the writer knows whether its earlier native operations had side effects.
    internal static Result? DisposeAndGetCommit(IDisposable? transaction, WriteJob job, string cleanupWarning, out Exception? cleanupError)
    {
        cleanupError = null;
        try { transaction?.Dispose(); }
        catch (Exception error) { cleanupError = error; }

        var committed = job.CommittedResult;
        if (committed == null || cleanupError == null) return committed;
        committed = committed with { warnings = [.. committed.warnings, cleanupWarning] };
        job.RecordCommit(committed);
        return committed;
    }
}
