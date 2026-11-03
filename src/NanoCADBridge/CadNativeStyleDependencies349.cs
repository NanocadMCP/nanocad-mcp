using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// Classify actual resident style ownership and ordinary native consumers only.
// An arbitrary dictionary or matching managed wrapper is not an owner proof.
internal static class CadNativeStyleDependencies349
{
    private static bool Live(ObjectId id)=>!id.IsNull&&id.IsValid&&!id.IsErased&&!id.IsEffectivelyErased;
    private static bool SameDatabase(DBObject value,Database db)=>db.UnmanagedObject!=IntPtr.Zero&&
        value.Database?.UnmanagedObject==db.UnmanagedObject;

    internal static bool OwningDictionary(DBObject style,string name,ObjectId reactor,ObjectId dictionaryId,
        Database db,Transaction tr,Action cancel)
    {
        cancel();
        if(!Live(style.ObjectId)||!Live(reactor)||reactor!=dictionaryId||style.OwnerId!=dictionaryId||!SameDatabase(style,db))return false;
        if(tr.GetObject(reactor,OpenMode.ForRead) is not DBDictionary dictionary||dictionary.GetType()!=typeof(DBDictionary)||
            dictionary.ObjectId!=dictionaryId||dictionary.OwnerId!=db.NamedObjectsDictionaryId||!SameDatabase(dictionary,db))return false;
        if(!dictionary.Contains(name)||dictionary.GetAt(name)!=style.ObjectId)return false;
        if(!Live(db.NamedObjectsDictionaryId)||tr.GetObject(db.NamedObjectsDictionaryId,OpenMode.ForRead) is not DBDictionary root||
            root.GetType()!=typeof(DBDictionary)||!SameDatabase(root,db))return false;
        int matches=0;var budget=new ModelInventoryBudget(cancel);
        foreach(DBDictionaryEntry entry in root)
        {
            cancel();budget.Add(new{name=entry.Key,handle=entry.Value.IsNull?null:entry.Value.Handle.ToString()});
            if(entry.Value==dictionaryId)matches++;
        }
        return matches==1;
    }

    internal static bool OrdinaryConsumer(Entity entity,Type exactType,Database db,Transaction tr,Action cancel)
    {
        cancel();
        if(entity.GetType()!=exactType||!Live(entity.ObjectId)||!Live(entity.OwnerId)||!SameDatabase(entity,db))return false;
        return tr.GetObject(entity.OwnerId,OpenMode.ForRead) is BlockTableRecord owner&&owner.GetType()==typeof(BlockTableRecord)&&
            owner.ObjectId==entity.OwnerId&&owner.OwnerId==db.BlockTableId&&SameDatabase(owner,db)&&
            !owner.IsDependent&&!owner.IsFromExternalReference&&!owner.IsFromOverlayReference;
    }
}
