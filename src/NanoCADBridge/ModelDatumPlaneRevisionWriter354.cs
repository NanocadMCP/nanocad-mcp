using System.Runtime.CompilerServices;
using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class ModelDatumPlaneRevisionWriter354
{
    sealed record Binding(ModelDatumPlaneRevisionSpec354 Edit,ModelDatumPlaneRevisionState354.Snapshot Before);
    internal static bool Handles(ModifyArguments args)=>args.Operations.Any(edit=>edit is ModelDatumPlaneRevisionSpec354);
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)]bool ok,string code,string message)=>ModelDatumPlaneRevisionState354.Need(ok,code,message);
    static void Writable(ModelDatumPlaneRevisionState354.Snapshot snapshot,Db.Transaction tx)
    {
        var representations=new List<Db.ObjectId>{snapshot.Part.Representation};if(snapshot.Datum.Representation.HasValue)representations.Add(snapshot.Datum.Representation.Value);
        foreach(var id in representations)
        {
            var entity=(Db.Entity)tx.GetObject(id,Db.OpenMode.ForRead);var layer=(Db.LayerTableRecord)tx.GetObject(entity.LayerId,Db.OpenMode.ForRead);
            Need(!layer.IsLocked&&!layer.IsOff&&!layer.IsFrozen&&!layer.IsDependent,"LAYER_UNAVAILABLE","The actual part or represented native datum layer is unavailable for definition revision.");
        }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static Binding Resolve(ModifyArguments args,DocumentState state,Db.Transaction tx,Action cancel)
    {
        Need(args.Operations.Length==1&&args.Operations[0] is ModelDatumPlaneRevisionSpec354,"INVALID_OPERATION_ORDER","Revise one retained native datum and its actual same-part consumers per request.");
        Need(args.Units=="Millimeters"&&args.TargetSpace=="model"&&(int)state.Document.Database.Insunits==4,"OUT_OF_SCOPE","Use Millimeters in Model Space.");
        var loaded=AppDomain.CurrentDomain.GetAssemblies().Select(item=>item.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Need(loaded.Contains("mapimgd")&&loaded.Contains("MapiBaseTypes"),"CAPABILITY_UNAVAILABLE","MultiCAD must already be loaded.");
        var edit=(ModelDatumPlaneRevisionSpec354)args.Operations[0];var body=ModelDatumPlaneRevisionReader354.Body(edit.Handles.Single(),state,tx,cancel,true);
        var plane=ModelDatumPlaneState353.Resolve(edit.PlaneId,ModelProfileDiscovery348.OwnerDocument(body,state),cancel);
        var before=ModelDatumPlaneRevisionState354.Capture(body,plane,args,state,tx,cancel);
        Writable(before,tx);
        bool consumers=edit.ExpectedDirectConsumerIds!=null?edit.ExpectedDirectConsumerIds.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(before.DirectConsumers):
            edit.ExpectedMirrorIds.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(before.Mirrors.Select(item=>item.feature_id))&&before.DirectConsumers.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(before.Mirrors.Select(item=>item.feature_id));
        Need(body.LastPartItemID.ToString().Equals(edit.ExpectedLastItemId,StringComparison.OrdinalIgnoreCase)&&before.Datum.Token.Equals(edit.ExpectedDatumToken,StringComparison.OrdinalIgnoreCase)&&before.Token.Equals(edit.ExpectedRevisionToken,StringComparison.OrdinalIgnoreCase)&&consumers,
            "PRECONDITION_FAILED","The exact datum, actual direct consumers, retained part/driver/source state or last history item changed; refresh datum_plane_revision_ids.");
        Need(!ModelDatumPlaneRevisionState354.SamePhysical(before.Datum.Read,edit.Plane),"NO_CHANGE","The same oriented physical datum plane is already stored; in-plane frame axes are not an independent revision.");
        return new(edit,before);
    }
    internal static void Preflight(ModifyArguments args,DocumentState state,Db.Transaction tx,Action? cancel=null)=>Resolve(args,state,tx,cancel??(()=>{}));
    static ModelDatumPlaneRevisionState354.Snapshot Current(Binding binding,ModifyArguments args,DocumentState state,Action cancel,bool forWrite=false)
    {
        using var tx=state.Document.Database.TransactionManager.StartTransaction();ModelFeatureReorderState345.Scope(state,tx,binding.Before.Part.DrawingDocument);
        var plane=ModelDatumPlaneState353.Resolve(binding.Edit.PlaneId,binding.Before.Datum.Document,cancel);
        var snapshot=ModelDatumPlaneRevisionState354.Capture(binding.Before.Part.Body,plane,args,state,tx,cancel);if(forWrite)Writable(snapshot,tx);return snapshot;
    }
    const string SuccessWarning="NATIVE_FIXED_DATUM_REVISED: The same fixed WorkPlane, actual selected-part sketch/profile/feature and Mirror identities, source links and driving intent were verified after native rebuild. Owned sketch geometry is checked in its observed local frame; immutable sources retain their actual geometry. Native in-plane frame representation is not a separately constrained axis. Review the related part and drawings through MCP; recovery and Undo are not guaranteed.";
    static Result Outcome(Binding binding,ModelDatumPlaneRevisionState354.Snapshot after,ModifyArguments args,WriteJob job,ReadContext context,bool estimate=false,string[]? verifiedWarnings=null)
    {
        var source=new EntityRef(args.BridgeId,args.DocumentId,binding.Edit.Handles.Single());var description=ModelDatumPlaneRevisionReader354.Describe(after,args.BridgeId,args.DocumentId);
        if(estimate)description=description with{body_ref=new(args.BridgeId,args.DocumentId,"FFFFFFFFFFFFFFFF")};
        var current=description.body_ref;bool replaced=source.handle!=current.handle;
        var oldDatum=binding.Before.Datum.Representation.HasValue?new EntityRef(args.BridgeId,args.DocumentId,Validation.NormalizeHandle(binding.Before.Datum.Representation.Value.Handle.ToString())):null;
        var nativeRepresentations=estimate?new[]{new EntityRef(args.BridgeId,args.DocumentId,"FFFFFFFFFFFFFFFE")}:
            after.Datum.Representation.HasValue?new[]{new EntityRef(args.BridgeId,args.DocumentId,Validation.NormalizeHandle(after.Datum.Representation.Value.Handle.ToString()))}:Array.Empty<EntityRef>();
        bool datumReplaced=oldDatum?.handle!=nativeRepresentations.SingleOrDefault()?.handle;
        var modified=new List<EntityRef>{current};modified.AddRange(nativeRepresentations);
        var erased=new List<EntityRef>();if(replaced)erased.Add(source);if(datumReplaced&&oldDatum!=null)erased.Add(oldDatum);
        var created=new List<EntityRef>();if(replaced)created.Add(current);if(datumReplaced)created.AddRange(nativeRepresentations);
        var change=new ModelDatumPlaneRevisionChange354(0,source,current,replaced,datumReplaced,nativeRepresentations,description);
        var result=Result.Ok(job.OriginalRequestId,new ModelDatumPlaneRevisionOutput354(args.OperationId,"committed",false,modified.ToArray(),erased.ToArray(),created.ToArray(),[],"not_guaranteed",[change]),context,
            binding.Before.Part.Warnings.Concat(after.Part.Warnings).Concat(verifiedWarnings??[]).Append(WriteRecovery.Warning).Append(SuccessWarning).Distinct(StringComparer.Ordinal).ToArray());
        return ModelViewFollowers.WithWarnings(result,binding.Before.Views);
    }
    static void Budget(Result result,Action cancel)
    {cancel();Need(JsonSerializer.SerializeToUtf8Bytes(result,Wire.Json).Length<=Wire.PayloadBudget,"RESPONSE_LIMIT_EXCEEDED","The complete native datum revision receipt exceeds actual transport bytes.");}
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Result Execute(ModifyArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        Binding? binding=null;McUndoPoint? tracker=null;bool started=false;string stage="preparation";
        void Trace(string next){stage=next;log.Write(job.Operation,job.OriginalRequestId,"model_datum_plane_revision354_checkpoint",transaction:next,operationId:job.OperationId);}
        void Close(){var value=tracker;tracker=null;if(value==null)return;try{value.Stop();}finally{value.Dispose();}}
        try
        {
            using(var tx=state.Document.Database.TransactionManager.StartTransaction())binding=Resolve(args,state,tx,job.CheckCancellation);
            var current=Current(binding,args,state,job.CheckCancellation,forWrite:true);
            Need(current.Token==binding.Before.Token&&current.Part.Inventory.SetEquals(binding.Before.Part.Inventory),"PRECONDITION_FAILED","Native datum/part state changed immediately before its definition setter.");
            ModelViewFollowers.CheckStandalone(binding.Before.Part.Body,binding.Before.Views,job.CheckCancellation);
            Budget(ModelViewFollowers.ReserveWarnings(Outcome(binding,current,args,job,context,true),binding.Before.Views),job.CheckCancellation);job.CheckCancellation();tracker=new McUndoPoint(false);tracker.Start();started=true;
            var plane=binding.Before.Datum.Plane;Trace("set_retained_fixed_native_definition");plane.TryModify((uint)McDbObject.ObjChangesType.Geom);
            using(var geometry=DrawingWorkplaneNative.Plane(binding.Edit.Plane))Need(plane.SetFixedDefinition(geometry),"MODEL_UPDATE_FAILED","Native retained fixed-plane definition was rejected.");
            plane.DbEntity.Update();McObjectManager.UpdateAll();Need(binding.Before.Part.Body.RebuildPart(),"MODEL_UPDATE_FAILED","Native part rebuild rejected its changed functional datum.");McObjectManager.UpdateAll();Trace("native_rebuild_returned");
            current=Current(binding,args,state,job.CheckCancellation);var warnings=ModelDatumPlaneRevisionState354.Verify(binding.Before,current,binding.Edit,job.CheckCancellation);Trace("native_datum_and_consumers_verified");
            job.BeginCommit();Close();current=Current(binding,args,state,job.CheckCancellation);warnings=warnings.Concat(ModelDatumPlaneRevisionState354.Verify(binding.Before,current,binding.Edit,job.CheckCancellation)).Distinct(StringComparer.Ordinal).ToArray();
            var result=Outcome(binding,current,args,job,context,verifiedWarnings:warnings);Budget(result,job.CheckCancellation);job.RecordCommit(result);Trace("commit_recorded");return result;
        }
        catch(Exception error)
        {
            if(job.CommittedResult is {} committed)return committed;Exception? cleanup=null;try{Close();}catch(Exception exception){cleanup=exception;}bool unknown=started||cleanup!=null;
            string cause=error is DomainException domain?domain.Error.code:error is OperationCanceledException?"CANCELLED":"MODEL_DATUM_REVISION_FAILED";string message=error.Message;if(message.Length>512)message=message[..512];
            return job.Error(unknown?"WRITE_OUTCOME_UNKNOWN":cause,unknown?"Native retained datum revision started but remains unverified. Inspect the exact plane, part, mirrors and current drawing state before dependent work. No rollback or replacement datum is claimed; replay only the identical request in this bridge lifetime.":message,
                unknown?"unknown":"not_started",context,new{cause,stage,message,plane_id=binding?.Edit.PlaneId,body_mc_id=binding?.Before.Part.Body.ID.ToString(),mc_document_id=binding?.Before.Datum.Document.ID.ToString(),cleanup_error=cleanup?.GetType().Name})with{warnings=[WriteRecovery.Warning]};
        }
    }
}
