using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
namespace NanoCADBridge;

// New style creation never sets/reflows the existing source consumers. Observe
// actual borrowed references without applying a Table content edit adapter.
internal static class CadTableStyleCloneReferences365
{
    internal sealed record Reference(ObjectId Id,JsonElement State);
    static string? Id(ObjectId value)=>value.IsNull?null:Validation.NormalizeHandle(value.Handle.ToString());
    static bool Live(ObjectId value)=>!value.IsNull&&value.IsValid&&!value.IsErased&&!value.IsEffectivelyErased;
    static void Need(bool good,string reason,object? details=null)
    {if(!good)throw new DomainException("UNSUPPORTED_STYLE_CLONE_OWNERSHIP",reason,false,details);}
    internal static Reference[] Capture(TableStyle source,Database db,Transaction tr,Action cancel)
    {
        using var links=source.GetPersistentReactorIds();var result=new List<Reference>();var seen=new HashSet<ObjectId>();var budget=new ModelInventoryBudget(cancel);
        foreach(ObjectId id in links)
        {
            cancel();Need(Live(id)&&seen.Add(id),"Source style has an unavailable/repeated borrowed reference.",new{source_style=Id(source.ObjectId),reactor=Id(id)});
            var item=tr.GetObject(id,OpenMode.ForRead);Need(item.ObjectId==id&&item.Database?.UnmanagedObject==db.UnmanagedObject,"Source dependency is not an actual local live object.");
            var path=new List<object>();var ancestors=new HashSet<ObjectId>();var current=id;
            while(!current.IsNull)
            {
                cancel();Need(Live(current)&&ancestors.Add(current),"Actual source dependency ownership is unavailable or cyclic.",new{reactor=Id(id),current=Id(current)});
                Need(current!=source.ObjectId,"A source-owned dependency requires a native clone owner-remapping adapter.",new{source_style=Id(source.ObjectId),reactor=Id(id),owner_path=path});
                var owner=tr.GetObject(current,OpenMode.ForRead);Need(owner.ObjectId==current&&owner.Database?.UnmanagedObject==db.UnmanagedObject,"Actual dependency owner belongs to another database.");
                var row=new{handle=Id(current),owner_handle=Id(owner.OwnerId),runtime_type=owner.GetType().FullName};budget.Add(row);path.Add(row);current=owner.OwnerId;
            }
            using var xdata=item.XData;using var reactors=item.GetPersistentReactorIds();var retained=new List<string?>();
            foreach(ObjectId link in reactors){cancel();budget.Add(Id(link));retained.Add(Id(link));}
            // No cell/value/text getter, link refresh, setter or layout call on
            // these untouched references. This is a reference-header inventory,
            // not a claim of complete consumer contents/edit eligibility.
            var state=Wire.Element(new{handle=Id(id),owner_handle=Id(item.OwnerId),runtime_type=item.GetType().FullName,
                table_style_handle=item.GetType()==typeof(Table)?Id(((Table)item).TableStyle):null,owner_path=path,
                extension_handle=Id(item.ExtensionDictionary),has_fields=item.HasFields,
                xdata=xdata==null?null:NativeOwnedMetadata.Buffer(xdata,cancel,budget.Add),reactors=retained.OrderBy(value=>value,StringComparer.Ordinal).ToArray(),
                scope="untouched_actual_borrowed_reference_headers_and_ownership; consumer_content_and_edit_eligibility_unobserved"});
            budget.Add(state);result.Add(new(id,state));
        }
        return result.OrderBy(value=>Id(value.Id),StringComparer.Ordinal).ToArray();
    }
    internal static void Verify(Reference[] before,TableStyle source,Database db,Transaction tr,Action cancel)
    {
        var actual=Capture(source,db,tr,cancel);
        Need(before.Length==actual.Length&&before.Zip(actual).All(pair=>pair.First.Id==pair.Second.Id&&pair.First.State.GetRawText()==pair.Second.State.GetRawText()),
            "New style creation changed a borrowed source reference header, ownership or complete dependency membership.");
    }
}
