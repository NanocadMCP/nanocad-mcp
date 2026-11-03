using System.Runtime.CompilerServices;
using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class ModelFaceDatumPlaneWriter356
{
    sealed record Binding(ModelFaceDatumPlaneCreate356 Edit,ModelDatumPlaneState353.Part Before,McDocument Owner,McObjectId[] ExistingPlanes,ModelFaceDatumPlaneState356.Source Source);
    internal static bool Handles(ModifyArguments args)=>args.Operations.Any(edit=>edit is ModelFaceDatumPlaneCreate356);
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool value,string code,string message)=>ModelDatumPlaneState353.Need(value,code,message);
    [MethodImpl(MethodImplOptions.NoInlining)]
    static Binding Resolve(ModifyArguments args,DocumentState state,Db.Transaction tx,Action cancel)
    {
        Need(args.Operations.Length==1&&args.Operations[0] is ModelFaceDatumPlaneCreate356,"INVALID_OPERATION_ORDER","Create one reusable native plane per request.");
        Need(args.TargetSpace=="model"&&args.Units=="Millimeters"&&(int)state.Document.Database.Insunits==4,"OUT_OF_SCOPE","Use Millimeters in Model Space.");
        var loaded=AppDomain.CurrentDomain.GetAssemblies().Select(item=>item.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Need(loaded.Contains("mapimgd")&&loaded.Contains("MapiBaseTypes"),"CAPABILITY_UNAVAILABLE","MultiCAD must already be loaded.");
        var edit=(ModelFaceDatumPlaneCreate356)args.Operations[0];var drawing=ModelFeatureReorderState345.Scope(state,tx);var raw=CadStructureWriter.Find(state.Document.Database,edit.Handles.Single());
        var entity=tx.GetObject(raw,Db.OpenMode.ForRead);Need(entity.GetType()==typeof(Db.Solid3d)&&entity.OwnerId==ModelFeatureReorderState345.Model(state.Document.Database,tx),"OUT_OF_SCOPE","Choose the exact current top-level native part.");
        var native=McObjectId.FromHandle(raw.Handle.Value,drawing.ID);var body=native.IsNull?null:native.GetObjectOfType<Mc3dSolid>();
        Need(body!=null&&body.ID==native&&ModelHistoryEditGuard.CurrentRepresentation(body,state,tx,cancel)==raw,"OBJECT_IDENTITY_MISMATCH","The current native part identity is unavailable.");
        Need(body.LastPartItemID.ToString().Equals(edit.ExpectedLastItemId,StringComparison.OrdinalIgnoreCase),"PRECONDITION_FAILED","Refresh the current part terminal before creating construction.");
        var before=ModelDatumPlaneState353.CapturePart(body,args,state,tx,cancel);var owner=ModelProfileDiscovery348.OwnerDocument(body,state);var planes=ModelProfileDiscovery348.Ids<WorkPlane>(owner,cancel);
        foreach(var id in planes){cancel();Need(!ModelDatumPlaneState353.Resolve(id.ToString(),owner,cancel).Name.Equals(edit.Name,StringComparison.OrdinalIgnoreCase),"DATUM_NAME_EXISTS","Choose a distinct native WorkPlane name in this actual owner document.");}
        var layer=(Db.LayerTableRecord)tx.GetObject(((Db.Solid3d)entity).LayerId,Db.OpenMode.ForRead);Need(!layer.IsLocked&&!layer.IsOff&&!layer.IsFrozen&&!layer.IsDependent,"LAYER_UNAVAILABLE","The native source part layer is unavailable.");
        var sources=ModelFaceDatumPlaneState356.Select(before,edit,state,args.BridgeId,cancel);return new(edit,before,owner,planes,sources);
    }
    internal static void Preflight(ModifyArguments args,DocumentState state,Db.Transaction tx,Action? cancel=null)=>Resolve(args,state,tx,cancel??(()=>{}));
    static void Budget(Result result,Action cancel)
    {cancel();Need(JsonSerializer.SerializeToUtf8Bytes(result,Wire.Json).Length<=Wire.PayloadBudget,"RESPONSE_LIMIT_EXCEEDED","The complete native datum receipt exceeds actual transport bytes.");}
    static Result Estimate(Binding binding,ModifyArguments args,WriteJob job,ReadContext context)
    {
        string id="{FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF}";var source=binding.Source;
        var relation=new ModelFaceDatumPlaneRelationRead356("face_angle",binding.Before.Body.ID.ToString(),source.Face.ToString(),source.Edge.ToString(),source.FaceGeometry,source.EdgeGeometry,source.Token,
            Math.Acos(Math.Clamp(Math.Abs(Math.Cos(binding.Edit.NativeAngleRadians)),0,1)),binding.Edit.NativeAngleRadians,new string('F',512));
        var driver=Wire.Element(new{mc_id=id,name=new string('\uFFFF',8192),expression=new string('\uFFFF',8192),type="Model3D, Reference",value=double.MaxValue,value_available=true,
            host_object_id=id,downstream_variables=binding.Before.Parameters.GetProperty("variables").EnumerateArray().Select(v=>v.GetProperty("mc_id").GetString()).ToArray(),
            downstream_objects=binding.Before.Contents.Select(value=>value.ToString()).ToArray(),scalar_scope="native_angular_units_unverified"});
        var read=new ModelDatumPlaneRead353(id,binding.Edit.Name,binding.Owner.ID.ToString(),new string('F',64),"PlaneLineNAngle",Wire.Element(new{reserved_native_plane_frame=new string('F',1024)}),
            binding.Edit.NativeAngleRadians,id,driver,Wire.Element(new object[]{new{native_id=source.Face.ToString(),IsNull=false,marker=long.MaxValue,data=int.MaxValue,native_scalar=double.MaxValue},
                new{native_id=source.Edge.ToString(),IsNull=false,marker=long.MaxValue,data=int.MaxValue,native_scalar=double.MaxValue},new{native_id=(string?)null,IsNull=true,marker=long.MaxValue,data=int.MaxValue,native_scalar=double.MaxValue}}),
            binding.Before.Body.ID.ToString(),true,binding.Edit.ExpectedLastItemId,"Ok",false,false,binding.Before.Contents.Select(value=>value.ToString()).ToArray(),[],[],
            Wire.Element(new{handle="FFFFFFFFFFFFFFFF",reserved_native_representation_metadata=new string('\uFFFF',16384)}),new string('F',512),relation);
        var body=new EntityRef(args.BridgeId,args.DocumentId,binding.Edit.Handles.Single());var representation=new EntityRef(args.BridgeId,args.DocumentId,"FFFFFFFFFFFFFFFF");
        return Result.Ok(job.OriginalRequestId,new ModelDatumPlaneOutput353(args.OperationId,"committed",false,[],[],[representation],[],"not_guaranteed",
            [new(0,body,binding.Before.Body.ID.ToString(),binding.Edit.ExpectedLastItemId,read,[representation],binding.Edit.NativeAngleRadians,[id])]),context,[WriteRecovery.Warning,SuccessWarning,ModelFeatureReorderState345.ValidityWarning]);
    }
    const string SuccessWarning="NATIVE_FACE_ANGLE_DATUM_CREATED: Native planar-face/lying-edge definition sources, actual owner/plane/unsigned dihedral and observed owning angular driver were checked. Signed input radians follow the SDK source/frame convention; current ParamVal can be rebased and is not assumed equal to initial angle or WCS rotation. Read the actual oriented plane and fresh token before Mirror reuse. No internal sketch solver, fixed-coordinate substitute or complete private solver-state claim. Review the related engineering feature group through MCP.";
    static (ModelDatumPlaneState353.Part Part,ModelDatumPlaneState353.Snapshot Datum) Current(Binding binding,WorkPlane plane,ModifyArguments args,DocumentState state,Action cancel)
    {
        using var tx=state.Document.Database.TransactionManager.StartTransaction();ModelFeatureReorderState345.Scope(state,tx,binding.Before.DrawingDocument);
        var part=ModelDatumPlaneState353.CapturePart(binding.Before.Body,args,state,tx,cancel);var datum=ModelDatumPlaneState353.Capture(plane,binding.Before.Body,state,tx,cancel);
        ModelFaceDatumPlaneState356.VerifyCreated(binding.Before,part,datum,binding.Source,binding.Edit,cancel);return(part,datum);
    }
    static Result Outcome(Binding binding,ModifyArguments args,WriteJob job,ReadContext context,ModelDatumPlaneState353.Part part,ModelDatumPlaneState353.Snapshot datum)
    {
        var body=new EntityRef(args.BridgeId,args.DocumentId,Validation.NormalizeHandle(part.Representation.Handle.ToString()));var references=datum.Representation.HasValue?
            new[]{new EntityRef(args.BridgeId,args.DocumentId,Validation.NormalizeHandle(datum.Representation.Value.Handle.ToString()))}:Array.Empty<EntityRef>();
        var oldVariables=binding.Before.Parameters.GetProperty("variables").EnumerateArray().Select(value=>value.GetProperty("mc_id").GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var created=part.Parameters.GetProperty("variables").EnumerateArray().Select(value=>value.GetProperty("mc_id").GetString()!).Where(id=>!oldVariables.Contains(id)).ToArray();
        var change=new ModelDatumPlaneChange353(0,body,part.Body.ID.ToString(),part.Body.LastPartItemID.ToString(),datum.Read,references,binding.Edit.NativeAngleRadians,created);
        return Result.Ok(job.OriginalRequestId,new ModelDatumPlaneOutput353(args.OperationId,"committed",false,[],[],references,[],"not_guaranteed",[change]),context,
            part.Warnings.Append(WriteRecovery.Warning).Append(SuccessWarning).Distinct(StringComparer.Ordinal).ToArray());
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Result Execute(ModifyArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        Binding? binding=null;WorkPlane? plane=null;McUndoPoint? tracker=null;bool started=false;string stage="preparation";
        void Trace(string next){stage=next;log.Write(job.Operation,job.OriginalRequestId,"model_face_datum_plane356_checkpoint",transaction:next,operationId:job.OperationId);}
        void Close(){var value=tracker;tracker=null;if(value!=null)try{value.Stop();}finally{value.Dispose();}}
        try
        {
            using(var tx=state.Document.Database.TransactionManager.StartTransaction())binding=Resolve(args,state,tx,job.CheckCancellation);
            Budget(ModelViewFollowers.ReserveWarnings(Estimate(binding,args,job,context),binding.Before.Views),job.CheckCancellation);job.CheckCancellation();ModelViewFollowers.CheckStandalone(binding.Before.Body,binding.Before.Views,job.CheckCancellation);tracker=new McUndoPoint(false);tracker.Start();started=true;Trace("construct_native_workplane");
            plane=new WorkPlane();var candidate=plane.ID;
            Need(candidate.IsNull||!binding.ExistingPlanes.Contains(candidate)&&!binding.Before.Contents.Contains(candidate),"OBJECT_IDENTITY_MISMATCH","Native plane construction returned an already retained identity; it is not reset or registered as new.");
            Need(plane.DbEntity.Document==null||plane.DbEntity.Document.ID==binding.Owner.ID,"DATUM_DOCUMENT_MISMATCH","The new native plane already belongs to another actual document; it is not reassigned.");
            Trace("register_actual_owner_document");Need(McObjectManager.Add2Document(plane.DbEntity,binding.Owner),"MODEL_UPDATE_FAILED","Native explicit owner registration was rejected.");
            Need(!plane.ID.IsNull&&!binding.ExistingPlanes.Contains(plane.ID)&&!binding.Before.Contents.Contains(plane.ID)&&plane.DbEntity.Document?.ID==binding.Owner.ID,
                "DATUM_DOCUMENT_MISMATCH","The registered native plane lacks a fresh matching actual owner identity.");
            Trace("set_native_face_line_angle_definition");
            var face=new McGeomParam();var edge=new McGeomParam();var angle=new McGeomParam{Param=binding.Edit.NativeAngleRadians};
            Need(face.Init(binding.Source.Face,binding.Source.FacePoint)&&edge.Init(binding.Source.Edge,binding.Source.EdgePoint),"DATUM_SOURCE_GEOMETRY_UNAVAILABLE","Native face/edge definition reference initialization was rejected.");
            Need(face.ID==binding.Source.Face&&edge.ID==binding.Source.Edge,"DATUM_SOURCE_SCOPE_MISMATCH","Native definition initialization changed the selected source identities.");
            Need(plane.SetDefinition(WPLDefinitionType.PlaneLineNAngle,new[]{face,edge,angle}),"MODEL_UPDATE_FAILED","Native linked face/line/angular plane definition was rejected.");
            plane.Name=binding.Edit.Name;plane.DbEntity.Update();McObjectManager.UpdateAll();Trace("native_plane_update_returned");
            var current=Current(binding,plane,args,state,job.CheckCancellation);Budget(Outcome(binding,args,job,context,current.Part,current.Datum),job.CheckCancellation);
            Trace("native_datum_verified");job.BeginCommit();Close();current=Current(binding,plane,args,state,job.CheckCancellation);var result=Outcome(binding,args,job,context,current.Part,current.Datum);Budget(result,job.CheckCancellation);job.RecordCommit(result);Trace("commit_recorded");return result;
        }
        catch(Exception error)
        {
            if(job.CommittedResult is {} committed)return committed;Exception? cleanup=null;try{Close();}catch(Exception e){cleanup=e;}bool unknown=started||cleanup!=null;
            string cause=error is DomainException domain?domain.Error.code:error is OperationCanceledException?"CANCELLED":"MODEL_FACE_DATUM_PLANE_FAILED";string message=error.Message;if(message.Length>512)message=message[..512];
            return job.Error(unknown?"WRITE_OUTCOME_UNKNOWN":cause,unknown?"Native face-angle datum construction started but remains unverified. Inspect the actual native ID/document/body state; no rollback, body ownership or reusable datum is claimed. Replay only the identical request in this bridge lifetime.":message,
                unknown?"unknown":"not_started",context,new{cause,stage,message,plane_id=plane==null?null:ModelDatumPlaneState353.Id(plane.ID),body_mc_id=binding?.Before.Body.ID.ToString(),mc_document_id=binding?.Owner.ID.ToString(),source_face_id=binding?.Source.Face.ToString(),source_edge_id=binding?.Source.Edge.ToString(),requested_native_angle_radians=binding?.Edit.NativeAngleRadians,cleanup_error=cleanup?.GetType().Name})with{warnings=[WriteRecovery.Warning]};
        }
    }
}
