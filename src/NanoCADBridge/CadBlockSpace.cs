using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Application = HostMgd.ApplicationServices.Application;

namespace NanoCADBridge;

// A top-level occurrence belongs to a native layout BTR. The same block/attribute
// ownership rules apply to Model and to one explicit paper layout.
internal static class CadBlockSpace
{
    internal sealed record Scope(ObjectId Space, ObjectId Layout, string Kind);
    public static Scope RequireSpace(Database db, Transaction tr, ObjectId id)
    {
        if (id.IsNull || !id.IsValid || id.IsErased || id.IsEffectivelyErased ||
            tr.GetObject(id, OpenMode.ForRead) is not BlockTableRecord space || space.GetType() != typeof(BlockTableRecord) ||
            space.OwnerId != db.BlockTableId || !space.IsLayout || space.IsDependent || space.IsFromExternalReference || space.IsFromOverlayReference)
            throw new DomainException("OUT_OF_SCOPE", "Block occurrences require a native Model or paper-layout block record from this document.");
        var layoutId = space.LayoutId;
        if (layoutId.IsNull || !layoutId.IsValid || layoutId.IsErased || layoutId.IsEffectivelyErased ||
            tr.GetObject(layoutId, OpenMode.ForRead) is not Layout layout || layout.GetType() != typeof(Layout) ||
            layout.OwnerId != db.LayoutDictionaryId || layout.BlockTableRecordId != id)
            throw new DomainException("OUT_OF_SCOPE", "Block occurrence space has inconsistent native layout ownership.");
        var blocks = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        var layouts = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
        bool model = id == blocks[BlockTableRecord.ModelSpace];
        if (layout.ModelType != model || !layouts.Contains(layout.LayoutName) || layouts.GetAt(layout.LayoutName) != layoutId)
            throw new DomainException("OUT_OF_SCOPE", "Block occurrence layout identity or dictionary indexing is inconsistent.");
        return new(id, layoutId, model ? "model" : "paper");
    }
    public static Scope RequireTopLevel(BlockReference reference, Database db, Transaction tr)
    {
        if ((reference.GetType() != typeof(BlockReference) && reference.GetType() != typeof(MInsertBlock)) || reference.ObjectId.IsNull || reference.ObjectId.IsErased || reference.ObjectId.IsEffectivelyErased)
            throw new DomainException("OUT_OF_SCOPE", "Expected an existing exact native top-level BlockReference or MInsertBlock.");
        return RequireSpace(db, tr, reference.OwnerId);
    }
    public static ObjectId RequireWriteSpace(Database db, Transaction tr, string targetSpace, Action check)
    {
        check(); var id = CadWriteSpace.Resolve(db, tr, targetSpace); var scope = RequireSpace(db, tr, id);
        if (scope.Kind != targetSpace) throw new DomainException("OUT_OF_SCOPE", "Block write space differs from the requested target_space.");
        if (targetSpace == "paper")
        {
            check(); object cvport = Application.GetSystemVariable("CVPORT"); check();
            long number = cvport switch { short value => value, int value => value, long value => value, _ => -1 };
            if (db.TileMode || number != 1)
                throw new DomainException("OUT_OF_SCOPE", "Activate the requested paper layout outside its model viewport (CVPORT=1) before block writes.");
        }
        return id;
    }
}
