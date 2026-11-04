using HostMgd.ApplicationServices;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;
using Application = HostMgd.ApplicationServices.Application;

namespace NanoCADBridge;

// Product document operations. No UI input, COM, process creation or arbitrary commands.
internal sealed class CadDocuments
{
    private readonly string _instance;
    private readonly DocumentRegistry _registry;
    private readonly int _uiThread = Environment.CurrentManagedThreadId;
    public bool ApplicationOperationRunning { get; private set; }
    public CadDocuments(string instance, DocumentRegistry registry) { _instance = instance; _registry = registry; }
    private Document? Ready(string? bridgeId, bool insideCommand = false)
    {
        if (Environment.CurrentManagedThreadId != _uiThread) throw new DomainException("UI_THREAD_MISMATCH", "Document operations require the host UI thread.");
        if (bridgeId != null && bridgeId != _instance) throw new DomainException("BRIDGE_UNAVAILABLE", "Request addresses another bridge lifetime.");
        if (!HostReadGate.IsWindowAvailable()) throw new DomainException("HOST_BUSY", "The host window is disabled or unavailable.", true);
        var active = Application.DocumentManager.MdiActiveDocument;
        if (!insideCommand && active != null && HostReadGate.IsBusy(active)) throw new DomainException("HOST_BUSY", "Finish the current command or dialog.", true);
        return active;
    }
    private DocumentState[] States(Action? check=null)
    {
        var states = new List<DocumentState>();
        var capacity=new SerializedCapacity357(Wire.PayloadBudget-4096,check);
        foreach (Document document in Application.DocumentManager)
        {
            check?.Invoke();var state=_registry.Get(document);capacity.Add(Info(state));states.Add(state);
        }
        return states.ToArray();
    }
    public static DocumentInfo Info(DocumentState state)
    {
        var document = state.Document;
        return new(state.Id, Path.GetFileName(document.Name), document.IsNamedDrawing ? document.Name : null,
            document.IsNamedDrawing, document.IsReadOnly, ReferenceEquals(Application.DocumentManager.MdiActiveDocument, document), state.Revision);
    }
    public ReadContext Context(DocumentState state) => new(_instance, state.Id, state.Revision, DateTime.UtcNow.ToString("O"));
    public Result List(Request request, CancellationToken cancellation)
    {
        var args = Validation.Parse(request.operation, request.arguments);
        cancellation.ThrowIfCancellationRequested();
        var active = Ready(args.BridgeId);
#if READ_ACCEPTANCE_AUDIT
        using var audit = active == null ? null : ReadAcceptanceAudit.Begin(active, _registry.Get(active), request);
#endif
        var states = States(cancellation.ThrowIfCancellationRequested);
        cancellation.ThrowIfCancellationRequested();
        var result = Result.Ok(request.requestId, new DocumentList(_instance, active == null ? null : _registry.Get(active).Id, states.Select(Info).ToArray()));
        if (System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(result, Wire.Json).Length > Wire.PayloadBudget)
            throw new DomainException("RESPONSE_LIMIT_EXCEEDED", "Open-document metadata exceeds the response budget.");
        return result;
    }
    private DocumentState Target(DocumentArguments args, bool mustBeActive, bool insideCommand = false, DocumentState? bound = null)
    {
        var active = Ready(args.BridgeId, insideCommand);
        var state = States().SingleOrDefault(s => s.Id == args.DocumentId) ?? throw new DomainException("DOCUMENT_CLOSED", "Target document is no longer open.");
        if (bound != null && !ReferenceEquals(bound, state)) throw new DomainException("DOCUMENT_CLOSED", "Target document lifetime changed.");
        if (mustBeActive && !ReferenceEquals(active, state.Document)) throw new DomainException("DOCUMENT_NOT_ACTIVE", "Activate the target document before saving.");
        if (state.Revision != args.ExpectedRevision) throw new DomainException("STALE_DOCUMENT", "Target changed since expected_revision.", true);
        return state;
    }
    private static bool SamePath(string? a, string? b) => a != null && b != null &&
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
    private string SavePath(DocumentArguments args, DocumentState state)
    {
        string? path = args.Path ?? (state.Document.IsNamedDrawing ? state.Document.Name : null);
        if (path == null) throw new DomainException("PATH_REQUIRED", "Specify a DWG path to save an unnamed drawing.");
        path = DocumentValidation.NormalizePath(path, args.Action);
        foreach (var other in States())
            if (other.Id != state.Id && other.Document.IsNamedDrawing && SamePath(other.Document.Name, path))
                throw new DomainException("FILE_OPEN_IN_HOST", "Target belongs to another open document; it will not be overwritten.");
        DocumentFilePolicy.Check(path, false, args.Overwrite);
        return path;
    }
    private static void CheckDwgLinetypes(Database database, Action? check)
    {
        using var transaction = database.TransactionManager.StartTransaction();
        var table = (LinetypeTable)transaction.GetObject(database.LinetypeTableId, OpenMode.ForRead);
        foreach (ObjectId id in table)
        {
            check?.Invoke(); if (id.IsErased) continue;
            if (transaction.GetObject(id, OpenMode.ForRead) is not LinetypeTableRecord record || record.GetType() != typeof(LinetypeTableRecord) || record.OwnerId != table.ObjectId)
                throw new DomainException("UNSUPPORTED_DWG_CONTENT", "Unrecognized linetype record; safe DWG saving cannot be established.");
            DwgSaveValidation.RequireLinetypeElementCount(record.NumDashes);
        }
        check?.Invoke();
        // ForRead only; disposal without Commit, before any native save boundary.
    }
    public DocumentState PreflightSave(DocumentArguments args, bool insideCommand = false, DocumentState? bound = null, Action? check = null)
    {
        var state = Target(args, true, insideCommand, bound);
        if (state.Document.IsReadOnly) throw new DomainException("DOCUMENT_READ_ONLY", "Read-only documents cannot be saved by this capability.");
        _ = SavePath(args, state);
        if (args.Action == "save") CheckDwgLinetypes(state.Document.Database, check);
        return state;
    }

