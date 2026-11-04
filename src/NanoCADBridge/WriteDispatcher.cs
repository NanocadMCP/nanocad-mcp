using HostMgd.ApplicationServices;
using HostMgd.EditorInput;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Application = HostMgd.ApplicationServices.Application;

namespace NanoCADBridge;

internal sealed class WriteDispatcher : IDisposable
{
    public const string CommandName = "NCMCP_APPLY2D";
    private readonly WriteQueue _queue;
    private readonly CadWriter _writer;
    private readonly CadDocuments _documents;
    private readonly CameraViewWriter _views;
    private readonly JsonLog _log;
    private readonly int _uiThread = Environment.CurrentManagedThreadId;
    private WriteJob? _scheduled;
    private NativeDispatch? _native;
    private readonly Stack<CommandFrame> _commandFrames = new();
    private bool _disabled;
    private bool _pumping;
    public bool Pending => _scheduled != null;
    public bool Available => !_disabled;
    public bool CameraAvailable => !_disabled && _views.Available;
    public bool DocumentOperationRunning => _documents.ApplicationOperationRunning;
    private sealed class NativeDispatch
    {
        internal NativeCommandAttempt409<WriteJob, Document> Identity { get; }
        internal DocumentState Bound { get; }
        internal Result? Outcome { get; set; }
        internal CommandEventHandler? StartedHandler { get; set; }
        internal CommandEventHandler? FinishedHandler { get; set; }
        internal CommandEventHandler? InterruptedHandler { get; set; }
        internal ReadContext? EndedContext { get; set; }
        internal NativeDispatch(WriteJob job, DocumentState bound)
        { Identity = new(job, bound.Document); Bound = bound; }
    }
    private sealed class CommandFrame
    {
        internal NativeDispatch Origin { get; }
        internal NativeCommandAttempt409<WriteJob, Document>? Entered { get; set; }
        internal CommandFrame(NativeDispatch origin) { Origin = origin; }
    }
    public WriteDispatcher(WriteQueue queue, CadWriter writer, CadDocuments documents, CameraViewWriter views, JsonLog log)
    {
        _queue = queue; _writer = writer; _documents = documents; _views = views; _log = log;
        Application.DocumentManager.DocumentToBeDestroyed += Closing;
    }

