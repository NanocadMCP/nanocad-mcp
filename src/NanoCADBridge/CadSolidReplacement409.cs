using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// Native HandOverTo transfers the database identity. It does not provide a
// geometry-only CopyFrom, and does not preserve individual face identities.
internal static class CadSolidReplacement409
{
    internal sealed record Plan(ObjectId Target,ObjectId Source,JsonElement TargetRoot,JsonElement SourceRoot,JsonElement SourceGeometry,bool HasAssocManager);
    private static void Need(bool ok,string message,string code="NATIVE_SOLID_REPLACEMENT_MISMATCH")
    {if(!ok)throw new DomainException(code,message);}
    private static string? Handle(ObjectId id)=>id.IsNull?null:Validation.NormalizeHandle(id.Handle.ToString());
    private static JsonElement Geometry(Solid3d body)
    {
        var box=body.GeometricExtents;var mass=body.MassProperties;
        return Wire.Element(new{volume=mass.Volume,min=new[]{box.MinPoint.X,box.MinPoint.Y,box.MinPoint.Z},
            max=new[]{box.MaxPoint.X,box.MaxPoint.Y,box.MaxPoint.Z},centroid=new[]{mass.Centroid.X,mass.Centroid.Y,mass.Centroid.Z},
            body.NumChanges});
    }
    private static JsonElement Root(Solid3d body,Transaction tx,Action cancel)
    {
        var budget=new ModelInventoryBudget(cancel);using var data=body.XData;using var links=body.GetPersistentReactorIds();
        var reactors=new List<string?>();foreach(ObjectId link in links){cancel();reactors.Add(Handle(link));}
        var value=Wire.Element(new{handle=Handle(body.ObjectId),owner=Handle(body.OwnerId),extension=Handle(body.ExtensionDictionary),
            body.HasFields,body.RecordHistory,body.ShowHistory,layer_handle=Handle(body.LayerId),display=EntityAdapter.StoredProperties(body),
            xdata=data==null?null:NativeOwnedMetadata.Buffer(data,cancel,budget.Add),reactors,
            owned=NativeOwnedMetadata.Capture(body,tx,cancel,observeAssocManager:true)});
        SerializedCapacity357.Check(value,Wire.PayloadBudget-16384,cancel);return value;
    }
    internal static Plan Prepare(SolidReplacement409 edit,Database db,Transaction tx,ObjectId target,ObjectId source,Action cancel)
    {
        cancel();Need(target!=source,"Replacement source and target cannot share an identity.");
        var destination=(Solid3d)tx.GetObject(target,OpenMode.ForRead);var original=(Solid3d)tx.GetObject(source,OpenMode.ForRead);
        CadSolidEditor.CheckSource(destination,db,cancel);var documentedSource=CadSolidEditor.CheckSource(original,db,cancel);
        using var sourceData=original.XData;using var sourceLinks=original.GetPersistentReactorIds();
        Need(!documentedSource&&!original.HasFields&&original.ExtensionDictionary.IsNull&&sourceLinks.Count==0&&
            (sourceData==null||sourceData.AsArray().Length==0),
            "Replacement input must be a plain direct shape without owned source data or documentation; source ownership is not transferred.","UNSUPPORTED_SOLID_DEPENDENCIES");
        var targetRoot=Root(destination,tx,cancel);
        bool manager=targetRoot.GetProperty("owned").EnumerateArray().Any(record=>record.TryGetProperty("assoc_manager_observation",out _));
        return new(target,source,targetRoot,Root(original,tx,cancel),Geometry(original),manager);
    }
    internal static void Apply(Plan plan,Solid3d target,Transaction tx,Action cancel,List<Entity> owned)
    {
        cancel();var source=(Solid3d)tx.GetObject(plan.Source,OpenMode.ForRead);
        Need(plan.SourceRoot.GetRawText()==Root(source,tx,cancel).GetRawText()&&
            plan.SourceGeometry.GetRawText()==Geometry(source).GetRawText(),"Replacement input changed before writing.","NATIVE_SOLID_REPLACEMENT_SOURCE_CHANGED");
        var candidate=source.Clone() as Solid3d;
        if(candidate==null||candidate.GetType()!=typeof(Solid3d)||!candidate.ObjectId.IsNull||!candidate.OwnerId.IsNull||
            candidate.UnmanagedObject==IntPtr.Zero||candidate.UnmanagedObject==source.UnmanagedObject||candidate.UnmanagedObject==target.UnmanagedObject)
        {
            if(candidate!=null)GC.SuppressFinalize(candidate);
            throw new DomainException("UNSAFE_NATIVE_CLONE","Native replacement clone did not prove a detached distinct exact body; an unsafe alias was not adopted or disposed.");
        }
        owned.Add(candidate!);CadSolidEditor.CheckGeometry(candidate!,cancel);
        using(var links=candidate!.GetPersistentReactorIds())
            Need(links.Count==0&&candidate.ExtensionDictionary.IsNull&&!candidate.HasFields,"The detached input contains source-owned data; no hand-over was attempted.","UNSAFE_NATIVE_CLONE");
        candidate.SetPropertiesFrom(target);cancel();
        // HandOverTo's managed wrapper does not rebind its internal AutoDelete
        // flag. It has no public setter. From this one-way call onward, ownership
        // may have transferred even if the kernel throws: quarantine this wrapper
        // rather than letting an owning Dispose/finalizer delete a resident root.
        // New reads use transaction-owned, non-owning wrappers of the retained ID.
        owned.Remove(candidate);GC.SuppressFinalize(candidate);
        target.HandOverTo(candidate,true,true);
        // Do not read the displaced transaction wrapper. Fresh post-commit
        // lookup verifies the retained identity and its owning relationships.
    }
    internal static void Verify(Plan plan,Database db,Action cancel)
    {
        using var tx=db.TransactionManager.StartTransaction();cancel();
        var target=tx.GetObject(plan.Target,OpenMode.ForRead) as Solid3d;var source=tx.GetObject(plan.Source,OpenMode.ForRead) as Solid3d;
        Need(target!=null&&source!=null&&target.GetType()==typeof(Solid3d)&&source.GetType()==typeof(Solid3d),"Replacement lost a native body identity.");
        CadSolidEditor.CheckGeometry(target!,cancel);
        Need(plan.TargetRoot.GetRawText()==Root(target!,tx,cancel).GetRawText(),"Native hand-over changed retained root data or dependencies.");
        Need(plan.SourceRoot.GetRawText()==Root(source!,tx,cancel).GetRawText()&&
            plan.SourceGeometry.GetRawText()==Geometry(source!).GetRawText(),"Replacement changed the original source.","NATIVE_SOLID_REPLACEMENT_SOURCE_CHANGED");
        // These observations support the intended copy; the useful shape is
        // assessed visually, rather than requiring an invented volume oracle.
        var current=Geometry(target!);foreach(var field in new[]{"volume","min","max","centroid"})
            Need(ModelParameterSnapshot.Equivalent(plan.SourceGeometry.GetProperty(field),current.GetProperty(field)),"Replacement body differs from the committed source geometry observation.");
    }
}
