using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// LayoutManager owns native layout removal/bookkeeping. Call Apply only after
// disposing every selector/preview transaction; it opens fresh readback itself.
internal static class CadEmptyLayoutDeletionWriter
{
    internal sealed record Existing(ObjectId Id,string Name,ObjectId Block,LayoutProperties Properties);
    internal sealed record Resolved(StructureLifecycleEdit Edit,EntityRef Reference,ObjectId Layout,ObjectId Block,
        ObjectId ActiveSpace,ObjectId ActiveLayout,bool TileMode,Existing[] Existing,JsonElement EntityInventory,JsonElement BlockInventory,ObjectId[] ReservedEntities);
    private static void Need(bool value,string message,string code="LAYOUT_UNAVAILABLE")
    {if(!value)throw new DomainException(code,message);}
    private static EntityRef Ref(EntityRef context,ObjectId id)=>new(context.bridge_instance_id,context.document_id,Validation.NormalizeHandle(id.Handle.ToString()));
    private static bool Gone(ObjectId id)=>!id.IsValid||id.IsErased||id.IsEffectivelyErased;
    private static void Dependencies(DBObject item)
    {
        using var reactors=item.GetPersistentReactorIds();
        Need(!item.HasFields&&reactors.Count==0,"Empty layout deletion excludes fields and persistent external dependencies.");
    }
    private static Existing[] Layouts(EntityRef context,Database db,Transaction tr,Action cancel)
    {
        var dictionary=(DBDictionary)tr.GetObject(db.LayoutDictionaryId,OpenMode.ForRead);var result=new List<Existing>();
        foreach(DBDictionaryEntry entry in dictionary)
        {
            cancel();if(Gone(entry.Value))continue;
            var layout=tr.GetObject(entry.Value,OpenMode.ForRead) as Layout;
            Need(layout!=null&&layout.GetType()==typeof(Layout)&&layout.OwnerId==dictionary.ObjectId&&layout.LayoutName==entry.Key,
                "Native layout dictionary identity/name/owner is inconsistent.");
            var block=tr.GetObject(layout!.BlockTableRecordId,OpenMode.ForRead) as BlockTableRecord;
            Need(block!=null&&block.GetType()==typeof(BlockTableRecord)&&block.OwnerId==db.BlockTableId&&block.IsLayout&&block.LayoutId==layout.ObjectId&&
                !block.IsDependent&&!block.IsFromExternalReference&&!block.IsFromOverlayReference,"Layout space has inconsistent/dependent native ownership.");
            result.Add(new(layout.ObjectId,layout.LayoutName,block!.ObjectId,
                LayoutMetadataReader.ReadLayout(layout,tr,db,id=>Ref(context,id),cancel,cancel)));
        }
        return result.ToArray();
    }
    private static JsonElement Entities(Database db,Transaction tr,ObjectId? excludedSpace,Action cancel)
    {
        var entities=new List<JsonElement>();
        foreach(ObjectId spaceId in (BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead))
        {
            cancel();if(spaceId==excludedSpace||Gone(spaceId))continue;
            var space=(BlockTableRecord)tr.GetObject(spaceId,OpenMode.ForRead);
            foreach(ObjectId id in space)
            {
                cancel();if(Gone(id))continue;var item=tr.GetObject(id,OpenMode.ForRead) as Entity;
                Need(item!=null&&item.OwnerId==spaceId,"Native block-space entity ownership is inconsistent.");
                entities.Add(Wire.Element(new{handle=Validation.NormalizeHandle(id.Handle.ToString()),owner=Validation.NormalizeHandle(spaceId.Handle.ToString()),
                    type=item!.GetType().FullName,properties=EntityAdapter.StoredProperties(item)}));
            }
        }
        return Wire.Element(entities.OrderBy(item=>item.GetProperty("handle").GetString(),StringComparer.Ordinal));
    }
    private static JsonElement Blocks(Database db,Transaction tr,ObjectId? excludedSpace,Action cancel)
    {
        var records=new List<JsonElement>();
        foreach(ObjectId id in (BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead))
        {
            cancel();if(id==excludedSpace||Gone(id))continue;var block=(BlockTableRecord)tr.GetObject(id,OpenMode.ForRead);
            // Internal paper-BTR spellings are native bookkeeping, not a user's
            // named block definition. Retain their IDs and reciprocal layout ID.
            records.Add(Wire.Element(new{handle=Validation.NormalizeHandle(id.Handle.ToString()),owner=Validation.NormalizeHandle(block.OwnerId.Handle.ToString()),
                layout=block.LayoutId.IsNull?null:Validation.NormalizeHandle(block.LayoutId.Handle.ToString()),name=block.IsLayout?null:block.Name,
                block.IsLayout,block.IsDependent,block.IsFromExternalReference,block.IsFromOverlayReference}));
        }
        return Wire.Element(records.OrderBy(item=>item.GetProperty("handle").GetString(),StringComparer.Ordinal));
    }
    internal static Resolved Resolve(StructureWriteArguments args,StructureLifecycleEdit edit,Database db,Transaction tr,Action cancel)
    {
        Need(edit.Operation=="delete_layout","Expected the sole delete_layout operation.","INVALID_ARGUMENT");
        var target=CadStructureWriter.Find(db,edit.Handle);var reference=new EntityRef(args.BridgeId,args.DocumentId,edit.Handle);
        var existing=Layouts(reference,db,tr,cancel);var source=existing.SingleOrDefault(item=>item.Id==target);
        // CurrentSpaceId can be Model while the user is inside a Paper model
        // viewport. Native current layout is a distinct ownership identity.
        string activeName=LayoutManager.Current.CurrentLayout;var activeLayout=LayoutManager.Current.GetLayoutId(activeName);
        var current=existing.SingleOrDefault(item=>item.Id==activeLayout&&item.Name==activeName);
        Need(current!=null&&current.Properties.is_model==db.TileMode,"Native current layout does not belong to this active document/context.");
        Need(source!=null&&!source.Properties.is_model,"Model or a non-layout record cannot be deleted.","STRUCTURE_PROTECTED");
        Need(source!.Name==edit.ExpectedName,"Layout name differs from expected_name; refresh get_structure layouts.","STRUCTURE_NAME_MISMATCH");
        Need(source.Id!=activeLayout&&source.Block!=db.CurrentSpaceId,"The active Paper layout cannot be deleted; activate a retained layout or Model first.","STRUCTURE_PROTECTED");
        Need(existing.Count(item=>!item.Properties.is_model)>1,"Keep at least one native Paper layout.","STRUCTURE_PROTECTED");
        var paper=existing.Where(item=>!item.Properties.is_model).ToArray();
        Need(paper.All(item=>item.Properties.tab_order>0)&&paper.Select(item=>item.Properties.tab_order).Distinct().Count()==paper.Length,
            "Existing Paper tab order is inconsistent; no automatic repair.");
        var layout=(Layout)tr.GetObject(source.Id,OpenMode.ForRead);var block=(BlockTableRecord)tr.GetObject(source.Block,OpenMode.ForRead);
        Dependencies(layout);Dependencies(block);
        var reserved=new List<ObjectId>();
        foreach(ObjectId id in block)
        {
            cancel();if(Gone(id))continue;var item=tr.GetObject(id,OpenMode.ForRead);
            Need(item.GetType()==typeof(Viewport)&&item.OwnerId==source.Block&&((Viewport)item).Number==1,
                "Native Paper layout contains drawing content or an ordinary viewport; empty-layout deletion never removes sheet content.","STRUCTURE_IN_USE");
            Dependencies(item);reserved.Add(id);
        }
        Need(reserved.Count<=1,"Native empty Paper layout contains more than one reserved viewport1.");
        return new(edit,reference,source.Id,source.Block,db.CurrentSpaceId,activeLayout,db.TileMode,existing,
            Entities(db,tr,source.Block,cancel),Blocks(db,tr,source.Block,cancel),reserved.ToArray());
    }
    private static void VerifySelection(Resolved expected,Resolved actual)
    {
        Need(expected.Layout==actual.Layout&&expected.Block==actual.Block&&expected.ActiveSpace==actual.ActiveSpace&&expected.ActiveLayout==actual.ActiveLayout&&expected.TileMode==actual.TileMode&&
            expected.ReservedEntities.ToHashSet().SetEquals(actual.ReservedEntities)&&
            ModelParameterSnapshot.Equivalent(expected.EntityInventory,actual.EntityInventory)&&ModelParameterSnapshot.Equivalent(expected.BlockInventory,actual.BlockInventory),
            "Native layout/entity/space state changed after the selector snapshot.","STRUCTURE_STATE_CHANGED");
        Need(expected.Existing.Length==actual.Existing.Length&&expected.Existing.All(item=>actual.Existing.Any(now=>now.Id==item.Id&&now.Name==item.Name&&now.Block==item.Block&&
            ModelParameterSnapshot.Equivalent(Wire.Element(item.Properties),Wire.Element(now.Properties)))),"Native layout metadata changed after selection.","STRUCTURE_STATE_CHANGED");
    }
    internal static StructureChange Apply(int index,StructureWriteArguments args,Resolved resolved,Database db,WriteJob job,Action beforeNative)
    {
        // Revalidate and dispose the selector before the callback marks a
        // native write as started. Never hold it across native manager writes.
        using(var selector=db.TransactionManager.StartTransaction())
            VerifySelection(resolved,Resolve(args,resolved.Edit,db,selector,job.CheckCancellation));
        job.CheckCancellation();beforeNative();LayoutManager.Current.DeleteLayout(resolved.Edit.ExpectedName);job.CheckCancellation();
        using var readback=db.TransactionManager.StartTransaction();
        var layouts=Layouts(resolved.Reference,db,readback,job.CheckCancellation);
        Need(Gone(resolved.Layout)&&Gone(resolved.Block)&&resolved.ReservedEntities.All(Gone)&&
            !((DBDictionary)readback.GetObject(db.LayoutDictionaryId,OpenMode.ForRead)).Contains(resolved.Edit.ExpectedName),
            "Native layout removal did not remove the requested layout/owned empty space.","NATIVE_STRUCTURE_MISMATCH");
        var retained=resolved.Existing.Where(item=>item.Id!=resolved.Layout).ToArray();
        Need(layouts.Length==retained.Length&&retained.All(item=>layouts.Any(now=>now.Id==item.Id&&now.Name==item.Name&&now.Block==item.Block&&
            ModelParameterSnapshot.Equivalent(Wire.Element(item.Properties with{tab_order=now.Properties.tab_order}),Wire.Element(now.Properties)))),
            "Native empty-layout deletion changed retained layout identity/name/settings/content ownership.","NATIVE_STRUCTURE_MISMATCH");
        var beforeOrder=retained.Where(item=>!item.Properties.is_model).OrderBy(item=>item.Properties.tab_order).Select(item=>item.Id).ToArray();
        var afterOrder=layouts.Where(item=>!item.Properties.is_model).OrderBy(item=>item.Properties.tab_order).Select(item=>item.Id).ToArray();
        Need(beforeOrder.SequenceEqual(afterOrder)&&layouts.Where(item=>!item.Properties.is_model).All(item=>item.Properties.tab_order>0)&&
            layouts.Where(item=>!item.Properties.is_model).Select(item=>item.Properties.tab_order).Distinct().Count()==afterOrder.Length,
            "Native empty-layout deletion changed retained relative Paper tab order.","NATIVE_STRUCTURE_MISMATCH");
        Need(db.CurrentSpaceId==resolved.ActiveSpace&&LayoutManager.Current.GetLayoutId(LayoutManager.Current.CurrentLayout)==resolved.ActiveLayout&&db.TileMode==resolved.TileMode&&
            ModelParameterSnapshot.Equivalent(resolved.EntityInventory,Entities(db,readback,null,job.CheckCancellation))&&
            ModelParameterSnapshot.Equivalent(resolved.BlockInventory,Blocks(db,readback,null,job.CheckCancellation)),
            "Native empty-layout deletion changed retained drawing content/current space.","NATIVE_STRUCTURE_MISMATCH");
        return new(index,resolved.Edit.Operation,resolved.Reference,"layout",resolved.Edit.ExpectedName,false);
    }
}
