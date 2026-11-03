using System.Runtime.CompilerServices;
using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class ModelFeatureReorderWriter345
{
    const string ReviewWarning="NATIVE_FEATURE_ORDER_CHANGED: Inspect the rebuilt part and linked drawings through MCP capture_view; refresh history and use the returned current body_ref before another edit.";
    const string DependentWarning="NATIVE_ORDER_REACTOR_OBSERVATION: Native reactor-derived downstream sets changed with the requested order. Their current IDs remain owned by this retained part; explicit source/profile/support/driver relationships were preserved independently.";
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok,string code,string message)=>ModelFeatureReorderState345.Need(ok,code,message);
    sealed record Binding(ModelFeatureReorderSpec345 Edit,ModelFeatureReorderState345.Snapshot Before,
        McObjectId Feature,McObjectId Predecessor,string[] ExpectedOrder,JsonElement Dependents,McObjectId ResultingLast,bool TerminalChanged);
    internal static bool Handles(ModifyArguments args)=>args.Operations.Any(edit=>edit is ModelFeatureReorderSpec345);
    static JsonElement Dependents(McObjectId[] selected,ModelFeatureReorderState345.Snapshot before,Action cancel)
    {
        var known=before.Contents.ToHashSet();known.Add(before.Body.ID);var result=new List<object>();var bytes=new ModelInventoryBudget(cancel);
        foreach(var id in selected)
        {
            cancel();var item=id.GetObjectOfType<HistoryItem>();Need(item!=null,"MODEL_STATE_UNAVAILABLE","Selected history item disappeared.");
            var direct=item.GetDependent();var all=item.GetAllDependent(false);
            Need(direct!=null&&all!=null,"DEPENDENCY_INVENTORY_UNAVAILABLE","Native downstream ownership is unknown; refresh the selected features' dependencies before reordering.");
            ModelInventoryBudget.Ids(direct,cancel);ModelInventoryBudget.Ids(all,cancel);
            foreach(var dependent in all.Concat(direct))
            {
                cancel();Need(!dependent.IsNull&&(known.Contains(dependent)||ModelViewFollowers.OwnsDependent(before.Views,dependent)),"DEPENDENCY_OWNERSHIP_MISMATCH","A selected feature has a dependent outside this retained part history.");
                if(dependent==before.Body.ID||ModelViewFollowers.OwnsDependent(before.Views,dependent))continue; // exact already-resolved native owner
                var node=dependent.GetObjectOfType<HistoryItem>();
                Need(node!=null&&node.HostSolidID==before.Body.ID,"DEPENDENCY_OWNERSHIP_MISMATCH","A selected feature has an unclassified or foreign native dependent.");
            }
            var observed=new{id=id.ToString(),direct=direct.Select(value=>value.ToString()).OrderBy(value=>value,StringComparer.Ordinal).ToArray(),
                all=all.Select(value=>value.ToString()).OrderBy(value=>value,StringComparer.Ordinal).ToArray()};bytes.Add(observed);result.Add(observed);
        }
        var snapshot=Wire.Element(result);new ModelInventoryBudget(cancel).Add(snapshot);return snapshot;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static Binding Resolve(ModifyArguments args,DocumentState state,Db.Transaction tx,Action cancel)
    {
        Need(args.Operations.Length==1&&args.Operations[0] is ModelFeatureReorderSpec345,"INVALID_OPERATION_ORDER","Native feature reorder must be the sole operation.");
        Need(args.TargetSpace=="model","OUT_OF_SCOPE","Native feature reorder requires Model Space.");
        var loaded=AppDomain.CurrentDomain.GetAssemblies().Select(value=>value.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Need(loaded.Contains("mapimgd")&&loaded.Contains("MapiBaseTypes"),"CAPABILITY_UNAVAILABLE","MultiCAD runtime must already be loaded.");
        var edit=(ModelFeatureReorderSpec345)args.Operations[0];var active=ModelFeatureReorderState345.Scope(state,tx);
        var source=CadStructureWriter.Find(state.Document.Database,edit.Handles.Single());var solid=tx.GetObject(source,Db.OpenMode.ForRead) as Db.Solid3d;
        Need(solid!=null&&solid.GetType()==typeof(Db.Solid3d),"UNSUPPORTED_ENTITY_TYPE","Select one existing native part representation.");
        var layer=(Db.LayerTableRecord)tx.GetObject(solid.LayerId,Db.OpenMode.ForRead);
        Need(!layer.IsLocked&&!layer.IsDependent&&!layer.IsFrozen&&!layer.IsOff,"LAYER_UNAVAILABLE","The selected part layer is unavailable for editing.");
        var native=McObjectId.FromHandle(source.Handle.Value,active.ID);var body=native.GetObjectOfType<Mc3dSolid>();
        Need(body!=null&&body.ID==native,"OBJECT_IDENTITY_MISMATCH","The selected handle is not the current native part identity.");
        Need(body.LastPartItemID.ToString().Equals(edit.ExpectedLastItemId,StringComparison.OrdinalIgnoreCase),"PRECONDITION_FAILED","The part's last_item_id changed; refresh history before reordering.");
        var before=ModelFeatureReorderState345.Capture(body,source,args,state,tx,cancel);
        Need(ModelHistoryEditGuard.CurrentRepresentation(body,state,tx,cancel)==source,"OBJECT_IDENTITY_MISMATCH","Select the unique current native representation.");
        McObjectId Find(string id)
        {foreach(var value in before.Contents){cancel();if(value.ToString().Equals(id,StringComparison.OrdinalIgnoreCase))return value;}return McObjectId.Null;}
        var featureId=Find(edit.FeatureId);var predecessorId=Find(edit.PredecessorId);
        var feature=featureId.GetObjectOfType<HistoryItem>();var predecessor=predecessorId.GetObjectOfType<HistoryItem>();
        Need(!featureId.IsNull&&!predecessorId.IsNull&&feature!=null&&predecessor!=null&&
            ModelFeatureReorderState345.Forming(featureId)&&ModelFeatureReorderState345.Forming(predecessorId)&&
            feature.HostSolidID==body.ID&&predecessor.HostSolidID==body.ID,"OUT_OF_SCOPE","Select two retained forming features owned by this same part.");
        Need(feature.OrderObjBefore.ToString().Equals(edit.ExpectedCurrentPredecessorId,StringComparison.OrdinalIgnoreCase),"PRECONDITION_FAILED","The selected feature's native predecessor changed; refresh history.");
        var expected=ModelFeatureReorderPlanning345.MoveAfter(before.Features,edit.FeatureId,edit.PredecessorId,cancel,allowTerminal:edit.ExpectedResultingLastItemId!=null);
        var resultingLast=Find(expected[^1]);bool terminalChanged=resultingLast!=before.Views.LastModelItem;
        if(edit.ExpectedResultingLastItemId!=null)
            Need(resultingLast.ToString().Equals(edit.ExpectedResultingLastItemId,StringComparison.OrdinalIgnoreCase),"PRECONDITION_FAILED","The requested resulting terminal does not match the complete planned native forming sequence.");
        if(terminalChanged)
            Need(edit.ExpectedResultingLastItemId!=null&&!before.Views.HasFollowers&&before.Views.LastHistoryItem==before.Views.LastModelItem&&
                before.Views.LastModelItem.ToString().Equals(before.Features[^1],StringComparison.OrdinalIgnoreCase),
                "OUT_OF_SCOPE","Terminal-changing reorder requires explicit resulting-last intent and no retained drawing-view followers or nonforming terminal; their marker-preservation adapter is separate.");
        var dependents=Dependents([featureId,predecessorId],before,cancel);
        return new(edit,before,featureId,predecessorId,expected,dependents,resultingLast,terminalChanged);
    }
    internal static void Preflight(ModifyArguments args,DocumentState state,Db.Transaction tx,Action? cancel=null)=>Resolve(args,state,tx,cancel??(()=>{}));
    static ModelFeatureReorderChange345 Change(Binding binding,ModifyArguments args,ModelFeatureReorderState345.Snapshot after,Action cancel,bool project=false)
    {
        var changes=new List<ModelFeatureOrderChange345>();
        foreach(var pair in binding.Before.Previous.OrderBy(value=>value.Key,StringComparer.Ordinal))
        {
            cancel();string? current=project?"{FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF}":after.Previous[pair.Key];
            if(project||pair.Value!=current)changes.Add(new(pair.Key,pair.Value,current));
        }
        var source=new EntityRef(args.BridgeId,args.DocumentId,binding.Edit.Handles.Single());
        var currentRef=new EntityRef(args.BridgeId,args.DocumentId,project?"FFFFFFFFFFFFFFFF":Validation.NormalizeHandle(after.Representation.Handle.ToString()));
        return new(0,source,currentRef,binding.Before.Body.ID.ToString(),binding.Feature.ToString(),binding.Predecessor.ToString(),
            binding.Edit.ExpectedCurrentPredecessorId,(project&&binding.TerminalChanged?binding.ResultingLast:after.Body.LastPartItemID).ToString(),project||source.handle!=currentRef.handle,changes.ToArray());
    }
    static Result Outcome(Binding binding,ModifyArguments args,WriteJob job,ReadContext context,ModelFeatureReorderState345.Snapshot after,Action cancel,bool project=false)
    {
        var change=Change(binding,args,after,cancel,project);var current=change.body_ref;bool replaced=change.source_representation_replaced;
        var data=new ModelFeatureReorderOutput345(args.OperationId,"committed",false,[current],replaced?[change.source]:[],replaced?[current]:[],[],"not_guaranteed",[change]);
        var currentDependents=project?binding.Dependents:Dependents([binding.Feature,binding.Predecessor],after,cancel);
        var notices=binding.Before.Warnings.Concat(after.Warnings).Concat(project?[ModelFeatureReorderState345.ValidityWarning,DependentWarning]:
                binding.Dependents.GetRawText()!=currentDependents.GetRawText()?[DependentWarning]:Array.Empty<string>())
            .Append(WriteRecovery.Warning).Append(ReviewWarning).Distinct(StringComparer.Ordinal).ToArray();
        return Result.Ok(job.OriginalRequestId,data,context,notices);
    }
    static void ResultBytes(Result result,Action cancel)
    {
        cancel();Need(JsonSerializer.SerializeToUtf8Bytes(result,Wire.Json).Length<=Wire.PayloadBudget,"RESPONSE_LIMIT_EXCEEDED","The complete native reorder receipt exceeds the transport byte budget; reduce the requested scope before a write.");
    }
    static ModelFeatureReorderState345.Snapshot Verify(Binding binding,ModifyArguments args,DocumentState state,Action cancel,bool reordered)
    {
        using var tx=state.Document.Database.TransactionManager.StartTransaction();ModelFeatureReorderState345.Scope(state,tx,binding.Before.NativeDocument);
        var current=ModelHistoryEditGuard.CurrentRepresentation(binding.Before.Body,state,tx,cancel);
        var after=ModelFeatureReorderState345.Capture(binding.Before.Body,current,args,state,tx,cancel);
        // Capture has completed. Evaluate only the existing read-only/pure
        // predicates together: a failed metadata check must not hide a second
        // design/source/constraint/order failure behind another native restart.
        bool inventory=after.Inventory.Contains(current)&&(current==binding.Before.Representation||!after.Inventory.Contains(binding.Before.Representation))&&
            after.Inventory.Except(binding.Before.Inventory).All(id=>{cancel();return id==current;})&&binding.Before.Inventory.Except(after.Inventory).All(id=>{cancel();return id==binding.Before.Representation;});
        bool display=binding.Before.Properties.GetRawText()==after.Properties.GetRawText();
        bool metadata=binding.Before.BodyMetadata.GetRawText()==after.BodyMetadata.GetRawText();
        var expectedLast=reordered&&binding.TerminalChanged?binding.ResultingLast:binding.Before.Views.LastHistoryItem;
        var actualLast=after.Body.LastPartItemID;bool terminal=actualLast==expectedLast;
        bool preserved=ModelFeatureReorderState345.Preserved(binding.Before,after,expectedLast,cancel);
        bool order=after.Features.SequenceEqual(reordered?binding.ExpectedOrder:binding.Before.Features,StringComparer.OrdinalIgnoreCase);
        bool previous=reordered?after.Previous.TryGetValue(binding.Feature.ToString(),out var predecessor)&&
            predecessor?.Equals(binding.Predecessor.ToString(),StringComparison.OrdinalIgnoreCase)==true:
            binding.Before.Previous.OrderBy(value=>value.Key,StringComparer.Ordinal).SequenceEqual(after.Previous.OrderBy(value=>value.Key,StringComparer.Ordinal));
        JsonElement? dependents=null;Exception? dependentError=null;
        try{dependents=Dependents([binding.Feature,binding.Predecessor],after,cancel);}
        catch(OperationCanceledException){throw;}
        catch(Exception error){dependentError=error;}
        bool? downstream=dependents.HasValue?(reordered||ModelFeatureReorderState345.Equivalent(binding.Dependents,dependents.Value,cancel)):null;
        object Diagnostic(bool? followers=null,string? followerReason=null)=>Diagnostics(binding,after,current,expectedLast,actualLast,reordered,
            inventory,display,metadata,terminal,preserved,order,previous,downstream,dependents,
            dependentError is DomainException domain?domain.Error.code:dependentError?.GetType().Name,
            followers,followerReason,cancel);
        void Require(bool ok,string code,string message)
        {if(!ok)throw new DomainException(code,message,false,Diagnostic(null,"not_evaluated_after_failed_prerequisite; follower_refresh_can_write_native_geometry"));}
        // Admission/order of reported failures is unchanged. The additional
        // observations cannot turn a false predicate or unreadable scope true.
        Require(inventory,"OBJECT_IDENTITY_MISMATCH","Reordering changed another top-level object or left an ambiguous body representation.");
        Require(display,"MODEL_STATE_UNAVAILABLE","Reordering changed the retained body display properties.");
        Require(metadata,"MODEL_STATE_UNAVAILABLE","Reordering changed the retained body representation's fields, reactors, XData or classified owned dictionary/Xrecord data.");
        Require(terminal,"MODEL_ORDER_MISMATCH","The current native End-of-Part does not match the explicitly retained/requested terminal.");
        Require(preserved,"MODEL_STATE_UNAVAILABLE","Reordering changed a retained native identity, source, profile, sketch, driver, parameter or ownership relationship.");
        Require(order,"MODEL_ORDER_MISMATCH","Observed native forming-feature order does not match the requested complete sequence.");
        Require(previous,reordered?"MODEL_ORDER_MISMATCH":"PRECONDITION_FAILED",reordered?
            "The selected feature did not retain the requested native predecessor.":"Native predecessor links changed before the reorder was started.");
        if(dependentError!=null)
        {
            string code=dependentError is DomainException domain?domain.Error.code:"MODEL_FEATURE_REORDER_FAILED";
            throw new DomainException(code,dependentError.Message,false,new{snapshot_diagnostics=Diagnostic(null,"not_evaluated_after_failed_dependency_observation"),
                observation_details=dependentError is DomainException observed?observed.Error.details:null});
        }
        Require(downstream==true,"DEPENDENCY_OWNERSHIP_MISMATCH","Native downstream identities changed before the reorder was started.");
        // This normal success-path call can refresh standalone native views.
        // It is never executed just to diagnose a failed preceding predicate.
        try{ModelViewFollowers.Verify(binding.Before.Body,binding.Before.Views,cancel,refreshStandalone:reordered);}
        catch(OperationCanceledException){throw;}
        catch(DomainException error)
        {throw new DomainException(error.Error.code,error.Message,error.Error.retryable,new{snapshot_diagnostics=Diagnostic(false,"normal_path_follower_verification_failed; snapshots_precede_refresh"),follower_details=error.Error.details});}
        catch(Exception error)
        {throw new DomainException("MODEL_FEATURE_REORDER_FAILED",error.Message,false,new{snapshot_diagnostics=Diagnostic(false,"normal_path_follower_verification_failed; snapshots_precede_refresh"),follower_exception_type=error.GetType().Name});}
        return after;
    }
    static object Diagnostics(Binding binding,ModelFeatureReorderState345.Snapshot after,Db.ObjectId current,McObjectId expectedLast,McObjectId actualLast,bool reordered,
        bool inventory,bool display,bool metadata,bool terminal,bool preserved,bool order,bool previous,bool? downstream,JsonElement? dependents,
        string? dependentError,bool? followers,string? followerReason,Action cancel)
    {
        var before=binding.Before;var compared=new List<ModelRepresentationDiagnostics377.Comparison>();
        void Add(string name,bool? retained,string comparison,JsonElement? prior,JsonElement? actual,string? reason=null)
        {cancel();compared.Add(new(name,retained,comparison,prior,actual,reason));}
        // Root and all readable owned data stay exact; this diagnostic has no
        // Field-evaluation/cache or runtime-class storage normalization.
        var oldRoot=before.BodyMetadata.GetProperty("root");var newRoot=after.BodyMetadata.GetProperty("root");
        var oldOwned=before.BodyMetadata.GetProperty("owned");var newOwned=after.BodyMetadata.GetProperty("owned");
        Add("body_root",oldRoot.GetRawText()==newRoot.GetRawText(),"exact_stored_root_metadata",oldRoot,newRoot);
        Add("body_owned_records",oldOwned.GetRawText()==newOwned.GetRawText(),"exact_stored_owned_graph; only existing direct root-owner back-reference normalized",oldOwned,newOwned);
        Add("body_metadata",metadata,"original_exact_BodyMetadata_predicate",null,null);
        Add("body_display",display,"original_exact_display_predicate",before.Properties,after.Properties);
        JsonElement Inventory(ModelFeatureReorderState345.Snapshot value)=>Wire.Element(value.Inventory.Select(id=>{cancel();return id.Handle.ToString();}).OrderBy(id=>id,StringComparer.Ordinal).ToArray());
        Add("top_level_inventory",inventory,"only_unique_current_representation_may_replace_prior",Inventory(before),Inventory(after));
        Add("terminal",terminal,"actual_native_expected_last_item",Wire.Element(expectedLast.ToString()),Wire.Element(actualLast.ToString()));
        var retainedAfter=System.Text.Json.Nodes.JsonNode.Parse(after.Preserved.GetRawText())!.AsObject();
        retainedAfter["last"]=System.Text.Json.Nodes.JsonNode.Parse(before.Preserved.GetProperty("last").GetRawText());
        Add("retained_design",preserved,"original_Preserved_including_constraint_predicate; only independently requested terminal normalized",before.Preserved,Wire.Element(retainedAfter));
        JsonElement Constraints(ModelFeatureReorderState345.Snapshot value)=>Wire.Element(value.Constraints.OrderBy(pair=>pair.Key,StringComparer.Ordinal).Select(pair=>new{id=pair.Key,observed=pair.Value}).ToArray());
        bool constraints=before.Constraints.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(after.Constraints.Keys)&&
            before.Constraints.All(pair=>{cancel();return ModelSketchConstraintObservation353.Equivalent(pair.Value,after.Constraints[pair.Key],cancel);});
        Add("retained_constraints",constraints,"existing_available_constraint_observation_comparison",Constraints(before),Constraints(after));
        Add("forming_order",order,"original_complete_forming_order_predicate",Wire.Element(reordered?binding.ExpectedOrder:before.Features),Wire.Element(after.Features));
        JsonElement Previous(ModelFeatureReorderState345.Snapshot value)=>Wire.Element(value.Previous.OrderBy(pair=>pair.Key,StringComparer.Ordinal).Select(pair=>new{id=pair.Key,previous=pair.Value}).ToArray());
        Add("predecessor_links",previous,reordered?"selected_predecessor_only; other changes are observed":"original_exact_prior_predecessor_links",Previous(before),Previous(after));
        Add("selected_downstream",downstream,reordered?"actual_owned_current_downstream; changes_are_observations":"original_exact_prior_downstream",binding.Dependents,dependents,dependentError);
        Add("followers",followers,"original_success_path_Verify; diagnostic never requests refresh",before.Views.Metadata,after.Views.Metadata,followerReason);
        string? Owner(ModelFeatureReorderState345.Snapshot value)=>value.Preserved.GetProperty("variables_owner").GetString();
        return ModelRepresentationDiagnostics377.Describe(new("reorder_model_feature",before.Body.ID.ToString(),before.Representation.Handle.ToString(),current.Handle.ToString(),
            before.NativeDocument,after.NativeDocument,Owner(before),Owner(after),oldRoot.GetProperty("owner").GetString(),newRoot.GetProperty("owner").GetString(),oldOwned.GetArrayLength(),newOwned.GetArrayLength()),compared,cancel);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Result Execute(ModifyArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        Binding? binding=null;McUndoPoint? tracker=null;bool started=false;string stage="preparation";
        void Trace(string value){stage=value;log.Write(job.Operation,job.OriginalRequestId,"model_feature_reorder_checkpoint",transaction:value,operationId:job.OperationId);}
        void Close(){var value=tracker;tracker=null;if(value==null)return;try{value.Stop();}finally{value.Dispose();}}
        try
        {
            using(var tx=state.Document.Database.TransactionManager.StartTransaction())binding=Resolve(args,state,tx,job.CheckCancellation);
            var fresh=Verify(binding,args,state,job.CheckCancellation,false);
            // Budget the largest complete mapping: every retained assigned
            // link can change. This happens BEFORE native tracking/setters.
            var projected=Outcome(binding,args,job,context,fresh,job.CheckCancellation,true);ResultBytes(ModelViewFollowers.ReserveWarnings(projected,binding.Before.Views),job.CheckCancellation);
            var feature=binding.Feature.GetObjectOfType<HistoryItem>();Need(feature!=null,"MODEL_STATE_UNAVAILABLE","Selected feature disappeared before native reordering.");
            job.CheckCancellation();bool eligible=feature.SetOrderObjBefore(binding.Predecessor,true);
            if(binding.TerminalChanged)
            {
                var terminal=binding.ResultingLast.GetObjectOfType<HistoryItem>();
                Need(terminal!=null,"MODEL_STATE_UNAVAILABLE","The requested resulting terminal disappeared.");
                job.CheckCancellation();eligible=terminal.SetAsLastPartItem(true)&&eligible;
            }
            // The SDK documents TryOnly as read-only. Observe the complete
            // retained state after it even when eligibility is false. A proven
            // unexpected mutation is unknown, never a claimed not-started edit.
            try{_=Verify(binding,args,state,job.CheckCancellation,false);}
            catch(DomainException error)when(error.Error.code is "MODEL_STATE_UNAVAILABLE" or "OBJECT_IDENTITY_MISMATCH" or "MODEL_ORDER_MISMATCH" or "PRECONDITION_FAILED" or "DEPENDENCY_OWNERSHIP_MISMATCH"
                or "INVALID_GEOMETRY" or "PROFILE_OWNERSHIP_MISMATCH" or "PROFILE_PLANE_MISMATCH" or "SKETCH_OWNERSHIP_MISMATCH"
                or "CONSTRAINT_SCOPE_MISMATCH" or "CONSTRAINTS_UNAVAILABLE" or "UNSUPPORTED_OWNED_METADATA" or "NATIVE_PATH_STATE_UNAVAILABLE")
            {throw new DomainException("WRITE_OUTCOME_UNKNOWN","Native TryOnly unexpectedly changed the observed part state.",false,ModelRepresentationDiagnostics377.Error(error.Error.code,"native_try_only_verification",error.Message,binding.Before.Body.ID.ToString(),binding.Feature.ToString(),null,error.Error.details,binding.Before.Representation.Handle.ToString()));}
            Need(eligible,"MODEL_ORDER_REJECTED","Native dependency rules reject this order; no order change was started.");
            ModelViewFollowers.CheckStandalone(binding.Before.Body,binding.Before.Views,job.CheckCancellation);tracker=new McUndoPoint(false);tracker.Start();Trace("native_order_before");started=true;
            feature.TryModify((uint)McDbObject.ObjChangesType.Geom);
            Need(feature.SetOrderObjBefore(binding.Predecessor,false),"MODEL_ORDER_REJECTED","Native feature reorder was rejected.");
            feature.DbEntity.Update();
            if(binding.TerminalChanged)
            {
                var terminal=binding.ResultingLast.GetObjectOfType<HistoryItem>();
                Need(terminal!=null,"MODEL_STATE_UNAVAILABLE","The requested resulting terminal disappeared after native ordering.");
                job.CheckCancellation();Trace("native_resulting_terminal");
                Need(terminal.SetAsLastPartItem(false),"MODEL_ORDER_REJECTED","Native End-of-Part rejected the explicitly requested resulting terminal.");
            }
            McObjectManager.UpdateAll();Need(binding.Before.Body.RebuildPart(),"MODEL_UPDATE_FAILED","Native part rebuild failed after reordering.");
            McObjectManager.UpdateAll();Trace("native_rebuild_returned");
            var after=Verify(binding,args,state,job.CheckCancellation,true);Trace("native_order_verified");
            job.BeginCommit();Close();after=Verify(binding,args,state,job.CheckCancellation,true);
            var result=Outcome(binding,args,job,context,after,job.CheckCancellation);ResultBytes(result,job.CheckCancellation);
            result=ModelViewFollowers.WithWarnings(result,binding.Before.Views);job.RecordCommit(result);Trace("commit_recorded");return result;
        }
        catch(Exception error)
        {
            if(job.CommittedResult is {} committed)return committed;
            Exception? cleanup=null;try{Close();}catch(Exception exception){cleanup=exception;}
            bool unknown=started||cleanup!=null||error is DomainException{Error.code:"WRITE_OUTCOME_UNKNOWN"};string cause=error is DomainException domain?domain.Error.code:error is OperationCanceledException?"CANCELLED":"MODEL_FEATURE_REORDER_FAILED";
            string message=error.Message;if(message.Length>512)message=message[..512];
            return job.Error(unknown?"WRITE_OUTCOME_UNKNOWN":cause,unknown?"Native reorder outcome is unverified; inspect the current part and its history before a dependent edit.":message,
                unknown?"unknown":"not_started",context,ModelRepresentationDiagnostics377.Error(cause,stage,message,binding?.Before.Body.ID.ToString(),binding?.Feature.ToString(),cleanup?.GetType().Name,
                    error is DomainException observed?observed.Error.details:null,binding?.Before.Representation.Handle.ToString()))
                with{warnings=[WriteRecovery.Warning]};
        }
    }
}
