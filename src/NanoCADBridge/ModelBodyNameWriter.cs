using System.Runtime.CompilerServices;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Db = Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class ModelBodyNameWriter
{
    private sealed record Target(ModelBodyNameEdit Edit, Db.ObjectId Representation, Mc3dSolid Root,
        McObjectId RootId, McObjectId OwnerDocument, string? PartName, string PreviousName)
    {
        internal bool Changes => PartName != Edit.Name;
    }

    internal static bool Handles(ModifyArguments args) => args.Operations.Any(edit => edit is ModelBodyNameEdit);

    internal static void Preflight(ModifyArguments args, DocumentState state, Db.Transaction tx, Action? cancel)
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!loaded.Contains("mapimgd") || !loaded.Contains("MapiBaseTypes"))
            throw new DomainException("CAPABILITY_UNAVAILABLE", "Load the native MultiCAD modeling runtime to identify the selected bodies.");
        Prepare(args, state, tx, cancel);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Target[] Prepare(ModifyArguments args, DocumentState state, Db.Transaction tx, Action? cancel)
    {
        var database = state.Document.Database;
        var model = CadWriteSpace.Resolve(database, tx, "model");
        var drawing = McDocument.ActiveDocument;
        if (drawing?.TopDoc == null || drawing.TopDoc.ModelSpaceId.ToOldIdPtr() != model.OldIdPtr)
            throw new DomainException("DOCUMENT_CONTEXT_MISMATCH", "The native drawing does not own the active Model Space.");

        var targets = new List<Target>();
        var roots = new HashSet<McObjectId>();
        foreach (var edit in args.Operations.Cast<ModelBodyNameEdit>())
        {
            cancel?.Invoke();
            var raw = CadStructureWriter.Find(database, edit.Handles.Single());
            if (tx.GetObject(raw, Db.OpenMode.ForRead) is not Db.Solid3d solid || solid.OwnerId != model || solid.IsNull)
                throw new DomainException("UNSUPPORTED_ENTITY_TYPE", "Use a top-level Model Space solid with a native body owner.");
            var layer = (Db.LayerTableRecord)tx.GetObject(solid.LayerId, Db.OpenMode.ForRead);
            if (layer.IsLocked || layer.IsDependent)
                throw new DomainException("LAYER_UNAVAILABLE", "The selected body's layer is locked or externally dependent.");
            var body = CadModelBodyNames.Resolve(solid, drawing);
            if (body == null)
                throw new DomainException("BODY_NAME_UNAVAILABLE", "The selected solid has no addressable local native body owner.");
            if (!roots.Add(body.ID))
                throw new DomainException("INVALID_ARGUMENT", "Choose each native body only once in a naming group.");
            if (CadModelBodyNames.IsAlienSolid(body))
                throw new DomainException("BODY_NAME_PERSISTENCE_UNAVAILABLE",
                    "nanoCAD 26 resets the 3D History caption of this nonparametric solid when the DWG is reopened.",
                    false, new { handle = edit.Handles.Single() });
            string? partName = body.PartName;
            string previous = !string.IsNullOrWhiteSpace(partName) ? partName : body.Name ?? "";
            if (edit.ExpectedName != null && !string.Equals(edit.ExpectedName, previous, StringComparison.Ordinal))
                throw new DomainException("PRECONDITION_FAILED", "The effective body name changed. Read the current name before renaming.");
            targets.Add(new(edit, raw, body, body.ID, body.DbEntity.Document.ID, partName, previous));
        }
        return targets.ToArray();
    }

    internal static Result Execute(ModifyArguments args, DocumentState state, WriteJob job, ReadContext context)
    {
        McUndoPoint? undo = null;
        Db.Transaction? tx = null;
        bool nativeStarted = false;
        string? currentHandle = null;
        Result Receipt(List<ModelBodyNameChange> changes, List<EntityRef> modified) =>
            Result.Ok(job.OriginalRequestId, new ModifiedEntities(args.OperationId, "committed", false,
                modified.ToArray(), [], [], [], "not_guaranteed", model_body_names: changes.ToArray()), context, WriteRecovery.Warning);
        void CloseUndo()
        {
            var point = undo;
            undo = null;
            if (point != null)
                try { point.Stop(); }
                finally { point.Dispose(); }
        }

        try
        {
            var transactions = state.Document.Database.TransactionManager;
            Target[] targets;
            using (var read = transactions.StartTransaction()) targets = Prepare(args, state, read, job.CheckCancellation);
            var changes = new List<ModelBodyNameChange>();
            var modified = new List<EntityRef>();
            foreach (var target in targets)
            {
                var reference = new EntityRef(args.BridgeId, args.DocumentId, target.Edit.Handles.Single());
                if (target.Changes) modified.Add(reference);
                changes.Add(new(reference, target.PreviousName, target.Edit.Name, "native_part_name",
                    target.Root.Name, null, target.Edit.Name));
            }
            SerializedCapacity357.Check(Receipt(changes, modified), Wire.PayloadBudget - 16384, job.CheckCancellation);
            changes.Clear();
            foreach (var target in targets.Where(t => t.Changes))
            {
                job.CheckCancellation();
                currentHandle = target.Edit.Handles.Single();
                if (undo == null)
                {
                    undo = new McUndoPoint(false);
                    undo.Start();
                }
                if (target.Root.PartName != target.PartName)
                    throw new DomainException("PRECONDITION_FAILED", "The native part name changed before the edit.");
                nativeStarted = true;
                target.Root.TryModify((uint)McDbObject.ObjChangesType.Geom);
                target.Root.PartName = target.Edit.Name;
                target.Root.DbEntity.Update();
            }
            CloseUndo();

            tx = transactions.StartTransaction();
            var drawing = McDocument.ActiveDocument;
            foreach (var target in targets)
            {
                job.CheckCancellation();
                currentHandle = target.Edit.Handles.Single();
                var reference = new EntityRef(args.BridgeId, args.DocumentId, currentHandle);
                var raw = CadStructureWriter.Find(state.Document.Database, currentHandle);
                if (raw != target.Representation || tx.GetObject(raw, Db.OpenMode.ForRead) is not Db.Solid3d solid || drawing == null)
                    throw new DomainException("OBJECT_IDENTITY_MISMATCH", "The selected physical body changed during naming.");
                var current = CadModelBodyNames.Resolve(solid, drawing);
                if (current == null || current.ID != target.RootId || current.DbEntity.Document.ID != target.OwnerDocument ||
                    CadModelBodyNames.IsAlienSolid(current))
                    throw new DomainException("OBJECT_IDENTITY_MISMATCH", "The native owner of the selected physical body changed during naming.");
                var partName = current.PartName;
                if (!string.Equals(partName, target.Edit.Name, StringComparison.Ordinal))
                    throw new DomainException("MODEL_UPDATE_FAILED", "The native body did not retain the requested part name.");
                changes.Add(new(reference, target.PreviousName, target.Edit.Name, "native_part_name",
                    current.Name, null, partName));
            }
            var result = Receipt(changes, modified);
            SerializedCapacity357.Check(result, Wire.PayloadBudget - 16384, job.CheckCancellation);
            job.CheckCancellation();
            job.BeginCommit();
            tx.Commit();
            job.RecordCommit(result);
            var completed = tx;
            tx = null;
            try { completed.Dispose(); }
            catch (Exception) { result = result with { warnings = result.warnings.Append("POST_COMMIT_CLEANUP_FAILED: Body naming committed; refresh the selected body names.").ToArray() }; job.RecordCommit(result); }
            return result;
        }
        catch (Exception error)
        {
            if (job.CommittedResult is { } committed) return committed;
            Exception? cleanup = null;
            try { CloseUndo(); } catch (Exception caught) { cleanup = caught; }
            try { tx?.Abort(); } catch (Exception caught) { cleanup ??= caught; }
            try { tx?.Dispose(); } catch (Exception caught) { cleanup ??= caught; }
            bool unknown = nativeStarted || cleanup != null;
            string cause = error is DomainException domain ? domain.Error.code : error is OperationCanceledException ? "CANCELLED" : "MODEL_UPDATE_FAILED";
            return job.Error(unknown ? "WRITE_OUTCOME_UNKNOWN" : cause, unknown
                    ? "Body naming was not fully verified. Read the current names for the selected handles before further edits." : error.Message,
                unknown ? "unknown" : "not_started", context,
                new { cause, handle = currentHandle, message = error.Message, cleanup_error = cleanup?.GetType().Name }) with
            { warnings = [WriteRecovery.Warning] };
        }
    }
}
