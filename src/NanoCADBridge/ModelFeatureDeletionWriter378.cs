using System.Runtime.CompilerServices;
using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class ModelFeatureDeletionWriter378
{
    const string ReviewWarning="NATIVE_FEATURE_DELETED: Review the resulting part through MCP capture_view, refresh history/variables and use the current body_ref. Native auxiliary cleanup can be deferred; Undo and rollback are not guaranteed.";
    const string RetiredStorageScope="exact_surviving_body_metadata_except_proved_selected_feature_storage_retirement";
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok,string code,string message)=>ModelFeatureDeletionState378.Need(ok,code,message);
    internal static bool Handles(ModifyArguments args)=>args.Operations.Any(edit=>edit is ModelFeatureDeletionSpec378);
    [MethodImpl(MethodImplOptions.NoInlining)]
    static ModelFeatureDeletionState378.Snapshot Resolve(ModifyArguments args,DocumentState state,Db.Transaction tx,Action cancel)
    {
        Need(args.Operations.Length==1&&args.Operations[0] is ModelFeatureDeletionSpec378,"INVALID_OPERATION_ORDER","Native feature deletion must be the sole operation.");
        var edit=(ModelFeatureDeletionSpec378)args.Operations[0];var value=ModelFeatureDeletionState378.Capture(edit.Handles.Single(),edit.FeatureId,args,state,tx,cancel);
        Need(value.Part.Body.LastPartItemID.ToString().Equals(edit.ExpectedLastItemId,StringComparison.OrdinalIgnoreCase)&&
            value.RemainingLast.ToString().Equals(edit.ExpectedRemainingLastItemId,StringComparison.OrdinalIgnoreCase)&&
            value.Token.Equals(edit.ExpectedDeletionToken,StringComparison.Ordinal)&&value.Drivers.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(edit.RetirableDriverIds),
            "PRECONDITION_FAILED","Refresh selected feature_deletion_ids and explicitly accept its current remaining terminal and exclusive native driver retirement scope.");
        Need(value.RetainedProfile==null?edit.ExpectedRetainedProfileId==null:
            string.Equals(edit.ExpectedRetainedProfileId,value.RetainedProfile.Profile.Profile.ID.ToString(),StringComparison.OrdinalIgnoreCase),
            "PRECONDITION_FAILED","Explicitly retain the current selected profile using expected_retained_profile_id from the fresh deletion read; omit it for finishing-feature deletion.");
        return value;
    }
    internal static void Preflight(ModifyArguments args,DocumentState state,Db.Transaction tx,Action? cancel=null)=>Resolve(args,state,tx,cancel??(()=>{}));
    sealed record Observed(ModelFeatureReorderState345.Snapshot Part,EntityRef Body,string[] Retired,string[] RetainedDrivers,ModelFeatureOrderChange345[] Order,string StorageScope,ModelFeatureDeletionProfile384? RetainedProfile);
    static Observed Projected(ModelFeatureDeletionState378.Snapshot before,ModifyArguments args)=>new(before.Part,
        new(args.BridgeId,args.DocumentId,"FFFFFFFFFFFFFFFF"),before.Drivers,before.Drivers,
        before.Part.Previous.Where(pair=>!pair.Key.Equals(before.Feature.ToString(),StringComparison.OrdinalIgnoreCase))
            .Select(pair=>new ModelFeatureOrderChange345(pair.Key,pair.Value,"{FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF}")).ToArray(),RetiredStorageScope,before.RetainedProfile==null?null:ModelFeatureProfileRetention384.Describe(before.RetainedProfile,ModelFeatureProfileRetention384.AfterDisposition));
    static Result Outcome(ModelFeatureDeletionState378.Snapshot before,ModifyArguments args,WriteJob job,ReadContext context,Observed observed,bool project=false)
    {
        var source=new EntityRef(args.BridgeId,args.DocumentId,args.Operations[0].Handles.Single());
        var current=observed.Body;
        bool replaced=project||source.handle!=current.handle;
        // Reserve every current assigned edge and both possible driver lists.
        // Actual retirement can only partition these exact preflight IDs.
        var order=observed.Order;
        var change=new ModelFeatureDeletionChange378(0,source,current,before.Part.Body.ID.ToString(),before.Feature.ToString(),before.Kind,
            before.RemainingLast.ToString(),[before.Feature.ToString()],observed.Retired,observed.RetainedDrivers,
            order,replaced,observed.StorageScope,ModelFeatureDeletionState378.CleanupScope,observed.RetainedProfile);
        var data=new ModelFeatureDeletionOutput378(args.OperationId,"committed",false,[current],replaced?[source]:[],replaced?[current]:[],[],"not_guaranteed",[change]);
        // Snapshot warnings are fixed strings: unavailable B-rep and sketch
        // inventories may first appear after erase. Sources stay identical,
        // so their existing fixed notice remains in before.Part.Warnings.
        var warnings=before.Part.Warnings.Concat(project?[ModelFeatureReorderState345.ValidityWarning,ModelSketchConstraintObservation353.Warning]:observed.Part.Warnings)
            .Concat(before.RetainedProfile==null?Array.Empty<string>():new[]{ModelFeatureProfileRetention384.Warning})
            .Append(WriteRecovery.Warning).Append(ReviewWarning).Distinct(StringComparer.Ordinal).ToArray();
        return Result.Ok(job.OriginalRequestId,data,context,warnings);
    }
    static void ResultBytes(Result result,Action cancel)
    {
        cancel();Need(JsonSerializer.SerializeToUtf8Bytes(result,Wire.Json).Length<=Wire.PayloadBudget,"RESPONSE_LIMIT_EXCEEDED",
            "The complete native deletion receipt exceeds the transport byte budget; no erase is started for an unreportable scope.");
    }
    static Observed Verify(ModelFeatureDeletionState378.Snapshot before,ModifyArguments args,DocumentState state,Action cancel,Action<EntityRef> current)
    {
        var body=before.Part.Body;ModelFeatureReorderState345.Snapshot after;Db.ObjectId raw;Dictionary<string,string> metadataBindings;
        ModelFeatureProfileRetention384.Binding? retainedProfile=null;
        using(var tx=state.Document.Database.TransactionManager.StartTransaction())
        {
            raw=ModelHistoryEditGuard.CurrentRepresentation(body,state,tx,cancel);
            var bodyRef=new EntityRef(args.BridgeId,args.DocumentId,Validation.NormalizeHandle(raw.Handle.ToString()));current(bodyRef);
            after=ModelFeatureReorderState345.Capture(body,raw,args,state,tx,cancel,captureFullDesign:true,captureResidentIdentity:true);
            metadataBindings=ModelFeatureDeletionMetadata383.Bindings(after,state,tx,cancel);
            if(before.RetainedProfile!=null)retainedProfile=ModelFeatureProfileRetention384.Verify(before.RetainedProfile,after,before.Feature,state,tx,cancel);
        }
        var expectedContents=before.Part.Contents.Where(id=>id!=before.Feature).ToHashSet();
        bool inventory=before.Part.Inventory.Except(after.Inventory).All(id=>id==before.Part.Representation)&&after.Inventory.Except(before.Part.Inventory).All(id=>id==raw);
        bool history=expectedContents.SetEquals(after.Contents)&&after.Contents.Length==expectedContents.Count;
        var variables=after.Preserved.GetProperty("variables").EnumerateArray().Select(value=>value.GetProperty("mc_id").GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var retired=before.Drivers.Where(id=>!variables.Contains(id)).ToArray();var retained=before.Drivers.Where(variables.Contains).ToArray();
        var expected=ModelFeatureDeletionState378.ExpectedDesign(before,retired,cancel);
        bool constraints=before.Part.Constraints.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(after.Constraints.Keys)&&
            before.Part.Constraints.All(pair=>{cancel();return ModelSketchConstraintObservation353.Equivalent(pair.Value,after.Constraints[pair.Key],cancel);});
        bool terminal=body.ID.ToString()==before.Part.Preserved.GetProperty("body").GetString()&&before.Part.NativeDocument==after.NativeDocument&&
            body.LastPartItemID==before.RemainingLast&&after.Views.LastHistoryItem==before.RemainingLast&&!after.Views.HasFollowers;
        bool order=after.Features.SequenceEqual(before.Part.Features[..^1],StringComparer.OrdinalIgnoreCase);
        var orderChanges=new List<ModelFeatureOrderChange345>();bool previous=before.Part.Previous.Count==after.Previous.Count+1;
        foreach(var pair in before.Part.Previous)
        {
            cancel();if(pair.Key.Equals(before.Feature.ToString(),StringComparison.OrdinalIgnoreCase))continue;
            string? expectedPrevious=pair.Value!=null&&pair.Value.Equals(before.Feature.ToString(),StringComparison.OrdinalIgnoreCase)?before.RemainingLast.ToString():pair.Value;
            if(!after.Previous.TryGetValue(pair.Key,out var actualPrevious)||!string.Equals(actualPrevious,expectedPrevious,StringComparison.OrdinalIgnoreCase))previous=false;
            if(!string.Equals(pair.Value,actualPrevious,StringComparison.OrdinalIgnoreCase))orderChanges.Add(new(pair.Key,pair.Value,actualPrevious));
        }
        bool display=before.Part.Properties.GetRawText()==after.Properties.GetRawText();
        // Retire only the exact selected tree and incoming raw/native reactor
        // references to it. All duplicated current-bound metadata shadows use
        // the same proved retirement; payloads/XData/unrelated links stay exact.
        bool metadata=ModelFeatureDeletionMetadata383.Retained(before.Part.BodyMetadata,after.BodyMetadata,expected,after.Preserved,
            before.Storage,metadataBindings,Validation.NormalizeHandle(before.Part.Representation.Handle.ToString()),Validation.NormalizeHandle(raw.Handle.ToString()),
            cancel,out string storageReason,out var normalizedBefore,out var normalizedDesign);
        bool design=ModelFeatureReorderState345.Equivalent(normalizedDesign,after.Preserved,cancel);
        string storageScope=storageReason=="exact_body_owned_metadata"?storageReason:RetiredStorageScope;
        var actualCritical=ModelFeatureDeletionState378.Critical(after,ModelProfileDiscovery348.OwnerDocument(body,state),cancel);
        var expectedCritical=ModelFeatureProfileRetention384.ExpectedCritical(before.Dependencies.GetProperty("retained_critical_inputs"),before.RetainedProfile,before.Feature,cancel);
        bool critical=ModelFeatureReorderState345.Equivalent(expectedCritical,actualCritical,cancel);
        object Diagnostic()
        {
            var identity=new ModelRepresentationDiagnostics377.Identity("delete_model_feature",body.ID.ToString(),before.Part.Representation.Handle.ToString(),raw.Handle.ToString(),
                before.Part.NativeDocument,after.NativeDocument,before.Part.Preserved.GetProperty("variables_owner").GetString(),after.Preserved.GetProperty("variables_owner").GetString(),
                before.Part.BodyMetadata.GetProperty("root").GetProperty("owner").GetString(),after.BodyMetadata.GetProperty("root").GetProperty("owner").GetString(),
                before.Part.BodyMetadata.GetProperty("owned").GetArrayLength(),after.BodyMetadata.GetProperty("owned").GetArrayLength());
            return new{snapshot_diagnostics=ModelRepresentationDiagnostics377.Describe(identity,[
                new("body_metadata",metadata,"only proved selected retired tree and its incoming reactors; all other stored data exact",normalizedBefore,after.BodyMetadata,storageReason),
                new("body_display",display,"exact retained display",before.Part.Properties,after.Properties),
                new("retained_design",design,"selected feature/accepted exclusive drivers removed; exact selected profile backlink released; exact proved retired storage reactors normalized in current-bound shadows",normalizedDesign,after.Preserved),
                new("history",history,"exact selected feature removal",Wire.Element(expectedContents.Select(id=>id.ToString()).OrderBy(id=>id,StringComparer.Ordinal)),Wire.Element(after.Contents.Select(id=>id.ToString()).OrderBy(id=>id,StringComparer.Ordinal))),
                new("inventory",inventory,"unique current body replacement only",null,null),new("constraints",constraints,"same actual available observations; no solver evaluation",null,null),
                new("terminal",terminal,"exact requested remaining native terminal",Wire.Element(before.RemainingLast.ToString()),Wire.Element(body.LastPartItemID.ToString())),
                new("forming_order",order,"remove selected terminal only",Wire.Element(before.Part.Features[..^1]),Wire.Element(after.Features)),new("predecessors",previous,"remove selected assigned predecessor edge only",null,null),
                new("retained_critical_inputs",critical,"same actual critical inputs of every retained item/body",expectedCritical,actualCritical)],cancel),
                storage_proof=before.Storage,retired_driver_ids=retired,retained_driver_ids=retained};
        }
        void Require(bool ok,string code,string message){if(!ok)throw new DomainException(code,message,false,Diagnostic());}
        Require(inventory&&history,"MODEL_STATE_UNAVAILABLE","Native erase changed another entity or history identity outside the explicit selected feature scope.");
        Require(display,"MODEL_STATE_UNAVAILABLE","Native erase changed the body display properties.");
        Require(metadata,"MODEL_STATE_UNAVAILABLE","Native erase changed unrelated body fields/XData/reactors/owned data; selected typed storage retirement is the only admitted metadata delta.");
        Require(terminal&&order&&previous,"MODEL_ORDER_MISMATCH","Native erase did not retain the requested remaining terminal and complete forming order.");
        Require(design&&constraints&&critical,"MODEL_STATE_UNAVAILABLE","Native erase changed a retained source, profile/sketch, peer feature, variable/formula, owner, critical input or available constraint observation.");
        var erased=before.Feature.GetObjectOfType<HistoryItem>();
        Require(erased==null||erased.Status.ToString()=="Erased","NATIVE_ERASE_FAILED","The selected native forming feature remains active after erase.");
        return new(after,new(args.BridgeId,args.DocumentId,Validation.NormalizeHandle(raw.Handle.ToString())),retired,retained,orderChanges.ToArray(),storageScope,
            retainedProfile==null?null:ModelFeatureProfileRetention384.Describe(retainedProfile,ModelFeatureProfileRetention384.AfterDisposition));
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Result Execute(ModifyArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        McUndoPoint? tracker=null;ModelFeatureDeletionState378.Snapshot? before=null;EntityRef? current=null;bool started=false;string stage="preparation";
        void Trace(string value){stage=value;log.Write(job.Operation,job.OriginalRequestId,"model_feature_deletion_checkpoint",transaction:value,operationId:job.OperationId);}
        void Close(){var value=tracker;tracker=null;if(value==null)return;try{value.Stop();}finally{value.Dispose();}}
        Result Failure(Exception error)
        {
            if(job.CommittedResult is {} committed)return committed;
            Exception? cleanup=null;try{Close();}catch(Exception failure){cleanup=failure;}
            bool unknown=started||cleanup!=null||error is DomainException{Error.code:"WRITE_OUTCOME_UNKNOWN"};string cause=error is DomainException domain?domain.Error.code:error is OperationCanceledException?"CANCELLED":"MODEL_FEATURE_DELETION_FAILED";
            var details=ModelRepresentationDiagnostics377.Error(cause,stage,error.Message,before?.Part.Body.ID.ToString(),before?.Feature.ToString(),cleanup?.Message,
                new{body_ref=current,verification_details=error is DomainException observed?observed.Error.details:null,
                    cleanup_scope=ModelFeatureDeletionState378.CleanupScope},before?.Part.Representation.Handle.ToString());
            return job.Error(unknown?"WRITE_OUTCOME_UNKNOWN":cause,unknown?"Native feature deletion is unverified; inspect the current model before a dependent write. No rollback is claimed.":error.Message,
                unknown?"unknown":"not_started",context,details);
        }
        try
        {
            using(var tx=state.Document.Database.TransactionManager.StartTransaction())before=Resolve(args,state,tx,job.CheckCancellation);
            NativeModelHistory421.EnsureAvailable();
            ResultBytes(Outcome(before,args,job,context,Projected(before,args),project:true),job.CheckCancellation);
            var proposedLast=before.RemainingLast.GetObjectOfType<HistoryItem>();
            Need(proposedLast!=null&&proposedLast.ID==before.RemainingLast,"MODEL_STATE_UNAVAILABLE","The explicitly retained terminal disappeared before native eligibility observation.");
            Trace("native_terminal_try_only");job.CheckCancellation();bool eligible=proposedLast.SetAsLastPartItem(true);
            // Documented TryOnly is read-only. Re-read the complete current
            // token even on refusal; unexpected change cannot be not_started.
            try
            {
                using var tx=state.Document.Database.TransactionManager.StartTransaction();
                var fresh=ModelFeatureDeletionState378.Capture(args.Operations[0].Handles.Single(),before.Feature.ToString(),args,state,tx,job.CheckCancellation);
                Need(fresh.Token==before.Token,"PRECONDITION_FAILED","Native TryOnly changed the complete observed part state.");
            }
            catch(DomainException error)
            {throw new DomainException("WRITE_OUTCOME_UNKNOWN","Native TryOnly did not retain the complete observed part state.",false,
                ModelRepresentationDiagnostics377.Error(error.Error.code,"native_terminal_try_only_verification",error.Message,before.Part.Body.ID.ToString(),before.Feature.ToString(),null,error.Error.details,before.Part.Representation.Handle.ToString()));}
            Need(eligible,"NATIVE_TERMINAL_REJECTED","Native dependency rules refuse the explicitly retained remaining terminal; erase was not started.");
            job.CheckCancellation();tracker=new McUndoPoint(false);tracker.Start();started=true;
            var feature=before.Feature.GetObjectOfType<HistoryItem>();Need(feature!=null&&feature.ID==before.Feature,"MODEL_STATE_UNAVAILABLE","The selected terminal disappeared before erase.");
            if(before.RetainedProfile!=null)
            {Trace("native_selected_profile_release");ModelFeatureProfileRetention384.Release(before.RetainedProfile,before.Feature,job.CheckCancellation);}
            Trace("native_erase_before");Need(feature.DbEntity.Erase(),"NATIVE_ERASE_FAILED","Native erase refused the selected terminal forming feature.");
            var remaining=before.RemainingLast.GetObjectOfType<HistoryItem>();
            Need(remaining!=null&&remaining.HostSolidID==before.Part.Body.ID&&remaining.SetAsLastPartItem(false),"NATIVE_TERMINAL_REJECTED","Native erase did not permit the explicitly retained remaining terminal.");
            Trace("native_update_before");McObjectManager.UpdateAll();job.CheckCancellation();
            Need(NativeModelHistory421.Rebuild(before.Part.Body),"MODEL_UPDATE_FAILED","Native part rebuild failed after feature deletion.");
            McObjectManager.UpdateAll();Trace("native_rebuild_returned");
            job.BeginCommit();Close();
            return NativePostCommandVerification422.Defer(job,()=>
            {
            var observed=Verify(before,args,state,job.CheckCancellation,refValue=>current=refValue);Trace("deletion_verified_after_command");
            var result=Outcome(before,args,job,context,observed);ResultBytes(result,job.CheckCancellation);
            job.RecordCommit(result);Trace("commit_recorded");return result;
            },Failure);
        }
        catch(Exception error){return Failure(error);}
    }
}
