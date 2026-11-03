using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// First phase of explicit lost-support recovery: create unconsumed native
// document construction, return its ACTUAL frame/token, leave the failed part
// unchanged. Assignment to the existing sketch is a separate reviewed step.
internal static class ModelSketchSupportRecoveryDatum440
{
    sealed record Binding(ModelDatumPlaneCreate353 Edit,McDocument Owner,ModelSketchSupportRecoveryState440.Scope Scope,
        ModelSketchSupportRecoveryState440.Snapshot Before,McObjectId[] ExistingPlanes);
    static string Key(McObjectId id)=>id.ToString();
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)]bool ok,string code,string message)=>ModelDatumPlaneState353.Need(ok,code,message);
    internal static bool Handles(ModifyArguments args)=>args.Operations.Any(edit=>edit is ModelDatumPlaneCreate353{RecoveryContext:not null});
    static Binding Resolve(ModifyArguments args,DocumentState state,Db.Transaction tx,Action cancel)
    {
        Need(args.Operations.Length==1&&args.Operations[0] is ModelDatumPlaneCreate353{RecoveryContext:not null},"INVALID_OPERATION_ORDER","Create one fixed native recovery datum with its exact failed sketch context.");
        Need(args.Units=="Millimeters"&&args.TargetSpace=="model"&&(int)state.Document.Database.Insunits==4,"OUT_OF_SCOPE","Use Model Space and verified Millimeters.");
        var edit=(ModelDatumPlaneCreate353)args.Operations[0];var context=edit.RecoveryContext!;
        var body=ModelDatumPlaneRevisionReader354.Body(edit.Handles.Single(),state,tx,cancel,true);
        Need(Key(body.LastPartItemID).Equals(edit.ExpectedLastItemId,StringComparison.OrdinalIgnoreCase),"PRECONDITION_FAILED","Refresh the selected failed part's current history terminal.");
        var owner=ModelProfileDiscovery348.OwnerDocument(body,state);var sketch=ModelSketchLifecycleReader362.Resolve(context.SketchId,owner,cancel);
        var scope=ModelSketchSupportRecoveryState440.Resolve(body,sketch,McObjectId.Null,context.ExpectedSupportId,cancel);
        var before=ModelSketchSupportRecoveryState440.Capture(body,scope,args,state,tx,cancel,true);var existing=ModelProfileDiscovery348.Ids<WorkPlane>(owner,cancel);
        foreach(var id in existing){cancel();Need(!ModelDatumPlaneState353.Resolve(Key(id),owner,cancel).Name.Equals(edit.Name,StringComparison.OrdinalIgnoreCase),"DATUM_NAME_EXISTS","Choose a distinct actual owner-document WorkPlane name.");}
        return new(edit,owner,scope,before,existing);
    }
    internal static void Preflight(ModifyArguments args,DocumentState state,Db.Transaction tx,Action? cancel=null)=>Resolve(args,state,tx,cancel??(()=>{}));
    static void Budget(Result result,Action cancel)
    {cancel();Need(JsonSerializer.SerializeToUtf8Bytes(result,Wire.Json).Length<=Wire.PayloadBudget,"RESPONSE_LIMIT_EXCEEDED","The complete recovery datum receipt exceeds transport bytes.");}
    const string Warning="RECOVERY_DATUM_CREATED_PART_UNCHANGED: A fresh unconsumed native fixed WorkPlane was created in the actual owner document; the same failed sketch/profile/extrusion graph and retained local controls remain unchanged. Read the returned actual datum frame/token and confirm engineering placement before explicit set_model_sketch_support recovery. Its in-plane origin/U/V representation is not a durable axis constraint and no support repair is claimed by datum creation.";
    static JsonElement Preparation(Binding binding)=>Wire.Element(new
    {
        sketch_id=Key(binding.Scope.Sketch),retained_support_id=Key(binding.Scope.OldSupport),
        coordinate_system="native_sketch_local",length_unit="Millimeters",
        geometry=binding.Before.Design.GetProperty("selected_sketch").GetProperty("local_geometry"),
        controls=binding.Before.Design.GetProperty("selected_sketch").GetProperty("controls"),
        frame_references=binding.Before.Design.GetProperty("selected_sketch").GetProperty("frame_references"),
        original_frame_wcs=binding.Before.Frame,
        placement_policy="The returned actual datum frame defines the proposed support. Retained associative frame references may also affect the final sketch frame; verify the intended WCS geometry after native reassignment."
    });
    static Result Estimate(Binding binding,ModifyArguments args,WriteJob job,ReadContext context)
    {
        string id="{FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF}";var frame=binding.Edit.Plane;var normal=DrawingWorkplane.Normal(frame);
        var geometry=Wire.Element(new{origin_wcs=new[]{frame.Origin.X,frame.Origin.Y,frame.Origin.Z},x_axis_wcs=new[]{frame.XAxis.X,frame.XAxis.Y,frame.XAxis.Z},
            y_axis_wcs=new[]{frame.YAxis.X,frame.YAxis.Y,frame.YAxis.Z},normal_wcs=new[]{normal.X,normal.Y,normal.Z},oriented_plane_offset=DrawingWorkplane.Dot(frame.Origin,normal)});
        var read=new ModelDatumPlaneRead353(id,binding.Edit.Name,Key(binding.Owner.ID),new string('F',64),"Fixed",geometry,null,null,null,Wire.Element(Array.Empty<object>()),
            null,false,null,"Ok",false,false,[],[],[],Wire.Element(new{handle="FFFFFFFFFFFFFFFF",reserved_native_representation_metadata=new string('\uFFFF',16384)}),
            "oriented_native_physical_plane; actual_in_plane_frame_read_before_separate_support_assignment");
        var body=new EntityRef(args.BridgeId,args.DocumentId,binding.Edit.Handles.Single());var representation=new EntityRef(args.BridgeId,args.DocumentId,"FFFFFFFFFFFFFFFF");
        return Result.Ok(job.OriginalRequestId,new ModelDatumPlaneOutput353(args.OperationId,"committed",false,[],[],[representation],[],"not_guaranteed",
            [new(0,body,Key(binding.Before.Body.ID),binding.Edit.ExpectedLastItemId,read,[representation],recovery_preparation:Preparation(binding))]),context,[WriteRecovery.Warning,Warning]);
    }
    static (ModelSketchSupportRecoveryState440.Snapshot Part,ModelDatumPlaneState353.Snapshot Datum,string[] Warnings) Current(Binding binding,WorkPlane plane,ModifyArguments args,DocumentState state,Action cancel)
    {
        using var tx=state.Document.Database.TransactionManager.StartTransaction();var datum=ModelDatumPlaneState353.Capture(plane,binding.Before.Body,state,tx,cancel);
        ModelDatumPlaneState353.VerifyFixed(datum,binding.Edit);
        Need(plane.HostSolidID.IsNull&&!datum.Read.in_selected_body_contents&&datum.Read.direct_consumer_ids.Length==0&&datum.Read.downstream_consumer_ids.Length==0,
            "DATUM_HOST_MISMATCH","The fresh fixed recovery datum must remain unconsumed document construction; no early selected-part support assignment is admitted.");
        var after=ModelSketchSupportRecoveryState440.Capture(binding.Before.Body,binding.Scope,args,state,tx,cancel,true);
        return(after,datum,ModelSketchSupportRecoveryState440.VerifyCreatedDatum(binding.Before,after,cancel));
    }
    static Result Outcome(Binding binding,ModifyArguments args,WriteJob job,ReadContext context,ModelSketchSupportRecoveryState440.Snapshot part,ModelDatumPlaneState353.Snapshot datum,IEnumerable<string> verification)
    {
        var body=new EntityRef(args.BridgeId,args.DocumentId,Validation.NormalizeHandle(part.Representation.Handle.ToString()));
        var references=datum.Representation.HasValue?new[]{new EntityRef(args.BridgeId,args.DocumentId,Validation.NormalizeHandle(datum.Representation.Value.Handle.ToString()))}:Array.Empty<EntityRef>();
        var change=new ModelDatumPlaneChange353(0,body,Key(part.Body.ID),Key(part.Body.LastPartItemID),datum.Read,references,recovery_preparation:Preparation(binding));
        return ModelViewFollowers.WithWarnings(Result.Ok(job.OriginalRequestId,new ModelDatumPlaneOutput353(args.OperationId,"committed",false,[],[],references,[],"not_guaranteed",[change]),context,
            verification.Append(WriteRecovery.Warning).Append(Warning).Distinct(StringComparer.Ordinal).ToArray()),binding.Before.Views);
    }
    internal static Result Execute(ModifyArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        Binding? binding=null;WorkPlane? plane=null;McUndoPoint? tracker=null;bool started=false;string stage="preparation";
        void Trace(string value){stage=value;log.Write(job.Operation,job.OriginalRequestId,"model_sketch_recovery_datum440_checkpoint",transaction:value,operationId:job.OperationId);}
        void Close(){var value=tracker;tracker=null;if(value!=null)try{value.Stop();}finally{value.Dispose();}}
        try
        {
            using(var tx=state.Document.Database.TransactionManager.StartTransaction())binding=Resolve(args,state,tx,job.CheckCancellation);
            Budget(Estimate(binding,args,job,context),job.CheckCancellation);
            using(var tx=state.Document.Database.TransactionManager.StartTransaction())
            {var current=Resolve(args,state,tx,job.CheckCancellation);Need(current.Before.Token==binding.Before.Token&&current.ExistingPlanes.ToHashSet().SetEquals(binding.ExistingPlanes),"PRECONDITION_FAILED","The exact failed graph or owner datum inventory changed before recovery construction.");}
            ModelViewFollowers.CheckStandalone(binding.Before.Body,binding.Before.Views,job.CheckCancellation);job.CheckCancellation();tracker=new McUndoPoint(false);tracker.Start();started=true;
            Trace("construct_fresh_fixed_native_recovery_datum");plane=new WorkPlane();var candidate=plane.ID;
            Need(candidate.IsNull||!binding.ExistingPlanes.Contains(candidate)&&!binding.Before.Contents.Contains(candidate),"OBJECT_IDENTITY_MISMATCH","Native recovery datum construction returned an existing identity.");
            Need(plane.DbEntity.Document==null||plane.DbEntity.Document.ID==binding.Owner.ID,"DATUM_DOCUMENT_MISMATCH","The candidate native recovery datum already belongs to another document.");
            Need(McObjectManager.Add2Document(plane.DbEntity,binding.Owner),"MODEL_UPDATE_FAILED","Native recovery datum explicit owner registration failed.");
            Need(!plane.ID.IsNull&&!binding.ExistingPlanes.Contains(plane.ID)&&!binding.Before.Contents.Contains(plane.ID)&&plane.DbEntity.Document?.ID==binding.Owner.ID,
                "DATUM_DOCUMENT_MISMATCH","The registered native recovery datum lacks a fresh actual owner identity.");
            Trace("define_fixed_native_recovery_plane");using(var geometry=DrawingWorkplaneNative.Plane(binding.Edit.Plane))Need(plane.SetFixedDefinition(geometry),"MODEL_UPDATE_FAILED","Native fixed recovery datum definition was rejected.");
            plane.Name=binding.Edit.Name;plane.DbEntity.Update();Trace("native_recovery_datum_update_returned");
            var observed=Current(binding,plane,args,state,job.CheckCancellation);Budget(Outcome(binding,args,job,context,observed.Part,observed.Datum,observed.Warnings),job.CheckCancellation);Trace("failed_graph_unchanged_and_actual_datum_verified");
            job.BeginCommit();Close();observed=Current(binding,plane,args,state,job.CheckCancellation);var result=Outcome(binding,args,job,context,observed.Part,observed.Datum,observed.Warnings);Budget(result,job.CheckCancellation);job.RecordCommit(result);Trace("commit_recorded");return result;
        }
        catch(Exception error)
        {
            if(job.CommittedResult is {} committed)return committed;Exception? cleanup=null;try{Close();}catch(Exception e){cleanup=e;}bool unknown=started||cleanup!=null;
            string cause=error is DomainException domain?domain.Error.code:error is OperationCanceledException?"CANCELLED":"MODEL_SKETCH_RECOVERY_DATUM_FAILED";string message=error.Message;if(message.Length>512)message=message[..512];
            return job.Error(unknown?"WRITE_OUTCOME_UNKNOWN":cause,unknown?"Native recovery datum creation started but remains unverified. Inspect the returned actual plane ID and the same failed graph before reuse; no support assignment, rollback or duplicate is claimed.":message,
                unknown?"unknown":"not_started",context,new{cause,stage,message,body_mc_id=binding==null?null:Key(binding.Before.Body.ID),sketch_id=binding==null?null:Key(binding.Scope.Sketch),
                    original_support_id=binding==null?null:Key(binding.Scope.OldSupport),plane_id=plane==null||plane.ID.IsNull?null:Key(plane.ID),native_details=(error as DomainException)?.Error.details,cleanup_error=cleanup?.GetType().Name})with{warnings=[WriteRecovery.Warning]};
        }
    }
}
