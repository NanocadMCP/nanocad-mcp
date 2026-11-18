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
    private sealed record Target(ModelBodyNameEdit Edit, Db.ObjectId Representation, Mc3dSolid Root, McObjectId RootId,
        McObjectId OwnerDocument, string PreviousName);

    internal static bool Handles(ModifyArguments args) => args.Operations.Any(edit => edit is ModelBodyNameEdit);

    internal static void Preflight(ModifyArguments args, DocumentState state, Db.Transaction tx, Action? cancel)
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!loaded.Contains("mapimgd") || !loaded.Contains("MapiBaseTypes"))
            throw new DomainException("CAPABILITY_UNAVAILABLE", "Load the native MultiCAD modeling runtime to name the body's tree entry.");
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
                throw new DomainException("BODY_NAME_UNAVAILABLE", "The selected solid has no addressable local native body tree entry.");
            if (!roots.Add(body.ID))
                throw new DomainException("INVALID_ARGUMENT", "Choose each native body only once in a naming group.");
            var previous = body.Name ?? "";
            if (edit.ExpectedName != null && !string.Equals(edit.ExpectedName, previous, StringComparison.Ordinal))
                throw new DomainException("PRECONDITION_FAILED", "The native body name changed. Read the current name before renaming.");
            targets.Add(new Target(edit, raw, body, body.ID, body.DbEntity.Document.ID, previous));
        }
        return targets.ToArray();
    }

    private static void Verify(Target[] targets, DocumentState state, Action cancel)
    {
        using var tx = state.Document.Database.TransactionManager.StartTransaction();
        var drawing = McDocument.ActiveDocument;
        foreach (var target in targets)
        {
            cancel();
            var raw = CadStructureWriter.Find(state.Document.Database, target.Edit.Handles.Single());
            var current = drawing != null && raw == target.Representation &&
                          tx.GetObject(raw, Db.OpenMode.ForRead) is Db.Solid3d solid
                ? CadModelBodyNames.Resolve(solid, drawing) : null;
            if (current == null || current.ID != target.RootId || current.DbEntity.Document.ID != target.OwnerDocument)
                throw new DomainException("OBJECT_IDENTITY_MISMATCH", "The current physical solid or its native body owner changed during naming.");
            if (!string.Equals(current.Name ?? "", target.Edit.Name, StringComparison.Ordinal))
                throw new DomainException("MODEL_UPDATE_FAILED", "The native tree entry did not retain the requested body name.");
        }
    }

    internal static Result Execute(ModifyArguments args, DocumentState state, WriteJob job, ReadContext context)
    {
        McUndoPoint? undo = null;
        bool started = false;
        string? currentHandle = null;
        void Close()
        {
            var point = undo;
            undo = null;
            if (point != null)
                try { point.Stop(); }
                finally { point.Dispose(); }
        }

        try
        {
            Target[] targets;
            using (var tx = state.Document.Database.TransactionManager.StartTransaction())
                targets = Prepare(args, state, tx, job.CheckCancellation);
            var changes = targets.Select(target => new ModelBodyNameChange(
                new EntityRef(args.BridgeId, args.DocumentId, target.Edit.Handles.Single()), target.PreviousName, target.Edit.Name)).ToArray();
            var modified = changes.Where(row => row.previous_name != row.name).Select(row => row.body_ref).ToArray();
            var result = Result.Ok(job.OriginalRequestId, new ModifiedEntities(args.OperationId, "committed", false,
                modified, [], [], [], "not_guaranteed", model_body_names: changes), context, WriteRecovery.Warning);
            SerializedCapacity357.Check(result, Wire.PayloadBudget - 16384, job.CheckCancellation);

            if (modified.Length != 0)
            {
                undo = new McUndoPoint(false);
                undo.Start();
                foreach (var target in targets.Where(t => t.PreviousName != t.Edit.Name))
                {
                    job.CheckCancellation();
                    currentHandle = target.Edit.Handles.Single();
                    started = true;
                    target.Root.TryModify((uint)McDbObject.ObjChangesType.Geom);
                    target.Root.Name = target.Edit.Name;
                    target.Root.DbEntity.Update();
                }
            }
            job.CheckCancellation();
            job.BeginCommit();
            Close();
            Verify(targets, state, job.CheckCancellation);
            job.RecordCommit(result);
            return result;
        }
        catch (Exception error)
        {
            if (job.CommittedResult is { } committed) return committed;
            Exception? cleanup = null;
            try { Close(); } catch (Exception caught) { cleanup = caught; }
            bool unknown = started || cleanup != null;
            string cause = error is DomainException domain ? domain.Error.code : error is OperationCanceledException ? "CANCELLED" : "MODEL_UPDATE_FAILED";
            return job.Error(unknown ? "WRITE_OUTCOME_UNKNOWN" : cause, unknown
                    ? "Body naming was not fully verified. Read the current names for the selected handles before further edits." : error.Message,
                unknown ? "unknown" : "not_started", context, new { cause, handle = currentHandle, message = error.Message, cleanup_error = cleanup?.GetType().Name })
                with
            { warnings = [WriteRecovery.Warning] };
        }
    }
}
