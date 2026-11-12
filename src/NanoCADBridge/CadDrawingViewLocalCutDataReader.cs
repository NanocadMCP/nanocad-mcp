using System.Diagnostics.CodeAnalysis;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db = Teigha.DatabaseServices;
using G = Multicad.Geometry;

namespace NanoCADBridge;

// WorkID.Handle is not assumed to be the current DWG handle. Callers supply
// the unique actual local representation established by their full inventory.
internal static class CadDrawingViewLocalCutDataReader
{
    internal sealed record Data(McObjectId TargetView, McObjectId DepthView, G.Point3d DepthPoint, double Depth);

    static void Need([DoesNotReturnIf(false)] bool condition, string message, string code = "DRAWING_VIEW_STATE_UNAVAILABLE", object? details = null)
    { if (!condition) throw new DomainException(code, message, false, details); }
    static bool Live(HistoryItem item) => item.Status.ToString() != "Erased" && (item.DbEntity.StatusFlags & McDbObjectStateFlags.Erased) == 0;

    internal static Data Read(PlanarSketch sketch, DocumentState state, Db.Transaction tx,
        IReadOnlyDictionary<McObjectId, Db.ObjectId> currentRepresentations, Action cancel)
    {
        cancel();
        if (currentRepresentations.TryGetValue(sketch.ID, out var physical))
            return Read(sketch, state, tx, physical, cancel);
        var parent = sketch.DbEntity.ParentId;
        Need(!parent.IsNull && currentRepresentations.TryGetValue(parent, out physical),
            "The local-cut sketch has neither a current physical representation nor a current physical native parent view.");
        return ReadOwned(sketch, state, tx, physical, cancel);
    }

    internal static Data Read(PlanarSketch sketch, DocumentState state, Db.Transaction tx, Db.ObjectId physical, Action cancel)
    {
        cancel(); var drawing = CadDrawingViewEditState.Context(state); var top = drawing.TopDoc;
        var id = sketch.ID; var owner = sketch.DbEntity.DocumentID; var database = state.Document.Database;
        Need(!id.IsNull && !owner.IsNull && top != null && !top.ID.IsNull &&
            !CadDrawingViewEditState.Gone(physical) && physical.Database?.UnmanagedObject == database.UnmanagedObject,
            "A local-cut sketch has no exact current native owner/physical context.");
        var entity = tx.GetObject(physical, Db.OpenMode.ForRead) as Db.Entity;
        Need(entity != null && entity.ObjectId == physical && entity.Database?.UnmanagedObject == database.UnmanagedObject &&
            !entity.OwnerId.IsNull && tx.GetObject(entity.OwnerId, Db.OpenMode.ForRead) is Db.BlockTableRecord block &&
            !block.IsDependent && !block.IsFromExternalReference && !block.IsFromOverlayReference &&
            McObjectId.FromHandle(physical.Handle.Value, drawing.ID) == id,
            "The supplied current physical sketch does not match its actual local native identity.", "OBJECT_IDENTITY_MISMATCH");
        string revision = state.Revision;
        var read = NativeModelHistory421.LoadDrawingViewLocalCutData();
        Need(state.Revision == revision, "Loading the native local-cut reader changed the current revision.", "STALE_DOCUMENT");
        var text = new char[NativeDrawingViewLocalCutData.BufferLength];
        var values = new double[NativeDrawingViewLocalCutData.ValueCount];
        int status = read(drawing.ID.ToString(), owner.ToString(), top!.ID.ToString(), unchecked((ulong)physical.Handle.Value),
            id.ToString(), text, text.Length, values, out int phase);
        cancel(); var current = CadDrawingViewEditState.Context(state);
        Need(state.Revision == revision && current.ID == drawing.ID && current.TopDoc?.ID == top.ID &&
            !CadDrawingViewEditState.Gone(physical) && physical.Database?.UnmanagedObject == database.UnmanagedObject &&
            McObjectId.FromHandle(physical.Handle.Value, current.ID) == id && sketch.ID == id && sketch.DbEntity.DocumentID == owner,
            "The actual native sketch context changed during the read-only local-cut observation.", "STALE_DOCUMENT",
            new { native_hresult = "0x" + unchecked((uint)status).ToString("X8"), native_phase = phase });
        return Decode(status, phase, text, values, new(drawing.ID.ToString(), top.ID.ToString(), owner.ToString(), id.ToString()));
    }

