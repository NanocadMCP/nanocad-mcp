using System.Runtime.CompilerServices;
using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// A real ThicknessFeature in the part history. The selected opening is a
// MultiCAD face of this body, never a Teigha B-rep native_id conversion.
internal static class ModelShellWriter
{
    internal static bool Handles(ModifyArguments args)=>args.Operations.Any(e=>e is ModelShellSpec);
    private static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok,string code,string message)
    {if(!ok)throw new DomainException(code,message);}
    private sealed record Binding(Mc3dSolid Body,ObjectId Source,string Layer,McObjectId[] Contents,
        McObjectId[] Openings,ModelFeatureOracle.Metrics Before,HashSet<ObjectId> Inventory,ModelViewFollowers.Snapshot Views);
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
    private static Binding Resolve(ModelShellSpec edit,ModifyArguments args,DocumentState state,Transaction tx,Action? cancel=null)
    {
        Need(args.Operations.Length==1,"INVALID_ARGUMENT","Native shell must be the sole operation.");
        var loaded=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetName().Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Need(loaded.Contains("mapimgd")&&loaded.Contains("MapiBaseTypes"),"CAPABILITY_UNAVAILABLE",
            "MultiCAD runtime must already be loaded.");
        var db=state.Document.Database;var model=Model(db,tx);
        Need(db.CurrentSpaceId==model,"OUT_OF_SCOPE","Activate Model Space.");
        var active=McDocument.ActiveDocument;
        Need(state.Document.IsNamedDrawing&&active!=null&&!string.IsNullOrWhiteSpace(active.Path)&&
            string.Equals(Path.GetFullPath(active.Path),Path.GetFullPath(state.Document.Name),StringComparison.OrdinalIgnoreCase),
            "DOCUMENT_CONTEXT_MISMATCH","Native shell requires this exact active named DWG.");
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
            "PRECONDITION_FAILED","Body or history anchor changed.");
        var contents=ModelInventoryBudget.History(body!,cancel);
        Need(contents.Count>0,"HISTORY_UNAVAILABLE","Shell requires a healthy existing native history.");
        var views=ModelViewFollowers.Resolve(body,contents.ToArray(),cancel,includeStandalone:true);
        ModelViewFollowers.PrepareInsertion(views,true);
        var external=ProjectedHistorySources.Read(body,contents.ToArray(),state,tx,cancel);
        ProjectedHistorySources.Healthy(contents.ToArray(),external);
        var faceSnapshot=Wire.Element(ModelFaceReader.Capture(source,state,args.BridgeId,CancellationToken.None,cancel));
        Need(faceSnapshot.GetProperty("face_token").GetString()?.Equals(edit.FaceToken,StringComparison.OrdinalIgnoreCase)==true,
            "STALE_TOPOLOGY","Native face snapshot changed; inspect_solid_topology again.");
        var observed=faceSnapshot.GetProperty("faces").EnumerateArray().ToArray();
        var openings=new List<McObjectId>();
        foreach(string selected in edit.OpenFaceIds)
        {
            cancel?.Invoke();
            var faces=observed.Where(f=>f.GetProperty("model_face_id").GetString()?.Equals(selected,StringComparison.OrdinalIgnoreCase)==true).ToArray();
            Need(faces.Length==1&&faces[0].GetProperty("plane").ValueKind==JsonValueKind.Object,
                "SUBENTITY_IDENTITY_UNAVAILABLE","Each opening must identify a current planar native face.");
            long marker=long.Parse(faces[0].GetProperty("marker").GetString()!,System.Globalization.CultureInfo.InvariantCulture);
            var opening=McObjectId.FromGsMarker(bodyId,marker);
            Need(!opening.IsNull&&opening.IsFace&&opening.ParentId==bodyId&&
                opening.ToString().Equals(selected,StringComparison.OrdinalIgnoreCase),
                "SUBENTITY_IDENTITY_UNAVAILABLE","An opening face changed owner or identity.");
            openings.Add(opening);
        }
        return new(body,source,solid.Layer,contents.ToArray(),openings.ToArray(),ModelFeatureOracle.Read(solid),TopLevel(state),views);
    }
    internal static void Preflight(ModifyArguments args,DocumentState state,Transaction tx,Action? cancel=null)=>
        Resolve((ModelShellSpec)args.Operations.Single(),args,state,tx,cancel);
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Result Execute(ModifyArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        McUndoPoint? tracker=null;Binding? binding=null;McObjectId featureId=McObjectId.Null;
        bool started=false;string stage="preparation";
        void Trace(string value){stage=value;log.Write(job.Operation,job.OriginalRequestId,"model_shell_checkpoint",transaction:value,operationId:job.OperationId);}
        void Close(){var value=tracker;tracker=null;if(value==null)return;try{value.Stop();}finally{value.Dispose();}}
        try
        {
            var edit=(ModelShellSpec)args.Operations.Single();
            using(var tx=state.Document.Database.TransactionManager.StartTransaction())binding=Resolve(edit,args,state,tx,job.CheckCancellation);
            job.CheckCancellation();ModelViewFollowers.CheckStandalone(binding.Body,binding.Views,job.CheckCancellation);tracker=new McUndoPoint(false);tracker.Start();started=true;
            ModelViewFollowers.PrepareInsertion(binding.Views,false);
            Trace("append_before");
            var shell=binding.Body.AddThicknessFeature(McObjectId.Null,FeatureExtentDirection.Negative,edit.Thickness,0);
            Need(shell!=null,"MODEL_UPDATE_FAILED","Native shell feature creation was rejected.");
            featureId=shell!.ID;
            foreach(var opening in binding.Openings){job.CheckCancellation();shell.AddOutFaces(opening);}
            shell.DbEntity.Update();
            var anchor=binding.Views.LastModelItem;
            if(shell.OrderObjBefore!=anchor)
                Need(shell.SetOrderObjBefore(anchor,false),"MODEL_UPDATE_FAILED","Shell predecessor link was rejected.");
            ModelViewFollowers.CompleteInsertion(binding.Views,featureId);
            Trace("update_before");McObjectManager.UpdateAll();Trace("update_returned");
            (EntityRef Body,ModelFeatureOracle.Metrics Metrics) Verify()
            {
                job.CheckCancellation();
                var history=ModelInventoryBudget.History(binding.Body,job.CheckCancellation);
                Need(history!=null&&binding.Contents.All(history.Contains)&&history.Contains(featureId)&&
                    binding.Body.LastPartItemID==(binding.Views.ViewIds.Length==0?featureId:binding.Views.LastHistoryItem),"MODEL_STATE_UNAVAILABLE","Shell or prior history is missing.");
                if(binding.Views.HasFollowers)ModelViewFollowers.VerifyAfterInsertion(binding.Body,binding.Views,featureId,job.CheckCancellation,refreshStandalone:true);
                var feature=featureId.GetObjectOfType<ThicknessFeature>();
                Need(feature!=null&&feature.HostSolidID==binding.Body.ID&&!feature.IsSuppressed&&
                    feature.Status.ToString()=="Ok"&&feature.OrderObjBefore==anchor&&
                    feature.SurfaceId.IsNull&&feature.Direction==FeatureExtentDirection.Negative,
                    "MODEL_STATE_UNAVAILABLE","Shell lost native owner, order, status or inward direction.");
                var openings=feature!.OutFaces?.ToArray()??[];
                Need(openings.Length==binding.Openings.Length&&openings.ToHashSet().SetEquals(binding.Openings),
                    "MODEL_STATE_UNAVAILABLE","Shell did not retain exactly the selected opening faces.");
                var actualThickness=feature.GetFaceThickness(McObjectId.Null);
                Need(double.IsFinite(actualThickness)&&ModelParameterSnapshot.Near(actualThickness,edit.Thickness),
                    "MODEL_STATE_UNAVAILABLE","Shell did not retain the requested native wall thickness.");
                var inventory=TopLevel(state);
                using var tx=state.Document.Database.TransactionManager.StartTransaction();
                var matches=inventory.Where(id=>tx.GetObject(id,OpenMode.ForRead) is Solid3d&&
                    McObjectId.FromHandle(id.Handle.Value,McDocument.ActiveDocument.ID)==binding.Body.ID).ToArray();
                Need(matches.Length==1&&(matches[0]==binding.Source||!inventory.Contains(binding.Source)),
                    "OBJECT_IDENTITY_MISMATCH","Shell body has no unique current representation.");
                Need(inventory.Except(binding.Inventory).All(id=>id==matches[0])&&
                    binding.Inventory.Except(inventory).All(id=>id==binding.Source),
                    "MODEL_STATE_UNAVAILABLE","Shell unexpectedly changed other top-level entities.");
                var solid=(Solid3d)tx.GetObject(matches[0],OpenMode.ForRead);
                var metrics=ModelFeatureOracle.Read(solid);
                Need(solid.Layer==binding.Layer,
                    "MODEL_STATE_UNAVAILABLE","Shell changed the body's layer.");
                return(new(args.BridgeId,args.DocumentId,Validation.NormalizeHandle(matches[0].Handle.ToString())),metrics);
            }
            var verified=Verify();Trace("shell_verified");job.BeginCommit();Close();verified=Verify();
            var sourceRef=new EntityRef(args.BridgeId,args.DocumentId,edit.Handles[0]);
            bool replaced=sourceRef.handle!=verified.Body.handle;
            var change=new ModelShellChange(edit.Operation,sourceRef,verified.Body,binding.Body.ID.ToString(),featureId.ToString(),
                binding.Openings[0].ToString(),edit.Thickness,null,binding.Before.Volume,verified.Metrics.Volume,replaced,
                binding.Openings.Select(face=>face.ToString()).ToArray());
            var data=new ModifiedEntities(args.OperationId,"committed",false,[verified.Body],
                replaced?[sourceRef]:[],replaced?[verified.Body]:[],[],"not_guaranteed",model_shell_changes:[change]);
            var result=Result.Ok(job.OriginalRequestId,data,context,WriteRecovery.Warning,
                "NATIVE_SHELL_APPENDED: Inspect the opening and wall geometry through MCP capture_view; refresh history and body reference before dependent edits.");
            result=ModelViewFollowers.WithWarnings(result,binding.Views);job.RecordCommit(result);Trace("commit_recorded");return result;
        }
        catch(Exception error)
        {
            if(job.CommittedResult is {} committed)return committed;
            Exception? cleanup=null;try{Close();}catch(Exception e){cleanup=e;}
            bool unknown=started||cleanup!=null;
            string cause=error is DomainException d?d.Error.code:error is OperationCanceledException?"CANCELLED":"MODEL_SHELL_FAILED";
            return job.Error(unknown?"WRITE_OUTCOME_UNKNOWN":cause,
                unknown?"Native shell outcome is unverified; inspect this body and history before another dependent write.":error.Message,
                unknown?"unknown":"not_started",context,new{cause,stage,message=error.Message,
                    body_mc_id=binding?.Body.ID.ToString(),feature_id=featureId.ToString(),cleanup_error=cleanup?.GetType().Name})
                with{warnings=[WriteRecovery.Warning]};
        }
    }
}
