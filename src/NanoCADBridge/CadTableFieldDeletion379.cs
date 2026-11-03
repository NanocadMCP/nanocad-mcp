using System.Text;
using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using static NanoCADBridge.CadTableContentsReader364;

namespace NanoCADBridge;

// Delete an actual selected content slot, not a copied/evaluated literal and
// not a reparented Field. Table.DeleteContent owns native cleanup. Only the
// selected sole-binding Field and its proved exclusively owned tree may retire.
// Otherwise typed storage/dependencies must survive exactly; no purge is requested.
internal static class CadTableFieldDeletion379
{
    internal sealed record Node(ObjectId Id,ObjectId Owner,string Runtime);
    internal sealed record Selection(HashSet<string> Fields,HashSet<string> Retirable,HashSet<string> Carriers,Dictionary<ObjectId,Node> Tree,HashSet<string> Definitions,Dictionary<ObjectId,JsonElement> Retained);
    internal sealed record ReadValue(bool eligible,string? reason,string scope);
    static void Need(bool value,string message,object? details=null)
    {if(!value)throw new DomainException("UNSUPPORTED_TABLE_FIELD_DELETION",message,false,details);}
    static bool Live(ObjectId id)=>!id.IsNull&&id.IsValid&&!id.IsErased&&!id.IsEffectivelyErased;
    static bool Local(DBObject item,Table table)=>table.Database!=null&&table.Database.UnmanagedObject!=IntPtr.Zero&&item.Database?.UnmanagedObject==table.Database.UnmanagedObject;
    static string Id(ObjectId id)=>Validation.NormalizeHandle(id.Handle.ToString());
    internal static string Id(JsonElement record)=>record.GetProperty("common").GetProperty("handle").GetString()!;
    internal static Dictionary<string,JsonElement> Records(Snapshot snapshot,Action cancel)
    {
        var result=new Dictionary<string,JsonElement>(StringComparer.Ordinal);
        foreach(var record in snapshot.Metadata.GetProperty("owned").EnumerateArray())
        {cancel();Need(result.TryAdd(Id(record),record),"Actual Table storage repeats an identity.");}
        return result;
    }
    static bool Reactors(DBObject item,Table table,Transaction tr,Action cancel,Action<string,string?>? trace=null,bool allowUnusedTableBinding=false)
    {
        trace?.Invoke("DBObject.GetPersistentReactorIds",Id(item.ObjectId));
        using var reactors=item.GetPersistentReactorIds();if(reactors.Count==0)return true;
        if(item.GetType()==typeof(Field)&&item.OwnerId==table.ObjectId&&reactors.Count==1&&reactors[0]==table.ObjectId)
        {
            int bindings=0;trace?.Invoke("owning_Table.current_Field_bindings",Id(item.ObjectId));
            for(int row=0;row<table.Rows.Count;row++)for(int column=0;column<table.Columns.Count;column++)
            for(int index=0;index<table.Cells[row,column].Contents.Count;index++)
            {cancel();if(table.GetFieldId(row,column,index)==item.ObjectId)bindings++;}
            return bindings==1||allowUnusedTableBinding&&bindings==0;
        }
        if(item.GetType()!=typeof(DBDictionary)&&item.GetType()!=typeof(Field)||!Live(item.OwnerId)||reactors.Count!=1)return false;
        trace?.Invoke("owning_dictionary.open_parent",Id(item.OwnerId));
        var parent=tr.GetObject(item.OwnerId,OpenMode.ForRead);
        if(parent.GetType()!=typeof(DBDictionary)||!Local(parent,table))return false;
        JsonElement Dictionary(DBDictionary dictionary,bool child)
        {
            var entries=new List<object>();var budget=new ModelInventoryBudget(cancel);
            trace?.Invoke("owning_dictionary.entries",Id(dictionary.ObjectId));
            foreach(DBDictionaryEntry entry in dictionary)
            {
                cancel();if(!Live(entry.Value))return JsonSerializer.SerializeToElement<object?>(null,Wire.Json);
                var value=tr.GetObject(entry.Value,OpenMode.ForRead);
                var edge=new{handle=Id(entry.Value),owner_handle=Live(value.OwnerId)?Id(value.OwnerId):null,runtime_type=value.GetType().FullName,owned=Local(value,table)&&value.OwnerId==dictionary.ObjectId};
                budget.Add(edge);entries.Add(edge);
            }
            return Wire.Element(new{common=new{handle=Id(dictionary.ObjectId),owner_handle=Id(dictionary.OwnerId),runtime_type=dictionary.GetType().FullName,
                reactors=child?reactors.Cast<ObjectId>().Select(Id).ToArray():Array.Empty<string>()},dictionary_hard_owned=dictionary.TreatElementsAsHard,entries});
        }
        trace?.Invoke("owning_dictionary.snapshot",Id(item.ObjectId));
        var current=item is DBDictionary nativeDictionary?Dictionary(nativeDictionary,true):Wire.Element(new{common=new{handle=Id(item.ObjectId),owner_handle=Id(item.OwnerId),runtime_type=item.GetType().FullName,
            reactors=reactors.Cast<ObjectId>().Select(Id).ToArray()},table_bound_field=Definition((Field)item)});
        var owner=Dictionary((DBDictionary)parent,false);
        if(current.ValueKind==JsonValueKind.Null||owner.ValueKind==JsonValueKind.Null)return false;
        return NativeTableSelectedFieldStorage364.ReactorsMatchOwningDictionary(current,new Dictionary<string,JsonElement>(StringComparer.Ordinal){{Id(item.ObjectId),current},{Id(parent.ObjectId),owner}},cancel);
    }
    static Dictionary<ObjectId,Node> Tree(Table table,Field root,Transaction tr,Action cancel,Action<string,string?>? trace=null)
    {
        var result=new Dictionary<ObjectId,Node>();var pending=new Stack<(ObjectId Id,ObjectId Owner)>();var budget=new ModelInventoryBudget(cancel);
        pending.Push((root.ObjectId,root.OwnerId));
        while(pending.Count>0)
        {
            cancel();var next=pending.Pop();Need(Live(next.Id),"Selected Field-owned identity is unavailable.");
            trace?.Invoke("selected_tree.open",Id(next.Id));
            var item=tr.GetObject(next.Id,OpenMode.ForRead);
            trace?.Invoke("selected_tree.identity_owner",Id(next.Id));
            Need(Local(item,table)&&item.OwnerId==next.Owner,
                "Selected Field child is foreign/shared/referenced rather than actually owned.",new{handle=Id(next.Id),owner_handle=Id(next.Owner)});
            var node=new Node(next.Id,next.Owner,item.GetType().FullName!);
            Need(result.TryAdd(next.Id,node),"Selected Field tree repeats an identity; shared child ownership is not inferred.",new{handle=Id(next.Id)});
            budget.Add(new{handle=Id(next.Id),owner_handle=Id(next.Owner),runtime_type=node.Runtime});
            if(item.GetType()==typeof(Field)&&item is Field field)
            {
                string? fieldHandle=trace==null?null:Id(next.Id);
                trace?.Invoke("Field.GetChildren",fieldHandle);
                var children=field.GetChildren();
                try
                {
                    Need(children!=null,"Actual selected Field children are unavailable.",new{field_handle=Id(next.Id)});
                    foreach(var child in children!)
                    {
                        trace?.Invoke("Field.child_identity_owner",Id(next.Id));
                        cancel();Need(child!=null&&child.GetType()==typeof(Field)&&Live(child.ObjectId)&&Local(child,table)&&child.OwnerId==next.Id,
                            "Actual child Field has no exclusive resident parent ownership; no child was erased.",new{field_handle=Id(next.Id)});
                        trace?.Invoke("Field.child_resident_open",Id(next.Id));
                        var resident=tr.GetObject(child!.ObjectId,OpenMode.ForRead);
                        Need(resident.GetType()==typeof(Field)&&resident.UnmanagedObject==child.UnmanagedObject&&resident.ObjectId==child.ObjectId&&resident.OwnerId==next.Id,
                            "Returned child Field wrapper differs from its current resident identity/owner.");
                        pending.Push((child.ObjectId,next.Id));
                    }
                }
                finally
                {
                    if(children!=null)foreach(var child in children)
                    {
                        try{child?.Dispose();}
                        catch{trace?.Invoke("Field.child_wrapper_release",fieldHandle);throw;}
                    }
                }
            }
            else if(item.GetType()==typeof(DBDictionary)&&item is DBDictionary dictionary)
            {
                trace?.Invoke("selected_tree.dictionary_entries",Id(next.Id));
                foreach(DBDictionaryEntry entry in dictionary)
                {
                    cancel();Need(Live(entry.Value),"Selected Field dictionary child is unavailable.");
                    var child=tr.GetObject(entry.Value,OpenMode.ForRead);
                    Need(Local(child,table)&&child.OwnerId==next.Id,
                        "Selected Field definition contains a pointer/foreign dictionary child.",new{dictionary_handle=Id(next.Id),child_handle=Id(entry.Value)});
                    pending.Push((entry.Value,next.Id));
                }
            }
            else Need(item.GetType()==typeof(Xrecord),"Selected Field-owned data "+Id(next.Id)+" ("+node.Runtime+") needs its exact typed retention/cleanup adapter.",new{handle=Id(next.Id),runtime_type=node.Runtime});
            trace?.Invoke("DBObject.ExtensionDictionary",Id(next.Id));
            if(!item.ExtensionDictionary.IsNull)pending.Push((item.ExtensionDictionary,next.Id));
        }
        return result;
    }
    // Read-only observation of an actual resident default Field left in the
    // same exclusive layout MText after native content cleanup. This is owned
    // storage, not a Table slot, and must remain observable/retained thereafter.
    internal static bool OwnedBy(Field field,MText text,Transaction tr,Action cancel)
    {
        DBObject child=field;var seen=new HashSet<ObjectId>();
        while(child.OwnerId!=text.ObjectId)
        {
            cancel();if(!Live(child.OwnerId)||!seen.Add(child.OwnerId))return false;
            var owner=tr.GetObject(child.OwnerId,OpenMode.ForRead);
            if(owner.GetType()!=typeof(DBDictionary)||owner.Database?.UnmanagedObject!=text.Database?.UnmanagedObject)return false;
            var dictionary=(DBDictionary)owner;int count=0;
            foreach(DBDictionaryEntry entry in dictionary){cancel();if(entry.Value==child.ObjectId)count++;}
            if(count!=1)return false;child=owner;
        }
        return child.GetType()==typeof(DBDictionary)&&text.ExtensionDictionary==child.ObjectId;
    }
    internal static void ObserveUnbound(Table table,Transaction tr,Dictionary<ObjectId,JsonElement> fields,Action cancel)
    {
        cancel();var id=table.BlockTableRecord;if(id.IsNull)return;
        var item=tr.GetObject(id,OpenMode.ForRead);
        Need(item.GetType()==typeof(BlockTableRecord)&&item.Database?.UnmanagedObject==table.Database?.UnmanagedObject&&item.OwnerId==table.Database!.BlockTableId,
            "Native retained Field storage requires the actual local Table layout block.");
        var block=(BlockTableRecord)item;
        Need(block.IsAnonymous&&!block.IsLayout&&!block.IsFromExternalReference,"Native retained Field storage requires the ordinary anonymous layout block.");
        using var refs=block.GetBlockReferenceIds(true,false);
        Need(refs.Count==1&&refs[0]==table.ObjectId,"Native retained Field storage has no exclusive reverse Table reference.");
        foreach(ObjectId member in block)
        {
            cancel();var value=tr.GetObject(member,OpenMode.ForRead);
            Need(value.Database?.UnmanagedObject==table.Database?.UnmanagedObject&&value.OwnerId==id,"Native layout member ownership is inconsistent.");
            if(value.GetType()!=typeof(MText)||!value.HasFields)continue;
            var text=(MText)value;var fieldId=text.GetField();if(fieldId.IsNull||fields.ContainsKey(fieldId))continue;
            Need(Live(fieldId),"Unbound native default Field identity is unavailable.");
            var native=tr.GetObject(fieldId,OpenMode.ForRead);
            Need(native.GetType()==typeof(Field)&&native.Database?.UnmanagedObject==table.Database?.UnmanagedObject&&OwnedBy((Field)native,text,tr,cancel),
                "Unbound default Field has no actual MText owning-dictionary path.",new{mtext_handle=Id(member),field_handle=Id(fieldId)});
            fields.Add(fieldId,Definition((Field)native));
        }
    }
    internal static Selection Prepare(Table table,Snapshot before,IEnumerable<string> selected,Transaction tr,Action cancel,Action<string,string?>? trace=null)
    {
        trace?.Invoke("selected_storage.records",null);
        var fields=selected.ToHashSet(StringComparer.Ordinal);var records=Records(before,cancel);var retired=new HashSet<string>(StringComparer.Ordinal);var carriers=new HashSet<string>(StringComparer.Ordinal);var tree=new Dictionary<ObjectId,Node>();bool canRetire=true;
        foreach(string id in fields)
        {
            trace?.Invoke("selected_binding.exclusivity",id);
            cancel();Need(before.Contents.SelectMany(slots=>slots).Count(slot=>slot.field_handle==id)==1,
                "Selected Field is shared by another current Table content slot; retain its definition and use a binding-specific adapter.",new{field_handle=id});
            Need(records.Values.Count(record=>record.TryGetProperty("table_cell_mtext_owner",out var role)&&role.GetProperty("field_handle").GetString()==id)<=1,
                "Selected Field has more than one actual layout MText default reference; binding-specific cleanup needs its owning adapter.",new{field_handle=id});
            Need(records.TryGetValue(id,out var record)&&record.TryGetProperty("table_bound_field",out _),"Selected Field is outside the actual observed Table owner graph.",new{field_handle=id});
            trace?.Invoke("selected_Field.resolve_open",id);
            var native=tr.GetObject(CadStructureWriter.Find(table.Database,id),OpenMode.ForRead);
            Need(native.GetType()==typeof(Field),"Selected Field runtime type changed.");
            foreach(var (key,node) in Tree(table,(Field)native,tr,cancel,trace))
            {
                cancel();Need(tree.TryAdd(key,node),"Selected definitions share a Field-owned child; no shared data was erased.",new{handle=Id(key)});
                if(key!=native.ObjectId&&before.Contents.SelectMany(slots=>slots).Any(slot=>slot.field_handle==Id(key)))canRetire=false;
                // Retirement is optional. A noncanonical persistent reference
                // selects exact typed retention instead of requiring a purge.
                if(!Reactors(tr.GetObject(key,OpenMode.ForRead),table,tr,cancel,trace))canRetire=false;
                retired.Add(Id(key));
            }
        }
        // Actual exclusively owned descendants of a selected definition may
        // intentionally retire. Pointer entries are not ownership evidence.
        trace?.Invoke("selected_storage.owned_descendants",null);
        bool changed;
        do
        {
            changed=false;
            foreach(var (id,record) in records)
            {
                cancel();if(retired.Contains(id))continue;string? owner=record.GetProperty("common").GetProperty("owner_handle").GetString();
                if(owner!=null&&retired.Contains(owner))
                {
                    if(record.TryGetProperty("table_cell_mtext_owner",out _)||record.TryGetProperty("table_layout_storage_block",out _)||
                        record.TryGetProperty("table_bound_field",out _)&&!fields.Contains(id))canRetire=false;
                    retired.Add(id);changed=true;
                }
            }
        }while(changed);
        foreach(string id in retired)
        {
            trace?.Invoke("selected_storage.entries_reactors",id);
            cancel();if(!records.TryGetValue(id,out var record))continue; // Actual GetChildren/owner tree, intentionally selected, may not be a cell-bound snapshot node.
            if(record.TryGetProperty("entries",out var edges))foreach(var edge in edges.EnumerateArray())
            {cancel();if(!edge.GetProperty("owned").GetBoolean()||!retired.Contains(edge.GetProperty("handle").GetString()!))canRetire=false;}
            // Selected roots already have the live native owner/reactor proof,
            // including a direct Table owner with its actual sole cell binding.
            if(!fields.Contains(id)&&!NativeTableSelectedFieldStorage364.ReactorsMatchOwningDictionary(record,records,cancel))canRetire=false;
        }
        trace?.Invoke("selected_storage.exclusive_ancestors",null);
        // Ancestors may disappear only if every actual hard-owned entry is a
        // selected Field/retirable node and no unrelated metadata is attached.
        do
        {
            changed=false;
            foreach(var (id,record) in records)
            {
                cancel();if(retired.Contains(id)||!record.TryGetProperty("entries",out var edges)||edges.GetArrayLength()==0)continue;
                var common=record.GetProperty("common");
                if(!record.TryGetProperty("dictionary_hard_owned",out var hard)||hard.ValueKind!=JsonValueKind.True||
                    common.GetProperty("xdata").ValueKind!=JsonValueKind.Null||common.GetProperty("extension_handle").ValueKind!=JsonValueKind.Null||
                    !NativeTableSelectedFieldStorage364.ReactorsMatchOwningDictionary(record,records,cancel))continue;
                bool only=true;
                foreach(var edge in edges.EnumerateArray())
                {
                    cancel();string child=edge.GetProperty("handle").GetString()!;
                    if(!edge.GetProperty("owned").GetBoolean()||edge.GetProperty("owner_handle").GetString()!=id||!retired.Contains(child)||
                        !records.TryGetValue(child,out var node)||node.GetProperty("common").GetProperty("owner_handle").GetString()!=id){only=false;break;}
                }
                if(only){retired.Add(id);changed=true;}
            }
        }while(changed);
        foreach(var (id,record) in records)
        {
            trace?.Invoke("selected_carrier.retained_data",id);
            cancel();if(!record.TryGetProperty("table_cell_mtext_owner",out var role)||!fields.Contains(role.GetProperty("field_handle").GetString()??""))continue;
            var common=record.GetProperty("common");var extension=common.GetProperty("extension_handle");
            if(common.GetProperty("xdata").ValueKind!=JsonValueKind.Null||common.GetProperty("reactors").GetArrayLength()!=0||
                extension.ValueKind!=JsonValueKind.Null&&!retired.Contains(extension.GetString()!))canRetire=false;
            carriers.Add(id);
        }
        var retained=new Dictionary<ObjectId,JsonElement>();
        if(!canRetire)
        {
            // No selected storage/carrier may disappear in binding-only mode.
            // The existing Table metadata comparer retains its owner graph;
            // this snapshot adds actual child definitions and typed references.
            retired.Clear();carriers.Clear();
            retained=CadTableFieldStorageRetention383.Capture(table,tree.Keys,tr,cancel,trace);
        }
        return new(fields,retired,carriers,tree,canRetire?tree.Keys.Select(Id).ToHashSet(StringComparer.Ordinal):new HashSet<string>(StringComparer.Ordinal),retained);
    }
    internal static void VerifyRetirement(Table table,Selection selected,Transaction tr,Action cancel)
    {
        if(selected.Retained.Count>0)
        {CadTableFieldStorageRetention383.Verify(table,selected.Retained,tr,cancel);return;}
        foreach(var (id,node) in selected.Tree)
        {
            cancel();if(!Live(id))continue;var current=tr.GetObject(id,OpenMode.ForRead);
            Need(Local(current,table)&&current.OwnerId==node.Owner&&current.GetType().FullName==node.Runtime,
                "Native cleanup reparented/retyped surviving selected definition storage.",new{handle=Id(id)});
            Need(Reactors(current,table,tr,cancel,allowUnusedTableBinding:selected.Fields.Contains(Id(id))&&node.Owner==table.ObjectId),"Native cleanup introduced a foreign dependency into selected unused storage.",new{handle=Id(id)});
            if(current.GetType()==typeof(Field)&&current is Field field)
            {
                var children=field.GetChildren();
                try
                {
                    Need(children!=null,"Actual selected retained Field children are unavailable.");
                    foreach(var child in children!)
                    {
                        cancel();Need(child!=null&&Live(child.ObjectId)&&selected.Tree.ContainsKey(child.ObjectId)&&Local(child,table)&&child.OwnerId==id,"Native cleanup introduced/rebound a child into selected definition storage.",new{field_handle=Id(id)});
                        var resident=tr.GetObject(child!.ObjectId,OpenMode.ForRead);
                        Need(resident.UnmanagedObject==child.UnmanagedObject&&resident.GetType()==typeof(Field)&&resident.OwnerId==id,"Returned selected child differs from its actual retained resident Field.");
                    }
                }
                finally{if(children!=null)foreach(var child in children)child?.Dispose();}
            }
            else if(current.GetType()==typeof(DBDictionary)&&current is DBDictionary dictionary)
            {
                foreach(DBDictionaryEntry entry in dictionary)
                {
                    cancel();Need(Live(entry.Value)&&selected.Tree.ContainsKey(entry.Value),"Native cleanup introduced a foreign dictionary edge into selected unused storage.",new{handle=Id(id)});
                    var child=tr.GetObject(entry.Value,OpenMode.ForRead);Need(Local(child,table)&&child.OwnerId==id,"Native cleanup changed a selected dictionary edge into a pointer/foreign owner.",new{handle=Id(id)});
                }
            }
            Need(current.ExtensionDictionary.IsNull||selected.Tree.TryGetValue(current.ExtensionDictionary,out var extension)&&extension.Owner==id,
                "Native cleanup introduced an extension outside the selected definition tree.",new{handle=Id(id)});
        }
    }
    internal static ReadValue? Read(Table table,Transaction tr,Snapshot snapshot,CellValue cell,Slot slot,Action cancel)
    {
        if(slot.field_handle==null)return null;
        string stage="selected_slot.anchor",observed=slot.field_handle;
        void Trace(string current,string? handle){stage=current;observed=handle??slot.field_handle;}
        string Failure(Exception error)
        {
            const int reasonBytes=2048;
            string status=error is Teigha.Runtime.Exception native?native.ErrorStatus.ToString():"not_native";
            string code=error is DomainException domain?domain.Error.code:"observation_exception";
            string prefix="Selected Field deletion observation unavailable at "+stage+" [selected_field="+slot.field_handle+", observed_handle="+observed+", cell="+cell.row+","+cell.column+","+slot.content_index+"]: "+error.GetType().FullName+"; code="+code+"; native_status="+status+"; message=";
            string message=error.Message??"",full=prefix+message;
            if(JsonSerializer.SerializeToUtf8Bytes(full,Wire.Json).Length<=reasonBytes)return full;
            const string omitted=" [message truncated]";
            if(JsonSerializer.SerializeToUtf8Bytes(prefix+omitted,Wire.Json).Length>reasonBytes)
                return "Selected Field deletion observation unavailable at "+stage+" [handle="+observed+"]: native_status="+status+"; oversized exception type/message omitted.";
            var text=new StringBuilder(prefix);
            foreach(var rune in message.EnumerateRunes())
            {
                string next=rune.ToString();
                if(JsonSerializer.SerializeToUtf8Bytes(text.ToString()+next+omitted,Wire.Json).Length>reasonBytes)break;
                text.Append(next);
            }
            return text.ToString()+omitted;
        }
        try
        {
            Need(Anchor(cell)&&cell.content_editable,"Selected Field slot is protected or not a merged anchor.");
            Trace("Table.GetPersistentReactorIds",snapshot.Identity.Split(':')[0]);
            using var reactors=table.GetPersistentReactorIds();Need(reactors.Count==0,"Persistent Table-root dependencies require their owning adapter.");
            var selected=Prepare(table,snapshot,new[]{slot.field_handle},tr,cancel,Trace);
            return new(true,null,selected.Retained.Count==0?
                "selected_sole_binding_local_Field; native_DeleteContent; proved_selected_owned_storage_may_retire; auxiliary_cleanup_unproved; no_purge_or_reuse":
                "selected_sole_binding_local_Field; native_DeleteContent_binding_only; selected_storage_children_and_typed_persistent_dependencies_must_survive_exactly; auxiliary_cleanup_unproved; no_purge_or_reuse");
        }
        catch(OperationCanceledException){throw;}
        catch(DomainException error){return new(false,Failure(error),"selected_Field_content_binding_only; exact_typed_storage_or_dependency_observation_unavailable; no_purge_or_foreign_waiver");}
        catch(Exception error){return new(false,Failure(error),"selected_Field_only; no_cleanup_or_reuse_inferred");}
    }
}