    static Data ReadOwned(PlanarSketch sketch, DocumentState state, Db.Transaction tx, Db.ObjectId physicalView, Action cancel)
    {
        cancel(); var drawing = CadDrawingViewEditState.Context(state); var top = drawing.TopDoc;
        var id = sketch.ID; var owner = sketch.DbEntity.DocumentID; var parent = sketch.DbEntity.ParentId;
        var host = id.IdHostDoc; var hostTop = id.IdHostTopDoc; var database = state.Document.Database;
        Need(!id.IsNull && !owner.IsNull && !parent.IsNull && top != null && !top.ID.IsNull &&
            !CadDrawingViewEditState.Gone(physicalView) && physicalView.Database?.UnmanagedObject == database.UnmanagedObject,
            "The view-owned local-cut sketch lacks its exact current native parent/document context.");
        var raw = tx.GetObject(physicalView, Db.OpenMode.ForRead) as Db.Entity;
        var viewId = McObjectId.FromHandle(physicalView.Handle.Value, drawing.ID);
        var view = viewId.IsNull ? null : viewId.GetObjectOfType<DrawingView>();
        Need(raw != null && raw.ObjectId == physicalView && raw.Database?.UnmanagedObject == database.UnmanagedObject &&
            !raw.OwnerId.IsNull && tx.GetObject(raw.OwnerId, Db.OpenMode.ForRead) is Db.BlockTableRecord block &&
            !block.IsDependent && !block.IsFromExternalReference && !block.IsFromOverlayReference &&
            view != null && view.ID == viewId && viewId == parent && view.DbEntity.DocumentID == owner && Live(view) && Live(sketch),
            "The actual local physical parent is not the same native drawing view/document.", "OBJECT_IDENTITY_MISMATCH");
        var attached = view.AttachedLocalCuts?.ToArray(); var children = view.DbEntity.ChildrenIds?.ToArray();
        Need(attached != null && attached.All(value => !value.IsNull) && attached.Distinct().Count() == attached.Length &&
            attached.Count(value => value == id) == 1 && children != null && children.Contains(id) &&
            sketch.IsLocalCutSketch && sketch.ViewIdForLocalCut == viewId &&
            (host.IsNull || host == owner) && (hostTop.IsNull || hostTop == top.ID),
            "The native local-cut sketch lacks complete reciprocal parent-view membership or has conflicting available host identities.");
        string revision = state.Revision;
        var read = NativeModelHistory421.LoadOwnedDrawingViewLocalCutData();
        Need(state.Revision == revision, "Loading the view-owned local-cut reader changed the current revision.", "STALE_DOCUMENT");
        var text = new char[NativeDrawingViewLocalCutData.BufferLength]; var values = new double[NativeDrawingViewLocalCutData.ValueCount];
        int status = read(drawing.ID.ToString(), owner.ToString(), top.ID.ToString(), unchecked((ulong)physicalView.Handle.Value),
            viewId.ToString(), id.ToString(), text, text.Length, values, out int phase);
        cancel(); var current = CadDrawingViewEditState.Context(state);
        Need(state.Revision == revision && current.ID == drawing.ID && current.TopDoc?.ID == top.ID &&
            !CadDrawingViewEditState.Gone(physicalView) && physicalView.Database?.UnmanagedObject == database.UnmanagedObject &&
            McObjectId.FromHandle(physicalView.Handle.Value, current.ID) == viewId && view.ID == viewId && view.DbEntity.DocumentID == owner &&
            view.AttachedLocalCuts != null && view.AttachedLocalCuts.SequenceEqual(attached) &&
            view.DbEntity.ChildrenIds != null && view.DbEntity.ChildrenIds.SequenceEqual(children) &&
            sketch.ID == id && sketch.DbEntity.DocumentID == owner && sketch.DbEntity.ParentId == viewId && Live(view) && Live(sketch) &&
            id.IdHostDoc == host && id.IdHostTopDoc == hostTop,
            "The current view-owned local-cut association changed during its read-only native observation.", "STALE_DOCUMENT",
            new { native_hresult = "0x" + unchecked((uint)status).ToString("X8"), native_phase = phase });
        var result = Decode(status, phase, text, values, new(drawing.ID.ToString(), top.ID.ToString(), owner.ToString(), id.ToString()));
        Need(result.TargetView == viewId, "The actual native local-cut data names another target than its proven parent view.", "OBJECT_IDENTITY_MISMATCH");
        return result;
    }

    static Data Decode(int status, int phase, char[] text, double[] values, NativeDrawingViewLocalCutExpected expected)
    {
        var result = NativeDrawingViewLocalCutData.Parse(status, phase, text, values, expected);
        static McObjectId Actual(string? value)
        {
            if (value == null) return McObjectId.Null;
            var actual = new McObjectId(Guid.ParseExact(value, "B"));
            Need(!actual.IsNull && string.Equals(actual.ToString(), value, StringComparison.OrdinalIgnoreCase),
                "An actual native local-cut reference could not be represented exactly.");
            return actual;
        }
        return new(Actual(result.TargetView), Actual(result.DepthView),
            new(result.DepthPoint.X, result.DepthPoint.Y, result.DepthPoint.Z), result.Depth);
    }
}
