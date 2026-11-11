using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using HostMgd.ApplicationServices;
using HostMgd.EditorInput;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;
using Application = HostMgd.ApplicationServices.Application;
using CadException = Teigha.Runtime.Exception;

namespace NanoCADBridge;

internal sealed class CadReader
{
    private readonly string _instance;
    private readonly DocumentRegistry _documents;
    private readonly JsonLog _log;
    private readonly HandlePages<EntitySummary> _snapshots = new();
    private readonly StructureReader _structure;
    private readonly int _uiThread = Environment.CurrentManagedThreadId;
    public CadReader(string instance, DocumentRegistry documents, JsonLog log) { _instance = instance; _documents = documents; _log = log; _structure = new(instance); }
    public Result Execute(Request request, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        ReadContext? context = null;
        string transactionState = "not_started";
        Result result;
        PointCloudReadScope402? pointScope = null;
        try
        {
            if (Environment.CurrentManagedThreadId != _uiThread)
                throw new DomainException("UI_THREAD_MISMATCH", "Native reads must execute on the bridge initialization thread.");
            cancellationToken.ThrowIfCancellationRequested();
            if (request.operation == DesktopCaptureBridge.ViewportTargetRead)
                return DesktopCaptureBridge.ReadViewportTarget(request, _documents);
            var args = Validation.Parse(request.operation, request.arguments);
            if (request.operation == "get_documents")
                return new CadDocuments(_instance, _documents).List(request, cancellationToken);
            if (!HostReadGate.IsWindowAvailable())
                throw new DomainException("HOST_BUSY", "The nanoCAD main window is unavailable or disabled by a modal dialog.", true);
            _log.Write(request.operation, request.requestId, "active_document_before");
            var document = Application.DocumentManager.MdiActiveDocument;
            if (document == null) throw new DomainException("NO_DOCUMENT", "No active drawing is open.");
            if (HostReadGate.IsBusy(document))
                throw new DomainException("HOST_BUSY", "The document is busy; finish the current command.", true);
            var state = _documents.Get(document);
            if (args.DocumentId != null && args.DocumentId != state.Id)
                throw new DomainException(_documents.IsOpen(args.DocumentId) ? "DOCUMENT_NOT_ACTIVE" : "DOCUMENT_CLOSED", "The requested document is not the active open document. Obtain get_document again.");
#if READ_ACCEPTANCE_AUDIT
            using var audit = ReadAcceptanceAudit.Begin(document, state, request);
#endif
            using var pointScopeLifetime = pointScope = request.operation == "analyze_point_cloud" ? new PointCloudReadScope402(state) : null;
            // Installed nanoCAD sets DBMOD=1 on LockDocument(Read) itself, even
            // before a transaction. Read only the active, idle document in this
            // uninterrupted UI callback, normally opening objects ForRead. The cloud
            // iterator factory has a scoped technical mode lease; no read commits.
            // The SDK modeless CrossCircleMgdList reader uses the same no-lock
            // approach. Writes will require a separate verified lock policy.
            _log.Write(request.operation, request.requestId, "read_transaction_before");
            // These mixed MultiCAD reads also inspect existing MCS values and
            // sketch relations. A normal uncommitted Teigha transaction aborts
            // on disposal, enclosing the native getters in an unrelated rollback
            // scope. Use tracked open/close lifetime for these two read routes;
            // every explicit open stays ForRead and every invalidation check below
            // remains in force, including after disposal. No read is committed.
            bool modelOpenCloseRead = request.operation == "get_model_variables" ||
                request.operation == "inspect_model_history" && args.ModelFeatureDeletionIds?.Length > 0 ||
                request.operation == "inspect_objects" && args.EraseAssessment;
            Transaction transaction = modelOpenCloseRead
                ? document.Database.TransactionManager.StartOpenCloseTransaction()
                : document.Database.TransactionManager.StartTransaction();
            try
            {
                transactionState = "opened_for_read";
                var revision = state.Revision;
                var selectionRevision = state.SelectionRevision;
                context = new(_instance, state.Id, revision, DateTime.UtcNow.ToString("O"));
                if (args.ExpectedRevision != null && args.ExpectedRevision != revision)
                    throw new DomainException("STALE_DOCUMENT", "The document changed since expected_revision.", true);
                var db = document.Database;
                var table = (BlockTable)transaction.GetObject(db.BlockTableId, OpenMode.ForRead);
                var modelId = table[BlockTableRecord.ModelSpace];
#if LOFT_SURFACE_PROBE
                LoftSurfaceProbe.Observe(_instance,state,request,transaction,modelId,cancellationToken.ThrowIfCancellationRequested);
#endif
#if CORE_READ_PROBE
                CoreReadProbe.Observe(_instance,state,request,transaction,modelId,cancellationToken.ThrowIfCancellationRequested);
#endif
#if CAMERA_READ_PROBE
                CameraReadProbe.Observe(_instance,state,request,cancellationToken.ThrowIfCancellationRequested);
#endif
                _log.Write(request.operation, request.requestId, "read_operation_before");
                object data = request.operation switch
                {
                    "get_document" => DocumentInfo(args, state, modelId),
                    "query_objects" or "get_selection" => ReadPage(request.operation, args, state, transaction, modelId, cancellationToken),
                    "inspect_objects" => Inspect(args, state, transaction, modelId, cancellationToken),
                    "analyze_geometry" => GeometryAnalyzer.Read(GeometryAnalysisValidation.Parse(request.arguments), state, transaction, modelId, _instance, cancellationToken),
                    "inspect_model_history" => ModelHistoryReader.Read(args, state, transaction, modelId, _instance, cancellationToken, includeProfileEditing: true),
                    "get_model_variables" => ModelVariablesReader.Read(args, state, transaction, modelId, _instance, cancellationToken),
                    "inspect_sketch_constraints" => SketchConstraintsReader.Read(args, state, transaction, modelId, _instance, cancellationToken),
                    "inspect_constraints" => DocumentConstraintsReader.Read(args, state, transaction, modelId, _instance, cancellationToken),
                    "get_drawing_views" => DrawingViewsReader.Read(args, state, transaction, _instance, cancellationToken),
                    "inspect_solid_topology" => SolidTopologyReader.Read(args, state, transaction, modelId, _instance, cancellationToken),
                    "inspect_point_cloud" => PointCloudReader.Read(PointCloudInspectValidation.Parse(request.arguments), state, transaction, modelId, _instance, cancellationToken),
                    "analyze_point_cloud" => PointCloudAnalysisReader.Read(PointCloudAnalysisValidation.Parse(request.arguments), state, transaction, modelId, _instance, cancellationToken),
                    "get_structure" => _structure.Read(args, state, transaction, cancellationToken),
                    "inspect_block" => _structure.Inspect(args, state, transaction, modelId, cancellationToken),
                    _ => throw new DomainException("INVALID_ARGUMENT", "Unsupported read operation.")
                };
                cancellationToken.ThrowIfCancellationRequested();
                if (state.Revision != revision) throw new DomainException("STALE_DOCUMENT", "Document invalidated during the read.", true);
                if (request.operation == "get_selection" && state.SelectionRevision != selectionRevision)
                    throw new DomainException("STALE_SELECTION", "Selection invalidated during the read.", true);
                string[] readWarnings = [];
                if (data is ModelSketchConstraintObservation353.HistoryOutcome observed) { data = observed.Data; readWarnings = observed.Warnings; }
                result = Result.Ok(request.requestId, data, context, readWarnings);
                if (JsonSerializer.SerializeToUtf8Bytes(result, Wire.Json).Length > Wire.PayloadBudget)
                    throw new DomainException("RESPONSE_LIMIT_EXCEEDED", "Read exceeds the response budget; request fewer handles or a smaller page.");
            }
            finally
            {
                using var releaseStage = PointCloudReadScope402.Stage("read_transaction_dispose");
                transaction.Dispose();
            }
            transactionState = modelOpenCloseRead ? "disposed_open_close_no_commit" : "disposed_no_commit";
            if (!_documents.IsOpen(state.Id)) throw new DomainException("DOCUMENT_CLOSED", "The document closed during the read.");
            if (!ReferenceEquals(Application.DocumentManager.MdiActiveDocument, document))
                throw new DomainException("DOCUMENT_NOT_ACTIVE", "The active document changed during the read.");
            if (state.Revision != context!.revision) throw new DomainException("STALE_DOCUMENT", "The document changed while the read transaction was disposed.", true);
            if (pointScope != null)
            {
                pointScope.VerifyAfterTransaction(_instance, cancellationToken);
                // Source verification is itself a read; context changes there remain stale.
                if (!_documents.IsOpen(state.Id)) throw new DomainException("DOCUMENT_CLOSED", "The document closed during point-source verification.");
                if (!ReferenceEquals(Application.DocumentManager.MdiActiveDocument, document))
                    throw new DomainException("DOCUMENT_NOT_ACTIVE", "The active document changed during point-source verification.");
                if (state.Revision != context.revision)
                    throw new DomainException("STALE_DOCUMENT", "Document changed during point-source verification.", true);
                result = Result.Ok(request.requestId, pointScope.AttachStatus(result.data!), context, result.warnings);
                if (JsonSerializer.SerializeToUtf8Bytes(result, Wire.Json).Length > Wire.PayloadBudget)
                    throw new DomainException("RESPONSE_LIMIT_EXCEEDED", "Point read diagnostics exceed the response budget; request a smaller page.");
                cancellationToken.ThrowIfCancellationRequested();
                pointScope.Accept();
            }
            if (request.operation == "inspect_solid_topology" && (args.IncludeNativeModelFaces || args.IncludeNativeModelEdges))
            {
                long bits = unchecked((long)ulong.Parse(args.Handles![0], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                if (!document.Database.TryGetObjectId(new Handle(bits), out var sourceId) || sourceId.IsNull || !sourceId.IsValid || sourceId.IsErased)
                    throw new DomainException("OBJECT_NOT_FOUND", "Model body changed before native face inspection.");
                using var nativeTrace = new NativeTopologyReadTrace362(state);
                var data = SolidTopologyReader.AttachNativeFaces(result.data!, sourceId, state, _instance, cancellationToken, args.IncludeNativeModelFaces, args.IncludeNativeModelEdges);
                cancellationToken.ThrowIfCancellationRequested();
                if (state.Revision != context.revision)
                    throw new DomainException("STALE_DOCUMENT", "Document changed during native face inspection.", true, nativeTrace.Snapshot());
                result = Result.Ok(request.requestId, data, context);
                if (JsonSerializer.SerializeToUtf8Bytes(result, Wire.Json).Length > Wire.PayloadBudget)
                    throw new DomainException("RESPONSE_LIMIT_EXCEEDED", "Native topology exceeds the response budget.");
            }
        }
        catch (DomainException e)
        {
            var error = pointScope == null ? e.Error : e.Error with { details = new { cause = e.Error.details, point_cloud_read_events = pointScope.Snapshot() } };
            result = Result.Fail(request.requestId, error, context);
        }
        catch (OperationCanceledException) { result = Result.Fail(request.requestId, new("CANCELLED", "Read cancelled; late results discarded.", details: pointScope?.Snapshot()), context); }
        catch (CadException e)
        {
            string native = e.ErrorStatus.ToString();
            object details = pointScope == null ? new { api_status = native } : new { api_status = native, point_cloud_read_events = pointScope.Snapshot() };
            result = Result.Fail(request.requestId, new(native.Contains("Lock", StringComparison.OrdinalIgnoreCase) ? "DOCUMENT_LOCKED" : "TRANSACTION_FAILED",
                "nanoCAD could not complete the read transaction.", true, details), context);
        }
        catch (System.Exception e)
        {
            object details = pointScope == null ? new { error_type = e.GetType().Name } : new { error_type = e.GetType().Name, point_cloud_read_events = pointScope.Snapshot() };
            result = Result.Fail(request.requestId, new("API_EXCEPTION", "Unexpected native read/serialization failure.", false, details), context);
        }
        if (transactionState == "opened_for_read") transactionState = "disposed_after_error";
        _log.Write(request.operation, request.requestId, result.success ? "ok" : result.error!.code, watch.ElapsedMilliseconds, transactionState);
        return result;
    }
    private object DocumentInfo(Arguments args, DocumentState state, ObjectId modelId)
    {
        var document = state.Document; var db = document.Database;
        var data = new Dictionary<string, object?>
        {
            ["document_id"] = state.Id,
            ["name"] = Path.GetFileName(document.Name),
            ["path"] = document.IsNamedDrawing ? document.Name : null,
            ["is_named"] = document.IsNamedDrawing,
            ["is_read_only"] = document.IsReadOnly,
            ["units"] = new { name = db.Insunits.ToString(), value = (int)db.Insunits },
            ["active_space"] = db.CurrentSpaceId == modelId ? "model" : "paper",
            ["revision"] = state.Revision,
            ["fingerprint_guid"] = db.FingerprintGuid
        };
        if (args.IncludeView)
        {
            var observation = CameraViewReader.Read(_instance, state);
            data["view"] = observation.view; data["view_unavailable_reason"] = observation.view_unavailable_reason;
        }
        return data;
    }
    private object ReadPage(string operation, Arguments args, DocumentState state, Transaction transaction, ObjectId modelId, CancellationToken cancellationToken)
    {
        bool selection = operation == "get_selection";
        var queryOwner = !selection && args.ReadSpace == "paper" ? CadWriteSpace.Resolve(state.Document.Database, transaction, "paper") : modelId;
        // Check on continuation pages too: disabling PICKFIRST invalidates the
        // ability to interpret an earlier implied-selection snapshot.
        if (selection) HostReadGate.RequireImpliedSelection();
        var selectionRevision = selection ? state.SelectionRevision : null;
        var watch = Stopwatch.StartNew();
        int unavailableBounds = 0;
        (Entity entity, EntitySummary summary)? ReadMatch(ulong handle)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!state.Document.Database.TryGetObjectId(new Handle(unchecked((long)handle)), out var id) || !id.IsValid || id.IsErased || id.IsEffectivelyErased)
                throw new DomainException("STALE_DOCUMENT", "An indexed object is no longer available.", true);
            if (transaction.GetObject(id, OpenMode.ForRead) is not Entity entity)
                throw new DomainException("OBJECT_READ_FAILED", "Indexed identity is not an entity.");
            bool paper = entity.OwnerId != modelId && transaction.GetObject(entity.OwnerId, OpenMode.ForRead) is BlockTableRecord owner && owner.IsLayout;
            var summary = EntityAdapter.Summary(entity, modelId, _instance, state.Id, paper);
            if (args.Types != null && !args.Types.Contains(summary.type)) return null;
            if (args.Layers != null && !args.Layers.Contains(summary.layer!.name, StringComparer.OrdinalIgnoreCase)) return null;
            if (!CadTargetedRead407.MatchBlock(entity, args.QueryBlock, transaction, cancellationToken.ThrowIfCancellationRequested)) return null;
            if (!CadTargetedRead407.MatchText(entity, args.QueryText, cancellationToken.ThrowIfCancellationRequested)) return null;
            return (entity, summary);
        }
        EntitySummary? Read(ulong handle)
        {
            if (ReadMatch(handle) is not { } match) return null;
            if (!CadTargetedRead407.MatchBounds(match.entity, args.QueryBounds, cancellationToken.ThrowIfCancellationRequested, out var unavailable))
            { if (unavailable) unavailableBounds++; return null; }
            return match.summary;
        }
        object Convert(HandlePages<EntitySummary>.Page page)
        {
            if (args.QueryBounds == null && args.QueryText == null && args.QueryBlock == null)
                return new Page(page.items, page.total_count, page.returned_count, page.complete, page.next_cursor, page.selection_revision);
            return new
            {
                page.items,
                page.total_count,
                page.returned_count,
                page.complete,
                page.next_cursor,
                page.selection_revision,
                match_semantics = CadTargetedRead407.Semantics(args, unavailableBounds)
            };
        }
        // Yield a continuation, including an empty filtered page, rather than reject
        // a drawing for exceeding a scan count/time. No entity DTOs are read ahead.
        bool Yield() => watch.ElapsedMilliseconds >= 200;
        if (args.Cursor != null) return Convert(_snapshots.Next(args.Cursor, state.Id, state.Revision, selectionRevision, args.FilterKey(operation), args.PageSize, Read, Yield));
        IEnumerable<ulong> Identities()
        {
            IEnumerable<ObjectId> ids;
            if (selection)
            {
                var selected = state.Document.Editor.SelectImplied();
                if (selected.Status == PromptStatus.Error) yield break;
                if (selected.Status != PromptStatus.OK || selected.Value == null) throw new DomainException("OBJECT_READ_FAILED", "Implied selection could not be read.");
                ids = selected.Value.GetObjectIds();
            }
            else ids = ((BlockTableRecord)transaction.GetObject(queryOwner, OpenMode.ForRead)).Cast<ObjectId>();
            foreach (var id in ids)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (id.IsNull || !id.IsValid) throw new DomainException("OBJECT_READ_FAILED", "Invalid enumerated identity.");
                if (!id.IsErased && !id.IsEffectivelyErased) yield return unchecked((ulong)id.Handle.Value);
            }
            watch.Restart();
        }
        if (args.SummaryOnly)
        {
            var summary = new QuerySummary();
            foreach (var handle in Identities())
                if (ReadMatch(handle) is { } match)
                    summary.Add(match.summary.type, match.summary.layer!.name,
                        CadTargetedRead407.ReadBounds(match.entity, cancellationToken.ThrowIfCancellationRequested), args.QueryBounds);
            return summary.Result(args.ReadSpace);
        }
        return Convert(_snapshots.First(state.Id, state.Revision, selectionRevision, args.FilterKey(operation), Identities(), args.PageSize, Read, Yield));
    }
    private object Inspect(Arguments args, DocumentState state, Transaction transaction, ObjectId modelId, CancellationToken cancellationToken)
    {
        var items = new List<object>();
        int failed = 0;
        int bytes = 4096;
        foreach (var handle in args.Handles!)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                ulong bits = ulong.Parse(handle, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                if (!state.Document.Database.TryGetObjectId(new Handle(unchecked((long)bits)), out var id) || id.IsNull || !id.IsValid)
                    throw new DomainException("OBJECT_NOT_FOUND", "Handle was not found in this document.");
                if (id.IsErased || id.IsEffectivelyErased) throw new DomainException("OBJECT_ERASED", "The entity has been erased.");
                if (transaction.GetObject(id, OpenMode.ForRead) is not Entity entity)
                    throw new DomainException("OUT_OF_SCOPE", "The handle does not identify an entity.");
                bool paper = entity.OwnerId != modelId && transaction.GetObject(entity.OwnerId, OpenMode.ForRead) is BlockTableRecord owner && owner.IsLayout;
                var summary = EntityAdapter.Summary(entity, modelId, _instance, state.Id, paper);
                if (entity.OwnerId != modelId && !paper) throw new DomainException("OUT_OF_SCOPE", "Only top-level Model or paper-layout entities can be inspected; nested block content uses inspect_block.", false, new { summary });
                bool needsGeometry = args.DetailSections == null || args.DetailSections.Contains("geometry", StringComparer.Ordinal) || args.DetailSections.Contains("editing", StringComparer.Ordinal);
                if (summary.type == "Unknown" && needsGeometry) throw new DomainException("UNSUPPORTED_ENTITY_TYPE", "Geometry for this type is not supported; sections:[] can still read its identity and owner.", false, new { summary });
                if (summary.type == "PointCloud" && needsGeometry) throw new DomainException("SPECIALIZED_READER_REQUIRED", "Use inspect_point_cloud for metadata/features or analyze_point_cloud for points, sections and measurements.", false, new { summary });

                if (args.DetailSections != null)
                {
                    var detail = CadTargetedRead407.Compact(entity, summary, args.DetailSections, cancellationToken);
                    if (needsGeometry) CadDynamicBlockObservation.Compact(detail, entity, transaction, _instance, state.Id, cancellationToken.ThrowIfCancellationRequested);
                    items.Add(new { handle, success = true, entity = detail, error = (DomainError?)null });
                }
                else
                {
                    var detail = EntityAdapter.Detail(entity, summary, cancellationToken, includeNativeCurve: true);
                    detail = CadDynamicBlockObservation.Detail(detail, entity, transaction, _instance, state.Id, cancellationToken.ThrowIfCancellationRequested);
                    items.Add(new ItemResult(handle, true, detail, null));
                }
            }
            catch (DomainException e) { failed++; items.Add(new ItemResult(handle, false, null, e.Error)); }
            catch (CadException e) when (!e.ErrorStatus.ToString().Contains("Lock", StringComparison.OrdinalIgnoreCase))
            { failed++; items.Add(new ItemResult(handle, false, null, new("OBJECT_READ_FAILED", "nanoCAD could not read this object.", false, new { api_status = e.ErrorStatus.ToString() }))); }
            bytes += JsonSerializer.SerializeToUtf8Bytes(items[^1], Wire.Json).Length;
            if (bytes > Wire.PayloadBudget) throw new DomainException("RESPONSE_LIMIT_EXCEEDED", "Batch exceeds byte budget; request fewer handles.");
        }
        if (args.EraseAssessment)
        {
            // The complete selected set is assessed together. Per-object
            // inspection failures stay visible and do not manufacture missing
            // dependency observations or individual eligibility claims.
            var assessment = CadEraseAssessment444.Read(args, state, transaction, _instance, cancellationToken);
            return new { items, complete = failed == 0, failed_count = failed, erase_assessment = assessment };
        }
        if (failed == items.Count) throw new DomainException("BATCH_READ_FAILED", "All requested objects failed to read.", false, new { items });
        return new { items, complete = failed == 0, failed_count = failed };
    }
}
