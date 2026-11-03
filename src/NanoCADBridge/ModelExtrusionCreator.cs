using System.Runtime.CompilerServices;
using System.Text.Json;
using HostMgd.ApplicationServices;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.DatabaseServices.Constraints;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;
using MPoint = Multicad.Geometry.Point3d;
using MVector = Multicad.Geometry.Vector3d;
using MPlane = Multicad.Geometry.Plane3d;

namespace NanoCADBridge;

// Native history creation is separate from detached direct Solid3d construction.
// No automatic rollback is claimed for the mixed MultiCAD/native object graph.
internal static class ModelExtrusionCreator
{
    internal static bool Handles(CreateArguments args) => args.Entities.Any(e => e is ModelExtrusionSpec or ModelLoftSpec or ModelSweepSpec);
    private static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool value,string code,string message)
    {if(!value)throw new DomainException(code,message);}
    internal static void Preflight(CreateArguments args,DocumentState state,Transaction transaction)
    {
        Need(args.Entities.Length==1&&args.Entities[0] is ModelExtrusionSpec or ModelLoftSpec or ModelSweepSpec,"INVALID_ARGUMENT","Model extrusion must be the only entity in its request.");
        var loaded=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Need(loaded.Contains("mapimgd")&&loaded.Contains("MapiBaseTypes"),"CAPABILITY_UNAVAILABLE","MultiCAD must already be loaded; no modeling module is loaded as a side effect.");
        var db=state.Document.Database;
        var model=((BlockTable)transaction.GetObject(db.BlockTableId,OpenMode.ForRead))[BlockTableRecord.ModelSpace];
        Need(db.CurrentSpaceId==model,"OUT_OF_SCOPE","Activate Model Space before creating native history.");
        
        var layers=(LayerTable)transaction.GetObject(db.LayerTableId,OpenMode.ForRead);
        Need(layers.Has(args.Entities[0].Layer),"LAYER_UNAVAILABLE","The requested layer must already exist.");
        var layer=(LayerTableRecord)transaction.GetObject(layers[args.Entities[0].Layer],OpenMode.ForRead);
        Need(!layer.IsLocked&&!layer.IsFrozen&&!layer.IsOff&&!layer.IsDependent,"LAYER_UNAVAILABLE","The target layer is unavailable.");
        RequireContext(state);
        if(args.Entities[0] is ModelExtrusionSpec {NativeGeometry:not null} or ModelExtrusionSpec {EditableProfile:true})
            ModelExtrusionFactory.ModelDocument();
        if(args.Entities[0] is ModelExtrusionSpec {NativeGeometry:not null} controlled)
        {
            Need(controlled.RegionPoint!=null,"INVALID_ARGUMENT","Select an explicit region in the solved native sketch.");
            NativeSketchValidation385.RequirePlane(controlled.NativeGeometry,controlled.Plane,"native_geometry");
            const string sampleId="{FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF}";
            var reservation=new ModelSketchCreation410(new(args.BridgeId,args.DocumentId,"FFFFFFFFFFFFFFFF"),sampleId,sampleId,sampleId,sampleId,sampleId,
                controlled.NativeGeometry.Geometry.Select(g=>new NativeSketchCreated385(g.Key,sampleId,[sampleId])).ToArray(),
                controlled.NativeGeometry.Constraints.Select(c=>new SketchConstraintChange(sampleId,sampleId,c.Kind,sampleId,c.Second==null?null:sampleId,sampleId,c.Value)).ToArray(),sampleId);
            Need(JsonSerializer.SerializeToUtf8Bytes(reservation,Wire.Json).Length+16384<Wire.PayloadBudget,"RESPONSE_LIMIT_EXCEEDED","The complete controlled model receipt exceeds transport bytes.");
        }
        if(args.Entities[0] is ModelLoftSpec loft)ModelLoftFactory.ResolvePaths(loft,state,transaction);
        if(args.Entities[0] is ModelSweepSpec sweep)ModelSweepFactory.ResolvePaths(sweep.PathHandles,state,transaction);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RequireContext(DocumentState state)
    {
        var active=McDocument.ActiveDocument;
        Need(state.Document.IsNamedDrawing&&active!=null&&!string.IsNullOrWhiteSpace(active.Path)&&
            string.Equals(Path.GetFullPath(active.Path),Path.GetFullPath(state.Document.Name),StringComparison.OrdinalIgnoreCase),
            "DOCUMENT_CONTEXT_MISMATCH","Native history creation requires the matching named drawing. Save a new drawing before creating its model history.");
    }
    private static ObjectId Model(Database db,Transaction transaction)=>((BlockTable)transaction.GetObject(db.BlockTableId,OpenMode.ForRead))[BlockTableRecord.ModelSpace];
    private static HashSet<ObjectId> Inventory(Document doc)
    {
        using var transaction=doc.Database.TransactionManager.StartTransaction();
        var result=((BlockTableRecord)transaction.GetObject(Model(doc.Database,transaction),OpenMode.ForRead)).Cast<ObjectId>().ToHashSet();
        return result;
    }
    private static MPoint Point(Point3 p)=>new(p.X,p.Y,p.Z);
    private static MVector Vector(Point3 p)=>new(p.X,p.Y,p.Z);

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Result Execute(CreateArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        McUndoPoint? tracker=null;Mc3dSolid? body=null;PlanarSketch? sketch=null;SketchProfile? selectedProfile=null;HistoryItem? extrusion=null;NativeSketchInitialization385.Created? nativeCreated=null;var observedNativeIds=new List<McObjectId>();bool started=false;string stage="preparation";
        void Trace(string checkpoint){stage=checkpoint;log.Write(job.Operation,job.OriginalRequestId,"model_create_checkpoint",transaction:checkpoint,operationId:job.OperationId);}
        void ObserveConstruction(PlanarSketch value,SketchProfile? profile){sketch=value;selectedProfile=profile;}
        void Close(){var value=tracker;tracker=null;if(value==null)return;try{value.Stop();}finally{value.Dispose();}}
        try
        {
            var loft=args.Entities.Single() as ModelLoftSpec;var sweep=args.Entities.Single() as ModelSweepSpec;var spec=loft?.Sections[0]??sweep?.Section??(ModelExtrusionSpec)args.Entities.Single();
            if(spec.NativeGeometry==null)ModelExtrusionGeometry.Measure(spec,job.CheckCancellation);
            else NativeSketchValidation385.RequirePlane(spec.NativeGeometry,spec.Plane,"native_geometry");
            var before=Inventory(state.Document);job.CheckCancellation();
            tracker=new McUndoPoint(false);tracker.Start();
            started=true;
            if(spec.NativeGeometry!=null)
            {
                // SDK controlled-sketch examples initialize document construction
                // before a forming body exists. Consume that exact solved profile
                // afterward; no empty placeholder body participates in its editor.
                Trace("standalone_controlled_sketch_before_body");
                var owner=ModelExtrusionFactory.ModelDocument();
                var construction=ModelExtrusionFactory.CreateProfile(null,spec,job.CheckCancellation,Trace,constructionDocument:owner,nativeInitializer:s=>
                {Trace("initialize_controlled_native_sketch");nativeCreated=NativeSketchInitialization385.Create(s,spec.NativeGeometry,state,job.CheckCancellation,observedNativeIds.Add);},observeConstruction:ObserveConstruction);
                sketch=construction.Sketch;selectedProfile=construction.Profile;
                Trace("body_create_before");body=new Mc3dSolid();body.DbEntity.Layer=spec.Layer;
                body.DbEntity.AddToCurrentDocument();Trace("body_created");
                extrusion=ModelExtrusionFactory.AppendProfile(body,spec,sketch,selectedProfile,null,Trace).Feature;
            }
            else
            {
            Trace("body_create_before");body=new Mc3dSolid();body.DbEntity.Layer=spec.Layer;
            body.DbEntity.AddToCurrentDocument();Trace("body_created");
            if(loft!=null){var items=ModelLoftFactory.Append(body,loft,state,job.CheckCancellation,Trace,ObserveConstruction);sketch=items.Sketch;selectedProfile=items.Profile;extrusion=items.Feature;}
            else if(sweep==null)
            {
                var items=ModelExtrusionFactory.Append(body,spec,null,job.CheckCancellation,Trace,observeConstruction:ObserveConstruction);sketch=items.Sketch;selectedProfile=items.Profile;extrusion=items.Feature;
            }
            else {var items=ModelSweepFactory.Append(body,sweep,state,job.CheckCancellation,Trace,ObserveConstruction);sketch=items.Sketch;selectedProfile=items.Profile;extrusion=items.Feature;}
            }
            Trace("update_before");McObjectManager.UpdateAll();Trace("update_returned");job.CheckCancellation();
            var after=Inventory(state.Document);Need(before.IsSubsetOf(after),"MODEL_UPDATE_FAILED","Creation removed pre-existing entities.");
            string Verify()
            {
                RequireContext(state);
                after=Inventory(state.Document);Need(before.IsSubsetOf(after),"MODEL_UPDATE_FAILED","Native update removed pre-existing entities.");
                using var transaction=state.Document.Database.TransactionManager.StartTransaction();
                var found=after.Except(before).Where(id=>transaction.GetObject(id,OpenMode.ForRead) is Solid3d&&McObjectId.FromHandle(id.Handle.Value,McDocument.ActiveDocument.ID)==body.ID).ToArray();
                Need(found.Length==1,"OBJECT_IDENTITY_MISMATCH","Cannot resolve one native representation for the created model.");
                var solid=(Solid3d)transaction.GetObject(found[0],OpenMode.ForRead);
                bool Near(double a,double b)=>double.IsFinite(a)&&Math.Abs(a-b)<=Math.Max(1e-6,Math.Abs(b)*1e-8);
                ModelFeatureOracle.Read(solid);
                var layers=(LayerTable)transaction.GetObject(state.Document.Database.LayerTableId,OpenMode.ForRead);
                Need(solid.LayerId==layers[spec.Layer],"NATIVE_GEOMETRY_MISMATCH","Native representation did not retain the requested layer.");
                var contents=ModelInventoryBudget.History(body,job.CheckCancellation);
                Need(contents!=null&&contents.Contains(sketch.ID)&&contents.Contains(extrusion.ID)&&extrusion.HostSolidID==body.ID,
                    "MODEL_UPDATE_FAILED","Created sketch/extrusion is not part of the returned model history.");
                if(extrusion is ExtrudeFeature ext){Need(!ext.DistanceParamVarID.IsNull,"MODEL_UPDATE_FAILED","Rebuilt extrusion has no bound distance variable.");
                Need(Near(ext.Distance,spec.Distance),"NATIVE_GEOMETRY_MISMATCH","Native extrusion distance differs from the request.");}
                if(extrusion is ExtrudeAlongPathFeature sw)ModelSweepFactory.Verify(sw,sweep!);
                if(extrusion is LoftFeature lf)ModelLoftFactory.Verify(lf,loft!);
                if(spec.NativeGeometry!=null)
                {
                    Need(nativeCreated!=null&&selectedProfile!=null&&contents.Contains(selectedProfile.ID)&&selectedProfile.ParentSketchID==sketch.ID,
                        "MODEL_UPDATE_FAILED","The controlled sketch/profile/feature chain is unavailable after native rebuild.");
                    NativeSketchInitialization385.VerifyCreated(sketch,spec.NativeGeometry,nativeCreated,state,transaction,job.CheckCancellation);
                    Need(extrusion is ExtrudeFeature controlledFeature&&controlledFeature.ProfileID==selectedProfile.ID,
                        "MODEL_UPDATE_FAILED","The initial feature did not retain its actual controlled profile.");
                }
                if(spec.NativeGeometry==null&&spec.EditableProfile&&loft==null)
                {
                    var sources=ProjectedHistorySources.Read(body,contents.ToArray(),state,transaction,job.CheckCancellation);
                    int count=spec.Profile.Kind=="circle"?1:spec.Profile.Vertices!.Length;
                    Need(sources.Length==count&&sources.All(s=>s.projection_ids.Length==1&&s.revolution_axis_ids.Length==0),"MODEL_UPDATE_FAILED","Editable profile did not retain each real source and projection.");
                }
                return Validation.NormalizeHandle(found[0].Handle.ToString());
            }
            string handle=Verify();Trace("geometry_verified");
            job.CheckCancellation();job.BeginCommit();Close();
            after=Inventory(state.Document);Need(before.IsSubsetOf(after),"MODEL_UPDATE_FAILED","Tracking close removed pre-existing entities.");handle=Verify();
            var bodyRef=new EntityRef(args.BridgeId,args.DocumentId,handle);
            var receipt=nativeCreated==null?null:new[]{new ModelSketchCreation410(bodyRef,body.ID.ToString(),sketch!.ID.ToString(),selectedProfile!.ID.ToString(),extrusion!.ID.ToString(),body.LastPartItemID.ToString(),
                nativeCreated.Objects,nativeCreated.Changes,(extrusion as ExtrudeFeature)?.DistanceParamVarID.ToString())};
            var result=Result.Ok(job.OriginalRequestId,new CreatedEntities(args.OperationId,"committed",false,new[]{bodyRef},"not_guaranteed",model_sketch_creation:receipt),context,
                WriteRecovery.Warning,nativeCreated==null?"MODEL_HISTORY_CREATED: Read inspect_model_history for the native sketch, profile, feature and parameters. Sketch constraints were not created.":
                "NATIVE_SKETCH_HISTORY_CREATED: Native inputs, requested relations, selected profile and initial feature retained. Read current control tokens before a driver edit; an unchanged initial value alone does not certify driving behavior.");
            if(nativeCreated?.Baseline is {Complete:false} unobserved)
                result=result with{warnings=result.warnings.Concat(new[]{"SKETCH_BASELINE_UNOBSERVED: Geometry-only initialization retained its actual inputs; the native relation baseline was unavailable and is not reported empty or preserved. "+unobserved.UnavailableReason}).ToArray()};
            if(nativeCreated?.Baseline.PreAddUnavailableReason!=null)
                result=result with{warnings=result.warnings.Concat(new[]{"SKETCH_NETWORK_MATERIALIZED: The pre-add relation inventory was unavailable. The fresh network was observed after the first requested relation and before solving; preservation of an unobserved pre-add network is not claimed."}).ToArray()};
            Need(JsonSerializer.SerializeToUtf8Bytes(result,Wire.Json).Length<Wire.PayloadBudget,"RESPONSE_LIMIT_EXCEEDED","The complete native model result exceeds transport bytes.");
            job.RecordCommit(result);Trace("commit_recorded");return result;
        }
        catch(System.Exception error)
        {
            if(job.CommittedResult is {} committed)return committed;
            System.Exception? cleanup=null;try{Close();}catch(System.Exception e){cleanup=e;}
            bool unknown=started||cleanup!=null;
            string code=error is DomainException domain?domain.Error.code:error is OperationCanceledException?"CANCELLED":"MODEL_CREATE_FAILED";
            return job.Error(unknown?"WRITE_OUTCOME_UNKNOWN":code,unknown?"Native model creation was not verified; partial model objects may remain. Do not assume rollback. Inspect the affected document before dependent edits.":"Native model creation did not start.",unknown?"unknown":"not_started",context,
                ModelSketchLifecycleDiagnostics362.Capture(code,stage,error.Message,body?.ID.ToString(),sketch?.ID.ToString(),selectedProfile?.ID.ToString(),extrusion?.ID.ToString(),[],
                    new{observed_native_input_ids=observedNativeIds.Select(id=>id.ToString()).ToArray(),verification_details=(error as DomainException)?.Error.details,error_type=error.GetType().Name},cleanup?.GetType().Name)) with {warnings=new[]{WriteRecovery.Warning}};
        }
    }
}