    public (ReadContext? Context, object? Plan) Preview(DocumentArguments args, Action check)
    {
        check();
        Ready(args.BridgeId);
        if (args.Action is "save" or "export_dxf")
        {
            var target = PreflightSave(args, check: check);
            return (Context(target), new { action = args.Action, destination = SavePath(args, target) });
        }
        if (args.Action is "activate" or "activate_layout")
        {
            var target = Target(args, args.Action == "activate_layout");
            if (args.Action == "activate") return (Context(target), new { action = args.Action, target_document_id = target.Id });
            var layout = ResolveLayout(args, target);
            return (Context(target), new { action = args.Action, layout_handle = args.LayoutHandle, layout_name = layout.Name });
        }
        if (args.Action == "open")
        {
            var existing = FindOpenSource(args, check);
            if (existing == null) DocumentFilePolicy.Check(args.Path!, true, false);
            return (existing == null ? null : Context(existing), new { action = args.Action, reused_open_document = existing != null });
        }
        return (null, null);
    }

    private DocumentState? FindOpenSource(DocumentArguments args, Action check)
    {
        var existing = States(check).SingleOrDefault(s => s.Document.IsNamedDrawing && SamePath(s.Document.Name, args.Path));
        if (existing != null && existing.Document.IsReadOnly != args.ReadOnly)
            throw new DomainException("DOCUMENT_MODE_MISMATCH", "The file is already open in a different read-only mode; it will not be reopened.");
        return existing;
    }

