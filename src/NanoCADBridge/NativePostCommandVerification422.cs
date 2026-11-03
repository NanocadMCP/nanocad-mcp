using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using NanoCAD.Contracts;
using NanoCAD.Ipc;

namespace NanoCADBridge;

// CAD-bound closures live only in the bridge, never in the IPC operation ledger.
// The dispatcher takes a registration once, after the matching command ended.
internal static class NativePostCommandVerification422
{
    private static readonly object Gate = new();
    private static readonly ConditionalWeakTable<WriteJob, Verification> Pending = new();

    internal sealed class Verification : IDisposable
    {
        private Func<Result>? _read;
        private Func<Exception, Result>? _failure;
        internal Verification(Func<Result> read, Func<Exception, Result>? failure)
        { _read = read; _failure = failure; }
        internal Result Execute(WriteJob job)
        {
            var read = _read ?? throw new InvalidOperationException("Final verification was already released.");
            var failure = _failure;
            Dispose();
            try { return read(); }
            catch (Exception error)
            {
                if (job.CommittedResult is { } committed) return committed;
                try { if (failure != null) return failure(error); }
                catch (Exception factoryError)
                {
                    return job.Error("WRITE_OUTCOME_UNKNOWN", "Final native observation failed; inspect the model before a dependent write. Do not repeat this operation with a new ID.", "unknown",
                        cause: new { stage = "post_command_verification", error_type = error.GetType().Name,
                            verification_details = error is DomainException originalDomain ? originalDomain.Error.details : null,
                            failure_factory_error = factoryError.GetType().Name });
                }
                return job.Error("WRITE_OUTCOME_UNKNOWN", "Final native observation failed; inspect the model before a dependent write. Do not repeat this operation with a new ID.", "unknown",
                    cause: new { stage = "post_command_verification", error_type = error.GetType().Name,
                        message = error.Message, verification_details = error is DomainException domain ? domain.Error.details : null });
            }
        }
        public void Dispose() { _read = null; _failure = null; }
    }

    internal static Result Defer(WriteJob job, Func<Result> read, Func<Exception, Result>? failure = null)
    {
        ArgumentNullException.ThrowIfNull(read);
        if (job.Phase != WritePhase.Committing)
            throw new InvalidOperationException("Final native observation requires the commit boundary.");
        lock (Gate) Pending.Add(job, new(read, failure));
        // Internal command return only. The dispatcher must retain this job until
        // its registered final observation; no commit has been recorded here.
        return job.Error("WRITE_OUTCOME_UNKNOWN", "Native changes await final observation after command completion; do not repeat this operation with a new ID.", "unknown",
            cause: new { stage = "awaiting_post_command_verification" });
    }
    internal static bool Contains(WriteJob job)
    { lock (Gate) return Pending.TryGetValue(job, out _); }
    internal static bool TryTake(WriteJob job, [NotNullWhen(true)] out Verification? verification)
    {
        lock (Gate)
        {
            if (!Pending.TryGetValue(job, out verification)) return false;
            Pending.Remove(job);
            return true;
        }
    }
    internal static void Clear(WriteJob job)
    {
        lock (Gate)
        {
            if (Pending.TryGetValue(job, out var verification)) verification.Dispose();
            Pending.Remove(job);
        }
    }
}
