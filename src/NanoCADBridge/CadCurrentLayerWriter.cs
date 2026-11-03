using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class CadCurrentLayerWriter
{
    public static ObjectId Resolve(SetCurrentLayerEdit edit, Database db, Transaction tr)
    {
        var target = CadStructureWriter.Find(db, edit.Handle);
        if (tr.GetObject(target, OpenMode.ForRead) is not LayerTableRecord layer || layer.GetType() != typeof(LayerTableRecord) || layer.OwnerId != db.LayerTableId)
            throw new DomainException("OUT_OF_SCOPE", "Current layer must be an existing native layer from this document.");
        if (layer.IsDependent || layer.Name.Contains('|') || layer.IsOff || layer.IsFrozen || layer.IsLocked)
            throw new DomainException("LAYER_UNAVAILABLE", "Current layer target must be local, visible, thawed and unlocked.");
        if (db.Clayer != CadStructureWriter.Find(db, edit.ExpectedCurrentHandle))
            throw new DomainException("CURRENT_LAYER_MISMATCH", "Current layer differs from expected_current_handle; refresh get_structure layers.");
        return target;
    }

    public static void Apply(SetCurrentLayerEdit edit, ObjectId target, Database db, Transaction tr)
    {
        if (Resolve(edit, db, tr) != target)
            throw new DomainException("API_EXCEPTION", "Current layer identity changed before apply.");
        db.Clayer = target;
        if (db.Clayer != target)
            throw new DomainException("API_EXCEPTION", "Native current layer setter did not retain the requested handle.");
    }
}
