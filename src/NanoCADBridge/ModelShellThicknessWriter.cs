using System.Runtime.CompilerServices;
using System.Globalization;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.DatabaseServices.Constraints;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class ModelShellThicknessWriter
{
    internal static bool Handles(ModifyArguments args)=>args.Operations.Any(e=>e is ModelShellThicknessSpec);
    private static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok,string code,string message)
    {if(!ok)throw new DomainException(code,message);}
    private sealed record Binding(Mc3dSolid Body,ObjectId Source,string Layer,McObjectId[] Contents,
        McObjectId Feature,McObjectId[] Openings,ModelViewFollowers.Snapshot Views,
        ModelFeatureOracle.Metrics Before,HashSet<ObjectId> Inventory,System.Text.Json.JsonElement ProtectedHistory,
        McObjectId ThicknessVariable,Dictionary<McObjectId,string> ExpectedExpressions,Dictionary<McObjectId,string> Names);
    private static ObjectId Model(Database db,Transaction tx)=>
        ((BlockTable)tx.GetObject(db.BlockTableId,OpenMode.ForRead))[BlockTableRecord.ModelSpace];
    private static HashSet<ObjectId> TopLevel(DocumentState state)
    {
        using var tx=state.Document.Database.TransactionManager.StartTransaction();
        var ids=((BlockTableRecord)tx.GetObject(Model(state.Document.Database,tx),OpenMode.ForRead))
            .Cast<ObjectId>().ToHashSet();
        
        return ids;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Binding Resolve(ModelShellThicknessSpec edit,ModifyArguments args,DocumentState state,Transaction tx,Action? cancel=null)
    {
        Need(args.Operations.Length==1,"INVALID_ARGUMENT","Native shell thickness change must be the sole operation.");
        var loaded=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetName().Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Need(loaded.Contains("mapimgd")&&loaded.Contains("MapiBaseTypes"),"CAPABILITY_UNAVAILABLE",
            "MultiCAD runtime must already be loaded.");
        var db=state.Document.Database;var model=Model(db,tx);
        Need(db.CurrentSpaceId==model,"OUT_OF_SCOPE","Activate Model Space.");
        var active=McDocument.ActiveDocument;
        Need(state.Document.IsNamedDrawing&&active!=null&&!string.IsNullOrWhiteSpace(active.Path)&&
            string.Equals(Path.GetFullPath(active.Path),Path.GetFullPath(state.Document.Name),StringComparison.OrdinalIgnoreCase),
            "DOCUMENT_CONTEXT_MISMATCH","Native shell edit requires this exact active named DWG.");
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
            "PRECONDITION_FAILED","Body or terminal history item changed.");
        var contents=ModelInventoryBudget.History(body!,cancel);
        Need(contents.Count>0,"HISTORY_UNAVAILABLE","Native shell history is unavailable.");
        var featureId=contents!.SingleOrDefault(id=>id.ToString().Equals(edit.FeatureId,StringComparison.OrdinalIgnoreCase));
        var shell=featureId.GetObjectOfType<ThicknessFeature>();
        var views=ModelViewFollowers.Resolve(body,contents.ToArray(),cancel,includeStandalone:true);
        Need(!featureId.IsNull&&shell!=null&&
            shell.HostSolidID==body.ID&&!shell.IsSuppressed&&shell.Status.ToString()=="Ok"&&
            shell.SurfaceId.IsNull&&shell.Direction==FeatureExtentDirection.Negative,
            "OUT_OF_SCOPE","Select one healthy inward body shell.");
        var openings=shell!.OutFaces?.ToArray()??[];
        Need(openings.Length>0&&openings.Distinct().Count()==openings.Length&&openings.All(face=>face.IsFace&&face.ParentId==body.ID),
            "MODEL_STATE_UNAVAILABLE","Shell does not retain distinct opening faces owned by this body.");
        double current=shell.GetFaceThickness(McObjectId.Null);
        Need(double.IsFinite(current)&&ModelParameterSnapshot.Near(current,edit.ExpectedThickness),
            "PRECONDITION_FAILED","Current native wall thickness differs from expected_thickness.");
        var driver=ModelShellParameter412.Observe(shell,cancel).SingleOrDefault();
        if(driver!=null)
        {
            ModelShellParameter412.ValidateRequested(shell,driver.Role,edit.Thickness);
            ModelNumericDriverEditor.Validate(shell,driver.Variable,edit.Thickness,edit.ReplaceDriverFormulas);
            ModelNumericDriverEditor.ValidateChangeScope(shell,driver.Variable,edit.Thickness,body,contents.ToArray(),views,cancel);
        }
        var expressions=new Dictionary<McObjectId,string>();
        var names=new Dictionary<McObjectId,string>();
        using(var manager=shell.DbEntity.Document.GetDocumentVariablesManager(Constraint2dNetType.MCS))
        {
            Need(manager!=null,"VARIABLES_UNAVAILABLE","The owning shell variable manager is unavailable.");
            var variables=manager.GetAllVariables();
            Need(variables!=null,"VARIABLES_UNAVAILABLE","The owning shell variable inventory is unavailable.");
            ModelInventoryBudget.Ids(variables,cancel);
            foreach(var variable in variables)
            {
                cancel?.Invoke();
                if(manager.GetVariableHostObjectId(variable)!=shell.ID)continue;
                var info=manager.GetVariableInfo(variable);
                Need(info!=null,"VARIABLES_UNAVAILABLE","A retained shell driver expression is unavailable.");
                names[variable]=info.Name;
                expressions[variable]=driver!=null&&driver.Variable==variable?
                    ModelNumericDriverEditor.RetainedExpression(shell,variable,edit.Thickness)??edit.Thickness.ToString("R",CultureInfo.InvariantCulture):info.Expression;
            }
        }
        var external=ProjectedHistorySources.Read(body,contents.ToArray(),state,tx,cancel);
        ProjectedHistorySources.Healthy(contents.ToArray(),external);
        return new(body,source,solid.Layer,contents.ToArray(),featureId,openings,views,
            ModelFeatureOracle.Read(solid),TopLevel(state),ModelHistoryEditGuard.Capture(args,state,featureId,cancel:cancel),
            driver?.Variable??McObjectId.Null,expressions,names);
    }
    internal static void Preflight(ModifyArguments args,DocumentState state,Transaction tx,Action? cancel=null)=>
        Resolve((ModelShellThicknessSpec)args.Operations.Single(),args,state,tx,cancel);
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Result Execute(ModifyArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        McUndoPoint? tracker=null;Binding? binding=null;bool started=false;string stage="preparation";
        void Trace(string value){stage=value;log.Write(job.Operation,job.OriginalRequestId,"model_shell_thickness_checkpoint",transaction:value,operationId:job.OperationId);}
        void Close(){var value=tracker;tracker=null;if(value==null)return;try{value.Stop();}finally{value.Dispose();}}
        try
        {
            var edit=(ModelShellThicknessSpec)args.Operations.Single();
            using(var tx=state.Document.Database.TransactionManager.StartTransaction())binding=Resolve(edit,args,state,tx,job.CheckCancellation);
            job.CheckCancellation();ModelViewFollowers.CheckStandalone(binding.Body,binding.Views,job.CheckCancellation);tracker=new McUndoPoint(false);tracker.Start();started=true;
            var shell=binding.Feature.GetObjectOfType<ThicknessFeature>();
            Need(shell!=null,"MODEL_STATE_UNAVAILABLE","Terminal shell disappeared before editing.");
            Trace("parameter_before");
            if(binding.ThicknessVariable.IsNull)shell!.SetFaceThickness(McObjectId.Null,edit.Thickness);
            else ModelNumericDriverEditor.Set(shell!,binding.ThicknessVariable,edit.Thickness,edit.ReplaceDriverFormulas);
            shell.DbEntity.Update();Trace("update_before");McObjectManager.UpdateAll();
            Need(binding.Body.RebuildPart(),"MODEL_UPDATE_FAILED","Native shell rebuild failed.");
            McObjectManager.UpdateAll();Trace("update_returned");
            (EntityRef Body,ModelFeatureOracle.Metrics Metrics) Verify()
            {
                job.CheckCancellation();
                var history=ModelInventoryBudget.History(binding.Body,job.CheckCancellation);
                Need(history!=null&&history.Count==binding.Contents.Length&&
                    history.ToHashSet().SetEquals(binding.Contents)&&
                    binding.Body.LastPartItemID==binding.Views.LastHistoryItem,
                    "MODEL_STATE_UNAVAILABLE","Shell thickness change altered history identities.");
                var feature=binding.Feature.GetObjectOfType<ThicknessFeature>();
                var openings=feature?.OutFaces?.ToArray()??[];
                Need(feature!=null&&feature.HostSolidID==binding.Body.ID&&!feature.IsSuppressed&&
                    feature.Status.ToString()=="Ok"&&feature.SurfaceId.IsNull&&
                    feature.Direction==FeatureExtentDirection.Negative&&openings.Length==binding.Openings.Length&&openings.ToHashSet().SetEquals(binding.Openings)&&
                    ModelParameterSnapshot.Near(feature.GetFaceThickness(McObjectId.Null),edit.Thickness),
                    "MODEL_STATE_UNAVAILABLE","Native shell lost its thickness, opening, owner or status.");
                var currentDriver=ModelShellParameter412.Observe(feature!,job.CheckCancellation).SingleOrDefault();
                Need(binding.ThicknessVariable.IsNull||currentDriver?.Variable==binding.ThicknessVariable,
                    "MODEL_STATE_UNAVAILABLE","Changing shell thickness replaced its exact native control variable.");
                ModelNumericDriverEditor.VerifyRetained(feature!,binding.ExpectedExpressions);
                using(var manager=feature!.DbEntity.Document.GetDocumentVariablesManager(Constraint2dNetType.MCS))
                    foreach(var pair in binding.Names)
                        Need(manager!=null&&manager.GetVariableHostObjectId(pair.Key)==feature.ID&&manager.GetVariableInfo(pair.Key)?.Name==pair.Value,
                            "MODEL_STATE_UNAVAILABLE","Changing shell thickness lost a retained native driver name or owner.");
                ModelViewFollowers.Verify(binding.Body,binding.Views,job.CheckCancellation,refreshStandalone:true);
                var inventory=TopLevel(state);
                using var tx=state.Document.Database.TransactionManager.StartTransaction();
                var matches=inventory.Where(id=>tx.GetObject(id,OpenMode.ForRead) is Solid3d&&
                    McObjectId.FromHandle(id.Handle.Value,McDocument.ActiveDocument.ID)==binding.Body.ID).ToArray();
                Need(matches.Length==1&&(matches[0]==binding.Source||!inventory.Contains(binding.Source)),
                    "OBJECT_IDENTITY_MISMATCH","Shell body has no unique current representation.");
                Need(inventory.Except(binding.Inventory).All(id=>id==matches[0])&&
                    binding.Inventory.Except(inventory).All(id=>id==binding.Source),
                    "MODEL_STATE_UNAVAILABLE","Thickness change unexpectedly altered other top-level entities.");
                var solid=(Solid3d)tx.GetObject(matches[0],OpenMode.ForRead);
                var metrics=ModelFeatureOracle.Read(solid);
                Need(solid.Layer==binding.Layer,
                    "MODEL_STATE_UNAVAILABLE","Thickness change altered the body's layer.");
                ModelHistoryEditGuard.Verify(binding.ProtectedHistory,args,state,binding.Feature,Validation.NormalizeHandle(matches[0].Handle.ToString()),job.CheckCancellation);
                return(new(args.BridgeId,args.DocumentId,Validation.NormalizeHandle(matches[0].Handle.ToString())),metrics);
            }
            var verified=Verify();Trace("thickness_verified");job.BeginCommit();Close();verified=Verify();
            var sourceRef=new EntityRef(args.BridgeId,args.DocumentId,edit.Handles[0]);
            bool replaced=sourceRef.handle!=verified.Body.handle;
            var change=new ModelShellChange(edit.Operation,sourceRef,verified.Body,binding.Body.ID.ToString(),
                binding.Feature.ToString(),binding.Openings[0].ToString(),edit.Thickness,edit.ExpectedThickness,
                binding.Before.Volume,verified.Metrics.Volume,replaced,binding.Openings.Select(face=>face.ToString()).ToArray());
            var data=new ModifiedEntities(args.OperationId,"committed",false,[verified.Body],
                replaced?[sourceRef]:[],replaced?[verified.Body]:[],[],"not_guaranteed",model_shell_changes:[change]);
            var result=Result.Ok(job.OriginalRequestId,data,context,WriteRecovery.Warning,
                "NATIVE_SHELL_THICKNESS_CHANGED: Inspect wall geometry through MCP capture_view and refresh native history.");
            result=ModelViewFollowers.WithWarnings(result,binding.Views);job.RecordCommit(result);Trace("commit_recorded");return result;
        }
        catch(Exception error)
        {
            if(job.CommittedResult is {} committed)return committed;
            Exception? cleanup=null;try{Close();}catch(Exception e){cleanup=e;}
            bool unknown=started||cleanup!=null;
            string cause=error is DomainException d?d.Error.code:error is OperationCanceledException?"CANCELLED":"MODEL_SHELL_THICKNESS_FAILED";
            return job.Error(unknown?"WRITE_OUTCOME_UNKNOWN":cause,
                unknown?"Native shell thickness outcome is unverified; inspect this body and history before another dependent write.":error.Message,
                unknown?"unknown":"not_started",context,new{cause,stage,message=error.Message,
                    body_mc_id=binding?.Body.ID.ToString(),feature_id=binding?.Feature.ToString(),cleanup_error=cleanup?.GetType().Name})
                with{warnings=[WriteRecovery.Warning]};
        }
    }
}
