using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.DatabaseServices.Constraints;
using Multicad.DatabaseServices.StandardObjects;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Db=Teigha.DatabaseServices;
using G=Multicad.Geometry;

namespace NanoCADBridge;

internal static class ModelPlacementWriter
{
    private static void Need(bool ok,string code,string message){if(!ok)throw new DomainException(code,message);}
    internal static bool Handles(ModifyArguments args)=>args.Operations.Any(e=>e is ModelPlacementEdit);
    private sealed record Binding(Mc3dSolid Body,Db.ObjectId Source,McObjectId[] Contents,ModelParameterArguments SnapshotArgs,JsonElement Before,ModelPlacement? Previous);
    internal static void Preflight(ModifyArguments args,DocumentState state,Db.Transaction tx,Action? cancel=null)
    {
        var loaded=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Need(loaded.Contains("mapimgd")&&loaded.Contains("MapiBaseTypes"),"CAPABILITY_UNAVAILABLE","MultiCAD must already be loaded.");
        Need(args.Operations.Length==1&&args.Operations[0] is ModelPlacementEdit,"INVALID_ARGUMENT","One placement must be the sole operation.");
        Resolve(args,state,tx,cancel);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Binding Resolve(ModifyArguments args,DocumentState state,Db.Transaction tx,Action? cancel=null)
    {
        var edit=(ModelPlacementEdit)args.Operations.Single();var db=state.Document.Database;var model=((Db.BlockTable)tx.GetObject(db.BlockTableId,Db.OpenMode.ForRead))[Db.BlockTableRecord.ModelSpace];
        Need(db.CurrentSpaceId==model,"OUT_OF_SCOPE","Activate Model Space.");var active=McDocument.ActiveDocument;
        Need(active!=null&&state.Document.IsNamedDrawing&&!string.IsNullOrEmpty(active.Path)&&string.Equals(Path.GetFullPath(active.Path),Path.GetFullPath(state.Document.Name),StringComparison.OrdinalIgnoreCase),"DOCUMENT_CONTEXT_MISMATCH","Use the matching named native document.");
        var source=CadStructureWriter.Find(db,edit.Handles[0]);var solid=tx.GetObject(source,Db.OpenMode.ForRead) as Db.Solid3d;
        Need(solid!=null&&solid.GetType()==typeof(Db.Solid3d)&&solid.OwnerId==model,"OUT_OF_SCOPE","Select an exact top-level native model representation.");
        var layer=(Db.LayerTableRecord)tx.GetObject(solid!.LayerId,Db.OpenMode.ForRead);Need(!layer.IsLocked&&!layer.IsFrozen&&!layer.IsOff&&!layer.IsDependent,"LAYER_UNAVAILABLE","Body layer unavailable.");
        var id=McObjectId.FromHandle(source.Handle.Value,active!.ID);var body=id.GetObjectOfType<Mc3dSolid>();Need(body!=null&&body.ID==id,"OBJECT_IDENTITY_MISMATCH","Native model identity unavailable.");
        Need(body!.LastPartItemID.ToString().Equals(edit.ExpectedLastItemId,StringComparison.OrdinalIgnoreCase),"PRECONDITION_FAILED","Refresh current last_item_id.");
        var contents=ModelInventoryBudget.History(body,cancel);Need(contents.Count>0,"HISTORY_UNAVAILABLE","Native history is empty.");var ids=contents.ToArray();
        var external=ProjectedHistorySources.Read(body,ids,state,tx,cancel);ProjectedHistorySources.Healthy(ids,external);
        var placements=ModelPlacementHistory.Ids(ids);bool change=edit.ExpectedPlacement!=null;
        var externalIds=external.Select(s=>s.id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Need(ids.All(i=>i==body.ID||externalIds.Contains(i.ToString())||i.GetObjectOfType<PlanarSketch>()!=null||i.GetObjectOfType<SketchProfile>()!=null||i.GetObjectOfType<ProjectionSketchEnt>()!=null||i.GetObjectOfType<ExtrudeFeature>()!=null||i.GetObjectOfType<RevolveFeature>()!=null||ModelPlacementHistory.IsPlacement(i)),"OUT_OF_SCOPE","Placement currently covers sketch-based extrusion/revolution histories; other feature kinds require their own rebuild verification.");
        ModelPlacement? previous=null;
        foreach(var p in placements){cancel?.Invoke();ModelPlacementHistory.Read(p);}
        if(change)
        {
            Need(placements.Contains(body.LastPartItemID),"OUT_OF_SCOPE","Only the final associative placement can be edited; append for an additional transform.");
            previous=ModelPlacementHistory.Read(body.LastPartItemID);Need(ModelPlacementHistory.Same(previous,edit.ExpectedPlacement!),"PRECONDITION_FAILED","Expected final placement changed.");
            using var manager=body.DbEntity.Document.GetDocumentVariablesManager(Constraint2dNetType.MCS);Need(manager!=null,"CAPABILITY_UNAVAILABLE","Variable manager unavailable.");
            foreach(var variable in ModelPlacementHistory.Variables(body.LastPartItemID))
            {
                var info=manager!.GetVariableInfo(variable);var dependents=manager.GetDependentOnVariables(variable);var consumers=manager.GetDependentOn3dObjects(variable);
                Need(!variable.IsNull&&info!=null&&double.TryParse(info.Expression,NumberStyles.Float,CultureInfo.InvariantCulture,out var value)&&double.IsFinite(value)&&dependents!=null&&dependents.Count==0&&consumers!=null&&consumers.All(c=>c==body.LastPartItemID),"OUT_OF_SCOPE","Placement parameters driven by formulas or shared consumers are protected.");
            }
        }
        var snapshotArgs=new ModelParameterArguments(args.BridgeId,args.DocumentId,args.ExpectedRevision,args.OperationId,args.Units,edit.Handles[0],"","",0);
        var before=ModelParameterSnapshot.Capture(snapshotArgs,state,cancel);ModelParameterSnapshot.Healthy(before);ModelPlacementHistory.Sequence(before,cancel);return new(body,source,ids,snapshotArgs,before,previous);
    }
    internal static Result Execute(ModifyArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        McUndoPoint? tracker=null;bool started=false;string stage="preparation";McObjectId featureId=McObjectId.Null,anchorId=McObjectId.Null;
        void Trace(string s){stage=s;log.Write(job.Operation,job.OriginalRequestId,"model_placement_checkpoint",transaction:s,operationId:job.OperationId);}
        void Close(){var t=tracker;tracker=null;if(t==null)return;try{t.Stop();}finally{t.Dispose();}}
        try
        {
            var edit=(ModelPlacementEdit)args.Operations.Single();Binding b;string layer;
            using(var tx=state.Document.Database.TransactionManager.StartTransaction())
            {
                b=Resolve(args,state,tx,job.CheckCancellation);var source=(Db.Solid3d)tx.GetObject(b.Source,Db.OpenMode.ForRead);layer=source.Layer;
                ModelFeatureOracle.Read(source);
            }
            Trace("source_verified");job.CheckCancellation();tracker=new McUndoPoint(false);tracker.Start();var v=edit.Placement.Vector;
            if(b.Previous==null)
            {
                started=true;Trace("append_before");HistoryItem feature;
                if(edit.Placement.Kind=="move")feature=b.Body.addAssocMoveFeature(new G.Vector3d(v.X,v.Y,v.Z),false);
                else
                {
                    var c=edit.Placement.Center!;var point=new WorkPoint();
                    McObjectManager.Add2Document(point.DbObject);anchorId=point.ID;
                    Need(point.SetFixedDefinition(new G.Point3d(c.X,c.Y,c.Z)),"MODEL_UPDATE_FAILED","Fixed WorkPoint definition rejected.");
                    // McGeomParam.Init requires an object visible in the viewport
                    // (SDK Init documentation). Bind before hiding the anchor.
                    point.DbEntity.Layer=layer;point.DbEntity.Visibility=1;point.DbEntity.Update();McObjectManager.UpdateAll();
                    var param=new McGeomParam();bool snapped=param.Init(point.ID,point.Position,McObjectId.Null,GeomParamCoordSys.ModelSpace);
                    // A fixed WorkPoint has one point. The public ID-reference form
                    // also used by SDK vertex examples needs no viewport snapping.
                    // Accept it only if the SDK resolves the exact requested point.
                    if(!snapped)param=new McGeomParam{ID=point.ID};
                    var resolved=G.Point3d.Origin;
                    Need(param.CalculatePoint(ref resolved)&&Math.Abs(resolved.X-c.X)<1e-7&&Math.Abs(resolved.Y-c.Y)<1e-7&&Math.Abs(resolved.Z-c.Z)<1e-7,"MODEL_UPDATE_FAILED","Explicit WorkPoint reference did not resolve to the requested center.");
                    Trace(snapped?"anchor_snap_verified":"anchor_identity_point_verified");
                    point.DbEntity.Visibility=0;point.DbEntity.Update();
                    var rotation=b.Body.addAssocRotateFeature(param);Need(rotation!=null,"MODEL_UPDATE_FAILED","Native rotation creation failed.");
                    rotation!.RotateX=v.X;rotation.RotateY=v.Y;rotation.RotateZ=v.Z;feature=rotation;
                }
                Need(feature!=null,"MODEL_UPDATE_FAILED","Native placement creation failed.");featureId=feature!.ID;
                var previous=b.Contents.Single(i=>i.ToString().Equals(edit.ExpectedLastItemId,StringComparison.OrdinalIgnoreCase));
                if(feature.OrderObjBefore!=previous)Need(feature.SetOrderObjBefore(previous,false),"MODEL_UPDATE_FAILED","Placement predecessor was rejected.");
                Need(feature.OrderObjBefore==previous,"MODEL_UPDATE_FAILED","Placement order was not retained.");
            }
            else
            {
                featureId=b.Body.LastPartItemID;started=true;Trace("parameter_set_before");
                if(edit.Placement.Kind=="move")featureId.GetObjectOfType<AssocMoveFeature>()!.AssocOffset=new(v.X,v.Y,v.Z);
                else{var rotation=featureId.GetObjectOfType<AssocRotateFeature>()!;rotation.RotateX=v.X;rotation.RotateY=v.Y;rotation.RotateZ=v.Z;}
            }
            Trace("update_before");McObjectManager.UpdateAll();Trace("update_returned");
            void Verify()
            {
                job.CheckCancellation();Need(b.Body.LastPartItemID==featureId&&featureId.GetObjectOfType<HistoryItem>()!.HostSolidID==b.Body.ID,"MODEL_STATE_UNAVAILABLE","Placement identity or ownership changed.");
                Need(ModelPlacementHistory.Same(ModelPlacementHistory.Read(featureId),edit.Placement),"MODEL_UPDATE_FAILED","Placement parameters or fixed pivot differ from request.");
                var after=ModelParameterSnapshot.Capture(b.SnapshotArgs,state,job.CheckCancellation);ModelParameterSnapshot.Healthy(after);var variables=ModelPlacementHistory.Variables(featureId).Select(i=>i.ToString()).ToHashSet();
                Need(variables.Count==3&&!ModelPlacementHistory.Variables(featureId).Any(i=>i.IsNull),"MODEL_STATE_UNAVAILABLE","Three native placement variables required.");
                JsonElement Filter(JsonElement s,string field,Func<JsonElement,bool> keep)=>Wire.Element(s.GetProperty(field).EnumerateArray().Where(keep).ToArray());
                foreach(string field in new[]{"owner","body"})Need(ModelParameterSnapshot.Equivalent(b.Before.GetProperty(field),after.GetProperty(field)),"MODEL_STATE_UNAVAILABLE","Placement changed "+field+".");
                Need(ModelParameterSnapshot.Equivalent(Filter(b.Before,"variables",e=>!variables.Contains(e.GetProperty("mc_id").GetString()!)),Filter(after,"variables",e=>!variables.Contains(e.GetProperty("mc_id").GetString()!))),"MODEL_STATE_UNAVAILABLE","Unrelated parameter values/expressions changed.");
                var anchor=anchorId.IsNull?null:anchorId.ToString();
                Need(ModelParameterSnapshot.Equivalent(b.Before.GetProperty("external_sources"),Filter(after,"external_sources",e=>e.GetProperty("id").GetString()!=anchor)),"MODEL_STATE_UNAVAILABLE","Prior source geometry or bindings changed.");
                var priorNodes=b.Before.GetProperty("nodes").EnumerateArray().Where(n=>n.GetProperty("mc_id").GetString()!=featureId.ToString()&&n.GetProperty("mc_id").GetString()!=b.Body.ID.ToString()).ToArray();
                var laterNodes=after.GetProperty("nodes").EnumerateArray().Where(n=>n.GetProperty("mc_id").GetString()!=featureId.ToString()&&n.GetProperty("mc_id").GetString()!=anchor&&n.GetProperty("mc_id").GetString()!=b.Body.ID.ToString()).ToArray();
                // nanoCAD may insert a retained WorkPoint into the predecessor
                // links during later rebuilds. Preserve the actual feature order,
                // while allowing only these semantically equivalent raw links.
                var sequence=ModelPlacementHistory.Sequence(b.Before);
                var expectedOrder=b.Previous==null?sequence.Append(featureId.ToString()).ToArray():sequence;
                Need(expectedOrder.SequenceEqual(ModelPlacementHistory.Sequence(after),StringComparer.OrdinalIgnoreCase),"MODEL_STATE_UNAVAILABLE","Placement changed the ordered feature chain.");
                Need(ModelParameterSnapshot.Equivalent(ModelPlacementHistory.WithoutOrderLinks(priorNodes),ModelPlacementHistory.WithoutOrderLinks(laterNodes)),"MODEL_STATE_UNAVAILABLE","Placement changed earlier history parameters, dependencies, ownership or status.");
                using var anchorTx=state.Document.Database.TransactionManager.StartTransaction();
                var anchorHandle=anchorId.IsNull?null:ModelPlacementHistory.SourceId(state,anchorTx,anchorId).Handle.ToString();
                Need(ModelParameterSnapshot.Equivalent(b.Before.GetProperty("handles"),Filter(after,"handles",e=>e.GetString()!=anchorHandle)),"MODEL_STATE_UNAVAILABLE","Unexpected native entity inventory change.");
                using var tx=state.Document.Database.TransactionManager.StartTransaction();var solid=(Db.Solid3d)tx.GetObject(b.Source,Db.OpenMode.ForRead);var actual=ModelFeatureOracle.Read(solid);
                Need(solid.Layer==layer,"MODEL_STATE_UNAVAILABLE","Native placement changed the model layer.");
            }
            Verify();Trace("placement_and_history_verified");job.BeginCommit();Close();Verify();var reference=new EntityRef(args.BridgeId,args.DocumentId,edit.Handles[0]);
            using var anchorRead=state.Document.Database.TransactionManager.StartTransaction();
            var anchorRef=anchorId.IsNull?null:new EntityRef(args.BridgeId,args.DocumentId,ModelPlacementHistory.SourceId(state,anchorRead,anchorId).Handle.ToString());
            var result=Result.Ok(job.OriginalRequestId,new ModifiedEntities(args.OperationId,"committed",false,[reference],[],anchorRef==null?[]:[anchorRef],[],"not_guaranteed",model_placement_changes:[new(reference,featureId.ToString(),edit.Placement.Kind,ModelPlacementHistory.Variables(featureId).Select(i=>i.ToString()).ToArray(),anchorRef)]),context,WriteRecovery.Warning,"MODEL_PLACEMENT_CHANGED: Native history retained. Rotation uses a hidden fixed WorkPoint anchor; keep it with the model. Source sketches stay in their original frame. Refresh history/topology/views and inspect the placed group visually.");
            job.RecordCommit(result);return result;
        }
        catch(Exception error)
        {
            if(job.CommittedResult is {} committed)return committed;Exception? cleanup=null;try{Close();}catch(Exception e){cleanup=e;}
            bool unknown=started||cleanup!=null;string code=error is DomainException d?d.Error.code:error is OperationCanceledException?"CANCELLED":"MODEL_PLACEMENT_FAILED";
            return job.Error(unknown?"WRITE_OUTCOME_UNKNOWN":code,unknown?"Placement result is unverified; inspect this model and refresh its history before another dependent edit.":"Placement did not start.",unknown?"unknown":"not_started",context,new{cause=code,stage,message=error.Message,verification_details=(error as DomainException)?.Error.details,feature_id=featureId.ToString(),anchor_id=anchorId.ToString(),cleanup_error=cleanup?.Message}) with{warnings=[WriteRecovery.Warning]};
        }
    }
}
