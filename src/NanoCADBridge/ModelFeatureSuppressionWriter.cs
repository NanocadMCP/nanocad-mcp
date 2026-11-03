using System.Runtime.CompilerServices;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class ModelFeatureSuppressionWriter
{
    internal static bool Handles(ModifyArguments args)=>args.Operations.Any(e=>e is ModelFeatureSuppressionSpec);
    private static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok,string code,string message)
    {if(!ok)throw new DomainException(code,message);}
    private sealed record Binding(Mc3dSolid Body,ObjectId Source,string Layer,McObjectId[] Contents,
        McObjectId Feature,string FeatureType,McObjectId[] Dependencies,
        ModelViewFollowers.Snapshot Views,ModelFeatureOracle.Metrics Before,HashSet<ObjectId> Inventory,
        ModelFeatureSuppressionState414.Snapshot? RetainedDesign,ModelFeatureSuppressionRecovery420.Context? Recovery);
    private static ObjectId Model(Database db,Transaction tx)=>
        ((BlockTable)tx.GetObject(db.BlockTableId,OpenMode.ForRead))[BlockTableRecord.ModelSpace];
    private static HashSet<ObjectId> TopLevel(DocumentState state)
    {
        using var tx=state.Document.Database.TransactionManager.StartTransaction();
        var ids=((BlockTableRecord)tx.GetObject(Model(state.Document.Database,tx),OpenMode.ForRead))
            .Cast<ObjectId>().ToHashSet();
        
        return ids;
    }
    private static string? FeatureType(McObjectId id)=>
        id.GetObjectOfType<ExtrudeFeature>()!=null?"extrusion":
        id.GetObjectOfType<RevolveFeature>()!=null?"revolution":
        id.GetObjectOfType<ExtrudeAlongPathFeature>()!=null?"sweep":
        id.GetObjectOfType<LoftFeature>()!=null?"loft":
        id.GetObjectOfType<HoleFeature>()!=null?"hole":
        id.GetObjectOfType<ThreadFeature>()!=null?"thread":
        id.GetObjectOfType<CircularPatternFeature>()!=null?"circular_pattern":
        id.GetObjectOfType<MirrorFeature>()!=null?"mirror":
        id.GetObjectOfType<RectangularPatternFeature>()!=null?"rectangular_pattern":
        id.GetObjectOfType<FilletFeature>()!=null?"fillet":
        id.GetObjectOfType<ChamferFeature>()!=null?"chamfer":
        id.GetObjectOfType<ThicknessFeature>()!=null?"shell":null;
    private static bool ActiveAdditiveFeature(McObjectId id,McObjectId body)
    {
        var item=id.GetObjectOfType<HistoryItem>();
        if(item==null||item.HostSolidID!=body||item.IsSuppressed||item.Status.ToString()!="Ok")return false;
        return id.GetObjectOfType<ExtrudeFeature>()?.PartOperation==PartFeatureOperation.Join||
            id.GetObjectOfType<RevolveFeature>()?.PartOperation==PartFeatureOperation.Join||
            id.GetObjectOfType<ExtrudeAlongPathFeature>()?.PartOperation==PartFeatureOperation.Join||
            id.GetObjectOfType<LoftFeature>()?.PartOperation==PartFeatureOperation.Join;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Binding Resolve(ModelFeatureSuppressionSpec edit,ModifyArguments args,DocumentState state,Transaction tx,Action? cancel=null)
    {
        Need(args.Operations.Length==1,"INVALID_ARGUMENT","Feature suppression must be the sole operation.");
        var loaded=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetName().Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Need(loaded.Contains("mapimgd")&&loaded.Contains("MapiBaseTypes"),"CAPABILITY_UNAVAILABLE",
            "MultiCAD runtime must already be loaded.");
        var db=state.Document.Database;var model=Model(db,tx);
        Need(db.CurrentSpaceId==model,"OUT_OF_SCOPE","Activate Model Space.");
        var active=McDocument.ActiveDocument;
        Need(state.Document.IsNamedDrawing&&active!=null&&!string.IsNullOrWhiteSpace(active.Path)&&
            string.Equals(Path.GetFullPath(active.Path),Path.GetFullPath(state.Document.Name),StringComparison.OrdinalIgnoreCase),
            "DOCUMENT_CONTEXT_MISMATCH","Feature suppression requires this exact active named DWG.");
        var source=CadStructureWriter.Find(db,edit.Handles[0]);
        var solid=tx.GetObject(source,OpenMode.ForRead) as Solid3d;
        Need(solid!=null&&solid.GetType()==typeof(Solid3d)&&solid.OwnerId==model&&!solid.IsNull,
            "UNSUPPORTED_ENTITY_TYPE","Select one nonempty top-level Mc3dSolid representation.");
        var layer=(LayerTableRecord)tx.GetObject(solid!.LayerId,OpenMode.ForRead);
        Need(!layer.IsLocked&&!layer.IsFrozen&&!layer.IsOff&&!layer.IsDependent,
            "LAYER_UNAVAILABLE","Body layer is unavailable.");
        var bodyId=McObjectId.FromHandle(source.Handle.Value,active!.ID);
        var body=bodyId.GetObjectOfType<Mc3dSolid>();
        Need(body!=null&&body.ID==bodyId&&body.LastPartItemID.ToString().Equals(edit.ExpectedLastItemId,StringComparison.OrdinalIgnoreCase),
            "PRECONDITION_FAILED","Body or last history item changed.");
        var contents=ModelInventoryBudget.History(body!,cancel);
        Need(contents.Count>0,"HISTORY_UNAVAILABLE","Native history is unavailable.");
        var featureId=contents!.SingleOrDefault(id=>id.ToString().Equals(edit.FeatureId,StringComparison.OrdinalIgnoreCase));
        var feature=featureId.GetObjectOfType<HistoryItem>();
        var kind=FeatureType(featureId);
        var views=ModelViewFollowers.Resolve(body,contents.ToArray(),cancel,includeStandalone:true);
        Need(!featureId.IsNull&&feature!=null&&kind!=null,"PRECONDITION_FAILED","Selected native forming feature is unavailable.");
        var recovery=ModelFeatureSuppressionRecovery420.Create(body,featureId,contents.ToArray(),views,cancel??(()=>{}));
        Need(!featureId.IsNull&&feature!=null&&kind!=null&&!feature.IsBeyondEndOfPart&&
            feature.HostSolidID==body.ID&&feature.IsSuppressed==edit.ExpectedSuppressed&&
            ((edit.ExpectedSuppressed?ProjectedHistorySources.HealthySuppressed(feature):feature.Status.ToString()=="Ok")||
             recovery!=null&&ModelFeatureSuppressionRecovery420.Admitted(feature,recovery)),
            "PRECONDITION_FAILED","Selected model feature, owner, End-of-Part scope or expected suppression state changed.");
        Need(edit.Suppressed||feature!.CanBeSafelyUnsuppressed,"NATIVE_UNSUPPRESSION_REJECTED","Native dependencies do not permit restoring this feature while its required constructions remain suppressed; no state change started.");
        var selectedSuppressed=edit.ExpectedSuppressed?new HashSet<string>(StringComparer.OrdinalIgnoreCase){featureId.ToString()}:null;
        var external=ProjectedHistorySources.Read(body,contents.ToArray(),state,tx,cancel);
        if(recovery==null)ProjectedHistorySources.Healthy(contents.ToArray(),external,selectedSuppressedIds:selectedSuppressed);
        else ModelFeatureSuppressionRecovery420.RequireHistory(body,contents.ToArray(),external,recovery,cancel??(()=>{}));
        if(edit.Suppressed)
        {
            // Never intentionally remove the only feature that makes a body.
            // GetPartContents is an inventory, not a documented linear order.
            // A different active cut does not establish a remaining solid base.
            Need(contents.Any(id=>id!=featureId&&ActiveAdditiveFeature(id,body.ID)),
                "OUT_OF_SCOPE","The sole base-producing feature cannot be suppressed into an empty body.");
        }
        // Pending recovery can regenerate native FEV identities. SDK parent
        // replacement retains exact source-object proof for that route only.
        var deps=feature!.GetDependsOn(recovery!=null,false);
        Need(deps!=null,"HISTORY_UNAVAILABLE","Feature dependency inventory is unavailable.");
        ModelFeatureSuppressionState414.Snapshot? retained=null;
        if(views.LastModelItem!=featureId||recovery!=null)
        {
            retained=ModelFeatureSuppressionState414.Capture(body,source,featureId,views,args,state,tx,cancel??(()=>{}),recovery);
        }
        return new(body,source,solid.Layer,contents.ToArray(),featureId,kind!,deps!.ToArray(),
            views,ModelFeatureOracle.Read(solid),TopLevel(state),retained,recovery);
    }
    internal static void Preflight(ModifyArguments args,DocumentState state,Transaction tx,Action? cancel=null)=>
        Resolve((ModelFeatureSuppressionSpec)args.Operations.Single(),args,state,tx,cancel);
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Result Execute(ModifyArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        McUndoPoint? tracker=null;Binding? binding=null;bool started=false;string stage="preparation";
        void Trace(string value){stage=value;log.Write(job.Operation,job.OriginalRequestId,"model_feature_suppression_checkpoint",transaction:value,operationId:job.OperationId);}
        void Close(){var value=tracker;tracker=null;if(value==null)return;try{value.Stop();}finally{value.Dispose();}}
        Result Failure(Exception error)
        {
            if(job.CommittedResult is {} committed)return committed;
            Exception? cleanup=null;try{Close();}catch(Exception e){cleanup=e;}
            bool unknown=started||cleanup!=null;
            string cause=error is DomainException d?d.Error.code:error is OperationCanceledException?"CANCELLED":"MODEL_FEATURE_SUPPRESSION_FAILED";
            return job.Error(unknown?"WRITE_OUTCOME_UNKNOWN":cause,
                unknown?"Native feature state is unverified; inspect this model and its history before a dependent write.":error.Message,
                unknown?"unknown":"not_started",context,new{cause,stage,message=error.Message,
                    body_mc_id=binding?.Body.ID.ToString(),feature_id=binding?.Feature.ToString(),cleanup_error=cleanup?.GetType().Name,
                    verification_details=error is DomainException observed?observed.Error.details:null})
                with{warnings=[WriteRecovery.Warning]};
        }
        try
        {
            var edit=(ModelFeatureSuppressionSpec)args.Operations.Single();
            using(var tx=state.Document.Database.TransactionManager.StartTransaction())binding=Resolve(edit,args,state,tx,job.CheckCancellation);
            job.CheckCancellation();ModelViewFollowers.CheckStandalone(binding.Body,binding.Views,job.CheckCancellation);tracker=new McUndoPoint(false);tracker.Start();started=true;
            var feature=binding.Feature.GetObjectOfType<HistoryItem>();
            Need(feature!=null,"MODEL_STATE_UNAVAILABLE","Selected feature disappeared before the edit.");
            Trace("suppression_before");
            Need(edit.Suppressed?feature!.Suppress(false):feature!.Unsuppress(false),"MODEL_UPDATE_FAILED","Native feature suppression state change was rejected.");
            feature.DbEntity.Update();Trace("update_before");McObjectManager.UpdateAll();
            Trace("update_returned");
            string[] designWarnings=[];
            (EntityRef Body,ModelFeatureOracle.Metrics Metrics) Verify()
            {
                job.CheckCancellation();
                var history=ModelInventoryBudget.History(binding.Body,job.CheckCancellation);
                Need(history!=null&&history.Count==binding.Contents.Length&&
                    history.ToHashSet().SetEquals(binding.Contents)&&
                    binding.Body.LastPartItemID==binding.Views.LastHistoryItem,
                    "MODEL_STATE_UNAVAILABLE","Feature suppression changed history identities.");
                var node=binding.Feature.GetObjectOfType<HistoryItem>();
                var deps=node?.GetDependsOn(binding.Recovery!=null,false)?.ToArray()??[];
                Need(node!=null&&node.HostSolidID==binding.Body.ID&&node.IsSuppressed==edit.Suppressed&&
                    (edit.Suppressed?ProjectedHistorySources.HealthySuppressed(node):node.Status.ToString()=="Ok")&&
                    deps.ToHashSet().SetEquals(binding.Dependencies),
                    "MODEL_STATE_UNAVAILABLE","Feature state, owner or dependencies changed unexpectedly.");
                ModelViewFollowers.Verify(binding.Body,binding.Views,job.CheckCancellation,refreshStandalone:false);
                var inventory=TopLevel(state);
                using var tx=state.Document.Database.TransactionManager.StartTransaction();
                var selectedSuppressed=edit.Suppressed?new HashSet<string>(StringComparer.OrdinalIgnoreCase){binding.Feature.ToString()}:null;
                var external=ProjectedHistorySources.Read(binding.Body,history.ToArray(),state,tx,job.CheckCancellation);
                if(binding.Recovery==null)ProjectedHistorySources.Healthy(history.ToArray(),external,selectedSuppressedIds:selectedSuppressed);
                else ModelFeatureSuppressionRecovery420.RequireHistory(binding.Body,history.ToArray(),external,binding.Recovery,job.CheckCancellation,allowPending:false);
                var matches=inventory.Where(id=>tx.GetObject(id,OpenMode.ForRead) is Solid3d&&
                    McObjectId.FromHandle(id.Handle.Value,McDocument.ActiveDocument.ID)==binding.Body.ID).ToArray();
                Need(matches.Length==1&&(matches[0]==binding.Source||!inventory.Contains(binding.Source)),
                    "OBJECT_IDENTITY_MISMATCH","Changed body has no unique current representation.");
                Need(inventory.Except(binding.Inventory).All(id=>id==matches[0])&&
                    binding.Inventory.Except(inventory).All(id=>id==binding.Source),
                    "MODEL_STATE_UNAVAILABLE","Feature suppression altered other top-level entities.");
                var solid=(Solid3d)tx.GetObject(matches[0],OpenMode.ForRead);
                var metrics=ModelFeatureOracle.Read(solid);
                Need(solid.Layer==binding.Layer,"MODEL_STATE_UNAVAILABLE","Feature suppression changed the body's layer.");
                if(binding.RetainedDesign!=null)
                {
                    var design=ModelFeatureSuppressionState414.Capture(binding.Body,matches[0],binding.Feature,binding.Views,args,state,tx,job.CheckCancellation,binding.Recovery);
                    designWarnings=ModelFeatureSuppressionState414.Verify(binding.RetainedDesign,design,binding.Feature,job.CheckCancellation);
                }
                return(new(args.BridgeId,args.DocumentId,Validation.NormalizeHandle(matches[0].Handle.ToString())),metrics);
            }
            ModelViewFollowers.PrepareStandaloneRefresh(binding.Body,binding.Views,job.CheckCancellation);
            job.BeginCommit();Close();
            return NativePostCommandVerification422.Defer(job,()=>
            {
            var verified=Verify();Trace("suppression_verified_after_command");
            var sourceRef=new EntityRef(args.BridgeId,args.DocumentId,edit.Handles[0]);
            bool replaced=sourceRef.handle!=verified.Body.handle;
            var change=new ModelFeatureSuppressionChange(sourceRef,verified.Body,binding.Body.ID.ToString(),
                binding.Feature.ToString(),binding.FeatureType,edit.Suppressed,
                binding.Before.Volume,verified.Metrics.Volume,replaced);
            var data=new ModifiedEntities(args.OperationId,"committed",false,[verified.Body],
                replaced?[sourceRef]:[],replaced?[verified.Body]:[],[],"not_guaranteed",
                model_feature_suppression_changes:[change]);
            var result=Result.Ok(job.OriginalRequestId,data,context,WriteRecovery.Warning,
                "NATIVE_FEATURE_STATE_CHANGED: Inspect the resulting model and any linked views through MCP capture_view; refresh history before further edits.");
            if(designWarnings.Length>0)result=result with{warnings=result.warnings.Concat(designWarnings).Distinct(StringComparer.Ordinal).ToArray()};
            result=ModelViewFollowers.WithWarnings(result,binding.Views);job.RecordCommit(result);Trace("commit_recorded");return result;
            },Failure);
        }
        catch(Exception error){return Failure(error);}
    }
}
