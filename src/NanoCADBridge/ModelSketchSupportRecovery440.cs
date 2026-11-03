using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// Explicit bounded native recovery. Unknown remains unknown after the first
// native setter; no alternate plane, coordinate rebuild or duplicate is tried.
internal static class ModelSketchSupportRecovery440
{
    sealed record Binding(ModelManufacturingEdit Edit,ModelSketchSupportSpec396 Request,Mc3dSolid Body,PlanarSketch Sketch,
        ModelSketchSupportState396.Binding Support,ModelSketchSupportRecoveryState440.Scope Scope,
        ModelSketchSupportRecoveryState440.Snapshot Before);
    static string Key(McObjectId id)=>id.ToString();
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)]bool ok,string code,string message)=>ModelDatumPlaneState353.Need(ok,code,message);
    internal static bool Handles(ModifyArguments args)=>args.Operations.Any(edit=>edit is ModelManufacturingEdit{Kind:"set_model_sketch_support"} m&&
        m.Definition.TryGetProperty("recover_lost_support",out var recovery)&&recovery.ValueKind==JsonValueKind.True);
    static Binding Resolve(ModifyArguments args,DocumentState state,Db.Transaction tx,Action cancel)
    {
        Need(args.Operations.Length==1&&args.Operations[0] is ModelManufacturingEdit,"INVALID_OPERATION_ORDER","Recover one retained sketch support per native request.");
        Need(args.Units=="Millimeters"&&args.TargetSpace=="model"&&(int)state.Document.Database.Insunits==4,"OUT_OF_SCOPE","Use Model Space and verified Millimeters.");
        var edit=(ModelManufacturingEdit)args.Operations[0];var request=ModelSketchSupportValidation396.Parse(edit.Definition,"definition",allowRecovery:true);
        Need(request.Kind=="workplane"&&request.RecoverLostSupport&&request.ExpectedSupportId!=null,"INVALID_ARGUMENT","Explicit lost-face recovery requires an exact existing WorkPlane and expected_support_id.");
        var body=ModelDatumPlaneRevisionReader354.Body(edit.Handles.Single(),state,tx,cancel,true);
        Need(Key(body.LastPartItemID).Equals(edit.ExpectedLastItemId,StringComparison.OrdinalIgnoreCase),"PRECONDITION_FAILED","Refresh the selected failed part's current history terminal.");
        var owner=ModelProfileDiscovery348.OwnerDocument(body,state);var sketch=ModelSketchLifecycleReader362.Resolve(edit.FeatureId!,owner,cancel);
        var representation=ModelHistoryEditGuard.CurrentRepresentation(body,state,tx,cancel);
        var support=ModelSketchSupportState396.Capture(request,body,representation,owner,state,tx,args.BridgeId,cancel);
        var scope=ModelSketchSupportRecoveryState440.Resolve(body,sketch,support.Support,request.ExpectedSupportId,cancel)
            with{UseTargetDatum=request.FrameReferencePolicy=="use_target_datum"};
        var supportNodes=support.Stored.GetProperty("nodes").EnumerateArray().SelectMany(row=>row.GetProperty("state").TryGetProperty("source_feature_id",out var source)?new[]{row.GetProperty("id").GetString()!,source.GetString()!}:new[]{row.GetProperty("id").GetString()!});
        Need(!supportNodes.Any(scope.Affected.Contains),"CYCLIC_DEPENDENCY","The chosen WorkPlane depends on the failed sketch or one of its retained consumers.");
        var before=ModelSketchSupportRecoveryState440.Capture(body,scope,args,state,tx,cancel,true);
        NativeSketchSupportReassignment440.EnsureAvailable(scope.UseTargetDatum);
        return new(edit,request,body,sketch,support,scope,before);
    }
    internal static void Preflight(ModifyArguments args,DocumentState state,Db.Transaction tx,Action? cancel=null)=>Resolve(args,state,tx,cancel??(()=>{}));
    static Result Outcome(Binding binding,ModelSketchSupportRecoveryState440.Snapshot after,ModelSketchSupportState396.Binding support,
        ModifyArguments args,WriteJob job,ReadContext context,IEnumerable<string>? verification=null)
    {
        var source=new EntityRef(args.BridgeId,args.DocumentId,binding.Edit.Handles.Single());
        var body=new EntityRef(args.BridgeId,args.DocumentId,Validation.NormalizeHandle(after.Representation.Handle.ToString()));bool replaced=source.handle!=body.handle;
        var actualFrame=after.Frame;
        var parameters=JsonNode.Parse(Wire.Element(ModelSketchSupport.Read(binding.Sketch)).GetRawText())!.AsObject();
        if(actualFrame!=null)parameters["native_support"]=JsonSerializer.SerializeToNode(ModelSketchSupportState396.Describe(support,binding.Body,after.Owner,actualFrame,binding.Sketch),Wire.Json);
        parameters["recovery"]=JsonSerializer.SerializeToNode(new{kind="lost_native_face_support",previous_support_id=Key(binding.Scope.OldSupport),chosen_support_id=Key(support.Support),
            frame_reference_policy=binding.Request.FrameReferencePolicy,
            original_frame_references=binding.Before.Design.GetProperty("selected_sketch").GetProperty("frame_references"),
            actual_frame_references=after.Design.GetProperty("selected_sketch").GetProperty("frame_references"),
            retired_frame_reference_ids=binding.Scope.UseTargetDatum?ModelSketchFrameRetirement440.FrameIds(binding.Before.Design.GetProperty("selected_sketch")):[],
            original_frame_wcs=binding.Before.Frame,original_frame_wcs_available=binding.Before.Frame!=null,chosen_support_frame_wcs=support.Plane,actual_frame_wcs=actualFrame,
            placement_preserved=binding.Before.Frame==null||actualFrame==null?(bool?)null:ModelSketchSupportRecoveryFrame440.Same(binding.Before.Frame,actualFrame),
            preserved_coordinate_system="native_sketch_and_profile_local",before_native_notices=binding.Before.Notices,after_native_notices=after.Notices,
            native_local_geometry_and_controls_verified=true,profile_ids=binding.Scope.Profiles.Select(Key).ToArray()},Wire.Json);
        var change=new ModelManufacturingChange(binding.Edit.Kind,body,Key(binding.Body.ID),Key(binding.Sketch.ID),Wire.Element(parameters),support.Datum?.Read);
        var warnings=(verification??[]).Append(WriteRecovery.Warning)
            .Append(binding.Scope.UseTargetDatum?
                "NATIVE_SKETCH_TARGET_FRAME_SELECTED: The explicitly chosen actual datum frame replaces the reported old frame references. The same sketch, local geometry, controls/drivers, selected profiles and dependent extrusions were verified. The reported original frame dependencies are intentionally retired; unchanged former engineering dependency intent is not claimed.":
                "NATIVE_LOST_SKETCH_SUPPORT_RECOVERED: The same sketch, selected profiles, local geometry, native relation/driver identities and dependent extrusions were retained and verified after native WorkPlane reassignment/rebuild. The target support and retained native frame references determine the returned actual WCS frame. Inspect the intended shape through MCP.");
        if(binding.Before.Frame==null)warnings=warnings.Append("ORIGINAL_SKETCH_WCS_UNAVAILABLE: The failed sketch did not expose a valid old native WCS frame. The exact retained local coordinates and controls were verified, and the explicitly selected WorkPlane establishes the intended support. Unchanged former WCS placement is not claimed.");
        else if(actualFrame!=null&&!ModelSketchSupportRecoveryFrame440.Same(binding.Before.Frame,actualFrame))warnings=warnings.Append("SKETCH_WCS_FRAME_CHANGED: Native support reassignment changed the sketch WCS frame; retained local geometry and controls are unchanged. Review the intended engineering placement.");
        return ModelViewFollowers.WithWarnings(Result.Ok(job.OriginalRequestId,new ModifiedEntities(args.OperationId,"committed",false,[body],replaced?[source]:[],replaced?[body]:[],[],"not_guaranteed",manufacturing_changes:[change]),context,warnings.Distinct(StringComparer.Ordinal).ToArray()),binding.Before.Views);
    }
    static void Budget(Result result,Action cancel)
    {cancel();Need(JsonSerializer.SerializeToUtf8Bytes(result,Wire.Json).Length<=Wire.PayloadBudget,"RESPONSE_LIMIT_EXCEEDED","The complete native recovery receipt exceeds transport bytes.");}
    internal static Result Execute(ModifyArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        Binding? binding=null;McUndoPoint? tracker=null;bool started=false;string stage="preparation";
        void Trace(string value){stage=value;log.Write(job.Operation,job.OriginalRequestId,"model_sketch_support_recovery440_checkpoint",transaction:value,operationId:job.OperationId);}
        void Close(){var value=tracker;tracker=null;if(value!=null)try{value.Stop();}finally{value.Dispose();}}
        try
        {
            using(var tx=state.Document.Database.TransactionManager.StartTransaction())binding=Resolve(args,state,tx,job.CheckCancellation);
            Budget(Outcome(binding,binding.Before,binding.Support,args,job,context),job.CheckCancellation);
            using(var tx=state.Document.Database.TransactionManager.StartTransaction())
            {var current=Resolve(args,state,tx,job.CheckCancellation);Need(current.Before.Token==binding.Before.Token&&current.Support.Token==binding.Support.Token,"PRECONDITION_FAILED","The exact failed graph or chosen WorkPlane changed before recovery.");}
            ModelViewFollowers.CheckStandalone(binding.Body,binding.Before.Views,job.CheckCancellation);job.CheckCancellation();tracker=new McUndoPoint(false);tracker.Start();started=true;
            Trace(binding.Scope.UseTargetDatum?"reassign_same_sketch_explicit_target_frame":"reassign_same_sketch_native_support_and_preserve_frame");binding.Sketch.TryModify((uint)McDbObject.ObjChangesType.Geom);
            var nativeUpdates=NativeSketchSupportReassignment440.Apply(binding.Sketch,binding.Body,binding.Scope.OldSupport,binding.Support.Support,binding.Scope.UseTargetDatum);
            log.Write(job.Operation,job.OriginalRequestId,"model_sketch_support_recovery440_native_update_results",
                transaction:$"sketch_update_hresult={nativeUpdates.sketch_update_hresult};update_all_hresult={nativeUpdates.update_all_hresult}",operationId:job.OperationId);
            binding.Sketch.DbEntity.Update();McObjectManager.UpdateAll();
            Need(binding.Body.RebuildPart(),"MODEL_UPDATE_FAILED","Native rebuild rejected lost sketch support recovery.");McObjectManager.UpdateAll();Trace("native_rebuild_returned");
            (ModelSketchSupportRecoveryState440.Snapshot Part,ModelSketchSupportState396.Binding Support,string[] Warnings) Verify()
            {
                using var tx=state.Document.Database.TransactionManager.StartTransaction();var representation=ModelHistoryEditGuard.CurrentRepresentation(binding.Body,state,tx,job.CheckCancellation);
                var support=ModelSketchSupportState396.Capture(binding.Request,binding.Body,representation,binding.Before.Owner,state,tx,args.BridgeId,job.CheckCancellation,false);
                ModelSketchSupportState396.RequireAttached(support,binding.Body,binding.Before.Owner,binding.Sketch,job.CheckCancellation);
                ModelSketchSupportState396.Retained(binding.Support,support,Key(binding.Sketch.ID),state,tx,job.CheckCancellation,binding.Scope.Affected);
                var after=ModelSketchSupportRecoveryState440.Capture(binding.Body,binding.Scope,args,state,tx,job.CheckCancellation,false);
                Need(after.Frame!=null,"SKETCH_FRAME_UNAVAILABLE","The recovered sketch must expose its actual finite native WCS frame.");
                if(binding.Scope.UseTargetDatum)Need(ModelSketchFrameRetirement440.EmptyFrame(after.Design.GetProperty("selected_sketch"))&&
                    ModelSketchSupportRecoveryFrame440.Same(after.Frame,binding.Support.Plane)&&ModelSketchSupportRecoveryFrame440.Same(after.Frame,support.Plane),
                    "TARGET_DATUM_FRAME_MISMATCH","Explicit replacement requires all native frame references empty and the actual recovered WCS frame equal to the actual chosen datum frame observed before and after mutation.");
                return(after,support,ModelSketchSupportRecoveryState440.Verify(binding.Before,after,binding.Scope,job.CheckCancellation));
            }
            var observed=Verify();Budget(Outcome(binding,observed.Part,observed.Support,args,job,context,observed.Warnings),job.CheckCancellation);Trace("local_design_and_native_dependencies_verified");
            job.BeginCommit();Close();observed=Verify();var result=Outcome(binding,observed.Part,observed.Support,args,job,context,observed.Warnings);Budget(result,job.CheckCancellation);job.RecordCommit(result);Trace("commit_recorded");return result;
        }
        catch(Exception error)
        {
            if(job.CommittedResult is {} committed)return committed;Exception? cleanup=null;try{Close();}catch(Exception e){cleanup=e;}bool unknown=started||cleanup!=null;
            string cause=error is DomainException domain?domain.Error.code:error is OperationCanceledException?"CANCELLED":"MODEL_SKETCH_SUPPORT_RECOVERY_FAILED";string message=error.Message;if(message.Length>512)message=message[..512];
            return job.Error(unknown?"WRITE_OUTCOME_UNKNOWN":cause,unknown?"Native lost-support reassignment started but remains unverified. Inspect the same retained sketch/profile/extrusion graph before continuing; no rollback or duplicate recovery is claimed.":message,
                unknown?"unknown":"not_started",context,new{cause,stage,message,body_mc_id=binding==null?null:Key(binding.Body.ID),sketch_id=binding==null?null:Key(binding.Sketch.ID),
                    previous_support_id=binding==null?null:Key(binding.Scope.OldSupport),chosen_support_id=binding==null?null:Key(binding.Support.Support),native_details=(error as DomainException)?.Error.details,cleanup_error=cleanup?.GetType().Name})with{warnings=[WriteRecovery.Warning]};
        }
    }
}

internal static class ModelSketchSupportRecoveryFrame440
{
    internal static bool Same(SketchPlaneSpec a,SketchPlaneSpec b)
    {
        static bool Point(Point3 x,Point3 y)=>ModelParameterSnapshot.Near(x.X,y.X)&&ModelParameterSnapshot.Near(x.Y,y.Y)&&ModelParameterSnapshot.Near(x.Z,y.Z);
        return Point(a.Origin,b.Origin)&&Point(a.XAxis,b.XAxis)&&Point(a.YAxis,b.YAxis);
    }
}
