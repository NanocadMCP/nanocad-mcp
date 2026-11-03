using System.Security.Cryptography;
using System.Text.Json;
using Multicad;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;
using static NanoCADBridge.CadDrawingViewEditState;

namespace NanoCADBridge;

internal static class CadDrawingViewLifecycleState345
{
    internal const string Warning="Native drawing-view lifecycle retains sources and native links; recovery/Undo is not guaranteed. A section's processing inventory is separate from the view's selected sources. Multi-body model-follower edits and association-aware annotation updates require their owning routes.";
    internal const int SnapshotBytes=8*1024*1024;
    internal static string Handle(Db.ObjectId id)=>Validation.NormalizeHandle(id.Handle.ToString());
    internal static void Bytes(object value,Action cancel,int maximum=SnapshotBytes)
    {cancel();Need(JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json).Length<=maximum,"Native view state exceeds the actual serialized-byte budget.","RESPONSE_LIMIT_EXCEEDED");}
    internal static Snapshot Capture(DocumentState state,Db.Transaction transaction,Action cancel)
    {
        var budget=new ModelInventoryBudget(cancel);
        var snapshot=CadDrawingViewEditState.Capture(state,transaction,cancel,item=>budget.Add(item),allowUnselectedForeignSources:true);
        Bytes(new{snapshot.Context,snapshot.Layouts,snapshot.Entities,
            views=snapshot.Views.Select(view=>view.Metadata),bodies=snapshot.Bodies.Select(body=>new{body.Geometry,contents=body.Contents.Select(id=>id.ToString()),body.Nodes}),snapshot.Sections},cancel);
        return snapshot;
    }
    private static object TokenState(DrawingView view,Db.Entity entity,Db.ObjectId layout,Db.Transaction transaction,Action cancel)
    {
        cancel();var metadata=Metadata(view);var owned=RootData(entity,transaction,cancel);
        var result=new{handle=Handle(entity.ObjectId),owner=Handle(entity.OwnerId),layout=Handle(layout),
            properties=EntityAdapter.StoredProperties(entity),metadata,owned};Bytes(result,cancel);return result;
    }
    internal static string Token(DrawingView view,Db.Entity entity,Db.ObjectId layout,Db.Transaction transaction,Action cancel)
        =>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(TokenState(view,entity,layout,transaction,cancel),Wire.Json)));
    internal static (string? Token,string? Reason) TryToken(DrawingView view,Db.Entity entity,Db.ObjectId layout,Db.Transaction transaction,Action cancel)
    {
        try{return(Token(view,entity,layout,transaction,cancel),null);}
        catch(DomainException error)when(error.Error.code is not ("RESPONSE_LIMIT_EXCEEDED" or "RESPONSE_TOO_LARGE"))
        {return(null,error.Error.code+": native view token needs its owning metadata adapter.");}
        catch(Teigha.Runtime.Exception error){cancel();return(null,"Native view token metadata unavailable: "+error.ErrorStatus);}
        catch(NotSupportedException){cancel();return(null,"Native view token metadata adapter unavailable.");}
    }
    internal static (EntityRef[]? Refs,string? Reason) SourceRefs(IEnumerable<McObjectId>? ids,Db.Database database,Db.Transaction transaction,
        string instance,string document,Action cancel)
    {
        if(ids==null)return(null,"native_source_inventory_unavailable");
        var wanted=new HashSet<McObjectId>();var budget=new ModelInventoryBudget(cancel);
        foreach(var id in ids){cancel();budget.Add(id.ToString());Need(!id.IsNull&&wanted.Add(id),"Native view source inventory contains null/duplicate IDs.");}
        if(wanted.Count==0)return(Array.Empty<EntityRef>(),null);
        var mapped=new Dictionary<McObjectId,EntityRef>();var mc=Multicad.DatabaseServices.McDocument.ActiveDocument;
        if(mc==null)return(null,"native_document_context_unavailable");
        var table=(Db.BlockTable)transaction.GetObject(database.BlockTableId,Db.OpenMode.ForRead);
        var model=(Db.BlockTableRecord)transaction.GetObject(table[Db.BlockTableRecord.ModelSpace],Db.OpenMode.ForRead);
        foreach(Db.ObjectId id in model)
        {
            cancel();if(Gone(id))continue;var native=McObjectId.FromHandle(id.Handle.Value,mc.ID);if(!wanted.Contains(native))continue;
            var entity=transaction.GetObject(id,Db.OpenMode.ForRead) as Db.Entity;
            if(entity==null||entity.GetType()!=typeof(Db.Solid3d)||entity.OwnerId!=model.ObjectId)return(null,"source_not_an_addressable_top_level_model_solid");
            Need(mapped.TryAdd(native,new(instance,document,Handle(id))),"Native source has multiple physical representations.");
        }
        if(mapped.Count!=wanted.Count)return(null,"foreign_complex_or_unmapped_native_source_ids");
        return(mapped.OrderBy(item=>item.Key.ToString(),StringComparer.Ordinal).Select(item=>item.Value).ToArray(),null);
    }
    internal static View Resolve(DrawingViewLifecycleTarget345 target,Snapshot snapshot,Db.Transaction transaction,Action cancel)
    {
        var view=CadDrawingViewEditState.Resolve(target.Target,snapshot,transaction,cancel);
        var entity=(Db.Entity)transaction.GetObject(view.Entity,Db.OpenMode.ForRead);
        Need(Token(view.Native,entity,view.Layout,transaction,cancel)==target.ViewToken,"View token differs; refresh get_drawing_views.","STALE_VIEW_TOKEN");
        return view;
    }
    internal static JsonElement Owned(View view,Db.Transaction transaction,Action cancel)
        =>RootData((Db.Entity)transaction.GetObject(view.Entity,Db.OpenMode.ForRead),transaction,cancel);
    private static JsonElement RootData(Db.Entity entity,Db.Transaction transaction,Action cancel)
    {
        var budget=new ModelInventoryBudget(cancel);var owned=NativeOwnedMetadata.Capture(entity,transaction,cancel);budget.Add(owned);
        using var xdata=entity.XData;using var reactors=entity.GetPersistentReactorIds();
        var data=xdata==null?null:NativeOwnedMetadata.Buffer(xdata,cancel,item=>budget.Add(item));var links=new List<object>();
        foreach(Db.ObjectId id in reactors)
        {
            cancel();Need(!id.IsNull&&!Gone(id),"A selected view persistent reactor is unavailable.","DRAWING_VIEW_DEPENDENTS");
            var item=transaction.GetObject(id,Db.OpenMode.ForRead);
            Need(item.Database==entity.Database,"A selected view reactor belongs to another drawing.","DRAWING_VIEW_DEPENDENTS");
            var descriptor=new{handle=Handle(id),owner_handle=item.OwnerId.IsNull?null:Handle(item.OwnerId),runtime_type=item.GetType().FullName};
            budget.Add(descriptor);links.Add(descriptor);
        }
        var result=Wire.Element(new{owned,xdata=data,persistent_reactors=links.OrderBy(item=>Wire.Element(item).GetProperty("handle").GetString(),StringComparer.Ordinal)});Bytes(result,cancel);return result;
    }
    internal static void ReactorGroup(View view,Snapshot snapshot,HashSet<McObjectId> group,Db.Transaction transaction,Action cancel)
    {
        var entity=(Db.Entity)transaction.GetObject(view.Entity,Db.OpenMode.ForRead);using var reactors=entity.GetPersistentReactorIds();
        var budget=new ModelInventoryBudget(cancel);var mc=Multicad.DatabaseServices.McDocument.ActiveDocument;
        foreach(Db.ObjectId id in reactors)
        {
            cancel();Need(!id.IsNull&&!Gone(id),"A view reactor is unavailable.","DRAWING_VIEW_DEPENDENTS");budget.Add(Handle(id));
            var native=McObjectId.FromHandle(id.Handle.Value,mc.ID);
            var related=snapshot.Views.SingleOrDefault(item=>item.Id==native&&item.Entity==id);
            if(related==null||!group.Contains(native))throw new DomainException("DRAWING_VIEW_DEPENDENTS",
                "This view has an external persistent association requiring its owning edit route; sources/tree were not changed.",false,
                new{view_handle=Handle(view.Entity),reactor_handle=Handle(id),reactor_type=id.ObjectClass.Name});
        }
    }
    internal static void Ordinary(View view)
    {
        Plain(view);Need(view.Native.EnableAutoUpdate&&view.Native.Status.ToString()=="Ok"&&!view.Native.IsSuppressed,
            "Use a healthy ordinary native projection.","MODEL_NOT_READY");
    }
    internal static DrawingViewLifecycleChange345 Change(View view,Snapshot snapshot,Db.Transaction transaction,StructureWriteArguments args,Action cancel)
    {
        var entity=(Db.Entity)transaction.GetObject(view.Entity,Db.OpenMode.ForRead);
        var ids=view.Native.GetTop3dEntitiesInView();var refs=SourceRefs(ids,entity.Database,transaction,args.BridgeId,args.DocumentId,cancel);
        Need(refs.Refs!=null,"Selected native view sources cannot all resolve to current Model solid references.","DRAWING_VIEW_SOURCE_UNAVAILABLE");
        return new(0,new(args.BridgeId,args.DocumentId,Handle(view.Entity)),Token(view.Native,entity,view.Layout,transaction,cancel),
            refs.Refs!,Ids(ids,"Selected view sources"),Id(view.Native.ParentViewID),Id(view.Native.RootViewID),Ids(view.Native.ChildViews,"Selected child views"));
    }
}
