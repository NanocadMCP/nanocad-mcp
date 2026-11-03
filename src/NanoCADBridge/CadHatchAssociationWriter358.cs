using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using static NanoCADBridge.CadHatchBoundaryState349;

namespace NanoCADBridge;

// Resident native association links only. Sources are not transformed/erased,
// no replacement Hatch is built, and no native mutation is retried on failure.
internal static class CadHatchAssociationWriter358
{
    const string Warning="NATIVE_HATCH_ASSOCIATION_EDIT: The same native Hatch retains its pattern/plane/owned data. Rebind uses explicit existing closed native boundaries; detach retains its stored fill and all source curves. Only selected forward IDs and this Hatch's reverse reactors change. Shared local Hatch links remain. Native recovery/Undo is not guaranteed; refresh tokens and inspect the resulting fill.";
    sealed record Source(ObjectId Id,JsonElement Before,bool WasLinked,bool WillLink);
    sealed record Other(ObjectId Id,JsonElement Before);
    sealed record Plan(int Index,SetHatchAssociation358 Edit,ObjectId Id,CadHatchEditingReader.Snapshot Before,Source[] Sources,Other[] Others,CadHatchAssociationGeometry362.Boundary[]? Geometry);
    static string Id(ObjectId id)
    {Need(!id.IsNull&&id.IsValid&&!id.IsErased&&!id.IsEffectivelyErased,"OBJECT_READ_FAILED","Native association identity is unavailable.");return Validation.NormalizeHandle(id.Handle.ToString());}
    static bool Exact(object? a,object? b)=>JsonSerializer.Serialize(a,Wire.Json)==JsonSerializer.Serialize(b,Wire.Json);
    static string[] Handles(SetHatchAssociation358 edit)=>HatchAssociationValidation358.Sources(edit);
    static string[][] Groups(SetHatchAssociation358 edit)=>HatchAssociationValidation358.Groups(edit);
    internal static bool Handles(ModifyArguments args)=>args.Operations.Any(edit=>edit is SetHatchAssociation358);
    static HatchAssociationLoopRead390[] LoopReads(Hatch hatch,Action cancel)
    {
        var values=new List<HatchAssociationLoopRead390>();var budget=new ModelInventoryBudget(cancel);
        for(int index=0;index<hatch.NumberOfLoops;index++)
        {
            cancel();using var ids=hatch.GetAssociatedObjectIdsAt(index);var sources=new List<string>();
            foreach(ObjectId id in ids){cancel();sources.Add(Id(id));}
            var value=new HatchAssociationLoopRead390(index,(int)hatch.GetLoopAt(index).LoopType,sources.ToArray());budget.Add(value);values.Add(value);
        }
        return values.ToArray();
    }
    internal static HatchAssociationRead358 Describe(Hatch hatch,Action? cancellation=null)=>new(hatch.Annotative!=AnnotativeStates.True,
        "retained_native_Hatch_forward_IDs_and_reverse_source_reactors",hatch.Associative?["detach","rebind"]:["rebind"],
        ["current_hatch_token","explicit_action","rebind_outer_and_hole_source_handles_or_groups"],"same_space_one_closed_zero_width_Polyline_or_Circle_or_full_Ellipse_per_group_or_connected_native_Line_Arc_chain_in_retained_OCS_plane_or_one_closed_native_Spline_in_WCS_XY_Z0; detach_retains_existing_readable_loops",
        ["closed_zero_width_Polyline_Line_Arc_bulges","Circle","full_Ellipse","connected_Line_Arc_chain","closed_native_Spline_WCS_XY_Z0"],"native_finite_curve_intersections_and_detached_Region_face_containment; strict_inside_separate_non_nested_holes",LoopReads(hatch,cancellation??(()=>{})));
    static void Layer(Entity entity,Transaction tr)
    {
        var layer=(LayerTableRecord)tr.GetObject(entity.LayerId,OpenMode.ForRead);
        Need(!layer.IsLocked&&!layer.IsDependent,"LAYER_UNAVAILABLE","Hatch and every touched source layer must be local and unlocked.");
    }
    static JsonElement OtherState(Hatch hatch,Transaction tr,Action cancel)=>Wire.Element(CadHatchEditingReader.Stored(hatch,cancel,tr));
    static Plan[] Resolve(ModifyArguments args,Database db,Transaction tr,Action cancel)
    {
        HatchAssociationValidation358.Batch(args.Operations);WriteSpace.Check(args);var space=CadWriteSpace.Resolve(db,tr,args.TargetSpace);
        var result=new List<Plan>();var budget=new ModelInventoryBudget(cancel);var allTouched=new HashSet<ObjectId>();
        for(int index=0;index<args.Operations.Length;index++)
        {
            cancel();var edit=(SetHatchAssociation358)args.Operations[index];var id=CadStructureWriter.Find(db,edit.Handles.Single());
            var item=tr.GetObject(id,OpenMode.ForRead);
            Need(item is Hatch&&item.GetType()==typeof(Hatch)&&item.Database==db&&item.OwnerId==space,"OUT_OF_SCOPE","Select an exact local resident Hatch in requested Model or active ordinary Paper space.");
            var hatch=(Hatch)item;Layer(hatch,tr);Need(hatch.Annotative!=AnnotativeStates.True,"UNSUPPORTED_HATCH_CONTENT","Annotative association editing requires its owning adapter.");
            var before=CadHatchEditingReader.Capture(hatch,db,tr,cancel);Need(before.Token==edit.HatchToken,"HATCH_STATE_CHANGED","Read this Hatch's current complete hatch_token.");
            if(edit.Action=="detach")Need(hatch.Associative&&before.Stored.SourceHandles.Length>0,"NO_CHANGE","This Hatch has no native association to detach.");
            var old=before.Stored.SourceHandles.Select(value=>CadStructureWriter.Find(db,value)).ToHashSet();
            var wanted=Handles(edit).Select(value=>CadStructureWriter.Find(db,value)).ToArray();
            var sources=new List<Source>();var other=new Dictionary<ObjectId,Other>();
            foreach(var sourceId in old.Concat(wanted).Distinct())
            {
                cancel();Need(sourceId!=id&&allTouched.Add(sourceId),"INVALID_OPERATION_ORDER","Each association batch must have disjoint touched source sets; refresh dependent tokens between shared-source edits.");
                var value=tr.GetObject(sourceId,OpenMode.ForRead);
                Need(value is Entity&&value.OwnerId==space&&value.Database==db,"OUT_OF_SCOPE","Each old/new boundary must remain a local native entity in this Hatch's actual space.");
                var source=(Entity)value;Layer(source,tr);Need(source.Annotative!=AnnotativeStates.True,"UNSUPPORTED_HATCH_BOUNDARY","Annotative source association needs its adapter.");
                var observed=CadHatchEditingReader.CaptureBoundary358(source,tr,cancel);bool was=old.Contains(sourceId),will=wanted.Contains(sourceId);int ownLinks=0;
                using var reactors=source.GetPersistentReactorIds();
                foreach(ObjectId link in reactors)
                {
                    cancel();if(link==id){ownLinks++;continue;}
                    _=Id(link);var dependent=tr.GetObject(link,OpenMode.ForRead);
                    Need(dependent is Hatch&&dependent.GetType()==typeof(Hatch)&&dependent.OwnerId==space&&dependent.Database==db,
                        "UNSUPPORTED_HATCH_BOUNDARY_DEPENDENCY","Source has an unclassified or foreign persistent dependency.");
                    var otherHatch=(Hatch)dependent;using var refs=otherHatch.GetAssociatedObjectIds();
                    Need(otherHatch.Associative&&refs.Cast<ObjectId>().Contains(sourceId),"UNSUPPORTED_HATCH_BOUNDARY_DEPENDENCY","A shared source reverse reactor has no matching native Hatch forward link.");
                    if(!other.ContainsKey(link)){var state=OtherState(otherHatch,tr,cancel);budget.Add(state);other.Add(link,new(link,state));}
                }
                Need(ownLinks==(was?1:0),"UNSUPPORTED_HATCH_BOUNDARY_DEPENDENCY","This Hatch's reverse link must exactly match its current forward source membership.");
                var entry=new Source(sourceId,observed,was,will);budget.Add(new{handle=Id(sourceId),before=observed,was_linked=was,will_link=will});sources.Add(entry);
            }
            var geometry=edit.Action=="rebind"?CadHatchAssociationTopology362.Validate(Groups(edit).Select(group=>group.Select(handle=>
                (Entity)tr.GetObject(CadStructureWriter.Find(db,handle),OpenMode.ForRead)).ToArray()).ToArray(),hatch,cancel):null;
            foreach(var source in sources)
            {
                cancel();Need(Exact(source.Before,CadHatchEditingReader.CaptureBoundary358((Entity)tr.GetObject(source.Id,OpenMode.ForRead),tr,cancel)),"NATIVE_HATCH_SOURCE_CHANGED","Native topology preparation changed an actual retained source.");
            }
            var plan=new Plan(index,edit,id,before,sources.ToArray(),other.Values.ToArray(),geometry);budget.Add(new{before,geometry});result.Add(plan);
        }
        return result.ToArray();
    }
    internal static void Preflight(ModifyArguments args,DocumentState state,Transaction tr,Action? cancel=null)=>Resolve(args,state.Document.Database,tr,cancel??(()=>{}));
    static JsonElement WithoutOwnLink(JsonElement snapshot,string hatchHandle)
    {
        var root=snapshot.EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.Clone());
        var metadata=root["metadata"].EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.Clone());
        metadata["reactors"]=Wire.Element(metadata["reactors"].EnumerateArray().Where(value=>value.GetString()!=hatchHandle).Select(value=>value.Clone()).ToArray());
        root["metadata"]=Wire.Element(metadata);return Wire.Element(root);
    }
    static void Retained(Plan plan,Hatch hatch,CadHatchEditingReader.Snapshot actual,Transaction tr,Action cancel)
    {
        Need(plan.Before.Identity==actual.Identity,"NATIVE_HATCH_ASSOCIATION_MISMATCH","Native association editing changed Hatch identity/owner.");
        foreach(var property in plan.Before.Stored.Preserved.EnumerateObject())
        {cancel();if(property.Name is not("loops" or "associative"))Need(Exact(property.Value,actual.Stored.Preserved.GetProperty(property.Name)),"NATIVE_HATCH_ASSOCIATION_MISMATCH","Association editing changed retained Hatch plane/root/owned metadata.");}
        Need(Exact(plan.Before.Stored.Pattern,actual.Stored.Pattern)&&Exact(plan.Before.Stored.Definitions,actual.Stored.Definitions),"NATIVE_HATCH_ASSOCIATION_MISMATCH","Association editing changed the retained pattern/island settings or definitions.");
        var expected=Handles(plan.Edit).OrderBy(value=>value,StringComparer.Ordinal).ToArray();
        Need(hatch.Associative==(plan.Edit.Action=="rebind")&&actual.Stored.SourceHandles.SequenceEqual(expected),"NATIVE_HATCH_ASSOCIATION_MISMATCH","Native forward association IDs differ from the explicit action/source set.");
        if(plan.Geometry!=null)
        {
            CadHatchAssociationGeometry362.Verify(hatch,plan.Geometry,cancel);
        }
        else
        {
            var prior=plan.Before.Stored.Preserved.GetProperty("loops");var current=actual.Stored.Preserved.GetProperty("loops");
            Need(prior.GetArrayLength()==current.GetArrayLength(),"NATIVE_HATCH_ASSOCIATION_MISMATCH","Detach changed retained native loop count.");
            for(int index=0;index<prior.GetArrayLength();index++)foreach(var property in prior[index].EnumerateObject())
            {cancel();if(property.Name!="associated_handles")Need(Exact(property.Value,current[index].GetProperty(property.Name)),"NATIVE_HATCH_ASSOCIATION_MISMATCH","Detach changed stored native loop geometry/topology.");}
        }
        foreach(var source in plan.Sources)
        {
            cancel();var entity=(Entity)tr.GetObject(source.Id,OpenMode.ForRead);var snapshot=CadHatchEditingReader.CaptureBoundary358(entity,tr,cancel);
            Need(Exact(WithoutOwnLink(source.Before,Id(plan.Id)),WithoutOwnLink(snapshot,Id(plan.Id))),"NATIVE_HATCH_ASSOCIATION_MISMATCH","Association editing changed source geometry/owner/root/owned data or another dependency.");
            using var refs=entity.GetPersistentReactorIds();Need(refs.Cast<ObjectId>().Count(value=>value==plan.Id)==(source.WillLink?1:0),"NATIVE_HATCH_ASSOCIATION_MISMATCH","Native reverse reactor does not exactly match the requested source membership.");
        }
        foreach(var other in plan.Others){cancel();Need(Exact(other.Before,OtherState((Hatch)tr.GetObject(other.Id,OpenMode.ForRead),tr,cancel)),"NATIVE_HATCH_ASSOCIATION_MISMATCH","Changing this association changed another shared Hatch's stored geometry, pattern, IDs or readable metadata.");}
    }
    static Result Outcome(ModifyArguments args,Plan[] plans,CadHatchEditingReader.Snapshot[] states,WriteJob job,ReadContext context)
    {
        EntityRef Ref(string handle)=>new(args.BridgeId,args.DocumentId,handle);
        var changed=plans.Select((plan,index)=>new HatchAssociationChange358(plan.Index,Ref(Id(plan.Id)),plan.Edit.Action,states[index].Token,plan.Edit.Action=="rebind",
            plan.Before.Stored.SourceHandles.Select(Ref).ToArray(),Handles(plan.Edit).Select(Ref).ToArray(),states[index].Stored.Preserved.GetProperty("loops").GetArrayLength(),
            "actual_retained_Hatch_bidirectional_native_source_links; sources_not_transformed_or_erased",
            plan.Geometry?.SelectMany((boundary,slot)=>boundary.SourceHandles.Select(handle=>new HatchAssociationSource362(Ref(handle),
                plan.Sources.Single(source=>Id(source.Id)==handle).Before.GetProperty("runtime_type").GetString()!.Split('.').Last(),slot==0?"outer":"hole"))).ToArray()??[],
            plan.Edit.BoundaryGroups==null?null:plan.Geometry?.Select((boundary,slot)=>new HatchAssociationLoopChange390(slot,slot==0?"outer":"hole",
                boundary.SourceHandles.Select(Ref).ToArray(),boundary.Kind)).ToArray())).ToArray();
        var modified=plans.SelectMany(plan=>plan.Sources.Select(source=>Id(source.Id)).Append(Id(plan.Id))).Distinct().Select(Ref).ToArray();
        return Result.Ok(job.OriginalRequestId,new HatchAssociationOutput358(args.OperationId,"committed",false,modified,[],[],[],"not_guaranteed",changed),context,[WriteRecovery.Warning,Warning]);
    }
    static void Bytes(Result result,Action cancel)
    {cancel();Need(JsonSerializer.SerializeToUtf8Bytes(result,Wire.Json).Length<=Wire.PayloadBudget-4096,"RESPONSE_LIMIT_EXCEEDED","Complete association receipt exceeds actual bytes; reduce the requested independent group before writing.");}
    internal static Result Execute(ModifyArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        Transaction? tr=null;Plan[]? plans=null;bool started=false;string stage="preparation";Exception? failure=null;
        try
        {
            var db=state.Document.Database;tr=db.TransactionManager.StartTransaction();plans=Resolve(args,db,tr,job.CheckCancellation);
            Bytes(Outcome(args,plans,plans.Select(plan=>plan.Before).ToArray(),job,context),job.CheckCancellation);var after=new List<CadHatchEditingReader.Snapshot>();
            foreach(var plan in plans)
            {
                job.CheckCancellation();var hatch=(Hatch)tr.GetObject(plan.Id,OpenMode.ForWrite);
                Need(CadHatchEditingReader.Capture(hatch,db,tr,job.CheckCancellation).Token==plan.Before.Token,"HATCH_STATE_CHANGED","Hatch/source state changed immediately before native association writes.");
                foreach(var source in plan.Sources){job.CheckCancellation();tr.GetObject(source.Id,OpenMode.ForWrite);}
                stage="native_forward_link_removal";started=true;job.CheckCancellation();hatch.RemoveAssociatedObjectIds();hatch.Associative=false;
                if(plan.Edit.Action=="rebind")
                {
                    for(int index=hatch.NumberOfLoops-1;index>=0;index--){job.CheckCancellation();hatch.RemoveLoopAt(index);}
                    job.CheckCancellation();hatch.Associative=true;var wanted=Groups(plan.Edit);
                    for(int index=0;index<wanted.Length;index++)
                    {job.CheckCancellation();using var ids=new ObjectIdCollection(wanted[index].Select(handle=>CadStructureWriter.Find(db,handle)).ToArray());hatch.AppendLoop(index==0?HatchLoopTypes.External:HatchLoopTypes.Default,ids);}
                }
                stage="native_reverse_link_update";
                foreach(var source in plan.Sources)
                {
                    job.CheckCancellation();var entity=(Entity)tr.GetObject(source.Id,OpenMode.ForWrite);using var links=entity.GetPersistentReactorIds();int actual=links.Cast<ObjectId>().Count(value=>value==plan.Id);
                    Need(actual<=1,"NATIVE_HATCH_ASSOCIATION_MISMATCH","Native association supplied duplicate reverse links.");
                    if(source.WillLink&&actual==0)entity.AddPersistentReactor(plan.Id);else if(!source.WillLink&&actual==1)entity.RemovePersistentReactor(plan.Id);
                }
                stage="native_evaluation";job.CheckCancellation();if(plan.Edit.Action=="rebind")hatch.EvaluateHatch(true);hatch.RecordGraphicsModified(true);job.CheckCancellation();
                var current=CadHatchEditingReader.Capture(hatch,db,tr,job.CheckCancellation);Retained(plan,hatch,current,tr,job.CheckCancellation);after.Add(current);
            }
            var result=Outcome(args,plans,after.ToArray(),job,context);Bytes(result,job.CheckCancellation);stage="native_commit";job.BeginCommit();tr.Commit();job.RecordCommit(result);
        }
        catch(Exception error){failure=error;}
        Exception? cleanup=null;try{tr?.Dispose();}catch(Exception error){cleanup=error;}
        if(job.CommittedResult is {} committed)
        {
            if(cleanup!=null)committed=committed with{warnings=committed.warnings.Append("POST_COMMIT_CLEANUP_FAILED: Association commit is recorded; inspect current native state before another mutation.").ToArray()};
            else try
            {
                using var verify=state.Document.Database.TransactionManager.StartTransaction();
                foreach(var plan in plans!){job.CheckCancellation();var hatch=(Hatch)verify.GetObject(plan.Id,OpenMode.ForRead);Retained(plan,hatch,CadHatchEditingReader.Capture(hatch,state.Document.Database,verify,job.CheckCancellation),verify,job.CheckCancellation);}
            }
            catch(Exception error){committed=committed with{warnings=committed.warnings.Append("POST_COMMIT_VERIFICATION_UNAVAILABLE: Native association commit is recorded, but fresh retained-state verification failed ("+error.GetType().Name+"). Inspect current Hatch and sources; do not repeat the committed operation.").ToArray()};}
            job.RecordCommit(committed);return committed;
        }
        string code=failure is DomainException domain?domain.Error.code:failure is OperationCanceledException?"CANCELLED":"NATIVE_HATCH_ASSOCIATION_FAILED";
        bool unknown=started||cleanup!=null||code=="NATIVE_HATCH_SOURCE_CHANGED";
        return job.Error(unknown?"WRITE_OUTCOME_UNKNOWN":code,unknown?"Native association writing started or cleanup failed; outcome is unverified. Preserve the exact request and inspect current Hatch/sources; do not submit another operation_id.":failure?.Message??"Association operation did not complete.",unknown?"unknown":"not_started",context,
            new{cause=code,stage,cleanup_error=cleanup?.GetType().Name}) with{warnings=[WriteRecovery.Warning]};
    }
}
