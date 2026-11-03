using System.Diagnostics;
using NanoCAD.Contracts;

namespace NanoCAD.Ipc;

public enum WritePhase { Queued, Scheduled, Running, Committing, Finished }

/// <summary>Bridge-owned operation state. Never stores a Document or other CAD object.</summary>
public sealed class WriteJob
{
    private readonly object _gate = new();
    private readonly TaskCompletionSource<Result> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private WritePhase _phase;
    private bool _cancelRequested;
    private Result? _committed;
    private IOperationArguments? _arguments;
    private readonly CancellationToken _ownerCancellation;
    private readonly CancellationToken _startCancellation;
    private readonly TimeSpan _executionBudget;
    private long _startedAt;
    private bool _ownerDetached;
    public string OperationId { get; }
    public string BridgeId { get; }
    public string Fingerprint { get; }
    public string OriginalRequestId { get; }
    public IOperationArguments Arguments { get { lock (_gate) return _arguments ?? throw new InvalidOperationException("Finished job released its payload."); } }
    public string Operation { get; }
    public WritePhase Phase { get { lock (_gate) return _phase; } }
    public Task<Result> Completion => _completion.Task;
    public Result? CommittedResult { get { lock (_gate) return _committed; } }
    internal WriteJob(Request request, IOperationArguments arguments, string fingerprint, CancellationToken ownerCancellation, CancellationToken startCancellation, TimeSpan executionBudget)
    { OriginalRequestId = request.requestId; Operation = arguments.Operation; OperationId = arguments.OperationId; BridgeId = arguments.BridgeId; _arguments = arguments; Fingerprint = fingerprint; _ownerCancellation = ownerCancellation; _startCancellation = startCancellation; _executionBudget = executionBudget; }
    internal (WritePhase Phase, bool JobFinished, bool CommitRecorded, Result? RecordedResult) ReadOperationSnapshot()
    {
        lock (_gate)
            return (_phase, _phase == WritePhase.Finished, _committed != null,
                _completion.Task.IsCompletedSuccessfully ? _completion.Task.Result : _committed);
    }
    public bool TrySchedule()
    {
        lock (_gate) { if (_ownerCancellation.IsCancellationRequested || _startCancellation.IsCancellationRequested) CancelOwner(); if (_phase != WritePhase.Queued) return false; _phase = WritePhase.Scheduled; return true; }
    }
    public bool TryStart()
    {
        lock (_gate) { if (_ownerCancellation.IsCancellationRequested || _startCancellation.IsCancellationRequested) CancelOwner(); if (_phase != WritePhase.Scheduled) return false; _startedAt = Stopwatch.GetTimestamp(); _phase = WritePhase.Running; return true; }
    }
    private double ElapsedMs => _startedAt == 0 ? 0 : (Stopwatch.GetTimestamp() - _startedAt) * 1000d / Stopwatch.Frequency;
    private bool ExecutionExpired => _startedAt != 0 && ElapsedMs >= _executionBudget.TotalMilliseconds;
    private bool Cancelled => _cancelRequested || (!_ownerDetached && _ownerCancellation.IsCancellationRequested) ||
        (_phase is WritePhase.Queued or WritePhase.Scheduled && _startCancellation.IsCancellationRequested) || ExecutionExpired;
    public void CheckCancellation()
    {
        lock (_gate) if (Cancelled || _phase == WritePhase.Finished) throw new OperationCanceledException();
    }
    public void BeginCommit()
    {
        lock (_gate)
        {
            if (Cancelled) throw new OperationCanceledException();
            if (_phase != WritePhase.Running) throw new InvalidOperationException("Invalid commit boundary.");
            _phase = WritePhase.Committing;
        }
    }
    public void RecordCommit(Result result)
    {
        lock (_gate)
        {
            if (_phase != WritePhase.Committing || !result.success) throw new InvalidOperationException("Commit outcome does not match state.");
            // Retain this before cleanup, event delivery, logging or IPC can fail.
            _committed = result;
        }
    }
    public Result Error(string code, string message, string outcome, ReadContext? context = null, object? cause = null) =>
        Result.Fail(OriginalRequestId, new(code, message, false, new WriteErrorDetails(OperationId, outcome, false,
            outcome == "not_started" && Phase == WritePhase.Queued ? "unchanged" : outcome is "not_started" or "rolled_back" ? "may_contain_empty_step" : "unknown", cause)), context);
    public void Complete(Result result)
    {
        lock (_gate)
        {
            if (_phase == WritePhase.Finished) return;
            // A later transport/cancel/shutdown error cannot overwrite a commit.
            if (_committed != null && !result.success) result = _committed;
            _phase = WritePhase.Finished; _arguments = null;
            _completion.TrySetResult(result);
        }
    }
    internal void CancelOwner()
    {
        lock (_gate)
        {
            if (_ownerDetached) return;
            if (_phase is WritePhase.Queued or WritePhase.Scheduled)
                Complete(Error("CANCELLED", "Request was cancelled before native execution; it will not run later.", "not_started"));
            else if (_phase == WritePhase.Running) _cancelRequested = true;
            // Committing cannot be interrupted or declared rolled back.
        }
    }
    internal void Stop()
    {
        lock (_gate)
        {
            if (_phase == WritePhase.Finished) return;
            Complete(_committed ?? Error("BRIDGE_UNAVAILABLE", "Bridge stopped before a final response.",
                _phase is WritePhase.Queued or WritePhase.Scheduled ? "not_started" : "unknown"));
        }
    }
    // The response wait has expired. Queued commands must not start late;
    // running native work owns its bounded lifetime after this handshake.
    internal void ResponseWaitExpired()
    {
        lock (_gate)
        {
            if (_phase is WritePhase.Queued or WritePhase.Scheduled) { CancelOwner(); return; }
            if (_phase is WritePhase.Running or WritePhase.Committing)
            {
                if (_ownerCancellation.IsCancellationRequested || _cancelRequested) CancelOwner();
                else _ownerDetached = true;
            }
        }
    }
    public object ExecutionState()
    {
        lock (_gate) return new { operation_id = OperationId, operation = Operation, phase = _phase.ToString().ToLowerInvariant(),
            continues_after_response_timeout = _ownerDetached, execution_elapsed_ms = Math.Round(ElapsedMs),
            execution_budget_ms = (long)_executionBudget.TotalMilliseconds, execution_budget_expired = ExecutionExpired };
    }
    public static Result ForCaller(Result result, string requestId, bool replayed)
    {
        var data = result.data switch
        {
            CreatedEntities created => created with { replayed = replayed },
            ModifiedEntities modified => modified with { replayed = replayed },
            DocumentOperationResult document => document with { replayed = replayed },
            ModifiedStructure structure => structure with { replayed = replayed },
            DrawingGroupResult431 group => group with { replayed = replayed },
            AssociativeArrayWriteResult431 array => array with { replayed = replayed },
            ModelParameterResult model => model with { replayed = replayed },
            ModelRebuildOutput419 rebuild => rebuild with { replayed = replayed },
            CameraChangeResult camera => camera with { replayed = replayed },
            _ => result.data
        };
        var error = result.error?.details is WriteErrorDetails details
            ? result.error with { details = details with { replayed = replayed } } : result.error;
        return result with { request_id = requestId, data = data, error = error };
    }
}

