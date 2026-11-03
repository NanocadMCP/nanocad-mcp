using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class CadLayerRenamer
{
    internal static JsonElement Properties(LayerTableRecord layer)
    {
        using var color=layer.Color;
        return Wire.Element(new{layer.IsOff,layer.IsFrozen,layer.IsLocked,layer.IsDependent,layer.IsPlottable,
            color=new{method=(int)color.ColorMethod,index=color.ColorIndex,color.Red,color.Green,color.Blue},
            linetype=layer.LinetypeObjectId.Handle.ToString(),lineweight=(int)layer.LineWeight});
    }
    private static void Dependencies(LayerTableRecord layer)
    {
        using var reactors=layer.GetPersistentReactorIds();
        if(layer.HasFields||reactors.Count!=0)throw new DomainException("LAYER_UNAVAILABLE","Layer renaming excludes fields and persistent dependencies.");
    }
    internal static ObjectId Resolve(RenameLayerEdit edit,Database db,Transaction tr)
    {
        var id=CadStructureWriter.Find(db,edit.Handle);
        if(tr.GetObject(id,OpenMode.ForRead) is not LayerTableRecord layer||layer.GetType()!=typeof(LayerTableRecord)||layer.OwnerId!=db.LayerTableId)
            throw new DomainException("OUT_OF_SCOPE","Expected an exact native layer-table record in this database.");
        Dependencies(layer);var table=(LayerTable)tr.GetObject(db.LayerTableId,OpenMode.ForRead);
        LayerRenameValidation.Source(layer.Name,layer.IsDependent,edit,table.Has(edit.Name));
        return id;
    }
    internal static void Preview(RenameLayerEdit edit,List<DBObject> owned)
    {
        // A fresh detached record validates the native name setter without a
        // copied owner/table association or any resident name-index operation.
        var layer=new LayerTableRecord();owned.Add(layer);layer.Name=edit.Name;
        if(layer.Name!=edit.Name)throw new DomainException("NATIVE_LAYER_MISMATCH","Native setter did not retain the exact requested name.");
    }
    internal static void Apply(RenameLayerEdit edit,ObjectId id,Database db,Transaction tr)
    {
        var table=(LayerTable)tr.GetObject(db.LayerTableId,OpenMode.ForRead);var layer=(LayerTableRecord)tr.GetObject(id,OpenMode.ForWrite);
        LayerRenameValidation.Source(layer.Name,layer.IsDependent,edit,table.Has(edit.Name));Dependencies(layer);
        var before=Properties(layer);var current=db.Clayer;layer.Name=edit.Name;
        if(layer.Name!=edit.Name||layer.ObjectId!=id||layer.OwnerId!=db.LayerTableId||!table.Has(edit.Name)||table[edit.Name]!=id||table.Has(edit.ExpectedName)||db.Clayer!=current||
            !BlockMoveGeometry.SameProperties(before,Properties(layer)))throw new DomainException("NATIVE_LAYER_MISMATCH","Rename changed identity/properties/current layer or failed to update the table name index.");
    }
}
