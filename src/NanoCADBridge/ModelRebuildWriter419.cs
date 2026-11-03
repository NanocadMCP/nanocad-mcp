using System.Runtime.CompilerServices;
using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.DatabaseServices.Constraints;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// Recovery must be callable before history is healthy. Read public identities,
// stored suppression/order/source links and expressions, not a healthy-edit
// snapshot which would reject the very NeedUpdate state being recovered.
internal static class ModelRebuildWriter419
{
    internal static bool Handles(ModifyArguments args)=>args.Operations.Any(e=>e is ModelRebuildSpec419);
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok,string code,string message)
    {if(!ok)throw new DomainException(code,message);}
    sealed record Snapshot(McObjectId[] Contents,McObjectId Last,JsonElement Design,ModelRebuildNode419[] Nodes);
    sealed record Binding(Mc3dSolid Body,Db.ObjectId Source,string Layer,HashSet<Db.ObjectId> Inventory,Snapshot Before);
    sealed record Report(ModelRebuildNode419[] unresolved_nodes,int unresolved_count,bool unresolved_nodes_complete,
        string[] suppressed_feature_ids,int suppressed_feature_count,bool suppressed_feature_ids_complete);
    static string? Id(McObjectId id)=>id.IsNull?null:id.ToString();
    static T[] Prefix<T>(IEnumerable<T> rows,int byteBudget)
    {
        int bytes=2;var kept=new List<T>();
        foreach(var row in rows){int size=JsonSerializer.SerializeToUtf8Bytes(row,Wire.Json).Length+1;if(bytes+size>byteBudget)break;kept.Add(row);bytes+=size;}
        return kept.ToArray();
    }
    static Snapshot Observe(Mc3dSolid body,DocumentState state,Action cancel)
    {
        var owner=body.DbEntity.Document;Need(owner!=null,"DOCUMENT_CONTEXT_MISMATCH","The actual native part owner is unavailable.");
        var contents=ModelInventoryBudget.History(body,cancel).ToArray();
        Need(contents.Length>0&&contents.Distinct().Count()==contents.Length,"HISTORY_UNAVAILABLE","Part history is empty or contains duplicate identities.");
        // An unresolved source remains observable for recovery, but must never
        // disappear from the health report just because it has no HistoryItem.
        var sources=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sourceFailures=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);string? sourceInventoryError=null;
        try
        {
            var inspected=NativePathSources.Inspect(contents,cancel);sources.UnionWith(inspected.Sources);
            foreach(var failure in inspected.Failures)sourceFailures[failure.Key]=failure.Value;
        }
        catch(DomainException error){sourceInventoryError=error.Error.code;}
        try
        {
            using var tx=state.Document.Database.TransactionManager.StartTransaction();
            sources.UnionWith(ProjectedHistorySources.Read(body,contents,state,tx,cancel).Select(source=>source.id));
        }
        catch(DomainException error)
        {
            // A projected axis/anchor may be outside GetPartContents. Keep its
            // consumer's source check explicit even when there is no source row.
            foreach(var id in contents)
            {
                cancel();
                if(id.GetObjectOfType<ProjectionSketchEnt>()!=null||id.GetObjectOfType<RevolveFeature>()!=null||id.GetObjectOfType<AssocRotateFeature>()!=null)
                    sourceFailures[id.ToString()]="PROJECTED_SOURCE_CHECK_UNAVAILABLE: "+error.Error.code;
            }
        }
        var nodes=new List<ModelRebuildNode419>();var design=new List<object>();var budget=new ModelInventoryBudget(cancel);
        foreach(var id in contents.OrderBy(id=>id.ToString(),StringComparer.OrdinalIgnoreCase))
        {
            cancel();var node=id.GetObjectOfType<HistoryItem>();
            if(node==null)
            {
                bool verified=sources.Contains(id.ToString());
                nodes.Add(new(id.ToString(),"native_source",verified?"Ok":"UnverifiedSource",false,false));
                design.Add(new{id=id.ToString(),kind="native_source"});budget.Add(design[^1]);continue;
            }
            bool documentConstruction=node.HostSolidID.IsNull&&(id.GetObjectOfType<PlanarSketch>()!=null||id.GetObjectOfType<SketchProfile>()!=null);
            Need(node.ID==id&&(node.HostSolidID==body.ID||documentConstruction)&&node.DbEntity.Document?.ID==owner!.ID,
                "DEPENDENCY_OWNERSHIP_MISMATCH","Part history contains a missing or foreign native node.");
            if(id.GetObjectOfType<PlanarSketch>() is {} sketch)
                Need(!sketch.IsInEditMode,"HOST_BUSY","Finish editing the actual sketch before rebuilding this part.");
            var dependencies=node.GetDependsOn(true,false);
            Need(dependencies!=null,"HISTORY_UNAVAILABLE","Native source dependencies are unavailable.");
            ModelInventoryBudget.Ids(dependencies!,cancel);
            sourceFailures.TryGetValue(id.ToString(),out var sourceError);
            nodes.Add(new(id.ToString(),node.GetType().Name,node.Status.ToString(),node.IsSuppressed,node.IsBeyondEndOfPart,
                sourceInventoryError??sourceError));
            design.Add(new{id=id.ToString(),kind=node.GetType().FullName,owner=Id(node.HostSolidID),document=Id(node.DbEntity.Document.ID),
                previous=Id(node.OrderObjBefore),suppressed=node.IsSuppressed,beyond_end_of_part=node.IsBeyondEndOfPart,
                sources=dependencies!.Select(Id).OrderBy(value=>value,StringComparer.OrdinalIgnoreCase).ToArray()});
            budget.Add(design[^1]);
        }
        using var manager=owner!.GetDocumentVariablesManager(Constraint2dNetType.MCS);
        Need(manager!=null,"VARIABLES_UNAVAILABLE","The native variable manager is unavailable.");
        var variables=manager!.GetAllVariables();Need(variables!=null,"VARIABLES_UNAVAILABLE","The native variable inventory is unavailable.");
        ModelInventoryBudget.Ids(variables!,cancel);var expressions=new List<object>();
        foreach(var id in variables!.OrderBy(id=>id.ToString(),StringComparer.OrdinalIgnoreCase))
        {
            cancel();var info=manager.GetVariableInfo(id);Need(info!=null,"VARIABLES_UNAVAILABLE","A retained variable has no readable native definition.");
            expressions.Add(new{id=id.ToString(),name=info!.Name,expression=info.Expression,type=info.Type.ToString(),owner=Id(manager.GetVariableHostObjectId(id))});
            budget.Add(expressions[^1]);
        }
        return new(contents,body.LastPartItemID,Wire.Element(new{body=Id(body.ID),owner=Id(owner.ID),last=Id(body.LastPartItemID),nodes=design,variables=expressions}),nodes.ToArray());
    }
    static ModelRebuildNode419[] Unresolved(Snapshot snapshot)=>snapshot.Nodes.Where(node=>!node.beyond_end_of_part&&
        (!(node.suppressed?node.status is "Ok" or "Suppressed":node.status is "Ok" or "Warn")||
         !node.suppressed&&node.source_verification_error!=null)).ToArray();
    static Report Summarize(Snapshot snapshot)
    {
        var all=Unresolved(snapshot);var suppressed=snapshot.Nodes.Where(node=>node.suppressed).Select(node=>node.mc_id).ToArray();
        // Reporting is limited by actual serialized bytes, never the model's
        // number of features. Complete observations stay internal for checks.
        var nodes=Prefix(all,Wire.PayloadBudget/2);var ids=Prefix(suppressed,Wire.PayloadBudget/4);
        return new(nodes,all.Length,nodes.Length==all.Length,ids,suppressed.Length,ids.Length==suppressed.Length);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static Binding Resolve(ModifyArguments args,DocumentState state,Db.Transaction tx,Action cancel)
    {
        Need(args.Operations.Length==1&&args.Operations[0] is ModelRebuildSpec419,"INVALID_ARGUMENT","Rebuild must be the sole operation on one native part.");
        var loaded=AppDomain.CurrentDomain.GetAssemblies().Select(assembly=>assembly.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Need(loaded.Contains("mapimgd")&&loaded.Contains("MapiBaseTypes"),"CAPABILITY_UNAVAILABLE","MultiCAD runtime must already be loaded.");
        var edit=(ModelRebuildSpec419)args.Operations[0];var active=ModelFeatureReorderState345.Scope(state,tx);
        var source=CadStructureWriter.Find(state.Document.Database,edit.Handles.Single());
        var solid=tx.GetObject(source,Db.OpenMode.ForRead) as Db.Solid3d;
        Need(solid!=null&&solid.GetType()==typeof(Db.Solid3d)&&solid.OwnerId==ModelFeatureReorderState345.Model(state.Document.Database,tx)&&!solid.IsNull,
            "UNSUPPORTED_ENTITY_TYPE","Select one nonempty current top-level native part representation.");
        var layer=(Db.LayerTableRecord)tx.GetObject(solid!.LayerId,Db.OpenMode.ForRead);
        Need(!layer.IsLocked&&!layer.IsOff&&!layer.IsFrozen&&!layer.IsDependent,"LAYER_UNAVAILABLE","The selected part layer is unavailable.");
        var id=McObjectId.FromHandle(source.Handle.Value,active.ID);var body=id.GetObjectOfType<Mc3dSolid>();
        Need(body!=null&&body.ID==id&&body.LastPartItemID.ToString().Equals(edit.ExpectedLastItemId,StringComparison.OrdinalIgnoreCase),
            "PRECONDITION_FAILED","The native part or its last history item changed; read current history.");
        NativeModelHistory421.EnsureAvailable();
        return new(body!,source,solid.Layer,ModelFeatureReorderState345.Inventory(state,tx,cancel),Observe(body!,state,cancel));
    }
    internal static void Preflight(ModifyArguments args,DocumentState state,Db.Transaction tx,Action? cancel=null)=>Resolve(args,state,tx,cancel??(()=>{}));
    internal static Result Execute(ModifyArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        Binding? binding=null;McUndoPoint? tracker=null;bool started=false;bool? accepted=null;string stage="preparation";
        void Close(){var value=tracker;tracker=null;if(value!=null)try{value.Stop();}finally{value.Dispose();}}
        Result Failure(Exception error)
        {
            if(job.CommittedResult is {} committed)return committed;Exception? cleanup=null;try{Close();}catch(Exception e){cleanup=e;}
            if(accepted==null&&error is DomainException nativeError&&nativeError.Error.details is {} nativeDetails)
            {
                var observedDetails=Wire.Element(nativeDetails);
                if(observedDetails.TryGetProperty("native_rebuild_accepted",out var known)&&known.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    accepted=known.GetBoolean();
            }
            bool unknown=started||cleanup!=null;string cause=error is DomainException d?d.Error.code:error is OperationCanceledException?"CANCELLED":"MODEL_REBUILD_FAILED";
            return job.Error(unknown?"WRITE_OUTCOME_UNKNOWN":cause,unknown?"Native rebuild is unverified. Inspect the current part/history; no rollback or prior-write success is claimed.":error.Message,
                unknown?"unknown":"not_started",context,new{cause,stage,message=error.Message,native_rebuild_accepted=accepted,
                    body_mc_id=binding?.Body.ID.ToString(),observed=started?Diagnostics():null,cleanup=cleanup?.Message}) with{warnings=[WriteRecovery.Warning]};
        }
        object? Diagnostics()
        {
            if(binding==null)return null;
            try{var observed=Observe(binding.Body,state,()=>{});return new{last_item_id=Id(observed.Last),history=Summarize(observed)};}
            catch(Exception e){return new{observation_available=false,error=e is DomainException d?d.Error.code:e.GetType().Name};}
        }
        try
        {
            using(var tx=state.Document.Database.TransactionManager.StartTransaction())binding=Resolve(args,state,tx,job.CheckCancellation);
            job.CheckCancellation();tracker=new McUndoPoint(false);tracker.Start();started=true;stage="native_rebuild";
            // Public SDK rebuildPart(true,true) evaluates this part now; the
            // managed overload only queues it. Still inspect actual history.
            accepted=NativeModelHistory421.Rebuild(binding.Body);stage="native_update";McObjectManager.UpdateAll();job.CheckCancellation();
            stage="close_update";Close();McObjectManager.UpdateAll();job.BeginCommit();
            return NativePostCommandVerification422.Defer(job,()=>
            {
            stage="observe_after_command";
            var after=Observe(binding.Body,state,job.CheckCancellation);
            Need(binding.Before.Contents.ToHashSet().SetEquals(after.Contents)&&binding.Before.Last==after.Last&&
                ModelParameterSnapshot.Equivalent(binding.Before.Design,after.Design),
                "MODEL_STATE_UNAVAILABLE","Rebuild changed a stored history identity, source, order, suppression choice or variable definition.");
            Db.ObjectId representation;
            using(var tx=state.Document.Database.TransactionManager.StartTransaction())
            {
                representation=ModelHistoryEditGuard.CurrentRepresentation(binding.Body,state,tx,job.CheckCancellation);
                var solid=(Db.Solid3d)tx.GetObject(representation,Db.OpenMode.ForRead);
                Need(!solid.IsNull&&solid.Layer==binding.Layer,"MODEL_STATE_UNAVAILABLE","Rebuild left an empty representation or changed its layer.");
                var inventory=ModelFeatureReorderState345.Inventory(state,tx,job.CheckCancellation);
                Need(inventory.Except(binding.Inventory).All(id=>id==representation)&&binding.Inventory.Except(inventory).All(id=>id==binding.Source),
                    "MODEL_STATE_UNAVAILABLE","Rebuild changed unrelated top-level entity membership.");
            }
            var report=Summarize(after);var sourceRef=new EntityRef(args.BridgeId,args.DocumentId,args.Operations[0].Handles.Single());
            var bodyRef=new EntityRef(args.BridgeId,args.DocumentId,Validation.NormalizeHandle(representation.Handle.ToString()));bool replaced=representation!=binding.Source;
            var change=new ModelRebuildChange419(sourceRef,bodyRef,binding.Body.ID.ToString(),after.Last.ToString(),accepted.Value,
                report.unresolved_count==0,replaced,report.unresolved_nodes,report.unresolved_count,report.unresolved_nodes_complete,
                report.suppressed_feature_ids,report.suppressed_feature_count,report.suppressed_feature_ids_complete);
            var data=new ModelRebuildOutput419(args.OperationId,"committed",false,[bodyRef],replaced?[sourceRef]:[],replaced?[bodyRef]:[],[],"not_guaranteed",[change]);
            var result=Result.Ok(job.OriginalRequestId,data,context,WriteRecovery.Warning,
                accepted.Value&&report.unresolved_count==0
                    ?"NATIVE_REBUILD_OBSERVED: No unresolved active history nodes were observed. Verify affected dimensions and the intended visual result; this does not certify a previous unknown write."
                    :"MODEL_REBUILD_INCOMPLETE: The native rebuild attempt finished, but its accepted flag or observed history still requires attention. Read unresolved_nodes and correct the indicated construction; do not repeat a previous unknown write.");
            int warningCount=after.Nodes.Count(node=>!node.beyond_end_of_part&&node.status=="Warn");
            if(warningCount>0)result=result with{warnings=result.warnings.Concat([$"MODEL_HISTORY_WARN: {warningCount} native history nodes have nonblocking warnings; inspect their current history details."]).ToArray()};
            job.RecordCommit(result);log.Write(job.Operation,job.OriginalRequestId,"model_rebuild419_observed",transaction:"committed",operationId:job.OperationId);return result;
            },Failure);
        }
        catch(Exception error){return Failure(error);}
    }
}
