using NanoCAD.Contracts;
using NanoCAD.Ipc;
using NanoCADBridge;

internal static class NativeTransactionCompletionTests
{
    private sealed class FailingCleanup : IDisposable
    {
        public int Calls { get; private set; }
        public void Dispose() { Calls++; throw new IOException("injected cleanup failure"); }
    }

    internal static Task Run()
    {
        foreach (bool recorded in new[] { false, true })
        {
            using var queue = new WriteQueue(Environment.CurrentManagedThreadId);
            var arguments = Wire.Element(new { bridge_instance_id = Guid.NewGuid().ToString("D"), document_id = Guid.NewGuid().ToString("D"),
                operation_id = Guid.NewGuid().ToString("D"), expected_revision = "1", units = "Millimeters", handle = "A",
                variable_id = Guid.NewGuid().ToString("B"), expected_expression = "10", value = 11 });
            var request = new Request(Wire.Version, Guid.NewGuid().ToString("D"), "set_model_parameter", 5000, arguments);
            var (job, _) = queue.Enqueue(request);
            if (queue.Take() != job || !job.TryStart()) throw new Exception("Could not start fixture write.");
            if (recorded) { job.BeginCommit(); job.RecordCommit(Result.Ok(request.requestId, "retained", null, "earlier warning")); }
            var resource = new FailingCleanup();
            var result = NativeTransactionCompletion.DisposeAndGetCommit(resource, job, "cleanup diagnostic", out var failure);
            if (resource.Calls != 1 || failure is not IOException || result != job.CommittedResult ||
                (recorded ? result?.success != true || !result.warnings.SequenceEqual(new[] { "earlier warning", "cleanup diagnostic" }) : result != null))
                throw new Exception("Cleanup changed the recorded write outcome.");
            job.Complete(result ?? job.Error("WRITE_OUTCOME_UNKNOWN", "test", "unknown"));
        }
        return Task.CompletedTask;
    }
}
