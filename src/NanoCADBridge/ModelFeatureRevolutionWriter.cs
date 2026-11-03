using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// Append into the existing native part; source drawing geometry is projected,
// never consumed or recreated as a detached replacement for parametric history.
internal static class ModelFeatureRevolutionWriter
{
    internal static bool Handles(ModifyArguments args)=>args.Operations.Any(e=>e is ModelFeatureRevolutionSpec);
    private static void Need([DoesNotReturnIf(false)] bool ok,string code,string message)
    {if(!ok)throw new DomainException(code,message);}
    private sealed record ProtectedSource(DrawingProfileWriter.Source Source,JsonElement Geometry,JsonElement Properties);
    private sealed record Binding(Mc3dSolid Body,Db.ObjectId Source,string Layer,McObjectId[] Contents,JsonElement Before,
        ModelExtrusionSpec ProfileDefinition,ProtectedSource[] ProfileSources,ProtectedSource Axis,
        ProjectedHistorySources.Source[] ExternalSources,ModelViewFollowers.Snapshot Views,string HostDocumentId,
        HashSet<Db.ObjectId> Inventory,ModelFeatureOracle.Metrics Metrics,JsonElement Properties);
    private static Db.ObjectId Model(DocumentState state,Db.Transaction tx)
        =>((Db.BlockTable)tx.GetObject(state.Document.Database.BlockTableId,Db.OpenMode.ForRead))[Db.BlockTableRecord.ModelSpace];
    private static McDocument Scope(DocumentState state,Db.Transaction tx,string? expectedDocument=null)
    {
        var active=McDocument.ActiveDocument;
        Need(state.Document.IsNamedDrawing&&active!=null&&!string.IsNullOrWhiteSpace(active.Path)&&
            string.Equals(Path.GetFullPath(active.Path),Path.GetFullPath(state.Document.Name),StringComparison.OrdinalIgnoreCase),
            "DOCUMENT_CONTEXT_MISMATCH","Native revolution append requires the matching named DWG.");
        Need(expectedDocument==null||active.ID.ToString()==expectedDocument,"DOCUMENT_CONTEXT_MISMATCH","Native document identity changed.");
        Need(state.Document.Database.CurrentSpaceId==Model(state,tx),"OUT_OF_SCOPE","Activate Model Space before native revolution append.");
        return active;
    }
    private static HashSet<Db.ObjectId> Inventory(DocumentState state,Db.Transaction tx,Action cancel)
    {
        var result=new HashSet<Db.ObjectId>();var budget=new ModelInventoryBudget(cancel);
        foreach(Db.ObjectId id in (Db.BlockTableRecord)tx.GetObject(Model(state,tx),Db.OpenMode.ForRead))
        {cancel();if(id.IsErased||id.IsEffectivelyErased)continue;budget.Add(id.Handle.ToString());result.Add(id);}
        return result;
    }
    private static ProtectedSource Source(string handle,SketchPlaneSpec plane,DocumentState state,Db.Transaction tx,Action cancel)
    {
        cancel();var id=CadStructureWriter.Find(state.Document.Database,handle);
        var source=DrawingProfileWriter.SourceOf(id,state,tx,true,plane);
        var entity=(Db.Entity)tx.GetObject(id,Db.OpenMode.ForRead);
        var summary=EntityAdapter.Summary(entity,Model(state,tx),"","",false);
        var detail=Wire.Element(EntityAdapter.Detail(entity,summary,CancellationToken.None));
        var result=new ProtectedSource(source,detail.GetProperty("geometry").Clone(),detail.GetProperty("properties").Clone());
        new ModelInventoryBudget(cancel).Add(new{source.Curve,source.Layer,result.Geometry,result.Properties});return result;
    }
    private static JsonElement Snapshot(ModifyArguments args,DocumentState state,string handle,Action cancel)
    {
        var result=ModelParameterSnapshot.Capture(new(args.BridgeId,args.DocumentId,args.ExpectedRevision,args.OperationId,args.Units,handle,"","",0),state,cancel);
        ModelParameterSnapshot.Healthy(result);return result;
    }
    internal static void Preflight(ModifyArguments args,DocumentState state,Db.Transaction tx,Action? cancel=null)
    {
        Resolve(args,state,tx,cancel??(()=>{}));
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Binding Resolve(ModifyArguments args,DocumentState state,Db.Transaction tx,Action cancel)
    {
        Need(args.Operations.Length==1&&args.Operations[0] is ModelFeatureRevolutionSpec,"INVALID_ARGUMENT","Native revolution append must be the sole operation.");
        var edit=(ModelFeatureRevolutionSpec)args.Operations[0];cancel();
        var loaded=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Need(loaded.Contains("mapimgd")&&loaded.Contains("MapiBaseTypes"),"CAPABILITY_UNAVAILABLE","MultiCAD must already be loaded.");
        var active=Scope(state,tx);var source=CadStructureWriter.Find(state.Document.Database,edit.Handles.Single());
        var solid=tx.GetObject(source,Db.OpenMode.ForRead) as Db.Solid3d;
        Need(solid!=null&&solid.GetType()==typeof(Db.Solid3d)&&solid.OwnerId==Model(state,tx)&&!solid.IsNull,
            "OUT_OF_SCOPE","Use a nonempty top-level native body representation in Model Space.");
        var layer=(Db.LayerTableRecord)tx.GetObject(solid.LayerId,Db.OpenMode.ForRead);
        Need(!layer.IsLocked&&!layer.IsOff&&!layer.IsFrozen&&!layer.IsDependent,"LAYER_UNAVAILABLE","Body layer is unavailable for editing.");
        var bodyId=McObjectId.FromHandle(source.Handle.Value,active.ID);var body=bodyId.GetObjectOfType<Mc3dSolid>();
        Need(body!=null&&body.ID==bodyId,"OBJECT_IDENTITY_MISMATCH","Handle does not identify this document's native part.");
        Need(body.LastPartItemID.ToString().Equals(edit.ExpectedLastItemId,StringComparison.OrdinalIgnoreCase),"PRECONDITION_FAILED","Read the current last_item_id before appending.");
        var contents=ModelInventoryBudget.History(body,cancel);Need(contents.Count>0,"HISTORY_UNAVAILABLE","A retained existing part history is required.");
        var views=ModelViewFollowers.Resolve(body,contents.ToArray(),cancel,includeStandalone:true);ModelViewFollowers.PrepareInsertion(views,true);
        var external=ProjectedHistorySources.Read(body,contents.ToArray(),state,tx,cancel);ProjectedHistorySources.Healthy(contents.ToArray(),external);
        var profileSources=new List<ProtectedSource>();var budget=new ModelInventoryBudget(cancel);
        foreach(string handle in edit.SourceHandles)
        {var item=Source(handle,edit.Plane,state,tx,cancel);budget.Add(new{item.Source.Curve,item.Source.Layer,item.Geometry,item.Properties});profileSources.Add(item);}
        var axis=Source(edit.AxisHandle,edit.Plane,state,tx,cancel);
        Need(axis.Source.Curve.Kind=="line"&&profileSources.All(s=>s.Source.Native!=axis.Source.Native),"INVALID_ARGUMENT","Use a separate exact native Line axis.");
        Need(profileSources.Select(s=>s.Source.Native).Distinct().Count()==profileSources.Count,"OBJECT_IDENTITY_MISMATCH","Profile sources must have distinct native identities.");
        var curves=profileSources.Select(s=>s.Source.Curve).ToArray();
        var profile=curves.Any(c=>c.Kind=="arc")?ModelPolycurve.FromCurves(curves,cancel):DrawingProfileGeometry.Profile(curves,cancel);
        var definition=new ModelExtrusionSpec(edit.Plane,profile,1,"positive",edit.Name,solid.Layer);
        // This validates the closed planar contour. It does not reconstruct or
        // compare the body's future revolved/boolean mass or bound box.
        ModelExtrusionGeometry.Measure(definition,cancel);
        var before=Snapshot(args,state,edit.Handles[0],cancel);var inventory=Inventory(state,tx,cancel);
        // Bound the combined retained verification data too, not merely each
        // selected source or the individual existing-history snapshot.
        var properties=Wire.Element(EntityAdapter.StoredProperties(solid));
        budget.Add(before);budget.Add(definition);budget.Add(new{axis.Source.Curve,axis.Source.Layer,axis.Geometry,axis.Properties});budget.Add(views.Metadata);budget.Add(properties);
        foreach(var id in contents)budget.Add(id.ToString());
        foreach(var id in inventory)budget.Add(id.Handle.ToString());
        return new(body,source,solid.Layer,contents.ToArray(),before,definition,
            profileSources.ToArray(),axis,external,views,active.ID.ToString(),inventory,ModelFeatureOracle.Read(solid),properties);
    }
    private static void VerifySources(Binding before,DocumentState state,Db.Transaction tx,Action cancel)
    {
        foreach(var old in before.ProfileSources.Append(before.Axis))
        {
            var now=Source(Validation.NormalizeHandle(old.Source.Id.Handle.ToString()),old.Source.Workplane,state,tx,cancel);
            Need(now.Source.Id==old.Source.Id&&now.Source.Native==old.Source.Native&&now.Source.Layer==old.Source.Layer,
                "OBJECT_IDENTITY_MISMATCH","Retained profile or axis identity/owner/layer changed.");
            Need(ModelParameterSnapshot.Equivalent(old.Geometry,now.Geometry)&&old.Properties.GetRawText()==now.Properties.GetRawText(),
                "MODEL_STATE_UNAVAILABLE","Native append changed retained source geometry or stored display properties.");
        }
    }
    private static JsonElement RetainedNodes(JsonElement snapshot,IReadOnlySet<string> original,string body,
        IReadOnlySet<string> projections,string feature,string? firstView,Action cancel)
    {
        var kept=new List<JsonNode>();var budget=new ModelInventoryBudget(cancel);
        foreach(var value in snapshot.GetProperty("nodes").EnumerateArray())
        {
            cancel();string id=value.GetProperty("mc_id").GetString()!;
            if(id==body||!original.Contains(id))continue;
            var node=JsonNode.Parse(value.GetRawText())!.AsObject();
            // Only the first terminal view receives the new predecessor.
            if(id==firstView)node["native_order_before_id"]=null;
            if(node["parameters"] is JsonObject parameters)
            {
                if(parameters["edge_ids"] is JsonArray edges){parameters["edge_count"]=edges.Count;parameters.Remove("edge_ids");}
                foreach(string field in new[]{"projection_ids","revolution_axis_ids"})
                    if(parameters[field] is JsonArray refs)
                        parameters[field]=new JsonArray(refs.Where(n=>n!=null&&n.GetValue<string>()!=feature&&!projections.Contains(n.GetValue<string>()))
                            .Select(n=>(JsonNode?)JsonValue.Create(n!.GetValue<string>())).ToArray());
            }
            budget.Add(Wire.Element(node));kept.Add(node);
        }
        return Wire.Element(kept);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Result Execute(ModifyArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        Binding? binding=null;ModelRevolutionFactory.Items? created=null;McUndoPoint? tracker=null;bool started=false;string stage="preparation";
        void Trace(string point){stage=point;log.Write(job.Operation,job.OriginalRequestId,"model_revolution_append_checkpoint",transaction:point,operationId:job.OperationId);}
        void Close(){var value=tracker;tracker=null;if(value==null)return;try{value.Stop();}finally{value.Dispose();}}
        try
        {
            var edit=(ModelFeatureRevolutionSpec)args.Operations.Single();
            using(var tx=state.Document.Database.TransactionManager.StartTransaction())binding=Resolve(args,state,tx,job.CheckCancellation);
            using(var tx=state.Document.Database.TransactionManager.StartTransaction())
            {Scope(state,tx,binding.HostDocumentId);VerifySources(binding,state,tx,job.CheckCancellation);}
            job.CheckCancellation();ModelViewFollowers.CheckStandalone(binding.Body,binding.Views,job.CheckCancellation);tracker=new McUndoPoint(false);tracker.Start();started=true;Trace("append_before");
            var anchor=binding.Views.LastModelItem;ModelViewFollowers.PrepareInsertion(binding.Views,false);
            created=ModelRevolutionFactory.Append(binding.Body,binding.ProfileDefinition,binding.ProfileSources.Select(s=>s.Source.Native).ToArray(),
                binding.Axis.Source.Native,edit.AngleDegrees,edit.PartOperation,job.CheckCancellation,Trace);
            if(created.Feature.OrderObjBefore!=anchor)
                Need(created.Feature.SetOrderObjBefore(anchor,false),"MODEL_UPDATE_FAILED","Native revolution predecessor was rejected.");
            Need(created.Feature.OrderObjBefore==anchor,"MODEL_UPDATE_FAILED","Native revolution did not retain its requested predecessor.");
            ModelViewFollowers.CompleteInsertion(binding.Views,created.Feature.ID);
            Trace("update_before");McObjectManager.UpdateAll();Trace("update_returned");
            (EntityRef Body,ModelFeatureOracle.Metrics Metrics,McObjectId Variable) Verify()
            {
                job.CheckCancellation();using var tx=state.Document.Database.TransactionManager.StartTransaction();
                Scope(state,tx,binding.HostDocumentId);
                var current=ModelHistoryEditGuard.CurrentRepresentation(binding.Body,state,tx,job.CheckCancellation);
                var afterInventory=Inventory(state,tx,job.CheckCancellation);
                Need(binding.Inventory.Except(afterInventory).All(id=>id==binding.Source),"MODEL_STATE_UNAVAILABLE","Native append removed unrelated top-level drawing objects.");
                Need(current==binding.Source||!afterInventory.Contains(binding.Source),"OBJECT_IDENTITY_MISMATCH","Previous and replacement body representations coexist.");
                var solid=(Db.Solid3d)tx.GetObject(current,Db.OpenMode.ForRead);Need(solid.Layer==binding.Layer,"MODEL_STATE_UNAVAILABLE","Body layer changed.");
                Need(binding.Properties.GetRawText()==Wire.Element(EntityAdapter.StoredProperties(solid)).GetRawText(),"MODEL_STATE_UNAVAILABLE","Body stored display properties changed.");
                VerifySources(binding,state,tx,job.CheckCancellation);
                var contents=ModelInventoryBudget.History(binding.Body,job.CheckCancellation);
                Need(binding.Contents.All(contents.Contains)&&contents.Contains(created.Sketch.ID)&&contents.Contains(created.Profile.ID)&&contents.Contains(created.Feature.ID),
                    "MODEL_STATE_UNAVAILABLE","Original or appended native history items are missing.");
                Need(created.Sketch.HostSolidID==binding.Body.ID&&!created.Sketch.IsInEditMode&&created.Profile.HostSolidID==binding.Body.ID&&
                    created.Feature.HostSolidID==binding.Body.ID&&created.Feature.ProfileID==created.Profile.ID&&created.Feature.Axis.ID==binding.Axis.Source.Native,
                    "MODEL_STATE_UNAVAILABLE","Appended sketch/profile/feature or native axis has another owner/binding.");
                DrawingWorkplaneNative.Match(created.Sketch,edit.Plane);
                var dependencies=created.Profile.GetDependsOn(false,false);Need(created.Profile.ParentSketchID==created.Sketch.ID&&dependencies!=null&&dependencies.Contains(created.Sketch.ID),"MODEL_STATE_UNAVAILABLE","Profile lost its owning sketch dependency.");
                Need(created.Feature.OrderObjBefore==anchor&&created.Feature.Status.ToString()=="Ok"&&!created.Feature.IsSuppressed&&
                    created.Feature.PartOperation==(edit.PartOperation=="cut"?PartFeatureOperation.Cut:PartFeatureOperation.Join)&&
                    created.Feature.Extent==RevolveParamExtType.Angle&&created.Feature.Direction==FeatureExtentDirection.Positive&&
                    created.Feature.Angle>0&&ModelParameterSnapshot.Near(created.Feature.Angle,edit.AngleDegrees*Math.PI/180)&&ModelParameterSnapshot.Near(created.Feature.Angle2,0),
                    "MODEL_STATE_UNAVAILABLE","Revolution operation, extent, angle, status or predecessor differs from the request.");
                Need(binding.Body.LastPartItemID==(binding.Views.ViewIds.Length==0?created.Feature.ID:binding.Views.LastHistoryItem),"MODEL_STATE_UNAVAILABLE","End-of-part changed unexpectedly.");
                if(binding.Views.HasFollowers)ModelViewFollowers.VerifyAfterInsertion(binding.Body,binding.Views,created.Feature.ID,job.CheckCancellation,refreshStandalone:true);
                var projections=new List<ProjectionSketchEnt>();
                foreach(var id in contents){job.CheckCancellation();var p=id.GetObjectOfType<ProjectionSketchEnt>();if(p!=null&&p.HostSketchID==created.Sketch.ID)projections.Add(p);}
                var selected=binding.ProfileSources.Select(s=>s.Source.Native).ToHashSet();
                Need(projections.Count==selected.Count&&projections.Select(p=>p.SrcObjectID).ToHashSet().SetEquals(selected),"MODEL_STATE_UNAVAILABLE","Every profile source must remain projected exactly once in the appended sketch.");
                var addedProjections=projections.Select(p=>p.ID.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var addedIds=projections.Select(p=>p.ID).Concat(projections.Select(p=>p.PlanarObjectID)).Concat(selected)
                    .Append(binding.Axis.Source.Native).Append(created.Sketch.ID).Append(created.Profile.ID).Append(created.Feature.ID).ToHashSet();
                Need(contents.Count==contents.Distinct().Count()&&contents.Except(binding.Contents).All(addedIds.Contains),
                    "MODEL_STATE_UNAVAILABLE","Native revolution append introduced an unrelated history item.");
                Need(afterInventory.Except(binding.Inventory).All(id=>id==current||addedIds.Contains(McObjectId.FromHandle(id.Handle.Value,McDocument.ActiveDocument.ID))),
                    "MODEL_STATE_UNAVAILABLE","Native revolution append introduced an unrelated top-level drawing object.");
                var external=ProjectedHistorySources.Read(binding.Body,contents.ToArray(),state,tx,job.CheckCancellation);ProjectedHistorySources.Healthy(contents.ToArray(),external);
                var priorIds=binding.ExternalSources.Select(s=>s.id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                Need(external.Where(s=>!priorIds.Contains(s.id)).All(s=>selected.Any(id=>id.ToString().Equals(s.id,StringComparison.OrdinalIgnoreCase))||s.id==binding.Axis.Source.Native.ToString()),
                    "MODEL_STATE_UNAVAILABLE","Native append introduced an unrelated external source binding.");
                var retainedExternal=external.Where(s=>priorIds.Contains(s.id)).Select(s=>s with{
                    projection_ids=s.projection_ids.Where(id=>!addedProjections.Contains(id)).ToArray(),
                    revolution_axis_ids=s.revolution_axis_ids.Where(id=>id!=created.Feature.ID.ToString()).ToArray()}).ToArray();
                Need(ModelParameterSnapshot.Equivalent(Wire.Element(binding.ExternalSources),Wire.Element(retainedExternal)),"MODEL_STATE_UNAVAILABLE","Prior external sources or bindings changed.");
                Need(external.Single(s=>s.id==binding.Axis.Source.Native.ToString()).revolution_axis_ids.Contains(created.Feature.ID.ToString()),"MODEL_STATE_UNAVAILABLE","The real native axis dependency was not retained.");
                var handle=Validation.NormalizeHandle(current.Handle.ToString());var snapshot=Snapshot(args,state,handle,job.CheckCancellation);
                Need(ModelParameterSnapshot.Equivalent(binding.Before.GetProperty("owner"),snapshot.GetProperty("owner"))&&
                    ModelParameterSnapshot.Equivalent(binding.Before.GetProperty("body"),snapshot.GetProperty("body")),"OBJECT_IDENTITY_MISMATCH","Native part/document identity changed.");
                var original=binding.Contents.Select(id=>id.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                string? firstView=binding.Views.ViewIds.Length==0?null:binding.Views.Metadata.EnumerateArray()
                    .Single(n=>n.GetProperty("previous").GetString()==anchor.ToString()).GetProperty("id").GetString();
                Need(ModelParameterSnapshot.Equivalent(RetainedNodes(binding.Before,original,binding.Body.ID.ToString(),addedProjections,created.Feature.ID.ToString(),firstView,job.CheckCancellation),
                    RetainedNodes(snapshot,original,binding.Body.ID.ToString(),addedProjections,created.Feature.ID.ToString(),firstView,job.CheckCancellation)),
                    "MODEL_STATE_UNAVAILABLE","Native append changed earlier feature parameters, dependencies, status or ownership.");
                var oldVariables=binding.Before.GetProperty("variables").EnumerateArray().ToDictionary(v=>v.GetProperty("mc_id").GetString()!,StringComparer.OrdinalIgnoreCase);
                var afterVariables=snapshot.GetProperty("variables").EnumerateArray().ToArray();
                Need(ModelParameterSnapshot.Equivalent(Wire.Element(oldVariables.Values),Wire.Element(afterVariables.Where(v=>oldVariables.ContainsKey(v.GetProperty("mc_id").GetString()!)).ToArray())),
                    "MODEL_STATE_UNAVAILABLE","Original parameter identities, formulas or values changed.");
                var owners=new HashSet<string>(StringComparer.OrdinalIgnoreCase){created.Sketch.ID.ToString(),created.Profile.ID.ToString(),created.Feature.ID.ToString()};
                Need(afterVariables.Where(v=>!oldVariables.ContainsKey(v.GetProperty("mc_id").GetString()!)).All(v=>owners.Contains(v.GetProperty("host_object_id").GetString()??"")),
                    "MODEL_STATE_UNAVAILABLE","An unrelated new native variable appeared.");
                var variable=created.Feature.RotationAngleParamVarID;Need(!variable.IsNull,"MODEL_STATE_UNAVAILABLE","Native revolution angle variable is unavailable.");
                var observed=ModelParameterSnapshot.Variable(snapshot,variable.ToString());
                Need(observed.GetProperty("host_object_id").GetString()==created.Feature.ID.ToString()&&observed.GetProperty("value_available").GetBoolean()&&
                    observed.GetProperty("value_unit").GetString()=="degrees"&&observed.GetProperty("value").GetDouble()>0&&ModelParameterSnapshot.Near(observed.GetProperty("value").GetDouble(),edit.AngleDegrees),
                    "MODEL_STATE_UNAVAILABLE","Native degree variable and radian feature angle disagree.");
                return(new(args.BridgeId,args.DocumentId,handle),ModelFeatureOracle.Read(solid),variable);
            }
            var verified=Verify();Trace("model_verified");job.CheckCancellation();job.BeginCommit();Close();verified=Verify();
            var sourceRef=new EntityRef(args.BridgeId,args.DocumentId,edit.Handles[0]);bool replaced=sourceRef.handle!=verified.Body.handle;
            EntityRef Ref(Db.ObjectId id)=>new(args.BridgeId,args.DocumentId,Validation.NormalizeHandle(id.Handle.ToString()));
            var change=new ModelFeatureRevolutionMapping(0,sourceRef,verified.Body,binding.Body.ID.ToString(),created.Sketch.ID.ToString(),created.Profile.ID.ToString(),
                created.Feature.ID.ToString(),verified.Variable.ToString(),edit.PartOperation,edit.AngleDegrees,Ref(binding.Axis.Source.Id),binding.ProfileSources.Select(s=>Ref(s.Source.Id)).ToArray(),
                binding.Metrics.Volume,verified.Metrics.Volume,replaced);
            var warnings=new List<string>{WriteRecovery.Warning,"MODEL_REVOLUTION_APPENDED: Refresh history, variables, topology and drawing views. Native rebuild may update dependents; inspect the actual join/cut and dimensions through MCP."};
            double effect=edit.PartOperation=="cut"?binding.Metrics.Volume-verified.Metrics.Volume:verified.Metrics.Volume-binding.Metrics.Volume;
            if(effect<=Math.Max(1e-8,binding.Metrics.Volume*1e-10))warnings.Add("MATERIAL_EFFECT_UNVERIFIED: Healthy native history and the requested revolution were retained, but mass integration did not establish the requested material change. Inspect the engineering result before accepting it.");
            var data=new ModifiedEntities(args.OperationId,"committed",false,[verified.Body],replaced?[sourceRef]:[],replaced?[verified.Body]:[],[],"not_guaranteed",model_revolution_features:[change]);
            var result=Result.Ok(job.OriginalRequestId,data,context,warnings.ToArray());result=ModelViewFollowers.WithWarnings(result,binding.Views);job.RecordCommit(result);Trace("commit_recorded");return result;
        }
        catch(Exception error)
        {
            if(job.CommittedResult is {} committed)return committed;Exception? cleanup=null;try{Close();}catch(Exception e){cleanup=e;}
            bool unknown=started||cleanup!=null;string cause=error is DomainException d?d.Error.code:error is OperationCanceledException?"CANCELLED":"MODEL_REVOLUTION_APPEND_FAILED";
            return job.Error(unknown?"WRITE_OUTCOME_UNKNOWN":cause,unknown?"Native revolution append is unverified; partial history may remain. Inspect this body and refresh its history before a dependent edit.":error.Message,
                unknown?"unknown":"not_started",context,new{cause,stage,message=error.Message,body_mc_id=binding?.Body.ID.ToString(),sketch_id=created?.Sketch.ID.ToString(),feature_id=created?.Feature.ID.ToString(),cleanup_error=cleanup?.Message})
                with{warnings=[WriteRecovery.Warning]};
        }
    }
}
