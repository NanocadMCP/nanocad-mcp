using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// A synchronous request-local classification of notifications from the SDK's
// getter-only point iterator access. This never resets a revision or ignores a
// different object/event/context. On any unsuccessful read all buffered events
// become normal document invalidations, including cancellation and cleanup failure.
internal sealed class PointCloudReadScope402 : IDisposable
{
    [ThreadStatic] private static PointCloudReadScope402? _current;
    private readonly PointCloudReadScope402? _prior;
    private readonly DocumentState _state;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly string _startRevision;
    private readonly string _startSelection;
    private readonly List<EventRow> _events = new();
    private string? _stage;
    private ObjectId _source;
    private string? _sourceHandle;
    private string? _sourceProof;
    private long _deferred, _observed, _omitted;
    private int _traceBytes;
    private bool _leaseAcquired, _leaseRestored, _iteratorReleased;
    private bool _sourceVerified, _postTransactionVerified, _verificationOnly, _otherEvent, _accepted, _disposed;

    internal PointCloudReadScope402(DocumentState state)
    {
        _state = state; _startRevision = state.Revision; _startSelection = state.SelectionRevision;
        _prior = _current; _current = this;
    }
    private static bool TechnicalStage(string? stage) => stage is
        "iterator_factory_upgrade" or "iterator_creation" or "iterator_factory_downgrade" or
        "iterator_release" or "point_cloud_source_release" or "read_transaction_dispose";

    // No DB opens, metadata queries, native wrapper storage or serialization in callbacks.
    // If the event's identity cannot be safely read, the ordinary counter wins.
    internal static bool Modified(DocumentState state, ObjectEventArgs args)
    {
        var scope = _current;
        if (scope == null || !ReferenceEquals(scope._state, state) || scope._disposed ||
            Environment.CurrentManagedThreadId != scope._thread) return false;
        try
        {
            var id = args.DBObject.ObjectId;
            bool exact = !scope._source.IsNull && id == scope._source;
            bool candidate = exact && !scope._verificationOnly && TechnicalStage(scope._stage);
            scope.Record("ObjectModified", exact ? scope._sourceHandle : SafeHandle(id), candidate);
            if (!candidate) { scope._otherEvent = true; return false; }
            scope._deferred++;
            return true;
        }
        catch
        {
            scope._otherEvent = true;
            return false;
        }
    }
    private static string? SafeHandle(ObjectId id)
    {
        try { return id.IsNull ? null : id.Handle.ToString(); }
        catch { return null; }
    }
    internal static void Other(DocumentState state, string kind, ObjectEventArgs? args = null, string? detail = null)
    {
        var scope = _current;
        if (scope == null || !ReferenceEquals(scope._state, state) || scope._disposed) return;
        try { scope._otherEvent = true; scope.Record(kind, args == null ? null : SafeHandle(args.DBObject.ObjectId), false, detail); }
        catch { scope._otherEvent = true; try { scope.Record(kind, null, false, "identity_unavailable"); } catch { } }
    }
    internal static void ContextChanged(string kind, string? detail = null)
    {
        var scope = _current;
        if (scope == null || scope._disposed) return;
        try { scope._otherEvent = true; scope.Record(kind, null, false, detail); }
        catch { scope._otherEvent = true; }
    }
    private void Record(string kind, string? handle, bool candidate, string? detail = null)
    {
        _observed++;
        var stage = _stage ?? "outside_technical_stage";
        long bytes = 192L + 6L * (kind.Length + stage.Length + (handle?.Length ?? 0) + (detail?.Length ?? 0));
        if (_traceBytes + bytes <= 6144)
        {
            _traceBytes += (int)bytes;
            _events.Add(new EventRow(kind, handle, stage, candidate ? "deferred_exact_insertion" : "ordinary_invalidation", detail));
        }
        else _omitted++;
    }
    internal static IDisposable Stage(string stage)
    {
        var scope = _current;
        if (scope == null) return EmptyStage.Instance;
        var prior = scope._stage; scope._stage = stage;
        return new NativeStage(scope, prior);
    }
    private sealed class NativeStage : IDisposable
    {
        private readonly PointCloudReadScope402 _scope;
        private readonly string? _prior;
        internal NativeStage(PointCloudReadScope402 scope, string? prior) { _scope = scope; _prior = prior; }
        public void Dispose() => _scope._stage = _prior;
    }
    private sealed class EmptyStage : IDisposable
    {
        internal static readonly EmptyStage Instance = new();
        public void Dispose() { }
    }

