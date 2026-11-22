using System.Collections.Concurrent;
using System.Diagnostics;
using NanoCAD.Contracts;

namespace NanoCAD.Ipc;

/// <summary>Contains only serializable requests/delegates, never CAD objects. Pump is called by the host UI.</summary>
public sealed class ReadQueue : IDisposable
{
    private sealed record Job(Request Request, TaskCompletionSource<Result> Completion, CancellationToken Cancellation);
    private readonly ConcurrentQueue<Job> _jobs = new();
    private readonly int _uiThread;
    private readonly int _capacity;
    private int _count;
    private readonly object _lifecycle = new();
    private bool _disposed;
    public ReadQueue(int uiThread, int capacity = 32) { _uiThread = uiThread; _capacity = capacity; }
    public int Count => Volatile.Read(ref _count);
    public async Task<Result> SubmitAsync(Request request, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Math.Min(request.timeoutMs, 5000));
        var completion = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);
        var job = new Job(request, completion, deadline.Token);
        lock (_lifecycle)
        {
            if (_disposed) return Result.Fail(request.requestId, new("BRIDGE_UNAVAILABLE", "Bridge is stopping.", true));
            if (Interlocked.Increment(ref _count) > _capacity)
            {
                Interlocked.Decrement(ref _count);
                return Result.Fail(request.requestId, new("BUSY", "Read queue is full.", true));
            }
            _jobs.Enqueue(job);
        }
        try { return await completion.Task.WaitAsync(deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return Result.Fail(request.requestId, new("UI_DISPATCH_UNAVAILABLE", "UI did not finish the read within 5 seconds. Native calls cannot be interrupted.", true)); }
    }
    public void Pump(Func<Request, CancellationToken, Result> execute) => Pump(execute, TimeSpan.Zero);

    public void Pump(Func<Request, CancellationToken, Result> execute, TimeSpan timeBudget, Func<bool>? yieldToWrite = null)
    {
        if (Environment.CurrentManagedThreadId != _uiThread) throw new InvalidOperationException("CAD dispatch must stay on the Initialize/UI thread.");
        // A native read cannot be preempted. Check the budget between live jobs and leave pending writes a turn.
        long start = Stopwatch.GetTimestamp();
        double budgetTicks = timeBudget.TotalSeconds * Stopwatch.Frequency;
        while (_jobs.TryDequeue(out var job))
        {
            Interlocked.Decrement(ref _count);
            if (job.Cancellation.IsCancellationRequested) { job.Completion.TrySetCanceled(job.Cancellation); continue; }
            try
            {
                var result = execute(job.Request, job.Cancellation);
                if (!job.Cancellation.IsCancellationRequested) job.Completion.TrySetResult(result);
                else job.Completion.TrySetCanceled(job.Cancellation);
            }
            catch (OperationCanceledException) { job.Completion.TrySetCanceled(); }
            catch (Exception) { job.Completion.TrySetResult(Result.Fail(job.Request.requestId, new("API_EXCEPTION", "Unhandled read error; see bridge diagnostics."))); }
            if (_jobs.IsEmpty || Stopwatch.GetTimestamp() - start >= budgetTicks || yieldToWrite?.Invoke() == true) return;
        }
    }
    public void Dispose()
    {
        lock (_lifecycle)
        {
            _disposed = true;
            while (_jobs.TryDequeue(out var job))
            {
                Interlocked.Decrement(ref _count);
                job.Completion.TrySetResult(Result.Fail(job.Request.requestId, new("BRIDGE_UNAVAILABLE", "Bridge is stopping.", true)));
            }
        }
    }
}