    private static (string Name, ObjectId Block, bool Model) ResolveLayout(DocumentArguments args, DocumentState state)
    {
        if (state.Document.IsReadOnly) throw new DomainException("DOCUMENT_READ_ONLY", "Layout activation may initialize native paper-space content; a writable document is required.");
        var database = state.Document.Database;
        var layoutId = CadStructureWriter.Find(database, args.LayoutHandle!);
        using var transaction = database.TransactionManager.StartTransaction();
        if (transaction.GetObject(layoutId, OpenMode.ForRead) is not Layout layout || layout.GetType() != typeof(Layout) || layout.OwnerId != database.LayoutDictionaryId)
            throw new DomainException("UNSUPPORTED_LAYOUT", "Expected an existing native layout from the active document.");
        var blockId = layout.BlockTableRecordId;
        if (transaction.GetObject(blockId, OpenMode.ForRead) is not BlockTableRecord block || block.GetType() != typeof(BlockTableRecord) ||
            block.OwnerId != database.BlockTableId || block.LayoutId != layoutId || !block.IsLayout)
            throw new DomainException("UNSUPPORTED_LAYOUT", "Layout has inconsistent paper/model block ownership.");
        return (LayoutMetadata.Text(layout.LayoutName), blockId, layout.ModelType);
    }
    public Result Save(WriteJob job, DocumentState bound)
    {
        ReadContext? context = null;
        bool started = false;
        try
        {
            job.CheckCancellation();
            var args = (DocumentArguments)job.Arguments;
            var state = PreflightSave(args, true, bound, job.CheckCancellation); context = Context(state);
            string path = SavePath(args, state);
            var database = state.Document.Database;
            // Resolve the already-loaded native full-document route before
            // crossing the save boundary; no raw fallback after native failure.
            var owningSave=args.Action=="save"?NativeDocumentSave358.Resolve(state,job.CheckCancellation):null;
            // Runs inside the existing fixed native command and its automatic
            // document lock. Save is separate from geometry transactions.
            job.BeginCommit(); started = true;
            if (args.Action == "export_dxf")
            {
                // DxfOut mutates bookkeeping in its database on this host.
                // Export a complete detached database. The surrounding command
                // still marks source DBMOD=1 on this host; see the recorded issue.
                using var copy = database.Wblock();
                copy.DxfOut(path, 16, DwgVersion.AC1032);
            }
            else if(owningSave!=null)owningSave(path);
            else database.SaveAs(path, true, DwgVersion.AC1032, database.SecurityParameters);
            var file = new FileInfo(path);
            if (!file.Exists || file.Length == 0) throw new IOException("Native save did not produce a nonempty target file.");
            var info = Info(state);
            if (args.Action == "save" && !SamePath(info.path, path))
                throw new DomainException("DOCUMENT_PATH_NOT_UPDATED", "A file was written but the host did not adopt the Save As path.");
            var result = Result.Ok(job.OriginalRequestId, new DocumentOperationResult(job.OperationId, "completed", false, args.Action,
                info, path, file.Length, false), Context(state), args.Action == "export_dxf"
                    ? new[] { "EXPORT_SOURCE_MAY_BE_DIRTY: native export may mark the source document modified; its original file is not saved automatically." }
                    : owningSave!=null?new[]{NativeDocumentSave358.VersionWarning}:Array.Empty<string>());
            job.RecordCommit(result);
            return result;
        }
        catch (System.Exception error) { return Failure(job, error, started, context); }
    }
    public void ExecuteOnUi(WriteJob job, Action<Result> finish)
    {
        var args = (DocumentArguments)job.Arguments;
        Ready(args.BridgeId);
        job.CheckCancellation();
        // The idle/timer dispatcher already owns the UI thread with no native
        // command active. ExecuteInApplicationContext and the password Open
        // overload are API-logger stubs in the installed host. Never call them.
        // Keep the original cancellation boundary before any native operation.
        if (!job.TryStart()) { finish(job.Error("CANCELLED", "Document operation expired before application context.", "not_started")); return; }
        bool started = false;
        ApplicationOperationRunning = true;
        Result result;
        try
        {
            job.CheckCancellation(); Ready(args.BridgeId);
            DocumentState state; bool reused = false;
            if (args.Action == "new")
            {
                var previous = States().Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
                job.CheckCancellation();
                job.BeginCommit(); started = true;
                // Delegate template selection to the host. Never replace the
                // user's settings with bootstrap.dxf or a detached blank DB.
                var document = Application.DocumentManager.Add(string.Empty);
                state = _registry.Get(document ?? throw new IOException("Native new returned no document."));
                if (previous.Contains(state.Id)) throw new IOException("Native new returned an existing document.");
                Application.DocumentManager.MdiActiveDocument = state.Document;
            }
            else if (args.Action == "activate_layout")
            {
                state = Target(args, true);
                var database = state.Document.Database;
                var (name, blockId, model) = ResolveLayout(args,state);
                job.CheckCancellation();
                job.BeginCommit(); started = true;
                // Genuine layout navigation on the existing UI dispatcher. Native
                // activation may initialize a previously empty paper layout.
                LayoutManager.Current.CurrentLayout = name;
                if (!model)
                {
                    // Native layout selection can retain an entered model viewport.
                    // Paper activation must enter the sheet before later Paper writes.
                    job.CheckCancellation();
                    object currentViewport = Application.GetSystemVariable("CVPORT");
                    int viewportNumber = currentViewport switch { int value => value, short value => value,
                        _ => throw new DomainException("LAYOUT_NOT_ACTIVE", "The native current viewport number is unavailable.") };
                    if (viewportNumber != 1) state.Document.Editor.SwitchToPaperSpace();
                    job.CheckCancellation();
                    object activeViewport = Application.GetSystemVariable("CVPORT");
                    if (activeViewport is not (int or short) || Convert.ToInt32(activeViewport) != 1)
                        throw new DomainException("LAYOUT_NOT_ACTIVE", "The host did not enter the requested paper sheet outside its model viewport.");
                }
                if (database.CurrentSpaceId != blockId || database.TileMode != model)
                    throw new DomainException("LAYOUT_NOT_ACTIVE", "The host did not enter the requested layout space.");
            }
            else if (args.Action == "activate")
            {
                state = Target(args, false);
                job.BeginCommit(); started = true;
                Application.DocumentManager.MdiActiveDocument = state.Document;
            }
            else
            {
                var existing = FindOpenSource(args,job.CheckCancellation);
                if (existing != null)
                {
                    state = existing; reused = true;
                    job.BeginCommit(); started = true;
                }
                else
                {
                    DocumentFilePolicy.Check(args.Path!, true, false);
                    job.BeginCommit(); started = true;
                    var document = Application.DocumentManager.Open(args.Path!, args.ReadOnly);
                    state = _registry.Get(document ?? throw new IOException("Native open returned no document."));
                }
                Application.DocumentManager.MdiActiveDocument = state.Document;
            }
            if (!ReferenceEquals(Application.DocumentManager.MdiActiveDocument, state.Document))
                throw new DomainException("DOCUMENT_NOT_ACTIVE", "The host did not activate the requested document.");
            result = Result.Ok(job.OriginalRequestId, new DocumentOperationResult(job.OperationId, "completed", false, args.Action,
                Info(state), null, null, reused), Context(state));
            job.RecordCommit(result);
        }
        catch (System.Exception error) { result = Failure(job, error, started, null); }
        finally { ApplicationOperationRunning = false; }
        finish(result);
    }
    private static Result Failure(WriteJob job, System.Exception error, bool started, ReadContext? context)
    {
        if (job.CommittedResult is { } known) return known;
        string code = error is DomainException domain ? domain.Error.code : error is OperationCanceledException ? "CANCELLED" :
            error is UnauthorizedAccessException ? "FILE_ACCESS_DENIED" : error is IOException ? "FILE_IO_ERROR" : "API_EXCEPTION";
        // A filesystem/host failure after the native boundary is never labelled
        // rolled_back. Keep the operation ID so a caller cannot blindly retry.
        return job.Error(started ? "DOCUMENT_OUTCOME_UNKNOWN" : code,
            started ? "Native document operation may have changed the host or file. Retrieve the same operation ID and inspect state before any new operation." : error is DomainException d ? d.Error.message : "Document operation failed before native execution.",
            started ? "unknown" : "not_started", context,
            new { code, error_type = error.GetType().Name, native_status = error is Teigha.Runtime.Exception cad ? cad.ErrorStatus.ToString() : null });
    }
}
