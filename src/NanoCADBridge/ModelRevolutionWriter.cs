using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.DatabaseServices.Constraints;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class ModelRevolutionWriter
{
    private static void Need([DoesNotReturnIf(false)] bool ok,string code,string message){if(!ok)throw new DomainException(code,message);}
    internal static bool Handles(ModifyArguments args)=>args.Operations.Any(e=>e is ModelRevolutionEdit);
    private sealed record Binding(Mc3dSolid Body,RevolveFeature Feature,McObjectId Variable,ModelParameterArguments SnapshotArgs,JsonElement Before,
        Db.ObjectId Source,string Layer,JsonElement Properties,string NativeDocumentId,HashSet<Db.ObjectId> Inventory,ModelViewFollowers.Snapshot Views);
    private static McDocument Scope(DocumentState state,Db.Transaction tx,string? expectedDocument=null)
    {
        var active=McDocument.ActiveDocument;
        Need(active!=null&&state.Document.IsNamedDrawing&&!string.IsNullOrWhiteSpace(active.Path)&&
            string.Equals(Path.GetFullPath(active.Path),Path.GetFullPath(state.Document.Name),StringComparison.OrdinalIgnoreCase),
            "DOCUMENT_CONTEXT_MISMATCH","Matching named drawing required.");
        Need(expectedDocument==null||active.ID.ToString()==expectedDocument,"DOCUMENT_CONTEXT_MISMATCH","Native document identity changed.");
        var model=((Db.BlockTable)tx.GetObject(state.Document.Database.BlockTableId,Db.OpenMode.ForRead))[Db.BlockTableRecord.ModelSpace];
        Need(state.Document.Database.CurrentSpaceId==model,"OUT_OF_SCOPE","Activate Model Space.");
        return active;
    }
    private static HashSet<Db.ObjectId> Inventory(DocumentState state,Db.Transaction tx,Action cancel)
    {
        var model=((Db.BlockTable)tx.GetObject(state.Document.Database.BlockTableId,Db.OpenMode.ForRead))[Db.BlockTableRecord.ModelSpace];
        var result=new HashSet<Db.ObjectId>();var budget=new ModelInventoryBudget(cancel);
        foreach(Db.ObjectId id in (Db.BlockTableRecord)tx.GetObject(model,Db.OpenMode.ForRead))
        {cancel();if(id.IsErased||id.IsEffectivelyErased)continue;budget.Add(id.Handle.ToString());result.Add(id);}
        return result;
    }
    internal static void Preflight(ModifyArguments args,DocumentState state,Db.Transaction tx,Action? cancel=null)
    {
        var loaded=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Need(loaded.Contains("mapimgd")&&loaded.Contains("MapiBaseTypes"),"CAPABILITY_UNAVAILABLE","MultiCAD must already be loaded.");
        Resolve(args,state,tx,cancel??(()=>{}));
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Binding Resolve(ModifyArguments args,DocumentState state,Db.Transaction tx,Action cancel)
    {
        var edit=(ModelRevolutionEdit)args.Operations.Single();var db=state.Document.Database;var active=Scope(state,tx);
        var model=((Db.BlockTable)tx.GetObject(db.BlockTableId,Db.OpenMode.ForRead))[Db.BlockTableRecord.ModelSpace];
        var id=CadStructureWriter.Find(db,edit.Handles[0]);var solid=tx.GetObject(id,Db.OpenMode.ForRead) as Db.Solid3d;
        Need(solid!=null&&solid.GetType()==typeof(Db.Solid3d)&&solid.OwnerId==model,"OUT_OF_SCOPE","Choose one top-level native model representation.");
        var layer=(Db.LayerTableRecord)tx.GetObject(solid!.LayerId,Db.OpenMode.ForRead);Need(!layer.IsLocked&&!layer.IsFrozen&&!layer.IsOff&&!layer.IsDependent,"LAYER_UNAVAILABLE","Body layer unavailable.");
        var body=McObjectId.FromHandle(id.Handle.Value,active!.ID).GetObjectOfType<Mc3dSolid>();Need(body!=null,"OBJECT_IDENTITY_MISMATCH","Native body unavailable.");
        Need(body!.LastPartItemID.ToString().Equals(edit.ExpectedLastItemId,StringComparison.OrdinalIgnoreCase),"PRECONDITION_FAILED","Refresh last_item_id.");
        var contents=ModelInventoryBudget.History(body,cancel);
        var roots=contents.Select(i=>{cancel();return i.GetObjectOfType<RevolveFeature>();}).Where(f=>f!=null).ToArray();Need(roots.Length==1,"OUT_OF_SCOPE","Select a body with one retained revolution.");var feature=roots[0]!;
        Need(feature.Extent==RevolveParamExtType.Angle&&feature.Direction==FeatureExtentDirection.Positive&&!feature.IsSuppressed&&feature.HostSolidID==body.ID&&DrawingProfileGeometry.Near(feature.Angle2,0),"OUT_OF_SCOPE","Use an existing positive Angle extent, with no second extent; legacy All extent is not converted implicitly.");
        Need(DrawingProfileGeometry.Near(feature.Angle,edit.ExpectedAngleDegrees*Math.PI/180),"PRECONDITION_FAILED","Angle differs from expected_angle_degrees.");
        var views=ModelViewFollowers.Resolve(body,contents.ToArray(),cancel,includeStandalone:true);
        var variable=feature.RotationAngleParamVarID;Need(!variable.IsNull,"CAPABILITY_UNAVAILABLE","Native angle variable must be available for ownership/formula checks.");
        using(var manager=body.DbEntity.Document.GetDocumentVariablesManager(Constraint2dNetType.MCS))
        {
            Need(manager!=null,"CAPABILITY_UNAVAILABLE","MCS variable manager unavailable.");var info=manager!.GetVariableInfo(variable);
            Need(info!=null&&double.TryParse(info.Expression,NumberStyles.Float,CultureInfo.InvariantCulture,out double literal)&&double.IsFinite(literal),"CAPABILITY_UNAVAILABLE","Formula-driven angles require their owning expression adapter.");
            Need(manager.IsAngularValue(variable)&&manager.GetVariableValue(variable,out double value)&&DrawingProfileGeometry.Near(value,edit.ExpectedAngleDegrees)&&double.TryParse(info!.Expression,NumberStyles.Float,CultureInfo.InvariantCulture,out double expressionValue)&&DrawingProfileGeometry.Near(value,expressionValue)&&manager.GetVariableHostObjectId(variable)==feature.ID,"MODEL_STATE_UNAVAILABLE","Native degree variable value/literal/owner disagree with the revolution's radian property.");
            var dependents=manager.GetDependentOnVariables(variable);var objects=manager.GetDependentOn3dObjects(variable);
            Need(dependents!=null&&dependents.Count==0&&objects!=null&&objects.Count>0&&objects.Contains(feature.ID)&&objects.All(consumer=>consumer==feature.ID||ModelViewFollowers.OwnsDependent(views,consumer)),"CAPABILITY_UNAVAILABLE","Shared or formula-driven angle consumers are protected.");
        }
        var snapshotArgs=new ModelParameterArguments(args.BridgeId,args.DocumentId,args.ExpectedRevision,args.OperationId,args.Units,edit.Handles[0],variable.ToString(),"1",1);
        var before=ModelParameterSnapshot.Capture(snapshotArgs,state,cancel);ModelParameterSnapshot.Healthy(before);
        ModelFeatureOracle.Read(solid);
        var inventory=Inventory(state,tx,cancel);var properties=Wire.Element(EntityAdapter.StoredProperties(solid));
        var budget=new ModelInventoryBudget(cancel);budget.Add(before);budget.Add(properties);budget.Add(views.Metadata);
        foreach(var item in inventory)budget.Add(item.Handle.ToString());
        return new(body,feature,variable,snapshotArgs,before,id,solid.Layer,properties,active.ID.ToString(),inventory,views);
    }
    internal static Result Execute(ModifyArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        bool started=false;string stage="preparation";McUndoPoint? tracker=null;
        void Trace(string value){stage=value;log.Write(job.Operation,job.OriginalRequestId,"revolution_angle_checkpoint",transaction:stage,operationId:job.OperationId);}
        void Close(){var t=tracker;tracker=null;if(t==null)return;try{t.Stop();}finally{t.Dispose();}}
        try
        {
            var edit=(ModelRevolutionEdit)args.Operations.Single();Binding b;
            using(var tx=state.Document.Database.TransactionManager.StartTransaction())b=Resolve(args,state,tx,job.CheckCancellation);
            using(var tx=state.Document.Database.TransactionManager.StartTransaction())Scope(state,tx,b.NativeDocumentId);
            job.CheckCancellation();ModelViewFollowers.CheckStandalone(b.Body,b.Views,job.CheckCancellation);tracker=new McUndoPoint(false);tracker.Start();started=true;Trace("degree_expression_before");
            // Once a native angular variable exists, Angle's setter routes its
            // numeric input into the degree expression (195 evidence), whereas
            // Angle's getter remains radians. Write the identified variable in
            // its proven units, then verify both representations independently.
            using(var manager=b.Body.DbEntity.Document.GetDocumentVariablesManager(Constraint2dNetType.MCS))
                Need(manager!=null&&manager.SetVariableExpression(b.Variable,edit.AngleDegrees.ToString("R",CultureInfo.InvariantCulture)),"MODEL_UPDATE_FAILED","Native angle expression rejected.");
            McObjectManager.UpdateAll();Need(b.Body.RebuildPart(),"MODEL_UPDATE_FAILED","Revolution chain rebuild rejected.");McObjectManager.UpdateAll();Trace("rebuild_returned");
            EntityRef Verify()
            {
                job.CheckCancellation();string handle;
                using(var tx=state.Document.Database.TransactionManager.StartTransaction())
                {
                    Scope(state,tx,b.NativeDocumentId);
                    var current=ModelHistoryEditGuard.CurrentRepresentation(b.Body,state,tx,job.CheckCancellation);
                    var inventory=Inventory(state,tx,job.CheckCancellation);
                    Need((current==b.Source||!inventory.Contains(b.Source))&&b.Inventory.Where(id=>id!=b.Source).ToHashSet().SetEquals(inventory.Where(id=>id!=current)),
                        "OBJECT_IDENTITY_MISMATCH","Angle rebuild changed unrelated drawing identities or retained two body representations.");
                    var solid=(Db.Solid3d)tx.GetObject(current,Db.OpenMode.ForRead);
                    Need(solid.Layer==b.Layer&&b.Properties.GetRawText()==Wire.Element(EntityAdapter.StoredProperties(solid)).GetRawText(),
                        "MODEL_STATE_UNAVAILABLE","Angle rebuild changed the body's stored display properties.");
                    ModelFeatureOracle.Read(solid);handle=Validation.NormalizeHandle(current.Handle.ToString());
                }
                var after=ModelParameterSnapshot.Capture(b.SnapshotArgs with{Handle=handle},state,job.CheckCancellation);ModelParameterSnapshot.Healthy(after);
                ModelViewFollowers.Verify(b.Body,b.Views,job.CheckCancellation,refreshStandalone:true);
                foreach(string field in new[]{"external_sources","native_source_ids","owner","body","last_item"})Need(ModelParameterSnapshot.Equivalent(b.Before.GetProperty(field),after.GetProperty(field)),"MODEL_STATE_UNAVAILABLE","Angle edit changed "+field+".");
                Need(ModelSecondaryRevolutionHistory.Sequence(b.Before,b.Views,job.CheckCancellation).SequenceEqual(ModelSecondaryRevolutionHistory.Sequence(after,b.Views,job.CheckCancellation),StringComparer.OrdinalIgnoreCase),"MODEL_STATE_UNAVAILABLE","Angle edit changed feature order.");
                JsonElement Protected(JsonElement snapshot,string field)
                {
                    var array=JsonNode.Parse(snapshot.GetProperty(field).GetRawText())!.AsArray();
                    foreach(var node in array)
                    {
                        job.CheckCancellation();
                        if(field=="variables"&&node!["mc_id"]!.GetValue<string>()==b.Variable.ToString()){node.AsObject().Remove("expression");node.AsObject().Remove("value");}
                        if(field=="nodes"&&node!["mc_id"]!.GetValue<string>()==b.Feature.ID.ToString())
                        {node["parameters"]!.AsObject().Remove("angle");node["parameters"]!.AsObject().Remove("full_turn");}
                    }
                    var value=Wire.Element(array);return field=="nodes"?ModelPlacementHistory.WithoutOrderLinks(value.EnumerateArray().ToArray()):value;
                }
                foreach(string field in new[]{"variables","nodes"})Need(ModelParameterSnapshot.Equivalent(Protected(b.Before,field),Protected(after,field)),"MODEL_STATE_UNAVAILABLE","Unrelated history/variables changed.");
                var variable=ModelParameterSnapshot.Variable(after,b.Variable.ToString());
                Need(variable.GetProperty("value_available").GetBoolean()&&variable.GetProperty("value_unit").GetString()=="degrees"&&variable.GetProperty("value").GetDouble()>0&&DrawingProfileGeometry.Near(variable.GetProperty("value").GetDouble(),edit.AngleDegrees)&&b.Feature.Angle>0&&DrawingProfileGeometry.Near(b.Feature.Angle,edit.AngleDegrees*Math.PI/180),"MODEL_UPDATE_FAILED","Degree variable/radian feature angle did not retain requested positive value.");
                return new(args.BridgeId,args.DocumentId,handle);
            }
            var verified=Verify();job.CheckCancellation();job.BeginCommit();Close();verified=Verify();
            var sourceRef=new EntityRef(args.BridgeId,args.DocumentId,edit.Handles[0]);bool replaced=sourceRef.handle!=verified.handle;
            var result=Result.Ok(job.OriginalRequestId,new ModifiedEntities(args.OperationId,"committed",false,[verified],replaced?[sourceRef]:[],replaced?[verified]:[],[],"not_guaranteed",
                model_revolution_changes:[new(verified,b.Feature.ID.ToString(),b.Variable.ToString(),edit.ExpectedAngleDegrees,edit.AngleDegrees)]),context,WriteRecovery.Warning);
            result=ModelViewFollowers.WithWarnings(result,b.Views);job.RecordCommit(result);return result;
        }
        catch(System.Exception error)
        {
            if(job.CommittedResult is {} committed)return committed;System.Exception? cleanup=null;try{Close();}catch(System.Exception e){cleanup=e;}
            bool unknown=started||cleanup!=null;
            return job.Error(unknown?"WRITE_OUTCOME_UNKNOWN":error is DomainException d?d.Error.code:"MODEL_UPDATE_FAILED",unknown?"Revolution edit cannot be verified. Refresh this model and its angle before another dependent edit; no rollback is claimed.":error.Message,unknown?"unknown":"not_started",context,new{stage,reason=error.Message,cleanup=cleanup?.Message});
        }
    }
}
