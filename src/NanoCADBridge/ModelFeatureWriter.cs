using System.Runtime.CompilerServices;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.DatabaseServices.Constraints;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class ModelFeatureWriter
{
    internal static bool Handles(ModifyArguments args)=>args.Operations.Any(operation=>operation is ModelFeatureExtrusionSpec);
    private static void Need(bool valid,string code,string message){if(!valid)throw new DomainException(code,message);}
    private sealed record Binding(Mc3dSolid Body,ObjectId Source,string Layer,McObjectId[] Contents,ProjectedHistorySources.Source[] ExternalSources,
        DrawingProfileWriter.Source[] ProfileSources,ModelViewFollowers.Snapshot Views);
    private static ObjectId Model(Database db,Transaction tx)=>((BlockTable)tx.GetObject(db.BlockTableId,OpenMode.ForRead))[BlockTableRecord.ModelSpace];
    private static SketchConstraintCurve[] Curves(ModelProfileSpec profile,Action? cancel=null)
    {
        if(profile.Kind=="circle")return new[]{new SketchConstraintCurve("expected","circle",Center:profile.Center,Radius:profile.Radius)};
        if(profile.Kind=="polycurve")return ModelPolycurve.Curves(profile,cancel);
        var vertices=profile.Vertices!;
        return Enumerable.Range(0,vertices.Length).Select(i=>{cancel?.Invoke();return new SketchConstraintCurve(i.ToString(),"line",vertices[i],vertices[(i+1)%vertices.Length]);}).ToArray();
    }
    private static bool SameCurve(SketchConstraintCurve actual,SketchConstraintCurve expected)=>
        DrawingProfileGeometry.Same(actual,expected)||
        actual.Kind=="line"&&expected.Kind=="line"&&DrawingProfileGeometry.Same(actual.Start!,expected.End!)&&DrawingProfileGeometry.Same(actual.End!,expected.Start!);
    private static DrawingProfileWriter.Source[] ResolveProfileSources(ModelFeatureExtrusionSpec edit,DocumentState state,Transaction tx,Action? cancel=null)
    {
        if(edit.SourceHandles is not {Length:>0} handles)return Array.Empty<DrawingProfileWriter.Source>();
        var sources=handles.Select(h=>{cancel?.Invoke();return DrawingProfileWriter.SourceOf(CadStructureWriter.Find(state.Document.Database,h),state,tx,true,edit.Definition.Plane);}).ToArray();
        var actual=sources.Select(s=>s.Curve).ToArray();
        if(actual.Any(c=>c.Kind=="arc"))ModelPolycurve.FromCurves(actual,cancel);
        else DrawingProfileGeometry.Profile(actual,cancel);
        var expected=Curves(edit.Definition.Profile,cancel);
        Need(actual.Length==expected.Length,"PROFILE_SOURCE_MISMATCH","The selected drawing sources do not have the same edge count as the requested profile.");
        var remaining=expected.ToList();
        foreach(var curve in actual)
        {
            cancel?.Invoke();int index=remaining.FindIndex(item=>{cancel?.Invoke();return SameCurve(curve,item);});
            Need(index>=0,"PROFILE_SOURCE_MISMATCH","The selected drawing geometry differs from the declared feature profile.");
            remaining.RemoveAt(index);
        }
        Need(remaining.Count==0,"PROFILE_SOURCE_MISMATCH","The declared feature profile has unmatched edges.");
        return sources;
    }
    internal static void Preflight(ModifyArguments args,DocumentState state,Transaction tx,Action? cancel=null)
    {
        Need(args.Operations.Length==1&&args.Operations[0] is ModelFeatureExtrusionSpec,"INVALID_ARGUMENT","Native history append must be the only operation.");
        var loaded=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Need(loaded.Contains("mapimgd")&&loaded.Contains("MapiBaseTypes"),"CAPABILITY_UNAVAILABLE","MultiCAD must already be loaded.");
        Resolve((ModelFeatureExtrusionSpec)args.Operations[0],state,tx,cancel);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Binding Resolve(ModelFeatureExtrusionSpec edit,DocumentState state,Transaction tx,Action? cancel=null)
    {
        var db=state.Document.Database;var model=Model(db,tx);
        Need(db.CurrentSpaceId==model,"OUT_OF_SCOPE","Activate Model Space before appending history.");
        var active=McDocument.ActiveDocument;
        Need(state.Document.IsNamedDrawing&&active!=null&&!string.IsNullOrWhiteSpace(active.Path)&&
            string.Equals(Path.GetFullPath(active.Path),Path.GetFullPath(state.Document.Name),StringComparison.OrdinalIgnoreCase),"DOCUMENT_CONTEXT_MISMATCH","Native history append requires the matching named document.");
        var source=CadStructureWriter.Find(db,edit.Handles.Single());
        Need(tx.GetObject(source,OpenMode.ForRead) is Solid3d&&tx.GetObject(source,OpenMode.ForRead).GetType()==typeof(Solid3d),"UNSUPPORTED_ENTITY_TYPE","Use a native representation of Mc3dSolid.");
        var entity=(Solid3d)tx.GetObject(source,OpenMode.ForRead);
        Need(entity.OwnerId==model&&!entity.IsNull,"OUT_OF_SCOPE","Use a nonempty top-level Model Space body.");
        var layer=(LayerTableRecord)tx.GetObject(entity.LayerId,OpenMode.ForRead);
        Need(!layer.IsLocked&&!layer.IsFrozen&&!layer.IsOff&&!layer.IsDependent,"LAYER_UNAVAILABLE","The body layer is unavailable for editing.");
        var identity=McObjectId.FromHandle(source.Handle.Value,active!.ID);var body=identity.GetObjectOfType<Mc3dSolid>();
        Need(body!=null&&body.ID==identity,"OBJECT_IDENTITY_MISMATCH","Source handle does not map to this document's native model history.");
        Need(string.Equals(body!.LastPartItemID.ToString(),edit.ExpectedLastItemId,StringComparison.OrdinalIgnoreCase),"PRECONDITION_FAILED","Model last_item_id changed; read its current history before appending a feature.");
        var contents=ModelInventoryBudget.History(body,cancel);Need(contents.Count>0,"HISTORY_UNAVAILABLE","Appending requires an available existing native history.");
        var views=ModelViewFollowers.Resolve(body,contents!.ToArray(),cancel,includeStandalone:true);
        ModelViewFollowers.PrepareInsertion(views,true);
        var external=ProjectedHistorySources.Read(body,contents!.ToArray(),state,tx,cancel);ProjectedHistorySources.Healthy(contents.ToArray(),external);
        var profileSources=ResolveProfileSources(edit,state,tx,cancel);
        Need(profileSources.All(s=>external.All(old=>!old.id.Equals(s.Native.ToString(),StringComparison.OrdinalIgnoreCase))),
            "OUT_OF_SCOPE","A selected drawing source already participates in this body's history; this append requires one new projection per source.");
        return new(body,source,entity.Layer,contents.ToArray(),external,profileSources,views);
    }
    private static HashSet<ObjectId> Inventory(DocumentState state)
    {
        using var tx=state.Document.Database.TransactionManager.StartTransaction();
        var ids=((BlockTableRecord)tx.GetObject(Model(state.Document.Database,tx),OpenMode.ForRead)).Cast<ObjectId>().ToHashSet();
        return ids;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Result Execute(ModifyArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        McUndoPoint? tracker=null;Binding? binding=null;ModelExtrusionFactory.Items? created=null;bool started=false;string stage="preparation";
        NanoCAD.Contracts.NativeSketchInitialization385? inlineInput=null;NativeSketchInitialization385.Created? nativeCreated=null;var observedNativeInputs=new List<McObjectId>();
        void Trace(string checkpoint){stage=checkpoint;log.Write(job.Operation,job.OriginalRequestId,"model_feature_checkpoint",transaction:checkpoint,operationId:job.OperationId);}
        void Close(){var value=tracker;tracker=null;if(value==null)return;try{value.Stop();}finally{value.Dispose();}}
        try
        {
            var edit=(ModelFeatureExtrusionSpec)args.Operations.Single();ModelFeatureOracle.Metrics before;ModelExtrusionSpec definition;
            using(var tx=state.Document.Database.TransactionManager.StartTransaction())
            {
                binding=Resolve(edit,state,tx,job.CheckCancellation);var source=(Solid3d)tx.GetObject(binding.Source,OpenMode.ForRead);
                var d=edit.Definition;definition=new(d.Plane,d.Profile,d.Distance,d.Direction,d.Name,binding.Layer,d.EditableProfile);
                before=ModelFeatureOracle.Read(source);
                // Admission uses the requested profile/plane and a healthy source.
                // Detached boolean reconstruction is not the native feature kernel.
                ModelExtrusionGeometry.Measure(definition,job.CheckCancellation);
                if(binding.ProfileSources.Length==0&&!definition.EditableProfile&&d.NativeGeometry==null)
                {
                    inlineInput=ModelInlineSketchInitialization413.Input(definition,job.CheckCancellation);
                }
            }
            Inventory(state);Trace("source_verified");job.CheckCancellation();
            ModelViewFollowers.CheckStandalone(binding.Body,binding.Views,job.CheckCancellation);tracker=new McUndoPoint(false);tracker.Start();started=true;Trace("append_before");
            var previousLast=binding.Views.LastModelItem;
            ModelViewFollowers.PrepareInsertion(binding.Views,false);
            created=ModelExtrusionFactory.Append(binding.Body,definition,edit.PartOperation,job.CheckCancellation,Trace,
                binding.ProfileSources.Length==0?null:binding.ProfileSources.Select(s=>s.Native).ToArray(),
                nativeInitializer:inlineInput==null?null:s=>
                {Trace("initialize_owned_inline_sketch");nativeCreated=NativeSketchInitialization385.Create(s,inlineInput,state,job.CheckCancellation,observedNativeInputs.Add);});
            if(created.Feature.OrderObjBefore!=previousLast)Need(created.Feature.SetOrderObjBefore(previousLast,false),"MODEL_UPDATE_FAILED","Native feature ordering was rejected.");
            Need(created.Feature.OrderObjBefore==previousLast,"MODEL_UPDATE_FAILED","Native feature did not retain its explicit preceding history item.");
            ModelViewFollowers.CompleteInsertion(binding.Views,created.Feature.ID);
            Trace("update_before");McObjectManager.UpdateAll();Trace("update_returned");job.CheckCancellation();
            (string Handle,ModelFeatureOracle.Metrics Metrics,bool MeasurableEffect) Verify()
            {
                var after=Inventory(state);
                var contents=ModelInventoryBudget.History(binding.Body,job.CheckCancellation);
                Need(contents!=null&&binding.Contents.All(contents.Contains)&&contents.Contains(created.Sketch.ID)&&contents.Contains(created.Profile.ID)&&contents.Contains(created.Feature.ID),"MODEL_STATE_UNAVAILABLE","Original or appended history items are missing.");
                if(inlineInput!=null)
                {
                    Need(nativeCreated!=null&&created.Profile.ParentSketchID==created.Sketch.ID,"MODEL_STATE_UNAVAILABLE","The appended native contour lost its owned sketch/profile construction.");
                    using var nativeTx=state.Document.Database.TransactionManager.StartTransaction();
                    NativeSketchInitialization385.VerifyCreated(created.Sketch,inlineInput,nativeCreated!,state,nativeTx,job.CheckCancellation);
                }
                using(var sourceTx=state.Document.Database.TransactionManager.StartTransaction())
                {
                    var external=ProjectedHistorySources.Read(binding.Body,contents!.ToArray(),state,sourceTx);ProjectedHistorySources.Healthy(contents.ToArray(),external);
                    var newProjections=contents!.Select(id=>id.GetObjectOfType<Multicad.Mc3D.ProjectionSketchEnt>()).Where(p=>p!=null&&p.HostSketchID==created.Sketch.ID).Select(p=>p!.ID.ToString()).ToHashSet();
                    var prior=external.Where(s=>!s.projection_ids.Any(newProjections.Contains)).ToArray();
                    Need(ModelParameterSnapshot.Equivalent(Wire.Element(binding.ExternalSources),Wire.Element(prior)),"MODEL_STATE_UNAVAILABLE","Appending a model feature changed prior external sources or their bindings.");
                    var added=external.Except(prior).ToArray();
                    Need(added.All(s=>(definition.EditableProfile||binding.ProfileSources.Length>0)&&s.projection_ids.Length==1&&newProjections.Contains(s.projection_ids[0])&&s.revolution_axis_ids.Length==0),"MODEL_STATE_UNAVAILABLE","Unexpected new source binding.");
                    if(binding.ProfileSources.Length>0)
                    {
                        var selected=binding.ProfileSources.Select(s=>s.Native.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        Need(added.Length==selected.Count&&added.Select(s=>s.id).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(selected),
                            "MODEL_STATE_UNAVAILABLE","Native history did not retain every selected drawing source exactly once.");
                    }
                }
                Need(created.Feature.OrderObjBefore==previousLast,"MODEL_STATE_UNAVAILABLE","Appended feature predecessor changed.");
                Need(binding.Body.LastPartItemID==(binding.Views.ViewIds.Length==0?created.Feature.ID:binding.Views.LastHistoryItem)&&
                    created.Feature.HostSolidID==binding.Body.ID&&created.Feature.ProfileID==created.Profile.ID&&!created.Feature.DistanceParamVarID.IsNull&&
                    created.Feature.PartOperation==(edit.PartOperation=="cut"?PartFeatureOperation.Cut:PartFeatureOperation.Join)&&Math.Abs(created.Feature.Distance-definition.Distance)<1e-7,
                    "MODEL_STATE_UNAVAILABLE","Native feature operation, distance or ownership differs from the request.");
                if(binding.Views.HasFollowers)ModelViewFollowers.VerifyAfterInsertion(binding.Body,binding.Views,created.Feature.ID,job.CheckCancellation,refreshStandalone:true);
                using var tx=state.Document.Database.TransactionManager.StartTransaction();
                var matches=after.Where(id=>tx.GetObject(id,OpenMode.ForRead) is Solid3d&&McObjectId.FromHandle(id.Handle.Value,McDocument.ActiveDocument.ID)==binding.Body.ID).ToArray();
                Need(matches.Length==1,"OBJECT_IDENTITY_MISMATCH","Cannot identify one resulting body representation.");
                Need(matches[0]==binding.Source||!after.Contains(binding.Source),"OBJECT_IDENTITY_MISMATCH","A previous body representation remains alongside its replacement.");
                var body=(Solid3d)tx.GetObject(matches[0],OpenMode.ForRead);var observed=ModelFeatureOracle.Read(body);
                Need(body.Layer==binding.Layer,"MODEL_STATE_UNAVAILABLE","Native model changed its original layer.");
                double materialChange=edit.PartOperation=="cut"?before.Volume-observed.Volume:observed.Volume-before.Volume;
                // Mass integration is a diagnostic. A retained healthy native
                // feature can be smaller than this numerical detection scale.
                return(Validation.NormalizeHandle(matches[0].Handle.ToString()),observed,
                    materialChange>Math.Max(1e-8,before.Volume*1e-10));
            }
            var verified=Verify();Trace("model_verified");job.CheckCancellation();job.BeginCommit();Close();verified=Verify();
            var sourceRef=new EntityRef(args.BridgeId,args.DocumentId,edit.Handles[0]);var bodyRef=new EntityRef(args.BridgeId,args.DocumentId,verified.Handle);
            bool replaced=sourceRef.handle!=bodyRef.handle;
            var projectedRefs=binding.ProfileSources.Length==0?null:binding.ProfileSources.Select(s=>new EntityRef(args.BridgeId,args.DocumentId,Validation.NormalizeHandle(s.Id.Handle.ToString()))).ToArray();
            var mapping=new ModelFeatureMapping(0,sourceRef,bodyRef,binding.Body.ID.ToString(),created.Sketch.ID.ToString(),created.Profile.ID.ToString(),created.Feature.ID.ToString(),created.Feature.DistanceParamVarID.ToString(),edit.PartOperation,before.Volume,verified.Metrics.Volume,replaced,projectedRefs);
            var data=new ModifiedEntities(args.OperationId,"committed",false,new[]{bodyRef},replaced?new[]{sourceRef}:Array.Empty<EntityRef>(),replaced?new[]{bodyRef}:Array.Empty<EntityRef>(),Array.Empty<CopyMapping>(),"not_guaranteed",model_history_changes:new[]{mapping});
            var warnings=new List<string>{WriteRecovery.Warning,"MODEL_HISTORY_APPENDED: Refresh history, variables, topology and drawing views. Native rebuild can update dependents; this result identifies the selected model, not a complete document-wide change inventory. This adds an explicit-plane feature, not a face attachment or sketch constraint."};
            if(!verified.MeasurableEffect)warnings.Add("MATERIAL_EFFECT_UNVERIFIED: Native history, requested operation and nonempty body were verified, but mass integration did not establish a material change. Inspect the actual cut/join and its dimensions through MCP before accepting this feature.");
            var result=Result.Ok(job.OriginalRequestId,data,context,warnings.ToArray());
            result=ModelViewFollowers.WithWarnings(result,binding.Views);job.RecordCommit(result);Trace("commit_recorded");return result;
        }
        catch(Exception error)
        {
            if(job.CommittedResult is {} committed)return committed;
            Exception? cleanup=null;try{Close();}catch(Exception e){cleanup=e;}
            bool unknown=started||cleanup!=null;
            string code=error is DomainException domain?domain.Error.code:error is OperationCanceledException?"CANCELLED":"MODEL_FEATURE_FAILED";
            return job.Error(unknown?"WRITE_OUTCOME_UNKNOWN":code,unknown?"Native feature append is unverified; partial history may remain. Inspect this model and refresh its history before another dependent edit.":"Native history append did not start.",unknown?"unknown":"not_started",context,
                new{cause=code,stage,message=error.Message.Length<=2048?error.Message:error.Message[..2048],verification_details=(error as DomainException)?.Error.details,body_mc_id=binding?.Body.ID.ToString(),sketch_id=created?.Sketch.ID.ToString(),feature_id=created?.Feature.ID.ToString(),observed_native_input_ids=observedNativeInputs.Select(id=>id.ToString()).ToArray(),cleanup_error=cleanup?.GetType().Name}) with{warnings=new[]{WriteRecovery.Warning}};
        }
    }
}
