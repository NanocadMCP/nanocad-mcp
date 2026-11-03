using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using static NanoCADBridge.CadDimensionAssociation385;

namespace NanoCADBridge;

internal static class CadDimensionAssociationWriter385
{
    sealed record Binding(int Index,DimensionAssociationEdit385 Edit,ObjectId Id,Snapshot Before,PointState[] Points,JsonElement Sources);
    internal static bool Handles(ModifyArguments args)=>args.Operations.Any(edit=>edit is DimensionAssociationEdit385);
    static Binding[] Resolve(ModifyArguments args,Database db,Transaction tr,Action cancel)
    {
        DimensionAssociationValidation385.Batch(args.Operations);WriteSpace.Check(args);var space=CadWriteSpace.Resolve(db,tr,args.TargetSpace);var result=new List<Binding>();var budget=new ModelInventoryBudget(cancel);
        for(int index=0;index<args.Operations.Length;index++)
        {
            cancel();var edit=(DimensionAssociationEdit385)args.Operations[index];var id=CadStructureWriter.Find(db,edit.Handles.Single());var dimension=tr.GetObject(id,OpenMode.ForRead) as Dimension;
            Need(dimension!=null&&Supported(dimension.GetType())&&dimension.Database?.UnmanagedObject==db.UnmanagedObject&&dimension.ObjectId==id&&dimension.OwnerId==space,
                "Select one exact resident ordinary dimension in requested Model or active ordinary Paper space.","OUT_OF_SCOPE");
            var layer=(LayerTableRecord)tr.GetObject(dimension!.LayerId,OpenMode.ForRead);Need(!layer.IsLocked&&!layer.IsDependent,"The dimension layer must be local and unlocked.","LAYER_UNAVAILABLE");
            var before=Capture(dimension,db,tr,cancel);Need(before.Token==edit.AssociationToken,"Dimension/source/association state changed; refresh geometry.association.association_token.","DIMENSION_ASSOCIATION_CHANGED");
            PointState[] points=before.Points;
            if(edit.Action=="associate_dimension")
            {
                Need(before.Association.IsNull,"The dimension already has native association storage; detach an active supported association first. Inactive retained storage is observed without replacement/purge.");
                points=Bindings(edit.Bindings!,before.Kind,space,db,tr,cancel);MatchPoints(dimension,points);
            }
            else Need(before.Flags==3,"Update/detach needs a current supported active native association.");
            var sources=SourcesFor(dimension,points,before.Association,db,tr,cancel);budget.Add(new{edit,before,sources});result.Add(new(index,edit,id,before,points,sources));
        }
        return result.ToArray();
    }
    internal static void Preflight(ModifyArguments args,DocumentState state,Transaction tr,Action? cancel=null)=>Resolve(args,state.Document.Database,tr,cancel??(()=>{}));
    internal static void PreflightCreation(DimensionSpec spec,Database db,Transaction tr,ObjectId space,Action cancel)
    {
        if(spec.Associations==null)return;var points=Bindings(spec.Associations,spec.Kind,space,db,tr,cancel);var roles=Roles(spec.Kind);
        var first=points.Single(point=>point.Role==roles.First);var second=points.Single(point=>point.Role==roles.Second);
        Need(first.Evaluated.DistanceTo(new Point3d(spec.First.X,spec.First.Y,0))<=1e-7&&second.Evaluated.DistanceTo(new Point3d(spec.Second.X,spec.Second.Y,0))<=1e-7,
            "Creation coordinates must describe the actual selected source snaps; association does not fabricate or replace source geometry.","DIMENSION_SOURCE_POINT_MISMATCH");
        foreach(var source in points.Select(point=>point.Source).Distinct())
        {var entity=Source(source,space,db,tr,cancel);SourcesForDefinition(entity,db,tr,cancel);}
    }
    static void SourcesForDefinition(Entity entity,Database db,Transaction tr,Action cancel)
    {
        // Exercise complete retained source observation during creation preflight,
        // before any entity is appended or a native association is posted.
        // Metadata source capture itself requires no dimension ObjectId.
        Source(entity.ObjectId,entity.OwnerId,db,tr,cancel);
        using var data=entity.XData;new ModelInventoryBudget(cancel).Add(data==null?null:NativeOwnedMetadata.Buffer(data,cancel,_=>{}));
        NativeOwnedMetadata.Capture(entity,tr,cancel,observeAssocManager:true);
    }
    internal static void CompleteCreation(Dimension dimension,DimensionSpec spec,Database db,Transaction tr,Action cancel)
    {
        if(spec.Associations==null)return;var before=Capture(dimension,db,tr,cancel);Need(before.Association.IsNull,"The newly created dimension already has an unexpected native association.");
        var points=Bindings(spec.Associations,spec.Kind,dimension.OwnerId,db,tr,cancel);MatchPoints(dimension,points);var sources=SourcesFor(dimension,points,ObjectId.Null,db,tr,cancel);
        var posted=Post(dimension,points,db,tr,cancel);Snapshot after;
        try
        {
            after=Capture(dimension,db,tr,cancel);VerifyBound(points,after);VerifyDefinition(before,after,false);
        }
        catch(DomainException failure)
        {
            throw new DomainException(failure.Error.code,failure.Error.message,failure.Error.retryable,new
            {
                original_details=failure.Error.details,posted_association_handle=SafeId434(posted.Association),
                before_update=posted.BeforeUpdate,after_update=posted.AfterUpdate,
                after_point_ref_cleanup=ObservePosted434(dimension,points,posted.Association,tr,"after_PointRef_cleanup")
            });
        }
        Retained(before.Metadata,after.Metadata,after.Association,tr,db,cancel,before.Containers);VerifySources(sources,SourcesFor(dimension,points,after.Association,db,tr,cancel),after.Association,tr,db,cancel);
    }
    static void VerifyBound(PointState[] expected,Snapshot after)
    {
        if(after.Flags!=3||after.Points.Length!=expected.Length)throw new DomainException("NATIVE_DIMENSION_ASSOCIATION_MISMATCH","Native association is not active with both requested point references.",false,
            new{association=after.AssociationData,requested_roles=expected.Select(point=>point.Role).ToArray()});
        foreach(var point in expected)
        {
            var current=after.Points.SingleOrDefault(item=>item.Role==point.Role);
            Need(current!=null&&current.Source==point.Source&&current.Snap==point.Snap&&current.Evaluated.DistanceTo(point.Evaluated)<=1e-7,
                "Native association differs from the requested source ObjectId, snap or measured point.","NATIVE_DIMENSION_ASSOCIATION_MISMATCH");
        }
    }
    static Result Outcome(ModifyArguments args,Binding[] bindings,Snapshot[] after,WriteJob job,ReadContext context)
    {
        var refs=bindings.Select(binding=>new EntityRef(args.BridgeId,args.DocumentId,binding.Edit.Handles.Single())).ToArray();
        var changes=bindings.Select((binding,index)=>new DimensionAssociationChange385(binding.Index,refs[index],after[index].Token,Id(after[index].Association),
            after[index].Points.Select(point=>Id(point.Source)!).Distinct().ToArray(),[Warning])).ToArray();
        return Result.Ok(job.OriginalRequestId,new DimensionAssociationOutput385(args.OperationId,"committed",false,refs,[],[],[],"not_guaranteed",changes),context,[WriteRecovery.Warning,Warning]);
    }
    static void Bytes(Result result,Action cancel)
    {cancel();Need(JsonSerializer.SerializeToUtf8Bytes(result,Wire.Json).Length<=Wire.PayloadBudget-4096,"The complete association receipt exceeds actual transport bytes; reduce the group before writing.","RESPONSE_LIMIT_EXCEEDED");}
    internal static Result Execute(ModifyArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        Transaction? tr=null;bool started=false;string stage="preparation";Exception? failure=null;
        try
        {
            var db=state.Document.Database;tr=db.TransactionManager.StartTransaction();var bindings=Resolve(args,db,tr,job.CheckCancellation);
            Bytes(Outcome(args,bindings,bindings.Select(binding=>binding.Before).ToArray(),job,context),job.CheckCancellation);var after=new List<Snapshot>();
            foreach(var binding in bindings)
            {
                job.CheckCancellation();var dimension=(Dimension)tr.GetObject(binding.Id,OpenMode.ForWrite);Need(Capture(dimension,db,tr,job.CheckCancellation).Token==binding.Before.Token,"Native association changed before mutation.","DIMENSION_ASSOCIATION_CHANGED");
                stage="resident_native_association";started=true;
                if(binding.Edit.Action=="associate_dimension")Post(dimension,binding.Points,db,tr,job.CheckCancellation);
                else if(binding.Edit.Action=="update_dimension_association")Refresh(dimension,binding.Before,db,tr,job.CheckCancellation);
                else Detach(dimension,binding.Before,db,tr,job.CheckCancellation);
                stage="native_association_readback";var current=Capture(dimension,db,tr,job.CheckCancellation,binding.Edit.Action=="detach_dimension_association"?binding.Before.Association:ObjectId.Null);
                if(binding.Edit.Action=="detach_dimension_association")Need(current.Flags==0,"Native removal left active source-point association flags.","NATIVE_DIMENSION_ASSOCIATION_MISMATCH");
                else VerifyBound(binding.Points,current);
                VerifyDefinition(binding.Before,current,binding.Edit.Action=="update_dimension_association");
                Retained(binding.Before.Metadata,current.Metadata,current.Association,tr,db,job.CheckCancellation,binding.Before.Containers);
                VerifySources(binding.Sources,SourcesFor(dimension,binding.Points,binding.Edit.Action=="detach_dimension_association"?binding.Before.Association:current.Association,db,tr,job.CheckCancellation),binding.Edit.Action=="detach_dimension_association"?binding.Before.Association:current.Association,tr,db,job.CheckCancellation);
                after.Add(current);
            }
            var result=Outcome(args,bindings,after.ToArray(),job,context);Bytes(result,job.CheckCancellation);stage="native_commit";job.BeginCommit();tr.Commit();job.RecordCommit(result);
        }
        catch(Exception error){failure=error;}
        Exception? cleanup=null;try{tr?.Dispose();}catch(Exception error){cleanup=error;}
        if(job.CommittedResult is {} committed)
        {
            if(cleanup!=null){committed=committed with{warnings=committed.warnings.Append("POST_COMMIT_CLEANUP_FAILED: Native association commit is recorded; inspect current dimensions before another mutation.").ToArray()};job.RecordCommit(committed);}return committed;
        }
        string code=failure is DomainException domain?domain.Error.code:failure is OperationCanceledException?"CANCELLED":"NATIVE_DIMENSION_ASSOCIATION_FAILED";bool unknown=started||cleanup!=null;
        string? nativeMessage=failure?.Message;if(nativeMessage!=null&&System.Text.Encoding.UTF8.GetByteCount(nativeMessage)>2048)nativeMessage="Native message exceeds2048 diagnostic bytes.";
        return job.Error(unknown?"WRITE_OUTCOME_UNKNOWN":code,unknown?"Native association mutation started or cleanup failed; outcome remains unverified. Preserve the exact request, refresh current state and do not retry with a new operation_id.":failure?.Message??"Native association did not complete.",
            unknown?"unknown":"not_started",context,new{cause=code,stage,native_details=failure is DomainException failed?failed.Error.details:null,native_status=failure is Teigha.Runtime.Exception native?native.ErrorStatus.ToString():null,native_message=nativeMessage,cleanup_error=cleanup?.GetType().Name}) with{warnings=[WriteRecovery.Warning,Warning]};
    }
}
