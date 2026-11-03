using System.Runtime.CompilerServices;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// A suppressed array position remains part of the same native pattern. It is
// never represented as a moved copy or a recreated set of coordinates.
internal static class ModelPatternElementWriter
{
    internal static bool Handles(ModifyArguments args)=>args.Operations.Any(e=>e is ModelPatternElementSpec);
    private static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool okay,string code,string message)
    {if(!okay)throw new DomainException(code,message);}
    private sealed record Binding(Mc3dSolid Body,ObjectId Source,string Layer,McObjectId[] Contents,
        McObjectId Feature,McObjectId SourceFeature,bool[] Suppressed,ModelFeatureOracle.Metrics Before,
        HashSet<ObjectId> Inventory,ModelViewFollowers.Snapshot Views,System.Text.Json.JsonElement ProtectedHistory);
    private static ObjectId Model(Database db,Transaction tx)=>
        ((BlockTable)tx.GetObject(db.BlockTableId,OpenMode.ForRead))[BlockTableRecord.ModelSpace];
    private static HashSet<ObjectId> TopLevel(DocumentState state)
    {
        using var tx=state.Document.Database.TransactionManager.StartTransaction();
        var ids=((BlockTableRecord)tx.GetObject(Model(state.Document.Database,tx),OpenMode.ForRead)).Cast<ObjectId>().ToHashSet();
        return ids;
    }
    private static bool[] Mask(RectangularPatternFeature pattern,int x,int y,Action? cancel=null)
    {
        var result=new List<bool>();var capacity=new SerializedCapacity357(cancel:cancel);
        for(int a=0;a<x;a++)for(int b=0;b<y;b++)
        {cancel?.Invoke();bool value=pattern.IsElementSuppressed(new ArrayElementIndex(a,b));capacity.Add(value);result.Add(value);}
        return result.ToArray();
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Binding Resolve(ModelPatternElementSpec edit,ModifyArguments args,DocumentState state,Transaction tx,Action? cancel=null)
    {
        Need(args.Operations.Length==1,"INVALID_ARGUMENT","Native pattern element change must be the sole operation.");
        var loaded=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Need(loaded.Contains("mapimgd")&&loaded.Contains("MapiBaseTypes"),"CAPABILITY_UNAVAILABLE","MultiCAD must already be loaded.");
        var db=state.Document.Database;var model=Model(db,tx);
        Need(db.CurrentSpaceId==model,"OUT_OF_SCOPE","Activate Model Space.");
        var active=McDocument.ActiveDocument;
        Need(state.Document.IsNamedDrawing&&active!=null&&!string.IsNullOrWhiteSpace(active.Path)&&
            string.Equals(Path.GetFullPath(active.Path),Path.GetFullPath(state.Document.Name),StringComparison.OrdinalIgnoreCase),
            "DOCUMENT_CONTEXT_MISMATCH","Native pattern requires this exact named document.");
        var source=CadStructureWriter.Find(db,edit.Handles[0]);
        var solid=tx.GetObject(source,OpenMode.ForRead) as Solid3d;
        Need(solid!=null&&solid.GetType()==typeof(Solid3d)&&solid.OwnerId==model&&!solid.IsNull,
            "UNSUPPORTED_ENTITY_TYPE","Select one top-level native Mc3dSolid representation.");
        var layer=(LayerTableRecord)tx.GetObject(solid!.LayerId,OpenMode.ForRead);
        Need(!layer.IsLocked&&!layer.IsFrozen&&!layer.IsOff&&!layer.IsDependent,"LAYER_UNAVAILABLE","Body layer is unavailable.");
        var bodyId=McObjectId.FromHandle(source.Handle.Value,active!.ID);var body=bodyId.GetObjectOfType<Mc3dSolid>();
        Need(body!=null&&body.ID==bodyId&&body.LastPartItemID.ToString().Equals(edit.ExpectedLastItemId,StringComparison.OrdinalIgnoreCase),
            "PRECONDITION_FAILED","Body identity or last history item changed.");
        var contents=ModelInventoryBudget.History(body!,cancel);
        Need(contents.Count>0,"HISTORY_UNAVAILABLE","Pattern needs healthy existing history.");
        var ids=contents.ToArray(); var views=ModelViewFollowers.Resolve(body,ids,cancel,includeStandalone:true);
        var feature=ids.SingleOrDefault(id=>id.ToString().Equals(edit.FeatureId,StringComparison.OrdinalIgnoreCase));
        var pattern=feature.GetObjectOfType<RectangularPatternFeature>();
        Need(!feature.IsNull&&pattern!=null&&pattern.HostSolidID==body.ID&&
            !pattern.IsSuppressed&&pattern.Status.ToString()=="Ok"&&pattern.SourceItemsCount==1&&pattern.HasSubFeatureItems,
            "OUT_OF_SCOPE","Select one healthy native subfeature array owned by this body.");
        var predecessor=pattern!.OrderObjBefore;
        var sourceFeature=predecessor.GetObjectOfType<ExtrudeFeature>();
        Need(predecessor.ToString().Equals(edit.ExpectedSourceFeatureId,StringComparison.OrdinalIgnoreCase)&&ids.Contains(predecessor)&&
            sourceFeature!=null&&sourceFeature.HostSolidID==body.ID&&!sourceFeature.IsSuppressed&&sourceFeature.Status.ToString()=="Ok"&&
            !pattern.GetSourceItemFeature(0).IsNull&&(pattern.GetDependsOn(false,false)?.Contains(predecessor)??false),
            "MODEL_STATE_UNAVAILABLE","Pattern lost its original source feature dependency.");
        Need(pattern.SizeX==edit.ExpectedCountX&&pattern.SizeY==edit.ExpectedCountY&&
            ModelParameterSnapshot.Near(pattern.DistancesX,edit.ExpectedStepX)&&
            ModelParameterSnapshot.Near(pattern.DistancesY,edit.ExpectedStepY),
            "PRECONDITION_FAILED","Pattern count or step changed.");
        var mask=Mask(pattern,edit.ExpectedCountX,edit.ExpectedCountY,cancel);
        Need(mask[edit.IndexX*edit.ExpectedCountY+edit.IndexY]==edit.ExpectedSuppressed,
            "PRECONDITION_FAILED","Selected array element state changed.");
        return new(body,source,solid.Layer,ids,feature,predecessor,mask,ModelFeatureOracle.Read(solid),TopLevel(state),views,ModelHistoryEditGuard.Capture(args,state,feature,cancel:cancel));
    }
    internal static void Preflight(ModifyArguments args,DocumentState state,Transaction tx,Action? cancel=null)=>
        Resolve((ModelPatternElementSpec)args.Operations.Single(),args,state,tx,cancel);
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Result Execute(ModifyArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        McUndoPoint? tracker=null;Binding? binding=null;bool started=false;string stage="preparation";
        void Trace(string value){stage=value;log.Write(job.Operation,job.OriginalRequestId,"model_pattern_element_checkpoint",transaction:value,operationId:job.OperationId);}
        void Close(){var value=tracker;tracker=null;if(value==null)return;try{value.Stop();}finally{value.Dispose();}}
        try
        {
            var edit=(ModelPatternElementSpec)args.Operations.Single();
            using(var tx=state.Document.Database.TransactionManager.StartTransaction())binding=Resolve(edit,args,state,tx,job.CheckCancellation);
            job.CheckCancellation();ModelViewFollowers.CheckStandalone(binding.Body,binding.Views,job.CheckCancellation);tracker=new McUndoPoint(false);tracker.Start();started=true;
            var pattern=binding.Feature.GetObjectOfType<RectangularPatternFeature>();
            Need(pattern!=null,"MODEL_STATE_UNAVAILABLE","Native pattern disappeared before update.");
            var index=new ArrayElementIndex(edit.IndexX,edit.IndexY);
            Trace("element_before");
            bool applied=edit.Suppressed?pattern!.SuppressElement(index):pattern!.UnsuppressElement(index);
            Need(applied,"MODEL_UPDATE_FAILED","Native array element state change was rejected.");
            pattern!.DbEntity.Update();Trace("rebuild_before");McObjectManager.UpdateAll();
            Need(binding.Body.RebuildPart(),"MODEL_UPDATE_FAILED","Pattern body rebuild failed.");
            McObjectManager.UpdateAll();Trace("rebuild_returned");
            (EntityRef Body,ModelFeatureOracle.Metrics Metrics) Verify()
            {
                job.CheckCancellation();
                var contents=ModelInventoryBudget.History(binding.Body,job.CheckCancellation);
                Need(contents!=null&&contents.Count==binding.Contents.Length&&contents.ToHashSet().SetEquals(binding.Contents)&&
                    binding.Body.LastPartItemID==binding.Views.LastHistoryItem,
                    "MODEL_STATE_UNAVAILABLE","Pattern or preceding history changed identity.");
                var current=binding.Feature.GetObjectOfType<RectangularPatternFeature>();
                Need(current!=null&&current.HostSolidID==binding.Body.ID&&!current.IsSuppressed&&current.Status.ToString()=="Ok"&&
                    current.OrderObjBefore==binding.SourceFeature&&current.SourceItemsCount==1&&current.HasSubFeatureItems&&
                    !current.GetSourceItemFeature(0).IsNull&&
                    (current.GetDependsOn(false,false)?.Contains(binding.SourceFeature)??false),
                    "MODEL_STATE_UNAVAILABLE","Pattern lost its native source or owner.");
                Need(current!.SizeX==edit.ExpectedCountX&&current.SizeY==edit.ExpectedCountY&&
                    ModelParameterSnapshot.Near(current.DistancesX,edit.ExpectedStepX)&&
                    ModelParameterSnapshot.Near(current.DistancesY,edit.ExpectedStepY),
                    "MODEL_UPDATE_FAILED","Pattern count or step changed unexpectedly.");
                var mask=Mask(current,edit.ExpectedCountX,edit.ExpectedCountY,job.CheckCancellation);
                int selected=edit.IndexX*edit.ExpectedCountY+edit.IndexY;
                Need(mask.Length==binding.Suppressed.Length&&
                    mask.Select((value,i)=>i==selected?value==edit.Suppressed:value==binding.Suppressed[i]).All(x=>x),
                    "MODEL_UPDATE_FAILED","Selected element did not change or another element changed.");
                var inventory=TopLevel(state);
                using var tx=state.Document.Database.TransactionManager.StartTransaction();
                var matches=inventory.Where(id=>tx.GetObject(id,OpenMode.ForRead) is Solid3d&&
                    McObjectId.FromHandle(id.Handle.Value,McDocument.ActiveDocument.ID)==binding.Body.ID).ToArray();
                Need(matches.Length==1&&(matches[0]==binding.Source||!inventory.Contains(binding.Source)),
                    "OBJECT_IDENTITY_MISMATCH","Pattern body has no unique current native representation.");
                Need(inventory.Except(binding.Inventory).All(id=>id==matches[0])&&
                    binding.Inventory.Except(inventory).All(id=>id==binding.Source),
                    "MODEL_STATE_UNAVAILABLE","Pattern unexpectedly changed top-level entities.");
                var solid=(Solid3d)tx.GetObject(matches[0],OpenMode.ForRead);
                Need(solid.Layer==binding.Layer,"MODEL_STATE_UNAVAILABLE","Pattern changed the body's layer.");
                var metrics=ModelFeatureOracle.Read(solid);
                ModelViewFollowers.Verify(binding.Body,binding.Views,job.CheckCancellation,refreshStandalone:true);
                ModelHistoryEditGuard.Verify(binding.ProtectedHistory,args,state,binding.Feature,Validation.NormalizeHandle(matches[0].Handle.ToString()),job.CheckCancellation);
                return(new(args.BridgeId,args.DocumentId,Validation.NormalizeHandle(matches[0].Handle.ToString())),metrics);
            }
            var verified=Verify();Trace("element_verified");job.CheckCancellation();job.BeginCommit();Close();verified=Verify();
            var sourceRef=new EntityRef(args.BridgeId,args.DocumentId,edit.Handles[0]);
            bool replaced=sourceRef.handle!=verified.Body.handle;
            var change=new ModelPatternElementChange(sourceRef,verified.Body,binding.Body.ID.ToString(),binding.Feature.ToString(),
                binding.SourceFeature.ToString(),edit.IndexX,edit.IndexY,edit.Suppressed,
                binding.Before.Volume,verified.Metrics.Volume,replaced);
            var data=new ModifiedEntities(args.OperationId,"committed",false,[verified.Body],
                replaced?[sourceRef]:[],replaced?[verified.Body]:[],[],"not_guaranteed",
                model_pattern_element_changes:[change]);
            var result=Result.Ok(job.OriginalRequestId,data,context,WriteRecovery.Warning,
                "NATIVE_PATTERN_ELEMENT_CHANGED: Refresh history and visually inspect the selected array position through MCP capture_view. The native pattern and its source dependency were retained.");
            result=ModelViewFollowers.WithWarnings(result,binding.Views);job.RecordCommit(result);Trace("commit_recorded");return result;
        }
        catch(Exception error)
        {
            if(job.CommittedResult is {} committed)return committed;
            Exception? cleanup=null;try{Close();}catch(Exception e){cleanup=e;}
            bool unknown=started||cleanup!=null;
            string cause=error is DomainException d?d.Error.code:error is OperationCanceledException?"CANCELLED":"MODEL_PATTERN_ELEMENT_FAILED";
            return job.Error(unknown?"WRITE_OUTCOME_UNKNOWN":cause,
                unknown?"Native pattern element outcome is unverified; inspect the affected body and history before dependent work.":error.Message,
                unknown?"unknown":"not_started",context,new{cause,stage,message=error.Message,
                    body_mc_id=binding?.Body.ID.ToString(),feature_id=binding?.Feature.ToString(),cleanup_error=cleanup?.GetType().Name})
                with{warnings=[WriteRecovery.Warning]};
        }
    }
}
