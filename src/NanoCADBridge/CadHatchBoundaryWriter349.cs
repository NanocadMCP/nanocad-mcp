using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using static NanoCADBridge.CadHatchBoundaryState349;

namespace NanoCADBridge;

// Existing independent native Hatch only. Associations are never detached;
// user-authored closed loop geometry goes through native Remove/Append/Evaluate.
internal static class CadHatchBoundaryWriter349
{
    const string Warning="NATIVE_HATCH_BOUNDARY_EDIT: Complete outer/holes replace the independent stored boundary in its existing OCS plane. Pattern and island style remain; outer/ignore island styles may hide a stored hole. Recovery/Undo is not guaranteed. Refresh the token and inspect the resulting fill through MCP.";
    sealed record Binding(int Index,SetHatchBoundarySpec349 Edit,ObjectId Id,CadHatchEditingReader.Snapshot Before);
    internal static bool Handles(ModifyArguments args)=>args.Operations.Any(edit=>edit is SetHatchBoundarySpec349);
    static Binding[] Resolve(ModifyArguments args,Database db,Transaction tr,Action cancel)
    {
        HatchBoundaryValidation349.Batch(args.Operations);WriteSpace.Check(args);var space=CadWriteSpace.Resolve(db,tr,args.TargetSpace);
        var values=new List<Binding>();var budget=new ModelInventoryBudget(cancel);
        for(int index=0;index<args.Operations.Length;index++)
        {
            cancel();var edit=(SetHatchBoundarySpec349)args.Operations[index];var id=CadStructureWriter.Find(db,edit.Handles.Single());
            var hatch=tr.GetObject(id,OpenMode.ForRead) as Hatch;
            Need(hatch!=null&&hatch.GetType()==typeof(Hatch)&&hatch.OwnerId==space,"OUT_OF_SCOPE","Use one exact existing Hatch in requested Model/active ordinary Paper space.");
            var layer=(LayerTableRecord)tr.GetObject(hatch!.LayerId,OpenMode.ForRead);
            Need(!layer.IsLocked&&!layer.IsDependent,"LAYER_UNAVAILABLE","Hatch layer must be local and unlocked.");
            Need(!hatch.Associative&&hatch.Annotative!=AnnotativeStates.True,"UNSUPPORTED_HATCH_BOUNDARY_DEPENDENCY","Associated/annotative boundaries require their owning route; this operation does not detach them.");
            var before=CadHatchEditingReader.Capture(hatch,db,tr,cancel);
            Need(before.Token==edit.HatchToken,"HATCH_STATE_CHANGED","Stored Hatch state changed; read its current geometry.editing.hatch_token.");
            Need(before.Stored.SourceHandles.Length==0&&before.Sources.GetArrayLength()==0,"UNSUPPORTED_HATCH_BOUNDARY_DEPENDENCY","Independent boundary editing cannot consume retained source associations.");
            foreach(var record in before.Stored.Preserved.GetProperty("metadata").GetProperty("owned_metadata").EnumerateArray())
            {cancel();Need(record.GetProperty("common").GetProperty("reactors").GetArrayLength()==0,"UNSUPPORTED_HATCH_BOUNDARY_DEPENDENCY","Owned persistent dependency data requires its owning boundary adapter.");}
            HatchBoundaryTopology349.Validate(edit.Outer,edit.Holes,cancel);budget.Add(new{edit,before});values.Add(new(index,edit,id,before));
        }
        return values.ToArray();
    }
    internal static void Preflight(ModifyArguments args,DocumentState state,Transaction tr,Action? cancel=null)=>Resolve(args,state.Document.Database,tr,cancel??(()=>{}));
    static void Append(Hatch hatch,HatchBoundaryLoop349 loop,HatchLoopTypes role,Action cancel)
    {
        using var points=new Point2dCollection();var bulges=new DoubleCollection();
        if(loop.Vertices is {} vertices)foreach(var point in vertices){cancel();points.Add(new(point.X,point.Y));bulges.Add(0);}
        else
        {
            var center=loop.Center!;points.Add(new(center.X+loop.Radius,center.Y));points.Add(new(center.X-loop.Radius,center.Y));
            // Exact two-semicircle native bulges, matching the existing creator.
            bulges.Add(1);bulges.Add(1);
        }
        cancel();hatch.AppendLoop(role,points,bulges);cancel();
    }
    static Result Outcome(ModifyArguments args,Binding[] bindings,CadHatchEditingReader.Snapshot[] after,WriteJob job,ReadContext context)
    {
        var refs=bindings.Select(value=>new EntityRef(args.BridgeId,args.DocumentId,value.Edit.Handles.Single())).ToArray();
        var changes=bindings.Select((value,index)=>new HatchBoundaryChange349(value.Index,refs[index],after[index].Token,
            value.Before.Stored.Preserved.GetProperty("loops").GetArrayLength(),1+value.Edit.Holes.Length,value.Edit.Holes.Length,
            "independent_native_OCS_polygon_or_circle_outer_and_holes",[Warning])).ToArray();
        return Result.Ok(job.OriginalRequestId,new HatchBoundaryOutput349(args.OperationId,"committed",false,refs,[],[],[],"not_guaranteed",changes),context,[WriteRecovery.Warning,Warning]);
    }
    static void ResultBytes(Result result,Action cancel)
    {cancel();Need(JsonSerializer.SerializeToUtf8Bytes(result,Wire.Json).Length<=Wire.PayloadBudget,"RESPONSE_LIMIT_EXCEEDED","Complete native boundary receipt exceeds the actual transport-byte budget; reduce the requested group.");}
    internal static Result Execute(ModifyArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        Transaction? tr=null;bool started=false;string stage="preparation";Exception? failure=null;
        try
        {
            tr=state.Document.Database.TransactionManager.StartTransaction();var bindings=Resolve(args,state.Document.Database,tr,job.CheckCancellation);
            ResultBytes(Outcome(args,bindings,bindings.Select(value=>value.Before).ToArray(),job,context),job.CheckCancellation);
            var after=new List<CadHatchEditingReader.Snapshot>();
            foreach(var binding in bindings)
            {
                job.CheckCancellation();var hatch=(Hatch)tr.GetObject(binding.Id,OpenMode.ForWrite);
                Need(CadHatchEditingReader.Capture(hatch,state.Document.Database,tr,job.CheckCancellation).Token==binding.Before.Token,"HATCH_STATE_CHANGED","Hatch changed before the native boundary setter.");
                stage="native_remove_append_loops";started=true;
                for(int index=hatch.NumberOfLoops-1;index>=0;index--){job.CheckCancellation();hatch.RemoveLoopAt(index);}
                Append(hatch,binding.Edit.Outer,HatchLoopTypes.External,job.CheckCancellation);
                foreach(var hole in binding.Edit.Holes)Append(hatch,hole,HatchLoopTypes.Default,job.CheckCancellation);
                stage="native_evaluation";job.CheckCancellation();hatch.EvaluateHatch(true);hatch.RecordGraphicsModified(true);job.CheckCancellation();
                var current=CadHatchEditingReader.Capture(hatch,state.Document.Database,tr,job.CheckCancellation);
                Retained(binding.Before,current,job.CheckCancellation);VerifyLoops(hatch,binding.Edit,job.CheckCancellation);after.Add(current);
            }
            var result=Outcome(args,bindings,after.ToArray(),job,context);ResultBytes(result,job.CheckCancellation);
            stage="native_commit";job.BeginCommit();tr.Commit();job.RecordCommit(result);
        }
        catch(Exception error)
        {
            failure=error;
        }
        Exception? cleanup=null;try{tr?.Dispose();}catch(Exception error){cleanup=error;}
        if(job.CommittedResult is {} committed)
        {
            if(cleanup!=null)
            {
                committed=committed with{warnings=committed.warnings.Append("POST_COMMIT_CLEANUP_FAILED: Native boundary commit is recorded; inspect the current Hatch before another mutation.").ToArray()};
                job.RecordCommit(committed);
            }
            return committed;
        }
        string code=failure is DomainException domain?domain.Error.code:failure is OperationCanceledException?"CANCELLED":"NATIVE_HATCH_BOUNDARY_FAILED";
        bool unknown=started||cleanup!=null;string message=failure?.Message??"Native boundary operation did not complete.";
        return job.Error(unknown?"WRITE_OUTCOME_UNKNOWN":code,unknown?
            "Native boundary editing started or cleanup failed; its outcome is unverified. Preserve the exact request and inspect the current Hatch; do not retry with a new operation_id.":message,
            unknown?"unknown":"not_started",context,new{cause=code,stage,cleanup_error=cleanup?.GetType().Name}) with{warnings=[WriteRecovery.Warning,Warning]};
    }
}
