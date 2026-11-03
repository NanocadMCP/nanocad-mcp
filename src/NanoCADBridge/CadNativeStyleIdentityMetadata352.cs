using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// Same generic native-owned identity/readable-data observation as shared block
// rename. Opaque/field internals are disclosed; invalid owners/bytes propagate.
internal static class CadNativeStyleIdentityMetadata352
{
    private static void Need(bool value,string message)
    {if(!value)throw new DomainException("NATIVE_STYLE_STATE_CHANGED",message);}
    private static bool Live(ObjectId id)=>!id.IsNull&&id.IsValid&&!id.IsErased&&!id.IsEffectivelyErased;
    private static string? Id(ObjectId id)=>id.IsNull?null:Validation.NormalizeHandle(id.Handle.ToString());
    private static bool SameDatabase(DBObject value,Database db)=>db.UnmanagedObject!=IntPtr.Zero&&value.Database?.UnmanagedObject==db.UnmanagedObject;
    private static object[] Buffer(ResultBuffer data,Action cancel)
    {
        var values=new List<object>();var budget=new ModelInventoryBudget(cancel);
        foreach(var typed in data.AsArray())
        {
            cancel();object? value;
            try{value=NativeOwnedMetadata.Value(typed.Value);}
            catch(DomainException error) when(error.Error.code=="UNSUPPORTED_OWNED_METADATA")
            {value=new{native_type=typed.Value?.GetType().FullName,value_scope="opaque_typed_value_unobserved"};}
            var item=new{code=typed.TypeCode,value};budget.Add(item);values.Add(item);
        }
        return values.ToArray();
    }
    internal static JsonElement Metadata(DBObject item,Database db,Transaction tr,Action cancel)
    {
        cancel();Need(Live(item.ObjectId)&&Live(item.OwnerId)&&SameDatabase(item,db),"Native identity member identity/database/owner is unavailable.");
        using var reactors=item.GetPersistentReactorIds();var links=new List<string?>();
        foreach(ObjectId id in reactors){cancel();links.Add(Id(id));}
        JsonElement owned;
        try{owned=NativeOwnedMetadata.Capture(item,tr,cancel);}
        catch(DomainException error) when(error.Error.code=="UNSUPPORTED_OWNED_METADATA"&&
            (error.Error.message.StartsWith("Field-bearing owned metadata",StringComparison.Ordinal)||
             error.Error.message.StartsWith("Native owned data needs",StringComparison.Ordinal)||
             error.Error.message.StartsWith("A native metadata value needs",StringComparison.Ordinal)))
        {owned=OpaqueOwned(item,db,tr,cancel);}
        using var data=item.XData;
        var result=Wire.Element(new{handle=Id(item.ObjectId),owner=Id(item.OwnerId),native_type=item.GetType().FullName,
            item.HasFields,extension=Id(item.ExtensionDictionary),reactors=links.OrderBy(value=>value,StringComparer.OrdinalIgnoreCase).ToArray(),
            xdata=data==null?null:Buffer(data,cancel),owned});
        new ModelInventoryBudget(cancel).Add(result);return result;
    }
    private static JsonElement OpaqueOwned(DBObject root,Database db,Transaction tr,Action cancel)
    {
        var values=new List<object>();var seen=new HashSet<ObjectId>();var pending=new Stack<(ObjectId Id,ObjectId Owner)>();
        var budget=new ModelInventoryBudget(cancel);if(!root.ExtensionDictionary.IsNull)pending.Push((root.ExtensionDictionary,root.ObjectId));
        while(pending.Count>0)
        {
            cancel();var next=pending.Pop();Need(Live(next.Id)&&seen.Add(next.Id),"Owned metadata identity is unavailable/repeated.");
            var item=tr.GetObject(next.Id,OpenMode.ForRead);Need(item.OwnerId==next.Owner&&SameDatabase(item,db),"Owned metadata has inconsistent actual owner/database.");
            using var reactors=item.GetPersistentReactorIds();using var data=item.XData;var links=new List<string?>();
            foreach(ObjectId id in reactors){cancel();links.Add(Id(id));}
            var entries=new List<object>();var entryBudget=new ModelInventoryBudget(cancel);
            if(item is DBDictionary dictionary)
                foreach(DBDictionaryEntry entry in dictionary)
                {
                    cancel();Need(Live(entry.Value),"Owned dictionary entry is unavailable.");var child=tr.GetObject(entry.Value,OpenMode.ForRead);
                    bool owns=child.OwnerId==item.ObjectId&&SameDatabase(child,db);
                    var entryValue=new{name=entry.Key,handle=Id(entry.Value),owner=Id(child.OwnerId),type=child.GetType().FullName,owned=owns};
                    entryBudget.Add(entryValue);entries.Add(entryValue);
                    if(owns)pending.Push((entry.Value,item.ObjectId));
                }
            object? stored=null;
            if(item.GetType()==typeof(Xrecord)&&item is Xrecord record)
            {using var buffer=record.Data;stored=new{record.XlateReferences,merge_style=record.MergeStyle.ToString(),data=buffer==null?null:Buffer(buffer,cancel)};}
            var value=new{handle=Id(item.ObjectId),owner=Id(item.OwnerId),type=item.GetType().FullName,item.HasFields,
                extension=Id(item.ExtensionDictionary),reactors=links.OrderBy(link=>link,StringComparer.OrdinalIgnoreCase).ToArray(),
                xdata=data==null?null:Buffer(data,cancel),dictionary_hard_owned=item is DBDictionary observedDictionary?(bool?)observedDictionary.TreatElementsAsHard:null,entries,stored,
                inner_state_scope=stored==null&&item is not DBDictionary?"opaque_native_inner_state_unobserved":"readable_stored_metadata"};
            budget.Add(value);values.Add(value);if(!item.ExtensionDictionary.IsNull)pending.Push((item.ExtensionDictionary,item.ObjectId));
        }
        return Wire.Element(new{observation_scope="generic_owned_identity_and_readable_data_opaque_native_internals_unobserved",records=values});
    }
}