    internal static void BindSource(PointCloudReader.Source source)
    {
        var scope = _current;
        if (scope == null || !ReferenceEquals(scope._state, source.State)) return;
        if (!scope._source.IsNull && scope._source != source.Id)
            throw new DomainException("POINT_CLOUD_SOURCE_CHANGED", "A point read cannot replace its selected insertion during access.");
        scope._source = source.Id; scope._sourceHandle = source.Id.Handle.ToString();
        scope._sourceProof = source.ObservationProof;
    }
    internal static void LeaseAcquired() { if (_current != null) _current._leaseAcquired = true; }
    internal static void LeaseRestored() { if (_current != null) _current._leaseRestored = true; }
    internal static void IteratorReleased() { if (_current != null) _current._iteratorReleased = true; }
    internal static void SourceVerified(PointCloudReader.Source source, CancellationToken cancellation)
    {
        var scope = _current;
        if (scope == null || !ReferenceEquals(scope._state, source.State)) return;
        scope._sourceVerified = scope._sourceProof != null && source.VerifyObservationProof(cancellation) == scope._sourceProof;
        if (scope._sourceProof != null && !scope._sourceVerified)
            throw new DomainException("POINT_CLOUD_SOURCE_CHANGED", "The selected insertion's native bounds or owner changed during point access.");
    }

    // Verify after the original no-commit transaction has fully disposed. This opens
    // only fresh ForRead metadata wrappers, and its own transaction must dispose
    // before any candidate events can be accepted. No iterator or source load here.
    internal void VerifyAfterTransaction(string instance, CancellationToken cancellation)
    {
        if (_deferred == 0) return;
        if (_source.IsNull || _sourceProof == null || !_leaseAcquired || !_leaseRestored ||
            !_iteratorReleased || !_sourceVerified || _otherEvent || _state.Revision != _startRevision ||
            _state.SelectionRevision != _startSelection)
            throw new DomainException("STALE_DOCUMENT", "Point-cloud access notifications could not be classified as an unchanged getter-only source read.", true);
        _verificationOnly = true;
        try
        {
            using var phase = Stage("point_cloud_post_transaction_verification");
            using var tx = _state.Document.Database.TransactionManager.StartTransaction();
            using var source = PointCloudReader.Source.Open(_source, _state, tx, instance, cancellation);
            _postTransactionVerified = source.VerifyObservationProof(cancellation) == _sourceProof;
        }
        finally { _verificationOnly = false; }
        cancellation.ThrowIfCancellationRequested();
        if (!_postTransactionVerified || _otherEvent || _state.Revision != _startRevision ||
            _state.SelectionRevision != _startSelection)
            throw new DomainException("STALE_DOCUMENT", "Point-cloud source or document context changed during post-transaction read verification.", true);
    }
    internal object AttachStatus(object data)
    {
        var node = JsonSerializer.SerializeToNode(data, Wire.Json);
        if (node is not JsonObject root || root["frame_observation"] is not JsonObject frame ||
            frame["status"] == null)
            throw new DomainException("POINT_CLOUD_RESULT_INVALID", "Point read did not provide its diagnostic frame observation.");
        var trace = new
        {
            scope = "exact_insertion_getter_only_sdk_access",
            notification_count = _deferred, observed_events = _observed, omitted_events = _omitted,
            source_verified = _sourceVerified, post_transaction_verified = _postTransactionVerified,
            stages = _events.Select(e => e.stage).Distinct().ToArray(),
            basis = _deferred == 0 ? "no_deferred_notifications" : "unchanged_native_identity_metadata_bounds_after_no_commit_disposal"
        };
        frame["status"] = frame["status"]!.GetValue<string>() + " Technical access notification observation: " + JsonSerializer.Serialize(trace, Wire.Json) + ".";
        return JsonSerializer.SerializeToElement(node, Wire.Json);
    }
    internal void Accept()
    {
        if (_deferred != 0 && (!_postTransactionVerified || _otherEvent || _state.Revision != _startRevision ||
            _state.SelectionRevision != _startSelection))
            throw new DomainException("STALE_DOCUMENT", "Point-cloud notifications failed the final unchanged-source classification.", true);
        _accepted = true;
    }
    internal object Snapshot() => new
    {
        scope = "actual_point_cloud_read_notifications_no_counter_rewind",
        start_revision = _startRevision, end_revision = _state.Revision,
        selected_insertion_handle = _sourceHandle, deferred_notifications = _deferred,
        disposition = _accepted ? "coalesced_verified_technical_access" : _disposed ? "counted_as_document_changes" : "pending_classification",
        lease_acquired = _leaseAcquired, lease_restored = _leaseRestored, iterator_released = _iteratorReleased,
        source_verified = _sourceVerified, post_transaction_verified = _postTransactionVerified,
        other_event_observed = _otherEvent, observed_events = _observed, omitted_events = _omitted, events = _events
    };
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_accepted)
            for (long count = 0; count < _deferred; count++) _state.RecordDeferredPointCloudChange();
        if (ReferenceEquals(_current, this)) _current = _prior;
    }
    private sealed record EventRow(string kind, string? handle, string stage, string disposition, string? detail);
}
