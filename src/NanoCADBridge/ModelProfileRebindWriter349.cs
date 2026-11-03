using System.Runtime.CompilerServices;
using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// Existing-feature mutation only. No feature factory, owner reset, cascade,
// order setter, solver or coordinate reconstruction implements this operation.
internal static class ModelProfileRebindWriter349
{
    const string ReviewWarning="NATIVE_PROFILE_REBOUND: Refresh body/history/variables and review the rebuilt part and linked drawings through MCP; use the returned current body_ref before another edit.";
    const string OldWarning="PREVIOUS_PROFILE_RETAINED: Its native ownership is observed in previous_profile; release and later reuse are not guaranteed or performed by this operation.";
    sealed record Binding(ModelProfileRebindSpec349 Edit,ExtrudeFeature Feature,ModelProfileRebindState349.Snapshot Before,
        ModelProfileRebindSelection430.Plan? Selection=null,bool SelectionCreated=false);
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok,string code,string message)=>ModelProfileRebindState349.Need(ok,code,message);
    internal static bool Handles(ModifyArguments args)=>args.Operations.Any(edit=>edit is ModelProfileRebindSpec349);
    [MethodImpl(MethodImplOptions.NoInlining)]
    static Binding Resolve(ModifyArguments args,DocumentState state,Db.Transaction tx,Action cancel)
    {
        Need(args.Operations.Length==1&&args.Operations[0] is ModelProfileRebindSpec349,"INVALID_OPERATION_ORDER","Native extrusion profile rebind must be the sole operation.");
        Need(args.TargetSpace=="model"&&(int)state.Document.Database.Insunits==4,"OUT_OF_SCOPE","Use Millimeters Model Space for retained native profile editing.");
        var loaded=AppDomain.CurrentDomain.GetAssemblies().Select(value=>value.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Need(loaded.Contains("mapimgd")&&loaded.Contains("MapiBaseTypes"),"CAPABILITY_UNAVAILABLE","MultiCAD must already be loaded.");
        var edit=(ModelProfileRebindSpec349)args.Operations[0];var drawing=ModelFeatureReorderState345.Scope(state,tx);
        var representation=CadStructureWriter.Find(state.Document.Database,edit.Handles.Single());
        var solid=tx.GetObject(representation,Db.OpenMode.ForRead) as Db.Solid3d;
        Need(solid!=null&&solid.GetType()==typeof(Db.Solid3d)&&solid.OwnerId==ModelFeatureReorderState345.Model(state.Document.Database,tx)&&!solid.IsNull,
            "OUT_OF_SCOPE","Select one current nonempty top-level native part representation.");
        var id=McObjectId.FromHandle(representation.Handle.Value,drawing.ID);var body=id.GetObjectOfType<Mc3dSolid>();
        Need(body!=null&&body.ID==id&&ModelHistoryEditGuard.CurrentRepresentation(body,state,tx,cancel)==representation,
            "OBJECT_IDENTITY_MISMATCH","The selected handle is not this body's unique current native representation.");
        Need(ModelProfileRebindState349.Same(body.LastPartItemID,edit.ExpectedLastItemId),"PRECONDITION_FAILED","Read the current last_item_id before changing its extrusion profile.");
        var contents=ModelInventoryBudget.History(body,cancel);var featureId=contents.SingleOrDefault(value=>ModelProfileRebindState349.Same(value,edit.FeatureId));
        var feature=featureId.IsNull?null:featureId.GetObjectOfType<ExtrudeFeature>();
        var owner=ModelProfileDiscovery348.OwnerDocument(body,state);
        Need(feature!=null&&feature.ID==featureId&&feature.HostSolidID==body.ID&&feature.Status.ToString()=="Ok"&&!feature.IsSuppressed&&!feature.IsBeyondEndOfPart&&
            feature.DbEntity.Document!=null&&feature.DbEntity.Document.ID==owner.ID,"PROFILE_OWNERSHIP_MISMATCH","Select a healthy existing extrusion in this body's actual owner document.");
        Need(ModelProfileRebindState349.Same(feature.ProfileID,edit.ExpectedCurrentProfileId),"PRECONDITION_FAILED","The selected extrusion's current profile changed; refresh history.");
        Need(edit.ReplacementRegions!=null||edit.ExpectedCurrentProfileId!=edit.ProfileId,"INVALID_ARGUMENT","Select a different replacement profile; no native rebind is needed for the current profile.");
        var before=ModelProfileRebindState349.Capture(body,feature,edit,args,state,tx,cancel,includeStandalone:true);
        Need(before.Representation==representation&&before.OldOwners.Contains(feature.ID.ToString(),StringComparer.OrdinalIgnoreCase),
            "PROFILE_OWNERSHIP_MISMATCH","The selected extrusion is not an actual forward consumer of the current same-owner profile.");
        // ProfileID is not a documented part-acquisition operation. An actual
        // unused outside-sketch rebind detached the existing forming history.
        // Keep already acquired sketches and actual same-part shared profiles
        // eligible; do not infer acquisition from document ownership alone.
        Need(before.NextOwners.Length>0||before.Contents.Contains(before.Next.Sketch.ID),
            "PROFILE_REBIND_ACQUISITION_UNAVAILABLE","An unused replacement profile on a document sketch outside this part cannot safely be acquired by the ProfileID setter. Select a profile on an already acquired same-part sketch, or an existing same-part consumed profile.");
        Need(before.Next.Profile.Status.ToString()=="Ok"&&!before.Next.Profile.IsSuppressed&&before.Next.Sketch.Status.ToString()=="Ok"&&!before.Next.Sketch.IsSuppressed,
            "MODEL_STATE_UNAVAILABLE","The replacement profile and parent sketch must be active and healthy.");
        ModelProfileRebindConsumers414.RequireAcyclic(feature,before.Next,before.AllDependents,cancel);
        Need(ModelProfileRebindState349.Same(before.Next.Sketch.ID,edit.ExpectedSketchId)&&before.Next.Token==edit.ExpectedProfileToken,
            "PRECONDITION_FAILED","The exact new profile, parent, selected contour or source bindings changed; refresh profile_ids.");
        Need(before.Old.Sketch.PlanarEntityID==before.Next.Sketch.PlanarEntityID&&
            ModelProfileRebindState349.Equivalent(before.Old.Geometry.GetProperty("frame"),before.Next.Geometry.GetProperty("frame"),cancel),
            "PROFILE_PLANE_MISMATCH","This contour replacement retains the current oriented sketch frame/support; use its supported datum editor for a separate support change.");
        var selection=edit.ReplacementRegions==null?null:ModelProfileRebindSelection430.Resolve(edit,before,state,tx,cancel);
        return new(edit,feature,before,selection);
    }
    internal static void Preflight(ModifyArguments args,DocumentState state,Db.Transaction tx,Action? cancel=null)=>Resolve(args,state,tx,cancel??(()=>{}));
    static ModelProfileRebindChange349 Change(Binding binding,ModifyArguments args,ModelProfileRebindState349.Snapshot after,bool project)
    {
        var source=new EntityRef(args.BridgeId,args.DocumentId,binding.Edit.Handles.Single());
        var current=new EntityRef(args.BridgeId,args.DocumentId,project?"FFFFFFFFFFFFFFFF":Validation.NormalizeHandle(after.Representation.Handle.ToString()));
        var previous=ModelProfileRebindState349.Disposition(after);
        if(project)previous=previous with{profile_host_solid_id="{FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF}",sketch_host_solid_id="{FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF}",
            dependent_feature_id="{FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF}",disposition="native_forming_backlink_present"};
        return new(0,source,current,after.Body.ID.ToString(),binding.Feature.ID.ToString(),binding.Edit.ExpectedCurrentProfileId,
            binding.Edit.ProfileId,after.Next.Sketch.ID.ToString(),after.Next.Token,after.OwnerDocument,binding.Before.Views.LastHistoryItem.ToString(),
            project||source.handle!=current.handle,binding.Before.Drivers,binding.Before.AllDependents.Length,after.AllDependents.Length,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(after.AllDependents,Wire.Json))),previous,
            binding.SelectionCreated||project&&binding.Selection!=null);
    }
    static Result Outcome(Binding binding,ModifyArguments args,WriteJob job,ReadContext context,ModelProfileRebindState349.Snapshot after,bool project=false)
    {
        var change=Change(binding,args,after,project);bool replaced=change.source_representation_replaced;
        var data=new ModelProfileRebindOutput349(args.OperationId,"committed",false,[change.body_ref],replaced?[change.source]:[],
            replaced?[change.body_ref]:[],[],"not_guaranteed",[change]);
        var warnings=binding.Before.Warnings.Concat(after.Warnings).Concat(project?[ModelFeatureReorderState345.ValidityWarning,ModelSketchConstraintMetadata379.Warning,ModelProfileRebindState349.AssocWarning,ModelSketchConstraintObservation353.Warning]:Array.Empty<string>())
            .Append(WriteRecovery.Warning).Append(OldWarning).Append(ReviewWarning).Distinct(StringComparer.Ordinal).ToArray();
        return Result.Ok(job.OriginalRequestId,data,context,warnings);
    }
    static void ResultBytes(Result result,Action cancel)
    {
        cancel();Need(JsonSerializer.SerializeToUtf8Bytes(result,Wire.Json).Length<=Wire.PayloadBudget,"RESPONSE_LIMIT_EXCEEDED",
            "The complete profile-rebind receipt exceeds the actual transport byte budget; reduce the requested scope before a write.");
    }
    static ModelProfileRebindState349.Snapshot Current(Binding binding,ModifyArguments args,DocumentState state,Action cancel)
    {
        using var tx=state.Document.Database.TransactionManager.StartTransaction();
        ModelFeatureReorderState345.Scope(state,tx,binding.Before.DrawingDocument);
        Need(ModelProfileDiscovery348.OwnerDocument(binding.Before.Body,state).ID.ToString()==binding.Before.OwnerDocument,
            "DOCUMENT_CONTEXT_MISMATCH","The body's actual native owner document changed before rebind.");
        var feature=binding.Feature.ID.GetObjectOfType<ExtrudeFeature>();Need(feature!=null&&feature.ID==binding.Feature.ID,"MODEL_STATE_UNAVAILABLE","Selected extrusion identity disappeared.");
        return ModelProfileRebindState349.Capture(binding.Before.Body,feature,binding.Edit,args,state,tx,cancel,includeStandalone:true);
    }
    static void Unchanged(Binding binding,ModelProfileRebindState349.Snapshot fresh,Action cancel)
    {
        var before=binding.Before;
        Need(before.Representation==fresh.Representation&&before.DrawingDocument==fresh.DrawingDocument&&before.OwnerDocument==fresh.OwnerDocument&&
            before.Contents.ToHashSet().SetEquals(fresh.Contents)&&before.Construction.ToHashSet().SetEquals(fresh.Construction)&&before.Inventory.SetEquals(fresh.Inventory)&&
            before.Display.GetRawText()==fresh.Display.GetRawText()&&before.BodyMetadata.GetRawText()==fresh.BodyMetadata.GetRawText()&&
            ModelProfileRebindState349.Equivalent(before.Parameters,fresh.Parameters,cancel)&&ModelProfileRebindState349.Equivalent(before.Profiles,fresh.Profiles,cancel)&&
            ModelProfileRebindState349.Equivalent(before.Sources,fresh.Sources,cancel)&&before.DirectDependents.SequenceEqual(fresh.DirectDependents,StringComparer.OrdinalIgnoreCase)&&
            before.AllDependents.SequenceEqual(fresh.AllDependents,StringComparer.OrdinalIgnoreCase)&&before.OldOwners.SequenceEqual(fresh.OldOwners,StringComparer.OrdinalIgnoreCase)&&
            before.NextOwners.SequenceEqual(fresh.NextOwners,StringComparer.OrdinalIgnoreCase)&&
            ModelProfileRebindState349.Equivalent(before.ConstraintObservations,fresh.ConstraintObservations,cancel),"PRECONDITION_FAILED","Retained feature, contour, owner, driver, constraints or downstream state changed before the native setter.");
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Result Execute(ModifyArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        Binding? binding=null;McUndoPoint? tracker=null;bool started=false;string stage="preparation";McObjectId createdProfile=McObjectId.Null;
        void Trace(string next){stage=next;log.Write(job.Operation,job.OriginalRequestId,"model_profile_rebind349_checkpoint",transaction:next,operationId:job.OperationId);}
        void Close(){var value=tracker;tracker=null;if(value==null)return;try{value.Stop();}finally{value.Dispose();}}
        try
        {
            using(var tx=state.Document.Database.TransactionManager.StartTransaction())binding=Resolve(args,state,tx,job.CheckCancellation);
            var fresh=Current(binding,args,state,job.CheckCancellation);Unchanged(binding,fresh,job.CheckCancellation);
            if(binding.Selection is {} selection)
            {
                using var tx=state.Document.Database.TransactionManager.StartTransaction();
                var sketch=ModelSketchLifecycleReader362.Capture(binding.Before.Body,selection.Sketch.Sketch,state,tx,job.CheckCancellation);
                Need(sketch.Token==selection.Sketch.Token,"PRECONDITION_FAILED","The current acquired sketch, peer profiles or constraints changed before selection creation.");
            }
            // The largest mapping and every warning are budgeted before tracking.
            ResultBytes(ModelViewFollowers.ReserveWarnings(Outcome(binding,args,job,context,fresh,true),binding.Before.Views),job.CheckCancellation);
            job.CheckCancellation();ModelViewFollowers.CheckStandalone(binding.Before.Body,binding.Before.Views,job.CheckCancellation);tracker=new McUndoPoint(false);tracker.Start();
            if(binding.Selection is {} selected)
            {
                started=true;
                var profile=ModelProfileRebindSelection430.Create(selected,binding.Edit,binding.Before,job.CheckCancellation,Trace,id=>createdProfile=id);
                var actual=binding with{Edit=binding.Edit with{ProfileId=profile.ID.ToString()},SelectionCreated=true};
                var prepared=Current(actual,args,state,job.CheckCancellation);
                using(var tx=state.Document.Database.TransactionManager.StartTransaction())
                    prepared=prepared with{Warnings=prepared.Warnings.Concat(binding.Before.Warnings).Concat(ModelProfileRebindSelection430.VerifyPrepared(selected,binding.Before,prepared,state,tx,job.CheckCancellation)).Distinct(StringComparer.Ordinal).ToArray()};
                ModelProfileRebindConsumers414.RequireAcyclic(binding.Feature,prepared.Next,prepared.AllDependents,job.CheckCancellation);
                binding=actual with{Before=prepared};Trace("replacement_selection_verified");
            }
            Trace("native_profile_setter");started=true;
            binding.Feature.TryModify((uint)McDbObject.ObjChangesType.Geom);
            binding.Feature.ProfileID=binding.Before.Next.Profile.ID;
            binding.Feature.DbEntity.Update();McObjectManager.UpdateAll();
            Need(binding.Before.Body.RebuildPart(),"MODEL_UPDATE_FAILED","Native part rebuild rejected the new extrusion profile.");
            McObjectManager.UpdateAll();Trace("native_rebuild_returned");
            var after=Current(binding,args,state,job.CheckCancellation);
            after=after with{Warnings=after.Warnings.Concat(ModelProfileRebindState349.Verify(binding.Before,after,binding.Feature,binding.Edit,job.CheckCancellation)).Distinct(StringComparer.Ordinal).ToArray()};Trace("native_profile_verified");
            if(binding.Selection is {} completed)
            {using var tx=state.Document.Database.TransactionManager.StartTransaction();ModelProfileRebindSelection430.VerifySelected(completed,after.Next.Profile,after.Body,state,tx,job.CheckCancellation);}
            job.BeginCommit();Close();after=Current(binding,args,state,job.CheckCancellation);
            after=after with{Warnings=after.Warnings.Concat(ModelProfileRebindState349.Verify(binding.Before,after,binding.Feature,binding.Edit,job.CheckCancellation)).Distinct(StringComparer.Ordinal).ToArray()};
            var result=Outcome(binding,args,job,context,after);ResultBytes(result,job.CheckCancellation);
            result=ModelViewFollowers.WithWarnings(result,binding.Before.Views);job.RecordCommit(result);Trace("commit_recorded");return result;
        }
        catch(Exception error)
        {
            if(job.CommittedResult is {} committed)return committed;
            Exception? cleanup=null;try{Close();}catch(Exception exception){cleanup=exception;}
            string cause=error is DomainException domain?domain.Error.code:error is OperationCanceledException?"CANCELLED":"MODEL_PROFILE_REBIND_FAILED";
            string message=error.Message;if(message.Length>512)message=message[..512];bool unknown=started||cleanup!=null;
            return job.Error(unknown?"WRITE_OUTCOME_UNKNOWN":cause,unknown?
                "Native profile rebind started but its outcome is unverified. Inspect the current body and exact old/new profiles before another mutation; replay only the identical request in this bridge lifetime.":message,
                unknown?"unknown":"not_started",context,new{cause,stage,message,body_mc_id=binding?.Before.Body.ID.ToString(),
                    feature_id=binding?.Feature.ID.ToString(),previous_profile_id=binding?.Edit.ExpectedCurrentProfileId,profile_id=binding?.Edit.ProfileId,
                    created_profile_id=createdProfile.IsNull?null:createdProfile.ToString(),cleanup_error=cleanup?.GetType().Name})
                with{warnings=[WriteRecovery.Warning]};
        }
    }
}
