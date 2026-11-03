using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

internal sealed record ModelParameterBody(string Handle,Mc3dSolid Body,JsonElement Before,
    ModelViewFollowers.Snapshot Views);
internal static class ModelParameterBodies
{
    internal static ModelParameterBody[] Resolve(ModelParameterArguments args,DocumentState state,Db.Transaction tx,Action? cancel=null)
    {
        void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok,string code,string message){if(!ok)throw new DomainException(code,message);}
        var active=McDocument.ActiveDocument;
        Need(active!=null&&state.Document.IsNamedDrawing&&!string.IsNullOrWhiteSpace(active.Path)&&string.Equals(Path.GetFullPath(active.Path),Path.GetFullPath(state.Document.Name),StringComparison.OrdinalIgnoreCase),"DOCUMENT_CONTEXT_MISMATCH","Use the matching named document.");
        var db=state.Document.Database;var model=((Db.BlockTable)tx.GetObject(db.BlockTableId,Db.OpenMode.ForRead))[Db.BlockTableRecord.ModelSpace];
        Need(db.CurrentSpaceId==model,"OUT_OF_SCOPE","Activate Model Space.");
        var result=new List<ModelParameterBody>();
        foreach(string handle in args.ScopeHandles??[args.Handle])
        {
            cancel?.Invoke();
            var id=CadStructureWriter.Find(db,handle);var entity=tx.GetObject(id,Db.OpenMode.ForRead) as Db.Solid3d;
            Need(entity!=null&&entity.GetType()==typeof(Db.Solid3d)&&entity.OwnerId==model,"UNSUPPORTED_ENTITY_TYPE","Every scoped handle must identify a top-level native model.");
            var layer=(Db.LayerTableRecord)tx.GetObject(entity!.LayerId,Db.OpenMode.ForRead);
            Need(!layer.IsLocked&&!layer.IsOff&&!layer.IsFrozen&&!layer.IsDependent,"LAYER_UNAVAILABLE","Every scoped model layer must be available.");
            var body=McObjectId.FromHandle(id.Handle.Value,active!.ID).GetObjectOfType<Mc3dSolid>();Need(body!=null,"OBJECT_IDENTITY_MISMATCH","Scoped body identity unavailable.");
            Need(result.Count==0||result[0].Body.DbEntity.Document.ID==body!.DbEntity.Document.ID,"OUT_OF_SCOPE","Scoped models must share one owning MCS document network.");
            var contents=ModelInventoryBudget.History(body!,cancel);
            var views=ModelViewFollowers.Resolve(body,contents!.ToArray(),cancel,includeStandalone:true);
            var before=ModelParameterSnapshot.Capture(args with{Handle=handle},state,cancel);
            ModelParameterSnapshot.Healthy(before);result.Add(new(handle,body,before,views));
        }
        // Only bodies explicitly admitted by scope_handles may legitimately change.
        var mutable=result.Select(value=>value.Body.ID).ToHashSet();
        foreach(var value in result)if(value.Views.Standalone!=null)value.Views.Standalone.MutableSources.UnionWith(mutable);
        return result.ToArray();
    }
    internal static ModelParameterBody Owner(ModelParameterBody[] bodies,McObjectId feature,Action? cancel=null)
    {
        var found=bodies.Where(b=>{cancel?.Invoke();return ModelInventoryBudget.History(b.Body,cancel).Contains(feature);}).ToArray();
        if(found.Length!=1)throw new DomainException("CAPABILITY_UNAVAILABLE","A parameter consumer is outside explicit scope_handles or lacks a unique history owner.");return found[0];
    }
}