/// <summary>Bounded queue and non-evicting idempotency ledger, independent of CAD and IPC connections.</summary>
public sealed class WriteQueue : IDisposable
{
    public const int ResponseWaitMilliseconds = 8000;
    public const int ExecutionBudgetMilliseconds = 300000;
    private readonly object _gate = new();
    private readonly Dictionary<string, WriteJob> _ledger = new(StringComparer.Ordinal);
    private readonly Queue<WriteJob> _waiting = new();
    private readonly int _uiThread, _capacity;
    private readonly TimeSpan _executionBudget;
    private bool _disposed;
    public WriteQueue(int uiThread, int capacity = 32, TimeSpan? executionBudget = null)
    {
        _uiThread = uiThread; _capacity = capacity;
        _executionBudget = executionBudget ?? TimeSpan.FromMilliseconds(ExecutionBudgetMilliseconds);
        if (_executionBudget <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(executionBudget));
    }
    public int Count { get { lock (_gate) return _waiting.Count; } }
    public int LedgerCount { get { lock (_gate) return _ledger.Count; } }
    public object[] ActiveExecutions { get { lock (_gate) return _ledger.Values.Where(j => j.Phase is WritePhase.Running or WritePhase.Committing).Select(j => j.ExecutionState()).ToArray(); } }
    public (WriteJob Job, bool Replay) Enqueue(Request request, CancellationToken ownerCancellation = default, CancellationToken startCancellation = default)
    {
        var arguments = WriteValidation.Parse(request.operation, request.arguments);
        string fingerprint = arguments.Fingerprint();
        lock (_gate)
        {
            if (_disposed) throw new DomainException("BRIDGE_UNAVAILABLE", "Bridge is stopping.");
            if (_ledger.TryGetValue(arguments.OperationId, out var previous))
            {
                if (previous.Fingerprint != fingerprint)
                    throw new DomainException("OPERATION_ID_CONFLICT", "This operation_id was already reserved for another payload.", false, new { operation_id = arguments.OperationId });
                return (previous, true);
            }
            while (_waiting.TryPeek(out var expired) && expired.Phase == WritePhase.Finished) _waiting.Dequeue();
            if (_waiting.Count >= _capacity) throw new DomainException("BUSY", "Write queue is full; nothing was scheduled.", true);
            var job = new WriteJob(request, arguments, fingerprint, ownerCancellation, startCancellation, _executionBudget);
            _ledger.Add(arguments.OperationId, job); _waiting.Enqueue(job);
            return (job, false);
        }
    }
    public async Task<Result> SubmitAsync(Request request, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Math.Min(request.timeoutMs, ResponseWaitMilliseconds));
        var (job, replay) = Enqueue(request, cancellationToken, deadline.Token);
        if (job.Completion.IsCompleted) return WriteJob.ForCaller(await job.Completion.ConfigureAwait(false), request.requestId, replay);
        if (replay && job.CommittedResult is { } known) return WriteJob.ForCaller(known with
            { warnings = new[] { "COMMAND_COMPLETION_PENDING: commit is recorded; refresh document state before a new write." } }, request.requestId, true);
        using var registration = replay ? default : deadline.Token.Register(job.ResponseWaitExpired);
        try { return WriteJob.ForCaller(await job.Completion.WaitAsync(deadline.Token).ConfigureAwait(false), request.requestId, replay); }
        catch (OperationCanceledException)
        {
            if (!replay)
            {
                if (cancellationToken.IsCancellationRequested) job.CancelOwner();
                else job.ResponseWaitExpired();
            }
            cancellationToken.ThrowIfCancellationRequested();
            // Cancellation callbacks and completion may race; preserve a result
            // if already available, otherwise ask for a replay of this same ID.
            if (job.Completion.IsCompleted) return WriteJob.ForCaller(await job.Completion.ConfigureAwait(false), request.requestId, replay);
            if (job.CommittedResult is { } committed) return WriteJob.ForCaller(committed with
                { warnings = new[] { "COMMAND_COMPLETION_PENDING: commit is recorded; refresh document state before a new write." } }, request.requestId, replay);
            return WriteJob.ForCaller(job.Error("WRITE_OUTCOME_PENDING", "Native execution is still pending; the response wait does not cancel already-started work. Repeat exactly the same operation_id and payload in this bridge lifetime to retrieve the result. Do not start dependent edits or restart nanoCAD.", "unknown", cause: job.ExecutionState()), request.requestId, replay);
        }
    }
    /// <summary>Observe the existing ledger without queueing work or changing the owner's execution lifetime.</summary>
    public async Task<Result> ReadOperationAsync(Request request, CancellationToken cancellationToken)
    {
        if (request.operation != "get_operation_result") throw new DomainException("INVALID_ARGUMENT", "Expected get_operation_result.");
        var arguments = OperationResultArguments432.Parse(request.arguments);
        cancellationToken.ThrowIfCancellationRequested();
        WriteJob? job;
        lock (_gate) _ledger.TryGetValue(arguments.OperationId, out job);
        if (job == null) return OperationResult432.NotFound(request.requestId, arguments);
        if (job.BridgeId != arguments.BridgeId) throw new DomainException("BRIDGE_UNAVAILABLE", "Request addresses another bridge instance.");
        var snapshot = job.ReadOperationSnapshot();
        if (!snapshot.JobFinished && arguments.WaitMs > 0)
        {
            try { await job.Completion.WaitAsync(TimeSpan.FromMilliseconds(arguments.WaitMs), cancellationToken).ConfigureAwait(false); }
            catch (TimeoutException) { }
            // A reader timeout/disconnect never calls CancelOwner or ResponseWaitExpired.
            snapshot = job.ReadOperationSnapshot();
        }
        cancellationToken.ThrowIfCancellationRequested();
        return OperationResult432.Recorded(request.requestId, arguments, job.Operation, job.OriginalRequestId,
            snapshot.Phase.ToString().ToLowerInvariant(), snapshot.JobFinished, snapshot.CommitRecorded, snapshot.RecordedResult, cancellationToken);
    }
    public WriteJob? Take()
    {
        if (Environment.CurrentManagedThreadId != _uiThread) throw new InvalidOperationException("Write dispatch must stay on the host UI thread.");
        lock (_gate)
        {
            while (_waiting.TryDequeue(out var job)) if (job.TrySchedule()) return job;
            return null;
        }
    }
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            foreach (var job in _ledger.Values) job.Stop();
            _waiting.Clear();
        }
    }
}
