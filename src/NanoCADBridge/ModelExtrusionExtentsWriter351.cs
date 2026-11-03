using System.Runtime.CompilerServices;
using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class ModelExtrusionExtentsWriter351
{
    sealed record Binding(ModelExtrusionExtentsSpec351 Edit,ModelExtrusionExtentsState351.Snapshot Before,ModelExtrusionExtentsState351.Plan Plan,McObjectId[] DriverIds);
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok,string code,string message)=>ModelExtrusionExtentsState351.Need(ok,code,message);
    internal static bool Handles(ModifyArguments args)=>args.Operations.Any(edit=>edit is ModelExtrusionExtentsSpec351);
    [MethodImpl(MethodImplOptions.NoInlining)]
    static Binding Resolve(ModifyArguments args,DocumentState state,Db.Transaction tx,Action cancel)
    {
        Need(args.Operations.Length==1&&args.Operations[0] is ModelExtrusionExtentsSpec351,"INVALID_OPERATION_ORDER","Supply one retained extrusion extent revision.");
        Need(args.Units=="Millimeters"&&args.TargetSpace=="model"&&(int)state.Document.Database.Insunits==4,"OUT_OF_SCOPE","Use Millimeters in Model Space.");
        var loaded=AppDomain.CurrentDomain.GetAssemblies().Select(value=>value.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);Need(loaded.Contains("mapimgd")&&loaded.Contains("MapiBaseTypes"),"CAPABILITY_UNAVAILABLE","MultiCAD must already be loaded.");
        var edit=(ModelExtrusionExtentsSpec351)args.Operations[0];var drawing=ModelFeatureReorderState345.Scope(state,tx);var representation=CadStructureWriter.Find(state.Document.Database,edit.Handles.Single());var raw=tx.GetObject(representation,Db.OpenMode.ForRead);
        Need(raw.GetType()==typeof(Db.Solid3d)&&raw.OwnerId==ModelFeatureReorderState345.Model(state.Document.Database,tx),"OUT_OF_SCOPE","Select the exact existing top-level native part representation.");
        var id=McObjectId.FromHandle(representation.Handle.Value,drawing.ID);var body=id.GetObjectOfType<Mc3dSolid>();Need(body!=null&&body.ID==id&&ModelHistoryEditGuard.CurrentRepresentation(body,state,tx,cancel)==representation,"OBJECT_IDENTITY_MISMATCH","Use the unique current native part representation.");
        Need(ModelExtrusionExtentsState351.Same(body.LastPartItemID,edit.ExpectedLastItemId),"PRECONDITION_FAILED","Refresh the native history terminal.");
        var contents=ModelInventoryBudget.History(body,cancel);var native=contents.SingleOrDefault(value=>ModelExtrusionExtentsState351.Same(value,edit.FeatureId));var feature=native.IsNull?null:native.GetObjectOfType<ExtrudeFeature>();
        if(feature==null||feature.ID!=native)
            throw new DomainException("OUT_OF_SCOPE","Select this actual existing native extrusion.",false,
                ModelExtrusionScopeDiagnostics412.Describe(edit.FeatureId,body,drawing.ID,representation,contents,state,tx,cancel));
        var before=ModelExtrusionExtentsState351.Capture(body,feature,args,state,tx,cancel,includeStandalone:true);
        Need(ModelExtrusionExtentsState351.Same(before.Profile.ID,edit.ExpectedProfileId)&&ModelExtrusionExtentsState351.Same(before.Sketch.ID,edit.ExpectedSketchId)&&before.Token==edit.ExpectedExtentToken,"PRECONDITION_FAILED","Exact extent/profile/parent/driver/consumer state changed; refresh extrusion_extent_ids.");
        var plan=ModelExtrusionExtentsState351.Prepare(before,edit,cancel);Need(feature.Direction!=(edit.Direction=="different"?FeatureExtentDirection.Different:FeatureExtentDirection.Symmetric)||plan.Changed.Count>0||
            edit.Direction=="different"&&(feature.Extent2!=ModelExtrusionExtentsState351.SecondaryMode(edit)||(edit.Extent2=="distance"&&!ModelExtrusionExtentsState351.RequestedEqual(feature.Distance2,edit.NativeDistance2!.Value))||!ModelExtrusionExtentsState351.RequestedEqual(feature.Angle2*180/Math.PI,edit.TaperAngle2!.Value*180/Math.PI)),"NO_CHANGE","The same native extents are already stored.");
        return new(edit,before,plan,ModelExtrusionExtentsState351.Variables(feature));
    }
    internal static void Preflight(ModifyArguments args,DocumentState state,Db.Transaction tx,Action? cancel=null)=>Resolve(args,state,tx,cancel??(()=>{}));
    static ModelExtrusionExtentsState351.Snapshot Current(Binding binding,ModifyArguments args,DocumentState state,Action cancel)
    {
        using var tx=state.Document.Database.TransactionManager.StartTransaction();ModelFeatureReorderState345.Scope(state,tx,binding.Before.DrawingDocument);var feature=binding.Before.Feature.ID.GetObjectOfType<ExtrudeFeature>();Need(feature!=null,"MODEL_STATE_UNAVAILABLE","The retained native extrusion disappeared.");
        return ModelExtrusionExtentsState351.Capture(binding.Before.Body,feature,args,state,tx,cancel,includeStandalone:true);
    }
    static Result Outcome(Binding binding,ModifyArguments args,WriteJob job,ReadContext context,ModelExtrusionExtentsState351.Snapshot after,bool estimate)
    {
        var source=new EntityRef(args.BridgeId,args.DocumentId,binding.Edit.Handles.Single());var body=new EntityRef(args.BridgeId,args.DocumentId,estimate?"FFFFFFFFFFFFFFFF":Validation.NormalizeHandle(after.Representation.Handle.ToString()));bool replaced=estimate||source.handle!=body.handle;
        var created=estimate?new[]{"{FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF}","{FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF}"}:ModelExtrusionExtentsState351.CreatedVariables(binding.Before,after);
        var extents=ModelExtrusionExtentsReader351.Describe(after);
        var ownedChange=estimate?new ModelExtrusionOwnedMetadataChange357(int.MaxValue,new string('F',64),false,ModelExtrusionOwnedMetadata357.Scope):
            ModelExtrusionOwnedMetadata357.Describe(binding.Before.Metadata,after.Metadata,job.CheckCancellation);
        // Reserve potential native secondary variables without publishing invented IDs.
        if(estimate)
        {
            string widest="{FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF}";var reserved=Wire.Element(new{mc_id=widest,name=new string('\uFFFF',8192),expression=new string('\uFFFF',8192),type="Model3D, Reference",value=double.MaxValue,value_available=true,value_unit="native_angular_units_unverified",host_object_id=widest,downstream_variables=Array.Empty<string>(),downstream_objects=new[]{widest}});
            extents=extents with{distance2_variable_id=widest,taper_angle2_variable_id=widest,driver_state=Wire.Element(extents.driver_state.EnumerateArray().Concat(new[]{reserved,reserved}).ToArray())};
        }
        var mapping=new ModelExtrusionExtentsChange351(0,source,body,after.Body.ID.ToString(),after.Feature.ID.ToString(),after.Profile.ID.ToString(),after.Sketch.ID.ToString(),after.OwnerDocument,
            binding.Before.Views.LastHistoryItem.ToString(),replaced,extents,binding.Plan.Changed.OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray(),binding.Plan.Affected.Except(binding.Plan.Changed,StringComparer.OrdinalIgnoreCase).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray(),created,after.Downstream,after.UnobservedGeometry,ownedChange);
        var data=new ModelExtrusionExtentsOutput351(args.OperationId,"committed",false,[body],replaced?[source]:[],replaced?[body]:[],[],"not_guaranteed",[mapping]);
        var warnings=binding.Before.Warnings.Concat(after.Warnings).Concat(estimate?[ModelFeatureReorderState345.ValidityWarning]:Array.Empty<string>()).Append(WriteRecovery.Warning).Append("NATIVE_EXTRUSION_EXTENTS_CHANGED: Refresh the returned body/history/variables and review the intended shape and linked drawings through MCP. Native Symmetric uses one retained length driver; body WCS bounds describe the complete part, not an isolated feature's span.")
            .Concat(estimate||ownedChange.added_manager_count>0?[ModelExtrusionOwnedMetadata357.Warning]:Array.Empty<string>())
            .Concat(after.UnobservedGeometry.Length>0?["DERIVED_GEOMETRY_PARTLY_UNOBSERVED: Listed native identities/dependencies and available geometry remain checked; unsupported curve details are not claimed verified."]:Array.Empty<string>()).Distinct(StringComparer.Ordinal).ToArray();
        return Result.Ok(job.OriginalRequestId,data,context,warnings);
    }
    static void Budget(Result result,Action cancel)
    {cancel();Need(JsonSerializer.SerializeToUtf8Bytes(result,Wire.Json).Length<=Wire.PayloadBudget,"RESPONSE_LIMIT_EXCEEDED","The complete extent receipt exceeds actual transport bytes; select/read a smaller related group, no CAD object-count ceiling is imposed.");}
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Result Execute(ModifyArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        Binding? binding=null;McUndoPoint? tracker=null;bool started=false;string stage="preparation";
        void Trace(string next){stage=next;log.Write(job.Operation,job.OriginalRequestId,"model_extrusion_extents351_checkpoint",transaction:next,operationId:job.OperationId);}
        void Close(){var value=tracker;tracker=null;if(value!=null)try{value.Stop();}finally{value.Dispose();}}
        try
        {
            using(var tx=state.Document.Database.TransactionManager.StartTransaction())binding=Resolve(args,state,tx,job.CheckCancellation);var fresh=Current(binding,args,state,job.CheckCancellation);
            Need(binding.Before.Token==fresh.Token&&binding.Before.Inventory.SetEquals(fresh.Inventory),"PRECONDITION_FAILED","Native state changed immediately before extent revision.");
            Budget(ModelViewFollowers.ReserveWarnings(Outcome(binding,args,job,context,fresh,true),binding.Before.Views),job.CheckCancellation);job.CheckCancellation();ModelViewFollowers.CheckStandalone(binding.Before.Body,binding.Before.Views,job.CheckCancellation);tracker=new McUndoPoint(false);tracker.Start();started=true;
            var f=binding.Before.Feature;var edit=binding.Edit;Trace("set_native_extent_direction");f.TryModify((uint)McDbObject.ObjChangesType.Geom);f.Direction=edit.Direction=="different"?FeatureExtentDirection.Different:FeatureExtentDirection.Symmetric;f.Extent=ExtrudeParamExtType.Dist;
            if(edit.Direction=="different")f.Extent2=ModelExtrusionExtentsState351.SecondaryMode(edit);
            var currentIds=ModelExtrusionExtentsState351.Variables(f);for(int i=0;i<binding.DriverIds.Length;i++)if(!binding.DriverIds[i].IsNull)Need(currentIds[i]==binding.DriverIds[i],"MODEL_DRIVER_CHANGED","Native mode activation replaced a protected owning driver ID.");
            void Set(int index,double scalar)
            {var id=binding.DriverIds[index];if(!id.IsNull&&binding.Plan.Changed.Contains(id.ToString()))ModelNumericDriverEditor.Set(f,id,scalar,edit.ReplaceDriverFormulas);}
            Trace("set_explicit_native_drivers");Set(0,edit.NativeDistance);Set(1,edit.TaperAngle*180/Math.PI);
            if(edit.Direction=="different")
            {
                if(edit.Extent2=="distance"){if(binding.DriverIds[2].IsNull)f.Distance2=edit.NativeDistance2!.Value;else Set(2,edit.NativeDistance2!.Value);}
                if(binding.DriverIds[3].IsNull)f.Angle2=edit.TaperAngle2!.Value;else Set(3,edit.TaperAngle2!.Value*180/Math.PI);
            }
            f.DbEntity.Update();McObjectManager.UpdateAll();Trace("native_extent_values_updated");Need(binding.Before.Body.RebuildPart(),"MODEL_UPDATE_FAILED","The retained native part rebuild rejected its extents.");McObjectManager.UpdateAll();Trace("native_rebuild_returned");
            var after=Current(binding,args,state,job.CheckCancellation);ModelExtrusionExtentsState351.Verify(binding.Before,after,edit,binding.Plan,job.CheckCancellation);Budget(Outcome(binding,args,job,context,after,false),job.CheckCancellation);
            Trace("native_extents_verified");job.BeginCommit();Close();after=Current(binding,args,state,job.CheckCancellation);ModelExtrusionExtentsState351.Verify(binding.Before,after,edit,binding.Plan,job.CheckCancellation);
            var result=Outcome(binding,args,job,context,after,false);Budget(result,job.CheckCancellation);result=ModelViewFollowers.WithWarnings(result,binding.Before.Views);job.RecordCommit(result);Trace("commit_recorded");return result;
        }
        catch(Exception error)
        {
            if(job.CommittedResult is {} committed)return committed;Exception? cleanup=null;try{Close();}catch(Exception e){cleanup=e;}bool unknown=started||cleanup!=null;
            string cause=error is DomainException domain?domain.Error.code:error is OperationCanceledException?"CANCELLED":"MODEL_EXTRUSION_EXTENTS_FAILED";string message=error.Message;if(message.Length>512)message=message[..512];
            return job.Error(unknown?"WRITE_OUTCOME_UNKNOWN":cause,unknown?"Native extent revision started but remains unverified. Inspect this retained body/profile/driver state before dependent writes; no rollback is claimed. Replay only the identical request in this bridge lifetime.":message,
                unknown?"unknown":"not_started",context,new{cause,stage,message,body_mc_id=binding?.Before.Body.ID.ToString(),feature_id=binding?.Edit.FeatureId,profile_id=binding?.Edit.ExpectedProfileId,sketch_id=binding?.Edit.ExpectedSketchId,native_details=(error as DomainException)?.Error.details,cleanup_error=cleanup?.GetType().Name}) with{warnings=[WriteRecovery.Warning]};
        }
    }
}