    public Result Preview(Request request, CancellationToken cancellation)
    {
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if (_scheduled != null || _documents.ApplicationOperationRunning)
                throw new DomainException("HOST_BUSY", "Wait for the active write before checking a new request.", true);
            var arguments = WritePreviewRequest.Parse(request.arguments);
            object? predicted = null;
            ReadContext? context;
            switch (arguments)
            {
                case DocumentArguments document:
                    (context, predicted) = _documents.Preview(document, cancellation.ThrowIfCancellationRequested);
                    break;
                case CameraChangeArguments camera:
                    (context, predicted) = _views.Preview(camera, cancellation.ThrowIfCancellationRequested);
                    break;
                case IWriteArguments write:
                    var state = _writer.Preflight(write, false, cancel: cancellation.ThrowIfCancellationRequested, observePlan: value => predicted = value);
                    context = _writer.Context(state);
                    break;
                default:
                    throw new DomainException("INVALID_ARGUMENT", "Unknown write preview request.");
            }
            cancellation.ThrowIfCancellationRequested();
            return Result.Ok(request.requestId, new WritePreviewResult(arguments.Operation, "validated", false,
                predicted is null ? "input_and_available_native_preflight" : "input_native_preflight_and_reported_effects", predicted), context);
        }
        catch (DomainException error) { return Result.Fail(request.requestId, error.Error); }
    }
    public void Pump()
    {
        if (Environment.CurrentManagedThreadId != _uiThread) throw new InvalidOperationException("Write dispatch must stay on the host UI thread.");
        if (_pumping || _disabled) return;
        _pumping = true;
        try { PumpCore(); }
        finally { _pumping = false; }
    }
    private void PumpCore()
    {
        if (_native is { EndedContext: not null } ended)
        {
            FinishDeferred(ended);
            return;
        }
        // Release a timed-out registration even while its native command has
        // not ended. Keep the original command slot/token until that boundary.
        if (_scheduled is { } pending && NativePostCommandVerification422.Contains(pending))
        {
            try { pending.CheckCancellation(); }
            catch (OperationCanceledException)
            {
                NativePostCommandVerification422.Clear(pending);
                pending.Complete(pending.CommittedResult ?? pending.Error("WRITE_OUTCOME_UNKNOWN",
                    "Final native observation was cancelled or exceeded its execution budget; do not repeat this operation with a new ID.", "unknown"));
            }
        }
        // The token route cannot start a different job when an expired command
        // arrives later. Retire only work which provably never started; running
        // and committed operations retain their original completion boundary.
        if (_scheduled is { Phase: WritePhase.Finished } finished &&
            (_native == null || _native.Identity.CanRetireNotStarted(finished, true)))
        {
            Trace(finished, "write_not_started_attempt_retired", CommandName);
            Clear(finished);
        }
        if (_scheduled != null || _disabled) return;
        var job = _queue.Take(); if (job == null) return;
        NativeDispatch? attempt = null;
        try
        {
            job.CheckCancellation();
            if (job.Arguments is CameraChangeArguments)
            {
                var result = _views.Execute(job); job.Complete(result);
                _log.Write(job.Operation, job.OriginalRequestId, result.success ? "camera_committed" : result.error!.code, operationId: job.OperationId);
                return;
            }
            if (job.Arguments is DocumentArguments { Action: "new" or "open" or "activate" or "activate_layout" })
            {
                _scheduled = job;
                _log.Write(job.Operation, job.OriginalRequestId, "document_ui_enter", operationId: job.OperationId);
                _documents.ExecuteOnUi(job, result =>
                {
                    // Do not clear another slot if a cancelled callback arrives late.
                    job.Complete(result);
                    _log.Write(job.Operation, job.OriginalRequestId, result.success ? "document_completed" : result.error!.code, operationId: job.OperationId);
                    if (ReferenceEquals(_scheduled, job)) Clear();
                });
                _log.Write(job.Operation, job.OriginalRequestId, "document_ui_return", operationId: job.OperationId);
                return;
            }
            Trace(job, "write_preflight_enter");
            var preflightStarted = System.Diagnostics.Stopwatch.StartNew();
            var bound = job.Arguments is DocumentArguments documentArgs
                ? _documents.PreflightSave(documentArgs, check: job.CheckCancellation)
                : _writer.Preflight((IWriteArguments)job.Arguments, false, cancel: job.CheckCancellation);
            Trace(job, "write_preflight_return", elapsedMs: preflightStarted.ElapsedMilliseconds);
            job.CheckCancellation();
            attempt = new(job, bound);
            _scheduled = job; _native = attempt;
            Subscribe(attempt);
            job.CheckCancellation();
            Trace(job, "write_command_submit_enter", CommandName);
            // SDK Editor.Command serializes native typed command arguments and
            // calls hostCommand. The only argument is our generated attempt ID,
            // never CAD/user text. There is no queued fallback or duplicate send.
            bound.Document.Editor.Command(CommandName, attempt.Identity.Token);
            Trace(job, "write_command_submit_return", CommandName);
        }
        catch (DomainException e)
        { FailSubmission(job, attempt, e.Error.code, e.Error.message, e.Error.details); }
        catch (OperationCanceledException)
        { FailSubmission(job, attempt, "CANCELLED", "Cancelled before command execution.", null); }
        catch (System.Exception e)
        {
            FailSubmission(job, attempt, "UI_DISPATCH_UNAVAILABLE", "Native command dispatch failed.",
                new { error_type = e.GetType().Name, dispatch_attempted = attempt != null });
        }
    }
    private void FailSubmission(WriteJob job, NativeDispatch? attempt, string code, string message, object? cause)
    {
        bool started = attempt?.Identity.WriteStarted == true;
        bool deferred = NativePostCommandVerification422.Contains(job);
        if (deferred && attempt?.EndedContext != null)
        {
            Trace(job, "write_submit_error_after_command_end", CommandName);
            return;
        }
        // A deferred command's internal fallback is never a completed outcome.
        var known = deferred ? job.CommittedResult : attempt?.Outcome ?? job.CommittedResult;
        // Token provenance makes a not-started late entry harmless. A genuinely
        // started operation without a known outcome still requires isolation.
        if (started && known == null) _disabled = true;
        job.Complete(known ?? job.Error(started ? "WRITE_OUTCOME_UNKNOWN" : code,
            started ? "Native command dispatch ended without a known write outcome; do not retry with a new ID." : message,
            started ? "unknown" : "not_started", cause: cause));
        Clear(job);
    }
    private void Subscribe(NativeDispatch attempt)
    {
        attempt.StartedHandler = (_, args) => Started(attempt, args);
        attempt.FinishedHandler = (_, args) => Finished(attempt, args, true);
        attempt.InterruptedHandler = (_, args) => Finished(attempt, args, false);
        var document = attempt.Bound.Document;
        document.CommandWillStart += attempt.StartedHandler;
        document.CommandEnded += attempt.FinishedHandler;
        document.CommandCancelled += attempt.InterruptedHandler;
        document.CommandFailed += attempt.InterruptedHandler;
    }
    private void Started(NativeDispatch attempt, CommandEventArgs args)
    {
        if (!string.Equals(args.GlobalCommandName, CommandName, StringComparison.OrdinalIgnoreCase) ||
            !ReferenceEquals(_native, attempt)) return;
        _commandFrames.Push(new(attempt));
        Trace(attempt.Identity.Job, "write_command_start_event", args.GlobalCommandName);
    }
    public void ExecuteCommand(Document? commandDocument)
    {
        if (Environment.CurrentManagedThreadId != _uiThread) throw new InvalidOperationException("Native write command is on the wrong thread.");
        if (commandDocument == null) { Trace(null, "write_command_without_document", CommandName); return; }
        // Consume the transported token even for a delayed/unbound invocation,
        // so it cannot become a separate command-line token. No key synthesis.
        var frame = _commandFrames.TryPeek(out var entryFrame) ? entryFrame : null;
        var input = commandDocument.Editor.GetString(new PromptStringOptions("") { AllowSpaces = false });
        var attempt = _native;
        if (input.Status != PromptStatus.OK || attempt == null || frame == null ||
            !_commandFrames.TryPeek(out var currentFrame) || !ReferenceEquals(currentFrame, frame) ||
            !ReferenceEquals(frame.Origin, attempt) ||
            !attempt.Identity.TryEnter(input.StringResult, _scheduled, commandDocument, attempt.Identity.Job.TryStart))
        { Trace(null, "write_command_attempt_not_matched", CommandName); return; }
        frame.Entered = attempt.Identity;
        var job = attempt.Identity.Job; var bound = attempt.Bound;
        if (!attempt.Identity.WriteStarted) { Trace(job, "write_command_not_started", CommandName); return; }
        Trace(job, "write_command_running", CommandName);
        try { attempt.Outcome = job.Arguments is DocumentArguments ? _documents.Save(job, bound) : _writer.Execute(job, bound); }
        catch (System.Exception e)
        { attempt.Outcome = job.CommittedResult ?? job.Error("WRITE_OUTCOME_UNKNOWN", "Unexpected dispatcher failure; do not retry with a new ID.", "unknown", cause: new { error_type = e.GetType().Name }); }
    }
    private void Finished(NativeDispatch attempt, CommandEventArgs args, bool ended)
    {
        if (!string.Equals(args.GlobalCommandName, CommandName, StringComparison.OrdinalIgnoreCase) ||
            !ReferenceEquals(_native, attempt) || !_commandFrames.TryPeek(out var frame) ||
            !ReferenceEquals(frame.Origin, attempt)) return;
        _commandFrames.Pop();
        if (!attempt.Identity.CanComplete(_native.Identity, frame.Entered, _scheduled, attempt.Bound.Document))
        { Trace(null, "write_command_completion_not_matched", args.GlobalCommandName); return; }
        var job = attempt.Identity.Job; var bound = attempt.Bound;
        Trace(job, "write_command_completion_event", args.GlobalCommandName);
        if (NativePostCommandVerification422.Contains(job))
        {
            if (ended && job.Phase == WritePhase.Committing)
            {
                // Only managed identity/counters are read in this event. Native
                // final-update boundary is observed in a later idle Pump.
                attempt.EndedContext = _writer.Context(bound);
                Trace(job, "write_final_verification_scheduled", args.GlobalCommandName);
                return;
            }
            NativePostCommandVerification422.Clear(job);
            Complete(attempt, job.CommittedResult ?? job.Error("WRITE_OUTCOME_UNKNOWN",
                "Native command was cancelled or failed before final observation; inspect the model and do not repeat this operation with a new ID.", "unknown"));
            return;
        }
        var completion = attempt.Identity.ResolveCompletion(attempt.Outcome, job.CommittedResult, started => job.Error(
            started ? "WRITE_OUTCOME_UNKNOWN" : "CANCELLED",
            started ? "Native command ended after write entry without a known outcome; do not retry with a new ID."
                : "Internal command ended without starting the write.",
            started ? "unknown" : "not_started"));
        if (completion.Isolate) _disabled = true;
        Complete(attempt, completion.Outcome);
    }
    private void FinishDeferred(NativeDispatch attempt)
    {
        var job = attempt.Identity.Job; var bound = attempt.Bound;
        if (!ReferenceEquals(_native, attempt) || !ReferenceEquals(_scheduled, job)) return;
        if (job.Phase == WritePhase.Finished) { Clear(job); return; }
        try
        {
            job.CheckCancellation();
            if (!HostReadGate.IsWindowAvailable()) return;
            var context = _writer.Context(bound);
            var ended = attempt.EndedContext ?? throw new InvalidOperationException("Command end was not observed.");
            if (job.Arguments is not IWriteArguments args ||
                context.bridge_instance_id != args.BridgeId || context.document_id != args.DocumentId ||
                ended.bridge_instance_id != context.bridge_instance_id ||
                ended.document_id != context.document_id ||
                !ReferenceEquals(Application.DocumentManager.MdiActiveDocument, bound.Document))
                throw new DomainException("DOCUMENT_CONTEXT_MISMATCH", "Target document context changed before final native observation.");
            if (HostReadGate.IsBusy(bound.Document)) return;
            job.CheckCancellation();
            if (!NativePostCommandVerification422.TryTake(job, out var verification))
                throw new DomainException("MODEL_STATE_UNAVAILABLE", "Final native observation registration is unavailable.");
            Trace(job, "write_final_verification_enter", CommandName);
            using (verification) Complete(attempt, verification.Execute(job));
        }
        catch (Exception error)
        {
            Complete(attempt, job.CommittedResult ?? job.Error("WRITE_OUTCOME_UNKNOWN",
                "Final native observation could not complete; inspect the model before a dependent write. Do not repeat this operation with a new ID.", "unknown",
                cause: new
                {
                    stage = "post_command_dispatch",
                    error_type = error.GetType().Name,
                    message = error.Message,
                    command_end_revision = attempt.EndedContext?.revision,
                    current_revision = bound.Revision,
                    verification_details = error is DomainException domain ? domain.Error.details : null
                }));
        }
    }
    private void Complete(NativeDispatch attempt, Result outcome)
    {
        var job = attempt.Identity.Job; var bound = attempt.Bound;
        // DocumentRegistry subscribed first and already advanced its counter.
        // Context reads only managed identity/counters; no CAD API is called here.
        if (outcome.context != null) outcome = outcome with { context = _writer.Context(bound) };
        if (outcome.data is DocumentOperationResult document)
            outcome = outcome with { data = document with { document = document.document with { revision = bound.Revision } } };
        job.Complete(outcome);
        try { Trace(job, outcome.success ? "committed" : outcome.error!.code); }
        finally { Clear(job); }
    }
    private void Trace(WriteJob? job, string outcome, string? command = null, long elapsedMs = 0)
    {
        try
        {
            _log.Write(job?.Operation ?? "native_command", job?.OriginalRequestId ?? "", outcome,
            elapsedMs: elapsedMs, transaction: command, operationId: job?.OperationId);
        }
        catch (System.Exception) { /* Diagnostic logging cannot change native scheduling or execution. */ }
    }
    private void Clear(WriteJob? expected = null)
    {
        if (expected != null && !ReferenceEquals(_scheduled, expected)) return;
        var attempt = _native;
        var job = _scheduled;
        _scheduled = null; _native = null; _commandFrames.Clear();
        if (job != null) NativePostCommandVerification422.Clear(job);
        if (attempt != null)
        {
            var document = attempt.Bound.Document;
            try { document.CommandWillStart -= attempt.StartedHandler; }
            catch (Exception) { Trace(job, "write_start_handler_detach_failed", CommandName); }
            try { document.CommandEnded -= attempt.FinishedHandler; }
            catch (Exception) { Trace(job, "write_end_handler_detach_failed", CommandName); }
            try { document.CommandCancelled -= attempt.InterruptedHandler; }
            catch (Exception) { Trace(job, "write_cancel_handler_detach_failed", CommandName); }
            try { document.CommandFailed -= attempt.InterruptedHandler; }
            catch (Exception) { Trace(job, "write_failure_handler_detach_failed", CommandName); }
        }
    }
    private void Closing(object? sender, DocumentCollectionEventArgs args)
    {
        if (_native == null || !ReferenceEquals(_native.Bound.Document, args.Document)) return;
        try
        {
            if (_scheduled is { } job)
                job.Complete(job.CommittedResult ?? job.Error("DOCUMENT_CLOSED", "Target document closed before the operation finished.",
                    job.Phase is WritePhase.Queued or WritePhase.Scheduled ? "not_started" : "unknown"));
        }
        finally { Clear(); }
    }
    public void Dispose()
    {
        _disabled = true;
        try
        {
            if (_scheduled is { } job && NativePostCommandVerification422.Contains(job))
                job.Complete(job.CommittedResult ?? job.Error("BRIDGE_UNAVAILABLE",
                    "Bridge stopped before final native observation; inspect the saved model before another edit.", "unknown"));
        }
        finally
        {
            try { Application.DocumentManager.DocumentToBeDestroyed -= Closing; }
            catch (Exception) { Trace(_scheduled, "write_close_handler_detach_failed", CommandName); }
            Clear();
        }
    }
}
