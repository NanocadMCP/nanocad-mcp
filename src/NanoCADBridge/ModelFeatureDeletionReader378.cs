using System.Runtime.CompilerServices;
using Multicad;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class ModelFeatureDeletionReader378
{
    const string EffectScope="Current ordinary terminal Fillet/Chamfer/Shell/Mirror or sole-consumer extrusion/revolution; a selected unsuppressed terminal Mirror may be NeedUpdate while retained history remains healthy. Required deletion is one selected feature, optional retirement only explicitly accepted exclusive feature-hosted drivers and exactly typed selected storage. Mirror keeps its original source features and datums. Profile-consuming deletion explicitly releases its exact forward/backward profile links while retaining the profile, sketch, source objects and observed controls. No other forming cascade or keep-used cleanup guarantee is inferred.";
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static ModelFeatureDeletionRead378[] Read(Arguments args,DocumentState state,Db.Transaction tx,Action cancel)
    {
        var output=new List<ModelFeatureDeletionRead378>();var bytes=new ModelInventoryBudget(cancel);string handle=args.Handles!.Single();
        var source=new EntityRef(args.BridgeId!,args.DocumentId!,handle);
        var nativeArgs=new ModifyArguments(args.BridgeId!,args.DocumentId!,args.ExpectedRevision!,"","Millimeters",[]);
        foreach(string id in args.ModelFeatureDeletionIds!)
        {
            cancel();ModelFeatureDeletionRead378 item;
            try
            {
                var value=ModelFeatureDeletionState378.Capture(handle,id,nativeArgs,state,tx,cancel);
                item=new(source,value.Part.Body.ID.ToString(),value.Feature.ToString(),value.Kind,true,value.Token,null,value.RemainingLast.ToString(),
                    [value.Feature.ToString()],value.Drivers,value.Part.Features[..^1],value.Dependencies,value.Storage.proved,value.Storage.reason,EffectScope,ModelFeatureDeletionState378.CleanupScope,
                    value.RetainedProfile==null?null:ModelFeatureProfileRetention384.Describe(value.RetainedProfile,ModelFeatureProfileRetention384.BeforeDisposition));
            }
            catch(DomainException error)
            {item=new(source,null,id,null,false,null,error.Error.code+": "+error.Message,null,[],[],[],null,false,"not_observed_after_unavailable_scope",EffectScope,ModelFeatureDeletionState378.CleanupScope);}
            bytes.Add(item);output.Add(item);
        }
        return output.ToArray();
    }
}
