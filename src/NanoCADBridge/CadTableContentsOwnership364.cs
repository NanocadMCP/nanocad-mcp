using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// A cell's native FieldId is a binding, not a claim that the Table's extension
// dictionary owns it. Resolve and retain the actual owner chain. No owner is
// reassigned, no Field is created/evaluated here and no arbitrary reference is
// treated as a Table-owned object.
internal static class CadTableContentsOwnership364
{
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)]bool value,string message,string code="UNSUPPORTED_OWNED_METADATA")
    {if(!value)throw new DomainException(code,message);}
    static bool Live(ObjectId id)=>!id.IsNull&&id.IsValid&&!id.IsErased&&!id.IsEffectivelyErased;
    static string Id(ObjectId id)
    {Need(Live(id),"Native formula ownership identity is unavailable.","OBJECT_READ_FAILED");return Validation.NormalizeHandle(id.Handle.ToString());}
    static string? ReferenceId(ObjectId id)=>id.IsNull?null:Validation.NormalizeHandle(id.Handle.ToString());
    static bool Local(DBObject value,Table table)=>table.Database!=null&&table.Database.UnmanagedObject!=IntPtr.Zero&&
        value.Database?.UnmanagedObject==table.Database.UnmanagedObject;

    sealed record FieldSlot(int row,int column,string handle,string owner_handle,string runtime_type,bool owned);
    sealed record ContentOwner(string table_handle,int rows,int columns,string table_style_handle,string scope);
    static void SlotNeed(bool value,string message,object mismatch)
    {if(!value)throw new DomainException("OUT_OF_SCOPE",message,false,mismatch);}
    static FieldSlot[] Slots(TableContent content,Table table,Transaction tr,Action cancel)
    {
        SlotNeed(Local(content,table)&&Live(content.ObjectId),"Native TableContent owner is outside the current drawing.",
            new{kind="content_owner_identity",content_handle=ReferenceId(content.ObjectId),content_owner_handle=ReferenceId(content.OwnerId),
                same_native_database=Local(content,table),content_identity_live=Live(content.ObjectId)});
        int contentRows=content.NumberOfRows,contentColumns=content.NumberOfColumns,tableRows=table.Rows.Count,tableColumns=table.Columns.Count;
        var contentStyle=content.TableStyleId;var tableStyle=table.TableStyle;
        SlotNeed(contentRows==tableRows&&contentColumns==tableColumns&&contentStyle==tableStyle,
            "Native formula TableContent does not match its resident Table's topology/style.",
            new{kind="content_topology_or_style",content_rows=contentRows,content_columns=contentColumns,table_rows=tableRows,table_columns=tableColumns,
                content_style_handle=ReferenceId(contentStyle),table_style_handle=ReferenceId(tableStyle)});
        var result=new List<FieldSlot>();var budget=new ModelInventoryBudget(cancel);
        for(int row=0;row<table.Rows.Count;row++)for(int column=0;column<table.Columns.Count;column++)
        {
            cancel();int count=table.Cells[row,column].Contents.Count;
            SlotNeed(count is 0 or 1,"Multiple native TableContent cell contents need their owning adapter.",new{kind="cell_content_count",row,column,table_content_count=count});
            var actual=content.GetFieldId(row,column);var bound=count==0?ObjectId.Null:table.GetFieldId(row,column,0);
            SlotNeed(actual==bound,"Native formula TableContent cell binding differs from its resident Table.",
                new{kind="cell_field_binding",row,column,content_field_handle=ReferenceId(actual),table_field_handle=ReferenceId(bound)});
            if(actual.IsNull)continue;
            SlotNeed(count==1,"Native TableContent has no single actual indexed Field binding.",
                new{kind="cell_field_binding",row,column,content_field_handle=ReferenceId(actual),table_content_count=count});
            var field=tr.GetObject(actual,OpenMode.ForRead);
            SlotNeed(field.GetType()==typeof(Field)&&Local(field,table)&&Live(field.OwnerId),"Native TableContent references an unavailable/foreign Field.",
                new{kind="cell_field_identity",row,column,field_handle=ReferenceId(actual),field_owner_handle=ReferenceId(field.OwnerId),
                    field_runtime_type=field.GetType().FullName,same_native_database=Local(field,table),field_owner_live=Live(field.OwnerId)});
            var slot=new FieldSlot(row,column,Id(actual),Id(field.OwnerId),field.GetType().FullName!,field.OwnerId==content.ObjectId);
            budget.Add(slot);result.Add(slot);
        }
        return result.ToArray();
    }

    static DomainException Unproved(Table table,ObjectId field,JsonElement definition,IReadOnlyList<object> path,DBObject? owner,string reason,Action cancel,object? mismatch=null)
    {
        IEnumerable<object> Bindings()
        {
            for(int row=0;row<table.Rows.Count;row++)for(int column=0;column<table.Columns.Count;column++)
            {
                cancel();for(int index=0;index<table.Cells[row,column].Contents.Count;index++)
                {
                if(table.GetContentTypes(row,column,index)==CellContentTypes.Block||table.GetFieldId(row,column,index)!=field)continue;
                var content=table.Cells[row,column].Contents[index];yield return new{row,column,content_index=index,native_has_formula=content.HasFormula,
                    native_formula=table.GetFormula(row,column,index),content_type=table.GetContentTypes(row,column,index).ToString()};
                }
            }
        }
        return new("OUT_OF_SCOPE","Native bound cell Field ownership is not proved: "+reason,false,
            CadTableFormulaOwnershipDiagnostics360.Create(Id(table.ObjectId),Id(field),owner==null?null:Id(owner.ObjectId),
                owner?.GetType().FullName,path,definition,Bindings(),mismatch,cancel));
    }

    static ObjectId Root(Table table,ObjectId field,JsonElement definition,Transaction tr,Action cancel,Dictionary<ObjectId,FieldSlot[]> contentOwners,
        CadTableContentsMTextOwner364.Inventory mtextOwners)
    {
        var child=tr.GetObject(field,OpenMode.ForRead);var visited=new HashSet<ObjectId>{field};var path=new List<object>();
        var budget=new ModelInventoryBudget(cancel);
        Need(child.GetType()==typeof(Field)&&Local(child,table),"Expected the actual local bound cell Field.","OUT_OF_SCOPE");
        while(child.OwnerId!=table.ObjectId)
        {
            cancel();if(!Live(child.OwnerId))throw Unproved(table,field,definition,path,null,"an owner identity is unavailable.",cancel);
            if(!visited.Add(child.OwnerId))throw Unproved(table,field,definition,path,null,"the owner path repeats an identity.",cancel);
            var parent=tr.GetObject(child.OwnerId,OpenMode.ForRead);
            var step=new{child_handle=Id(child.ObjectId),owner_handle=Id(parent.ObjectId),owner_runtime_type=parent.GetType().FullName};
            budget.Add(step);path.Add(step);
            if(!Local(parent,table))throw Unproved(table,field,definition,path,parent,"an owner belongs to another drawing.",cancel);
            if(parent.GetType()==typeof(DBDictionary)&&parent is DBDictionary dictionary)
            {
                bool listed=false;
                foreach(DBDictionaryEntry entry in dictionary){cancel();if(entry.Value==child.ObjectId)listed=true;}
                if(!listed)throw Unproved(table,field,definition,path,parent,"the actual owner dictionary does not list its child.",cancel);
            }
            else if(parent.GetType()==typeof(TableContent)&&parent is TableContent content)
            {
                if(!contentOwners.TryGetValue(content.ObjectId,out var slots))
                {
                    try{slots=Slots(content,table,tr,cancel);contentOwners.Add(content.ObjectId,slots);}
                    catch(DomainException error)when(error.Error.code=="OUT_OF_SCOPE")
                    {throw Unproved(table,field,definition,path,parent,error.Error.message,cancel,error.Error.details);}
                }
                bool owns=child.GetType()==typeof(Field)&&slots.Any(slot=>slot.handle==Id(child.ObjectId)&&slot.owned)||
                    child.GetType()==typeof(DBDictionary)&&content.ExtensionDictionary==child.ObjectId;
                if(!owns)throw Unproved(table,field,definition,path,parent,"the native TableContent does not expose this owned child binding.",cancel);
            }
            else if(parent.GetType()==typeof(MText)&&parent is MText text)
            {
                try{CadTableContentsMTextOwner364.Prove(text,child,field,mtextOwners);}
                catch(DomainException error)when(error.Error.code=="OUT_OF_SCOPE")
                {throw Unproved(table,field,definition,path,parent,error.Error.message,cancel,error.Error.details);}
                // A real exclusive Table layout block owns this MText. The
                // Table references that block; it does not own/reparent it.
                return text.ObjectId;
            }
            else throw Unproved(table,field,definition,path,parent,"this actual native owner needs its dedicated adapter.",cancel);
            child=parent;
        }
        // The returned root can be a direct Field, a dictionary which is not
        // the extension dictionary, or the Table's actual TableContent object.
        return child.ObjectId;
    }

    internal static JsonElement Capture(Table table,Transaction tr,Action cancel,IReadOnlyDictionary<ObjectId,JsonElement> fields)
    {
        cancel();var contentOwners=new Dictionary<ObjectId,FieldSlot[]>();var roots=new HashSet<ObjectId>();
        var mtextOwners=CadTableContentsMTextOwner364.Capture(table,tr,cancel,fields);
        if(!table.ExtensionDictionary.IsNull)roots.Add(table.ExtensionDictionary);
        var fieldsByHandle=fields.Keys.ToDictionary(Id,StringComparer.Ordinal);
        foreach(var (id,definition) in fields){cancel();roots.Add(Root(table,id,definition,tr,cancel,contentOwners,mtextOwners));}
        foreach(var id in mtextOwners.Owners.Keys)roots.Add(id);
        var pending=new Stack<(ObjectId Id,ObjectId Owner)>();var scheduled=new Dictionary<ObjectId,ObjectId>();
        void Push(ObjectId id,ObjectId owner)
        {
            if(scheduled.TryGetValue(id,out var prior)){Need(prior==owner,"Native formula ownership repeats an identity under different owners.");return;}
            scheduled.Add(id,owner);pending.Push((id,owner));
        }
        foreach(var id in roots.OrderByDescending(value=>Id(value),StringComparer.Ordinal))
            Push(id,mtextOwners.Owners.TryGetValue(id,out var owner)?owner.OwnerId:table.ObjectId);
        var records=new List<JsonElement>();long bytes=2;
        void Charge(object value)
        {
            cancel();bytes+=JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json).LongLength+1;
            Need(bytes<=8L*1024*1024,"Native formula ownership inventory exceeds the actual8MiB budget; no owner or sibling was truncated.","RESPONSE_LIMIT_EXCEEDED");
        }
        while(pending.Count>0)
        {
            cancel();long recordStart=bytes;var next=pending.Pop();_=Id(next.Id);var item=tr.GetObject(next.Id,OpenMode.ForRead);
            Need(Local(item,table)&&item.OwnerId==next.Owner,"Native formula ownership has inconsistent document/owner identity.");
            bool formula=item.GetType()==typeof(Field)&&fields.ContainsKey(item.ObjectId);
            bool nativeContent=item.GetType()==typeof(TableContent)&&contentOwners.ContainsKey(item.ObjectId);
            bool nativeMText=item.GetType()==typeof(MText)&&mtextOwners.Owners.ContainsKey(item.ObjectId);
            Need(!item.HasFields||formula||nativeContent||nativeMText,"Field-bearing owned data has no actual indexed content owner adapter.");
            using var xdata=item.XData;using var reactors=item.GetPersistentReactorIds();var links=new List<string>();
            foreach(ObjectId link in reactors){cancel();var handle=Id(link);Charge(handle);links.Add(handle);}
            var common=new{handle=Id(item.ObjectId),owner_handle=Id(item.OwnerId),runtime_type=item.GetType().FullName,
                extension_handle=item.ExtensionDictionary.IsNull?null:Id(item.ExtensionDictionary),
                xdata=xdata==null?null:NativeOwnedMetadata.Buffer(xdata,cancel,Charge),reactors=links.OrderBy(value=>value,StringComparer.Ordinal).ToArray()};
            JsonElement record;
            if(item.GetType()==typeof(DBDictionary)&&item is DBDictionary dictionary)
            {
                var entries=new List<object>();var children=new List<(string Name,ObjectId Id)>();
                foreach(DBDictionaryEntry entry in dictionary)
                {
                    cancel();_=Id(entry.Value);var child=tr.GetObject(entry.Value,OpenMode.ForRead);bool owns=Local(child,table)&&child.OwnerId==item.ObjectId;
                    var descriptor=new{name=entry.Key,handle=Id(entry.Value),owner_handle=ReferenceId(child.OwnerId),runtime_type=child.GetType().FullName,owned=owns};
                    Charge(descriptor);entries.Add(descriptor);if(owns)children.Add((entry.Key,entry.Value));
                }
                foreach(var child in children.OrderByDescending(value=>value.Name,StringComparer.Ordinal))Push(child.Id,item.ObjectId);
                record=Wire.Element(new{common,dictionary_hard_owned=dictionary.TreatElementsAsHard,entries});
            }
            else if(item.GetType()==typeof(Xrecord)&&item is Xrecord xrecord)
            {
                using var data=xrecord.Data;record=Wire.Element(new{common,xlate_references=xrecord.XlateReferences,merge_style=xrecord.MergeStyle.ToString(),data=data==null?null:NativeOwnedMetadata.Buffer(data,cancel,Charge)});
            }
            else if(formula)record=Wire.Element(new{common,table_bound_field=fields[item.ObjectId]});
            else if(nativeMText)
            {
                var owner=mtextOwners.Owners[item.ObjectId];
                record=Wire.Element(new{common,table_cell_mtext_owner=owner.Role,cached_contents=owner.Contents});
            }
            else if(nativeContent&&item is TableContent content)
            {
                var slots=contentOwners[item.ObjectId];
                foreach(var slot in slots)
                {
                    cancel();if(!slot.owned)continue;
                    Need(fieldsByHandle.TryGetValue(slot.handle,out var bound),"Actual TableContent owns an unclassified native Field.");Push(bound,item.ObjectId);
                }
                record=Wire.Element(new{common,table_content_owner=new ContentOwner(Id(table.ObjectId),content.NumberOfRows,content.NumberOfColumns,
                    Id(content.TableStyleId),"actual_Table_owned_TableContent_and_all_native_cell_FieldId_bindings"),field_bindings=slots});
            }
            else throw new DomainException("UNSUPPORTED_OWNED_METADATA","Native Table formula owned data needs its dedicated adapter; no sibling record was omitted.",false,
                new{handle=Id(item.ObjectId),owner_handle=Id(item.OwnerId),runtime_type=item.GetType().FullName});
            if(!item.ExtensionDictionary.IsNull)Push(item.ExtensionDictionary,item.ObjectId);
            bytes=recordStart;Charge(record);records.Add(record);
        }
        var actual=records.Select(record=>record.GetProperty("common").GetProperty("handle").GetString()!).ToHashSet(StringComparer.Ordinal);
        foreach(var id in fields.Keys){cancel();Need(actual.Contains(Id(id)),"The actual Table owner graph did not contain a bound bound cell Field.","OUT_OF_SCOPE");}
        if(mtextOwners.Block.ValueKind!=JsonValueKind.Null)
        {
            var block=mtextOwners.Block;
            var observation=Wire.Element(new{common=new{handle=block.GetProperty("handle").GetString(),owner_handle=block.GetProperty("owner_handle").GetString(),
                runtime_type=block.GetProperty("runtime_type").GetString(),extension_handle=block.GetProperty("extension_handle"),
                xdata=block.GetProperty("xdata"),reactors=block.GetProperty("reactors")},table_layout_storage_block=block});
            Charge(observation);records.Add(observation);
        }
        return Wire.Element(records);
    }

}
