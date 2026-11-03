using System.Runtime.CompilerServices;
using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class ModelSketchVisibilityWriter436
{
    sealed record Binding(ModelSketchVisibilitySpec436 Edit,Mc3dSolid Body,Db.ObjectId Representation,
        SketchVisibilityState436.Snapshot Before,SketchVisibilityState436.Dimension[] Selected);
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok,string code,string message)=>SketchVisibilityState436.Need(ok,code,message);
    internal static bool Handles(ModifyArguments args)=>args.Operations.Any(edit=>edit is ModelSketchVisibilitySpec436);
    [MethodImpl(MethodImplOptions.NoInlining)]
    static Binding Resolve(ModifyArguments args,DocumentState state,Db.Transaction tx,Action cancel)
    {
        Need(args.Operations.Length==1&&args.Operations[0] is ModelSketchVisibilitySpec436,"INVALID_OPERATION_ORDER","Supply one selected sketch presentation operation.");
        var loaded=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Need(loaded.Contains("mapimgd")&&loaded.Contains("MapiBaseTypes"),"CAPABILITY_UNAVAILABLE","MultiCAD must already be loaded.");
        var edit=(ModelSketchVisibilitySpec436)args.Operations[0];var drawing=ModelFeatureReorderState345.Scope(state,tx);
        var raw=CadStructureWriter.Find(state.Document.Database,edit.Handles.Single());
        Need(tx.GetObject(raw,Db.OpenMode.ForRead) is Db.Solid3d solid&&solid.OwnerId==ModelFeatureReorderState345.Model(state.Document.Database,tx),"OUT_OF_SCOPE","Select the current top-level native part representation.");
        var native=McObjectId.FromHandle(raw.Handle.Value,drawing.ID);var body=native.GetObjectOfType<Mc3dSolid>();
        Need(body!=null&&body.ID==native&&ModelHistoryEditGuard.CurrentRepresentation(body,state,tx,cancel)==raw,"OBJECT_IDENTITY_MISMATCH","Select the exact current native body.");
        var ids=ModelInventoryBudget.History(body,cancel).Where(id=>id.ToString().Equals(edit.SketchId,StringComparison.OrdinalIgnoreCase)).ToArray();
        Need(ids.Length==1,"OUT_OF_SCOPE","The requested sketch is not in this part's current history.");
        var sketch=ids[0].GetObjectOfType<PlanarSketch>();
        Need(sketch!=null&&sketch.ID==ids[0]&&sketch.DbEntity.Document?.ID==body.DbEntity.Document?.ID,"SKETCH_BINDING_UNAVAILABLE","The exact selected sketch and part owner do not match.");
        var before=SketchVisibilityState436.Capture(body,sketch,cancel);
        Need(before.Presentation.visibility_token==edit.ExpectedVisibilityToken,"PRECONDITION_FAILED","Sketch mode, relations or native presentation changed; refresh inspect_sketch_constraints.presentation.");
        SketchVisibilityState436.Dimension[] selected=[];
        if(edit.DimensionsVisible.HasValue)
        {
            Need(before.Presentation.dimensions!=null,"PRESENTATION_UNAVAILABLE","The current annotation inventory is unavailable; sketch visibility can still be changed separately.");
            var requested=edit.ConstraintIds?.ToHashSet(StringComparer.OrdinalIgnoreCase);
            Need(requested==null||requested.All(id=>before.Dimensions.Any(d=>d.Constraint.ToString().Equals(id,StringComparison.OrdinalIgnoreCase))),"OUT_OF_SCOPE","Select actual dimensional constraint IDs belonging to this sketch.");
            selected=before.Dimensions.Where(d=>requested==null||requested.Contains(d.Constraint.ToString())).ToArray();
            Need(edit.SketchVisible.HasValue||selected.Any(d=>d.Object!=null),"DIMENSION_ANNOTATION_UNAVAILABLE","No selected native dimensional annotations are accessible in the current mode. Any existing driving constraints remain present; this display operation does not enter an editor or create replacement dimensions.");
        }
        return new(edit,body,raw,before,selected);
    }
    internal static void Preflight(ModifyArguments args,DocumentState state,Db.Transaction tx,Action? cancel=null)=>Resolve(args,state,tx,cancel??(()=>{}));
    internal static Result Execute(ModifyArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        Binding? binding=null;bool started=false;McUndoPoint? tracker=null;string stage="preparation";
        void Close(){var value=tracker;tracker=null;if(value!=null)try{value.Stop();}finally{value.Dispose();}}
        Result Failure(Exception error)
        {
            if(job.CommittedResult is{} committed)return committed;Exception? cleanup=null;try{Close();}catch(Exception e){cleanup=e;}
            bool unknown=started||cleanup!=null;string cause=error is DomainException d?d.Error.code:error is OperationCanceledException?"CANCELLED":"SKETCH_VISIBILITY_FAILED";
            return job.Error(unknown?"WRITE_OUTCOME_UNKNOWN":cause,unknown?"Sketch presentation needs actual-state inspection; no rollback is claimed.":error.Message,unknown?"unknown":"not_started",context,
                new{cause,stage,message=error.Message,sketch_id=binding?.Edit.SketchId,cleanup=cleanup?.Message}) with{warnings=[WriteRecovery.Warning]};
        }
        try
        {
            using(var tx=state.Document.Database.TransactionManager.StartTransaction())binding=Resolve(args,state,tx,job.CheckCancellation);
            var before=binding.Before;var sketch=before.Sketch;var edit=binding.Edit;
            bool? parent=edit.DimensionsVisible==true?true:edit.SketchVisible;
            tracker=new McUndoPoint(false);tracker.Start();
            if(parent.HasValue&&before.Presentation.native_visibility!=(parent.Value?1d:0d))
            {
                job.CheckCancellation();stage="native_sketch_visibility";started=true;
                sketch.DbEntity.Visibility=parent.Value?1d:0d;
                Need(sketch.DbEntity.Update(),"SKETCH_VISIBILITY_FAILED","Native sketch graphics update failed.");
            }
            foreach(var target in binding.Selected.Where(d=>d.Object!=null))
            {
                job.CheckCancellation();double desired=edit.DimensionsVisible!.Value?1d:0d;
                // Read again because a native parent callback may alter a child.
                if(target.Object!.DbEntity.Visibility==desired)continue;
                stage="native_dimension_visibility";started=true;target.Object.DbEntity.Visibility=desired;
                Need(target.Object.DbEntity.Update(),"SKETCH_VISIBILITY_FAILED","Native dimensional annotation graphics update failed.");
            }
            job.BeginCommit();Close();stage="post_command_presentation";
            return NativePostCommandVerification422.Defer(job,()=>
            {
                var after=SketchVisibilityState436.Capture(binding.Body,sketch,job.CheckCancellation);
                Need(ModelParameterSnapshot.Equivalent(before.Design,after.Design),"SKETCH_BINDING_CHANGED","A presentation update changed native editor mode, history or constraint/variable bindings.");
                if(parent.HasValue)Need(after.Presentation.native_visibility==(parent.Value?1d:0d),"SKETCH_VISIBILITY_FAILED","The requested sketch visibility did not persist after command completion.");
                var selected=binding.Selected.Select(d=>d.Constraint).ToHashSet();var changed=new List<string>();
                foreach(var old in before.Dimensions)
                {
                    var current=after.Dimensions.Single(d=>d.Constraint==old.Constraint);
                    if(old.Object==null){Need(current.Reason==old.Reason,"SKETCH_BINDING_CHANGED","Native annotation availability changed during presentation update.");continue;}
                    double? expected=selected.Contains(old.Constraint)&&edit.DimensionsVisible.HasValue?(edit.DimensionsVisible.Value?1d:0d):old.Visibility;
                    Need(current.Visibility==expected&&current.Object!=null,"SKETCH_VISIBILITY_FAILED","A requested or retained annotation visibility differs from the actual final state.");
                    if(current.Visibility!=old.Visibility)changed.Add(old.Constraint.ToString());
                }
                using(var tx=state.Document.Database.TransactionManager.StartTransaction())
                    Need(ModelHistoryEditGuard.CurrentRepresentation(binding.Body,state,tx,job.CheckCancellation)==binding.Representation,"OBJECT_IDENTITY_MISMATCH","Presentation changed the native body representation.");
                var reference=new EntityRef(args.BridgeId,args.DocumentId,edit.Handles.Single());
                var unavailable=binding.Selected.Where(d=>d.Object==null).Select(d=>d.Constraint.ToString()).ToArray();
                var change=new ModelSketchVisibilityChange436(0,reference,reference,edit.SketchId,edit.SketchVisible,edit.DimensionsVisible,
                    binding.Selected.Select(d=>d.Constraint.ToString()).ToArray(),edit.DimensionsVisible==true&&before.Presentation.sketch_visible!=true,
                    before.Presentation,after.Presentation,changed.ToArray(),unavailable);
                var data=new ModelSketchVisibilityOutput436(args.OperationId,"committed",false,[reference],[],[],[],"not_guaranteed",[change]);
                var warnings=new List<string>{WriteRecovery.Warning,"NATIVE_SKETCH_PRESENTATION: Only native sketch/annotation visibility was requested. Editor mode and history identities were observed unchanged. Layers and current display context can affect rendered visibility."};
                if(before.Presentation.dimensions!=null)warnings.Add("SKETCH_RELATIONS_OBSERVED: Current relation/variable bindings and expressions were observed unchanged; no solving or driver edit was requested.");
                else warnings.Add("SKETCH_RELATIONS_UNAVAILABLE: Annotation/relation preservation could not be observed in this mode; the receipt covers native sketch visibility and observed history/editor state only.");
                if(unavailable.Length>0)warnings.Add("DIMENSION_ANNOTATIONS_UNAVAILABLE: Some selected driving constraints have no accessible annotations in this mode; their IDs are reported and no all-shown claim is made.");
                var result=Result.Ok(job.OriginalRequestId,data,context,warnings.ToArray());
                Need(JsonSerializer.SerializeToUtf8Bytes(result,Wire.Json).Length<=Wire.PayloadBudget,"RESPONSE_LIMIT_EXCEEDED","The presentation receipt exceeds response bytes.");
                job.RecordCommit(result);return result;
            },Failure);
        }
        catch(Exception error){return Failure(error);}
    }
}
