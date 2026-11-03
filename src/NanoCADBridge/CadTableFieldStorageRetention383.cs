using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using static NanoCADBridge.CadTableContentsReader364;

namespace NanoCADBridge;

// Binding removal does not request a private purge. When retirement is not
// proved, retain the actual typed storage and its persistent reference graph.
// Fields/dictionaries/Xrecords are observed. A reference to this exact Table
// is verified by the existing full snapshot/selected-slot origin plan, not by
// admitting another Table or snapshotting its intentionally changed binding.
internal static class CadTableFieldStorageRetention383
{
    static bool Live(ObjectId id)=>!id.IsNull&&id.IsValid&&!id.IsErased&&!id.IsEffectivelyErased;
    static string Id(ObjectId id)=>Validation.NormalizeHandle(id.Handle.ToString());
    static string? Reference(ObjectId id)=>id.IsNull?null:Id(id);
    static void Need(bool value,string message,object? details=null)
    {if(!value)throw new DomainException("UNSUPPORTED_TABLE_FIELD_DELETION",message,false,details);}
    internal static Dictionary<ObjectId,JsonElement> Capture(Table table,IEnumerable<ObjectId> roots,Transaction tr,Action cancel,Action<string,string?>? trace=null)
    {
        var records=new Dictionary<ObjectId,JsonElement>();var queued=new HashSet<ObjectId>();var pending=new Queue<ObjectId>();var budget=new ModelInventoryBudget(cancel);
        void Queue(ObjectId id){if(!id.IsNull&&queued.Add(id))pending.Enqueue(id);}
        foreach(var id in roots)Queue(id);
        while(pending.Count>0)
        {
            cancel();var id=pending.Dequeue();trace?.Invoke("retained_storage.open",Reference(id));
            Need(Live(id),"Nonretirable selected storage/dependency is unavailable; no purge was requested.",new{handle=Reference(id)});
            var item=tr.GetObject(id,OpenMode.ForRead);
            Need(item.ObjectId==id&&table.Database!=null&&table.Database.UnmanagedObject!=IntPtr.Zero&&item.Database?.UnmanagedObject==table.Database.UnmanagedObject,
                "Selected storage/dependency has no exact current local identity.",new{handle=Id(id),runtime_type=item.GetType().FullName});
            trace?.Invoke("retained_storage.metadata",Id(id));
            using var xdata=item.XData;using var reactors=item.GetPersistentReactorIds();
            var references=new List<string>();
            foreach(ObjectId link in reactors){cancel();Need(Live(link),"Selected storage has an unavailable persistent reference.",new{handle=Id(id),reference_handle=Reference(link)});references.Add(Id(link));Queue(link);}
            var common=new{handle=Id(id),owner_handle=Reference(item.OwnerId),runtime_type=item.GetType().FullName,
                extension_handle=Reference(item.ExtensionDictionary),has_fields=id==table.ObjectId?(bool?)null:item.HasFields,
                xdata=xdata==null?null:NativeOwnedMetadata.Buffer(xdata,cancel,value=>budget.Add(value)),reactors=references.OrderBy(value=>value,StringComparer.Ordinal).ToArray()};
            object record;
            if(id==table.ObjectId)
            {
                Need(item.GetType()==typeof(Table)&&item.UnmanagedObject==table.UnmanagedObject&&item.OwnerId==table.OwnerId,
                    "Selected Table dependency differs from its current native root.",new{handle=Id(id),runtime_type=item.GetType().FullName});
                // HasFields/bindings can intentionally change. Root metadata,
                // topology and every retained slot/Field are checked by the
                // editor's existing complete before/after snapshot and plan.
                record=new{common,selected_table_reference=new{handle=Id(table.ObjectId),owner_handle=Reference(table.OwnerId),rows=table.Rows.Count,columns=table.Columns.Count}};
            }
            else if(item.GetType()==typeof(Field)&&item is Field field)
            {
                trace?.Invoke("retained_storage.Field.GetChildren",Id(id));var nativeChildren=field.GetChildren();var children=new List<object>();
                try
                {
                    Need(nativeChildren!=null,"Retained Field children are unavailable.",new{field_handle=Id(id)});
                    foreach(var child in nativeChildren!)
                    {
                        cancel();Need(child!=null&&child.GetType()==typeof(Field)&&Live(child.ObjectId)&&child.Database?.UnmanagedObject==table.Database!.UnmanagedObject,
                            "Retained Field child needs its exact local typed dependency adapter.",new{field_handle=Id(id)});
                        var resident=tr.GetObject(child!.ObjectId,OpenMode.ForRead);
                        Need(resident.GetType()==typeof(Field)&&resident.UnmanagedObject==child.UnmanagedObject&&resident.ObjectId==child.ObjectId&&resident.OwnerId==child.OwnerId,
                            "Returned retained Field child differs from its resident identity/owner.",new{field_handle=Id(id)});
                        children.Add(new{handle=Id(child.ObjectId),owner_handle=Reference(child.OwnerId),owned=child.OwnerId==id});Queue(child.ObjectId);
                    }
                }
                finally
                {
                    if(nativeChildren!=null)foreach(var child in nativeChildren)
                    {try{child?.Dispose();}catch{trace?.Invoke("retained_storage.Field.child_wrapper_release",Id(id));throw;}}
                }
                trace?.Invoke("retained_storage.Field.definition",Id(id));record=new{common,definition=Definition(field),children};
            }
            else if(item.GetType()==typeof(DBDictionary)&&item is DBDictionary dictionary)
            {
                var entries=new List<object>();trace?.Invoke("retained_storage.dictionary_entries",Id(id));
                foreach(DBDictionaryEntry entry in dictionary)
                {
                    cancel();Need(Live(entry.Value),"Retained dictionary reference is unavailable.",new{dictionary_handle=Id(id),reference_handle=Reference(entry.Value)});
                    var child=tr.GetObject(entry.Value,OpenMode.ForRead);
                    Need(child.Database?.UnmanagedObject==table.Database!.UnmanagedObject,"Retained dictionary reference is outside the current database.");
                    entries.Add(new{name=entry.Key,handle=Id(entry.Value),owner_handle=Reference(child.OwnerId),runtime_type=child.GetType().FullName,owned=child.OwnerId==id});Queue(entry.Value);
                }
                record=new{common,dictionary_hard_owned=dictionary.TreatElementsAsHard,entries};
            }
            else if(item.GetType()==typeof(Xrecord)&&item is Xrecord xrecord)
            {
                using var data=xrecord.Data;record=new{common,xlate_references=xrecord.XlateReferences,merge_style=xrecord.MergeStyle.ToString(),
                    data=data==null?null:NativeOwnedMetadata.Buffer(data,cancel,value=>budget.Add(value))};
            }
            else throw new DomainException("UNSUPPORTED_TABLE_FIELD_DELETION","Nonretirable selected storage/dependency "+Id(id)+" ("+item.GetType().FullName+") requires its exact typed retention adapter; no private purge or foreign class waiver.",false,new{handle=Id(id),runtime_type=item.GetType().FullName});
            Queue(item.ExtensionDictionary);var captured=Wire.Element(record);budget.Add(captured);records.Add(id,captured);
        }
        return records;
    }
    internal static void Verify(Table table,IReadOnlyDictionary<ObjectId,JsonElement> before,Transaction tr,Action cancel)
    {
        var after=Capture(table,before.Keys,tr,cancel);
        Need(after.Count==before.Count,"Native content removal changed the nonretirable storage/dependency inventory.",new{before_count=before.Count,after_count=after.Count});
        foreach(var (id,prior) in before)
        {
            cancel();Need(after.TryGetValue(id,out var current)&&JsonSerializer.Serialize(prior,Wire.Json)==JsonSerializer.Serialize(current,Wire.Json),
                "Native content removal changed nonretirable Field definition/children, owned data or persistent references.",new{handle=Id(id),runtime_type=prior.GetProperty("common").GetProperty("runtime_type").GetString()});
        }
    }
}
