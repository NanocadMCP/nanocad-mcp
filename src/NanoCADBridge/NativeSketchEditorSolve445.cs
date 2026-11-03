using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.DatabaseServices.Constraints;
using Multicad.DatabaseServices.StandardObjects;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using G=Multicad.Geometry;

namespace NanoCADBridge;

// A documented editor/MCS solve, not a geometry copyback or another expression
// setter. The generic graph still verifies original IDs, variables and metadata.
internal static class NativeSketchEditorSolve445
{
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok,string code,string message)
    {if(!ok)throw new DomainException(code,message);}
    internal static bool HasOwnedSpline(PlanarSketch sketch,Action cancel)
    {
        cancel();var owner=sketch.DbEntity.Document;
        Need(owner!=null&&!owner.ID.IsNull&&!sketch.IsInEditMode,"CONSTRAINT_SCOPE_MISMATCH","Spline solve selection requires the actual outside-editor sketch owner.");
        var projections=sketch.GetContainingObjects(false,true,false);
        Need(projections!=null,"SKETCH_BINDING_UNAVAILABLE","Current source-projection membership is unavailable for Spline solve selection.");
        var raw=sketch.GetAllItemsGeometry(false);
        Need(raw!=null,"SKETCH_GEOMETRY_UNAVAILABLE","Actual native WCS geometry is unavailable for Spline solve selection.");
        var splines=new HashSet<McObjectId>();bool allOwned=true;
        Dictionary<McObjectId,System.Text.Json.JsonElement>? abstractSplines=null;
        try
        {
            ModelInventoryBudget.Ids(raw.Keys,cancel);
            foreach(var pair in raw)
            {
                cancel();if(pair.Value?.GeometryType!=G.EntityGeomType.kSpline)continue;
                Need(!pair.Key.IsNull,"SKETCH_BINDING_UNAVAILABLE","A current native Spline has no actual identity.");
                if(projections.Contains(pair.Key)||pair.Key.GetObjectOfType<ProjectionSketchEnt>()!=null){allOwned=false;continue;}
                var value=pair.Key.GetObjectOfType<DbGeometry>();var actual=pair.Key.GetObject();
                if(actual==null)
                {
                    abstractSplines??=NativeSketchMembership445.AbstractSplines(sketch,cancel,projections.ToHashSet());
                    Need(abstractSplines.ContainsKey(pair.Key),"SKETCH_BINDING_UNAVAILABLE","An abstract Spline lacks exact native map/item/frame evidence.");
                }
                else if(value==null||value.ID!=pair.Key||value.DbEntity.Document?.ID!=owner.ID||!sketch.ContainsObject(pair.Key)){allOwned=false;continue;}
                using var item=sketch.GetItemGeometry(pair.Key,false);
                Need(item!=null&&item.GeometryType==G.EntityGeomType.kSpline,"SKETCH_GEOMETRY_UNAVAILABLE","The current exact Spline item geometry is unavailable.");
                using var mapped=pair.Value.Nurb;using var direct=item.Nurb;
                Need(ModelParameterSnapshot.Equivalent(NativeSketchCurveGeometry445.Spline(mapped,cancel),NativeSketchCurveGeometry445.Spline(direct,cancel)),
                    "SKETCH_GEOMETRY_UNAVAILABLE","Native Spline map and exact WCS item getter disagree during solve selection.");
                splines.Add(pair.Key);
            }
        }
        finally{foreach(var value in raw.Values.Distinct(ReferenceEqualityComparer.Instance))if(value is IDisposable disposable)disposable.Dispose();}
        if(splines.Count==0||!allOwned)return false;
        foreach(var id in ModelProfileDiscovery348.Ids<ProjectionSketchEnt>(owner,cancel))
        {
            cancel();var projection=id.GetObjectOfType<ProjectionSketchEnt>();
            if(projection==null||!splines.Contains(projection.SrcObjectID))continue;
            Need(projection.ID==id&&projection.DbEntity.Document?.ID==owner.ID&&!projection.HostSketchID.IsNull,
                "SKETCH_BINDING_UNAVAILABLE","A current Spline projection source lacks its exact native identity, owner or sketch binding.");
            return false;
        }
        foreach(var id in ModelProfileDiscovery348.Ids<PlanarSketch>(owner,cancel))
        {
            cancel();if(id==sketch.ID)continue;var peer=id.GetObjectOfType<PlanarSketch>();
            Need(peer!=null&&peer.ID==id&&peer.DbEntity.Document?.ID==owner.ID,"SKETCH_BINDING_UNAVAILABLE","The current peer-sketch owner inventory is unavailable.");
            var geometry=peer.GetAllItemsGeometry(false);
            Need(geometry!=null,"SKETCH_BINDING_UNAVAILABLE","The current peer geometry inventory is unavailable for Spline ownership proof.");
            try{if(splines.Any(geometry.ContainsKey))return false;}
            finally{foreach(var value in geometry.Values.Distinct(ReferenceEqualityComparer.Instance))if(value is IDisposable disposable)disposable.Dispose();}
        }
        return true;
    }
    internal sealed record Transition(McObjectId SketchId,NativeSketchMembers385.Snapshot BeforeInputs,JsonElement BeforeRelations,
        NativeSketchCorrespondence410.Mapping Mapping,JsonElement Receipt,NativeSketchCorrespondence410.PointRoleWitness[] PointRoles);
    internal sealed record Pending(McObjectId SketchId,NativeSketchMembers385.Snapshot BeforeInputs,JsonElement BeforeRelations,
        NativeSketchCorrespondence410.Markers Markers,Dictionary<McObjectId,JsonElement> Variables,
        NativeSketchCorrespondence410.PointRoleWitness[] PointRoles,JsonElement Receipt,NativeBodyTransparency445.Witness BodyTransparency);
    static (NativeSketchMembers385.Snapshot Inputs,JsonElement Relations) Capture(PlanarSketch sketch,DocumentState document,Constraints2D manager,Action cancel)
    {
        cancel();var inventory=manager.GetAllConstraints();Need(inventory!=null,"CONSTRAINTS_UNAVAILABLE","The live native solver relation inventory is unavailable, not empty.");
        NativeSketchMembers385.Snapshot inputs;
        using(var tx=document.Document.Database.TransactionManager.StartTransaction())inputs=NativeSketchMembers385.Capture(sketch,document,tx,cancel,manager,inventory);
        Need(inputs.Members.Any(member=>member.Owned)&&inputs.Members.All(member=>member.Owned||member.ReferenceOnly),"OUT_OF_SCOPE","The live solver inputs are not owned members with immutable same-sketch references.");
        var relations=NativeSketchRelations410.Capture(manager,inputs,cancel,inventory).Relations;
        return(inputs,relations);
    }
    static Dictionary<McObjectId,JsonElement> Variables(NativeSketchMembers385.Snapshot inputs,JsonElement relations,Action cancel)
    {
        var result=new Dictionary<McObjectId,JsonElement>();
        foreach(string key in relations.GetProperty("constraints").EnumerateArray().Where(row=>row.GetProperty("variable").ValueKind==JsonValueKind.String).Select(row=>row.GetProperty("variable").GetString()!).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancel();var ids=inputs.Helpers.Where(id=>string.Equals(id.ToString(),key,StringComparison.OrdinalIgnoreCase)).ToArray();
            Need(ids.Length==1,"VARIABLES_UNAVAILABLE","A live solver variable has no exact observed helper identity.");
            result.Add(ids[0],NativeSketchCorrespondence410.OwnerVariable(inputs,ids[0],cancel));
        }
        return result;
    }
    static void SameVariables(Dictionary<McObjectId,JsonElement> before,NativeSketchMembers385.Snapshot current,JsonElement relations,Action cancel)
    {
        var after=Variables(current,relations,cancel);Need(before.Keys.ToHashSet().SetEquals(after.Keys),"VARIABLES_UNAVAILABLE","The editor transition changed permanent owner variable identities.");
        foreach(var pair in before){cancel();Need(ModelParameterSnapshot.Equivalent(pair.Value,after[pair.Key]),"CONSTRAINT_PARAMETER_UNAVAILABLE","The native editor changed a retained variable's exact owner, host, type, name, expression or evaluated value.");}
    }
    internal static Pending Solve(PlanarSketch sketch,DocumentState document,NativeSketchConstraintState385.Snapshot original,Action cancel)
    {
        var transparency=original.BodyTransparency;
        Need(transparency!=null&&transparency.BodyId==original.Part.Body.ID&&transparency.OwnerId==sketch.DbEntity.Document?.ID,
            "MODEL_APPEARANCE_UNAVAILABLE","The original owned-Spline snapshot has no exact pre-setter native body-transparency witness.");
        // This fresh state is deliberately after the existing expression write.
        // It witnesses identities, never replaces the original expected design.
        NativeSketchMembers385.Snapshot beforeInputs;JsonElement beforeRelations;
        using(var manager=NativeSketchMembers385.Manager(sketch)){var captured=Capture(sketch,document,manager,cancel);beforeInputs=captured.Inputs;beforeRelations=captured.Relations;}
        var roles=NativeSketchSolveExpected445.PointRoles(original,beforeInputs,beforeRelations,cancel);
        var markers=NativeSketchCorrespondence410.Capture(beforeInputs,beforeRelations,cancel) with{PointRoles=roles};var variables=Variables(beforeInputs,beforeRelations,cancel);
        Exception? primary=null,cleanup=null;bool attempted=false;PlanarSketch? editable=null;var owner=sketch.DbEntity.Document;
        NativeSketchCorrespondence410.Mapping? entered=null,applied=null;
        try
        {
            cancel();Need(owner!=null&&!owner.ID.IsNull&&!sketch.IsInEditMode&&Service.GetActiveEditSketchID().IsNull,"CONSTRAINT_SCOPE_MISMATCH","Native editor solve requires the exact retained sketch outside edit mode and no other active sketch editor.");
            attempted=true;Need(sketch.EnterEditMode(),"SKETCH_EDIT_UNAVAILABLE","The retained Spline sketch refused native edit mode.");
            var sheet=McDocumentsManager.GetActiveSheet();var active=Service.GetActiveEditSketchID();editable=Service.GetActiveEditSketch();
            Need(sheet!=null&&!sheet.ID.IsNull&&active==sketch.ID&&editable!=null&&editable.ID==sketch.ID&&editable.IsInEditMode&&editable.DbEntity.Document?.ID==owner.ID,
                "CONSTRAINT_SCOPE_MISMATCH","The current native editable sketch does not identify the selected sketch and its permanent owner.");
            using var manager=NativeSketchMembers385.Manager(editable);
            Need(manager.ConstraintsNetworkType==Constraint2dNetType.MCS&&manager.DocumentId==sheet.ID&&manager.PlanarSketchId==sketch.ID&&!manager.ScopeId.IsNull,
                "CONSTRAINT_SCOPE_MISMATCH","The editor solver does not bind the actual sheet/MCS/sketch network.");
            var entry=Capture(editable,document,manager,cancel);
            // Pending motion was authorized by the already applied expression.
            // Structural bindings/owner variables are proved in the editor. The
            // SDK completes sketch geometry after Exit/Update and command end;
            // a live editor curve/pick cache is not a physical-result oracle.
            entered=NativeSketchCorrespondence410.ResolveForSolve(markers,entry.Inputs,entry.Relations,cancel,"editor_before_parameter_solve",preserveGeometry:false,solved:false,observedManager:manager);
            SameVariables(variables,entry.Inputs,entry.Relations,cancel);
            cancel();Need(manager.ApplyAllConstraints(true),"CONSTRAINT_SOLVE_FAILED","The native editor/MCS solver refused the retained Spline sketch.");cancel();
            var current=Capture(editable,document,manager,cancel);
            applied=NativeSketchCorrespondence410.ResolveForSolve(markers,current.Inputs,current.Relations,cancel,"editor_after_parameter_solve",preserveGeometry:false,solved:false,observedManager:manager);
            SameVariables(variables,current.Inputs,current.Relations,cancel);
        }
        catch(Exception error){primary=error;}
        finally
        {
            if(attempted)
            {
                try
                {
                    var active=Service.GetActiveEditSketchID();bool selectedEditing=sketch.IsInEditMode||editable?.IsInEditMode==true||active==sketch.ID;
                    if(selectedEditing)
                    {
                        Need(active.IsNull||active==sketch.ID,"CONSTRAINT_SCOPE_MISMATCH","Another native sketch became active; its editor is not closed by this operation.");
                        var target=editable??sketch;
                        Need(target.ID==sketch.ID&&target.ExitEditMode(),"SKETCH_EDITOR_CLEANUP_FAILED","The selected Spline sketch did not leave native edit mode.");
                        Need(!sketch.IsInEditMode&&editable?.IsInEditMode!=true&&Service.GetActiveEditSketchID()!=sketch.ID,"SKETCH_EDITOR_CLEANUP_FAILED","The selected Spline sketch remains active in native edit mode.");
                        // Original SDK examples: dispose MCS manager, Exit,
                        // then Update. This is uncancelled cleanup, not another
                        // parameter setter or a retry of the native solve.
                        sketch.DbEntity.Update();
                    }
                }
                catch(Exception error){cleanup=error;}
            }
        }
        if(cleanup!=null)throw new DomainException("SKETCH_EDITOR_CLEANUP_FAILED","Native Spline editor cleanup failed; inspect the current editor before further writes.",false,
            new{primary_error_type=primary?.GetType().Name,primary_code=(primary as DomainException)?.Error.code,primary_message=primary?.Message,
                cleanup_error_type=cleanup.GetType().Name,cleanup_code=(cleanup as DomainException)?.Error.code,cleanup_message=cleanup.Message,sketch_id=sketch.ID.ToString()});
        if(primary!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
        Need(applied!=null&&entered!=null,"SKETCH_CORRESPONDENCE_UNAVAILABLE","The native apply/context witnesses are unavailable.");
        var receipt=Wire.Element(new{sketch_id=sketch.ID.ToString(),entered=entered!.Receipt,applied=applied!.Receipt,
            final_verification_pending=true,final_phase="after_matching_native_command_end; no_editor_cached_geometry_result_claim"});
        return new(sketch.ID,beforeInputs,beforeRelations,markers,variables,roles,receipt,transparency);
    }

    // After the caller's last native UpdateAll/follower preparation and before
    // closing the command. Both dimensional entry points share this one SDK path.
    internal static void PreserveTransparency(Pending pending,DocumentState document,Action cancel)
    {
        Need(pending.BeforeInputs.Sketch.ID==pending.SketchId&&pending.BeforeInputs.Sketch.DbEntity.Document?.ID==pending.BodyTransparency.OwnerId,
            "OBJECT_IDENTITY_MISMATCH","The pending transparency witness does not identify the selected native sketch owner.");
        NativeBodyTransparency445.Preserve(pending.BodyTransparency,document,cancel);
    }

    // Read-only finalization. Call only from the matching command's existing
    // NativePostCommandVerification422 callback, after normal SDK updates.
    // No second Apply, setter, body rebuild or geometric copyback occurs here.
    internal static Transition Complete(Pending pending,DocumentState document,Action cancel)
    {
        cancel();var sketch=pending.BeforeInputs.Sketch;
        Need(sketch.ID==pending.SketchId&&!sketch.IsInEditMode&&Service.GetActiveEditSketchID().IsNull,
            "CONSTRAINT_SCOPE_MISMATCH","Final native solver observation requires the exact retained sketch outside all active editors.");
        using var manager=NativeSketchMembers385.Manager(sketch);
        var after=Capture(sketch,document,manager,cancel);
        var mapping=NativeSketchCorrespondence410.ResolveForSolve(pending.Markers,after.Inputs,after.Relations,cancel,
            "persistent_after_command_parameter_solve",preserveGeometry:false,solved:true,observedManager:manager);
        SameVariables(pending.Variables,after.Inputs,after.Relations,cancel);
        NativeBodyTransparency445.Verify(pending.BodyTransparency,document,cancel);
        var receipt=Wire.Element(new{sketch_id=sketch.ID.ToString(),entered=pending.Receipt.GetProperty("entered"),applied=pending.Receipt.GetProperty("applied"),persistent=mapping.Receipt,
            preservation="strict_final_native_marker_endpoint_and_owner_variable_proof_after_command; original_expected_geometry_metadata_not_replaced"});
        return new(sketch.ID,pending.BeforeInputs,pending.BeforeRelations,mapping,receipt,pending.PointRoles);
    }
}
