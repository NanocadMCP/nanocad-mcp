using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// Native Table layout storage is referenced by the Table, not owned by it.
// Admit a cell Field's MText carrier only through the actual exclusive layout
// block, its actual member/owner identity and MText.GetField(). No name, pointer
// text, NOD path or spatial correspondence supplies an ownership proof.
internal static class CadTableFormulaMTextOwner362
{
    internal sealed record Binding(int row,int column,string field_handle);
    internal sealed record Owner(ObjectId Id,ObjectId OwnerId,ObjectId FieldId,JsonElement Role,string Contents);
    internal sealed record Inventory(JsonElement Block,Dictionary<ObjectId,Owner> Owners);
    static bool Live(ObjectId id)=>!id.IsNull&&id.IsValid&&!id.IsErased&&!id.IsEffectivelyErased;
    static string? Id(ObjectId id)=>id.IsNull?null:Validation.NormalizeHandle(id.Handle.ToString());
    static bool Local(DBObject value,Table table)=>value.Database?.UnmanagedObject==table.Database?.UnmanagedObject&&
        table.Database!=null&&table.Database.UnmanagedObject!=IntPtr.Zero;
    static void Need(bool value,string message,object details)
    {if(!value)throw new DomainException("OUT_OF_SCOPE",message,false,details);}

    internal static Inventory Capture(Table table,Transaction tr,Action cancel,IReadOnlyDictionary<ObjectId,JsonElement> fields)
    {
        var owners=new Dictionary<ObjectId,Owner>();var blockId=table.BlockTableRecord;
        if(blockId.IsNull)return new(JsonSerializer.SerializeToElement<object?>(null,Wire.Json),owners);
        Need(Live(blockId),"Native Table layout block is unavailable.",new{table_handle=Id(table.ObjectId),block_handle=Id(blockId)});
        var value=tr.GetObject(blockId,OpenMode.ForRead);
        Need(value.GetType()==typeof(BlockTableRecord)&&Local(value,table)&&value.OwnerId==table.Database!.BlockTableId,
            "Native Table layout storage has no actual local block-table ownership.",
            new{table_handle=Id(table.ObjectId),block_handle=Id(blockId),block_owner_handle=Id(value.OwnerId),runtime_type=value.GetType().FullName});
        var block=(BlockTableRecord)value;
        Need(block.IsAnonymous&&!block.IsLayout&&!block.IsFromExternalReference,
            "Native Table cell storage requires its local anonymous layout block.",
            new{table_handle=Id(table.ObjectId),block_handle=Id(blockId),anonymous=block.IsAnonymous,layout=block.IsLayout,external=block.IsFromExternalReference});
        using var references=block.GetBlockReferenceIds(true,false);var refs=references.Cast<ObjectId>().ToArray();
        Need(refs.Length==1&&refs[0]==table.ObjectId,
            "Native Table cell storage has no exact exclusive reverse Table reference.",
            new{table_handle=Id(table.ObjectId),block_handle=Id(blockId),reference_count=refs.Length,
                reference_handles=refs.Take(16).Select(Id).ToArray(),omitted_reference_diagnostics=Math.Max(0,refs.Length-16)});
        var budget=new ModelInventoryBudget(cancel);var bindings=new Dictionary<ObjectId,List<Binding>>();
        for(int row=0;row<table.Rows.Count;row++)for(int column=0;column<table.Columns.Count;column++)
        {
            cancel();if(table.Cells[row,column].Contents.Count!=1)continue;
            var id=table.GetFieldId(row,column,0);if(id.IsNull)continue;
            Need(fields.ContainsKey(id),"Native Table layout Field is outside its proved cell formula inventory.",
                new{table_handle=Id(table.ObjectId),row,column,field_handle=Id(id)});
            if(!bindings.TryGetValue(id,out var slots))bindings.Add(id,slots=[]);
            var slot=new Binding(row,column,Id(id)!);budget.Add(slot);slots.Add(slot);
        }
        foreach(ObjectId member in block)
        {
            cancel();Need(Live(member),"Native Table layout member is unavailable.",new{table_handle=Id(table.ObjectId),block_handle=Id(blockId),member_handle=Id(member)});
            var item=tr.GetObject(member,OpenMode.ForRead);
            Need(Local(item,table)&&item.OwnerId==blockId,"Native Table layout membership disagrees with its actual member owner.",
                new{table_handle=Id(table.ObjectId),block_handle=Id(blockId),member_handle=Id(member),member_owner_handle=Id(item.OwnerId),runtime_type=item.GetType().FullName});
            if(item.GetType()!=typeof(MText))continue;
            var text=(MText)item;var field=text.HasFields?text.GetField():ObjectId.Null;
            Need(text.Annotative!=AnnotativeStates.True,"Annotative native Table layout MText requires its context owner adapter.",
                new{table_handle=Id(table.ObjectId),block_handle=Id(blockId),mtext_handle=Id(member),annotative=text.Annotative.ToString()});
            Need(field.IsNull||Live(field)&&fields.ContainsKey(field)&&bindings.ContainsKey(field),
                "Native Table layout MText has an unclassified/non-cell Field.",
                new{table_handle=Id(table.ObjectId),block_handle=Id(blockId),mtext_handle=Id(member),field_handle=Id(field)});
            Need(!text.HasFields||!field.IsNull,"Field-bearing native Table layout MText has no actual default Field binding.",
                new{table_handle=Id(table.ObjectId),block_handle=Id(blockId),mtext_handle=Id(member)});
            var role=Wire.Element(new{table_handle=Id(table.ObjectId),block_handle=Id(blockId),field_handle=Id(field),
                cell_bindings=field.IsNull?[]:bindings[field].ToArray(),
                scope="actual_exclusive_Table_layout_block_member_and_MText_GetField; block_not_Table_owned"});
            var owner=new Owner(member,blockId,field,role,text.Contents);budget.Add(new{role,owner.Contents});owners.Add(member,owner);
        }
        using var xdata=block.XData;using var reactors=block.GetPersistentReactorIds();
        var state=Wire.Element(new{handle=Id(blockId),owner_handle=Id(block.OwnerId),runtime_type=block.GetType().FullName,
            anonymous=block.IsAnonymous,layout=block.IsLayout,external=block.IsFromExternalReference,
            reference_handles=refs.Select(Id).ToArray(),extension_handle=Id(block.ExtensionDictionary),
            xdata=xdata==null?null:NativeOwnedMetadata.Buffer(xdata,cancel,item=>budget.Add(item)),
            reactors=reactors.Cast<ObjectId>().Select(Id).ToArray(),owned=NativeOwnedMetadata.Capture(block,tr,cancel)});
        budget.Add(state);return new(state,owners);
    }

    internal static void Prove(MText text,DBObject child,ObjectId field,Inventory inventory)
    {
        Need(inventory.Owners.TryGetValue(text.ObjectId,out var owner)&&owner.FieldId==field&&
            text.ExtensionDictionary==child.ObjectId&&child.GetType()==typeof(DBDictionary),
            "Native MText does not expose this actual cell Field through its retained extension dictionary and exclusive Table layout storage.",
            new{mtext_handle=Id(text.ObjectId),mtext_owner_handle=Id(text.OwnerId),actual_default_field_handle=Id(owner?.FieldId??ObjectId.Null),
                required_field_handle=Id(field),extension_handle=Id(text.ExtensionDictionary),child_handle=Id(child.ObjectId),child_runtime_type=child.GetType().FullName});
    }
}
