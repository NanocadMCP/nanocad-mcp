using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// Existing direct body is edited in place. Only proved detached curve copies
// cross the native imprint boundary; resident source wrappers stay ForRead.
internal static class CadSolidImprintEditor393
{
    const string Scope="NATIVE_SOLID_IMPRINT: Native curves mark the retained body in their actual WCS positions. A completed call does not promise a new selectable face or a volume change. No persistent source association or model feature was created; reacquire actual topology before later face editing. Original curve identity/geometry/appearance and observed root data are retained; opaque owned private payloads are not deeply verified.";
    sealed record Source(ObjectId Id,SolidImprintCurve393 Input,CadSolidFacePath389.State Before);
    sealed record Binding(SolidImprintEditSpec393 Edit,ObjectId Target,ObjectId Model,bool Documented,
        JsonElement Root,SolidImprintTopology393 Topology,Source[] Sources);
    static void Need([DoesNotReturnIf(false)]bool ok,string code,string text)
    {if(!ok)throw new DomainException(code,text);}
    static bool Live(ObjectId id)=>!id.IsNull&&id.IsValid&&!id.IsErased&&!id.IsEffectivelyErased;
    static string Id(ObjectId id)=>Validation.NormalizeHandle(id.Handle.ToString());
    internal static bool Handles(ModifyArguments args)=>args.Operations.Any(value=>value is SolidImprintEditSpec393);
    static SolidImprintTopology393 Topology(Solid3d body,EntityRef reference,Action cancel)
    {
        var observed=Wire.Element(SolidTopologyReader.Capture(body,reference,CancellationToken.None,cancel));
        return new(observed.GetProperty("snapshot_token").GetString()!,observed.GetProperty("faces").GetArrayLength(),observed.GetProperty("edges").GetArrayLength());
    }
    static Solid3d Body(ObjectId id,ObjectId model,Database db,Transaction tr,Action cancel)
    {
        cancel();Need(Live(id)&&tr.GetObject(id,OpenMode.ForRead) is Solid3d,
            "OBJECT_NOT_FOUND","The selected native imprint body is no longer available.");
        var body=(Solid3d)tr.GetObject(id,OpenMode.ForRead);
        Need(body.GetType()==typeof(Solid3d)&&body.ObjectId==id&&body.OwnerId==model&&
            body.Database?.UnmanagedObject==db.UnmanagedObject&&body.UnmanagedObject!=IntPtr.Zero,
            "OUT_OF_SCOPE","Imprint requires one exact top-level native Solid3d in the current Model Space.");return body;
    }
    static Curve Curve(ObjectId id,ObjectId model,Database db,Transaction tr,Action cancel)
    {
        cancel();Need(Live(id)&&tr.GetObject(id,OpenMode.ForRead) is Entity,
            "OBJECT_NOT_FOUND","A retained native imprint curve is no longer available.");
        return CadSolidFacePath389.CheckPath((Entity)tr.GetObject(id,OpenMode.ForRead),db,model,cancel,"Solid imprint");
    }
    static Binding Resolve(ModifyArguments args,DocumentState state,Transaction tr,Action cancel)
    {
        Need(args.Operations is [SolidImprintEditSpec393],"INVALID_ARGUMENT","Native imprint must be the sole operation in its request.");
        WriteSpace.Check(args);Need(args.TargetSpace=="model","OUT_OF_SCOPE","Native solid imprint edits Model Space only.");
        var edit=(SolidImprintEditSpec393)args.Operations[0];var db=state.Document.Database;var model=CadWriteSpace.Resolve(db,tr,"model");
        var id=CadStructureWriter.Find(db,edit.Handles[0]);var body=Body(id,model,db,tr,cancel);
        var layer=(LayerTableRecord)tr.GetObject(body.LayerId,OpenMode.ForRead);
        Need(layer.OwnerId==db.LayerTableId&&!layer.IsDependent&&!layer.IsLocked,"LAYER_UNAVAILABLE","The imprint target layer must be local and unlocked.");
        bool documented=CadSolidEditor.CheckSource(body,db,cancel);var reference=new EntityRef(args.BridgeId,args.DocumentId,Id(id));
        var topology=Topology(body,reference,cancel);Need(topology.snapshot_token==edit.TopologyToken,"STALE_TOPOLOGY","Reacquire the target's current native topology before imprinting.");
        var sources=new List<Source>();var budget=new ModelInventoryBudget(cancel);
        foreach(var input in edit.SourceCurves)
        {
            cancel();var sourceId=CadStructureWriter.Find(db,input.Handle);Need(sourceId!=id,"INVALID_ARGUMENT","The imprint source cannot be the target solid.");
            var curve=Curve(sourceId,model,db,tr,cancel);CadRetainedSolidPath392.RequireCurrentToken(curve,input.CurveToken,cancel);
            var before=CadSolidFacePath389.Capture(curve,cancel);budget.Add(new{before.Shape,before.Metadata,before.Display});sources.Add(new(sourceId,input,before));
        }
        return new(edit,id,model,documented,CadSolidStockEditor353.RootState(body,cancel),topology,sources.ToArray());
    }
    internal static void Preflight(ModifyArguments args,DocumentState state,Transaction tr,Action? cancel=null)
        =>Resolve(args,state,tr,cancel??(()=>{}));
    static void Sources(Binding binding,Database db,Transaction tr,Action cancel)
    {
        foreach(var source in binding.Sources)
        {
            cancel();Need(Live(source.Id)&&tr.GetObject(source.Id,OpenMode.ForRead) is Entity entity&&
                entity.ObjectId==source.Id&&entity.OwnerId==binding.Model&&entity.Database?.UnmanagedObject==db.UnmanagedObject,
                "NATIVE_IMPRINT_SOURCE_CHANGED","Native imprint lost or re-owned a retained original curve.");
            var actual=CadSolidFacePath389.Capture(Curve(source.Id,binding.Model,db,tr,cancel),cancel);
            Need(ModelParameterSnapshot.Equivalent(source.Before.Shape,actual.Shape)&&
                ModelParameterSnapshot.Equivalent(source.Before.Metadata,actual.Metadata)&&
                ModelParameterSnapshot.Equivalent(source.Before.Display,actual.Display),
                "NATIVE_IMPRINT_SOURCE_CHANGED","Native imprint changed retained source geometry, identity, appearance or observed root data.");
        }
    }
    static void RetainedBody(Binding binding,Solid3d body,Action cancel)
    {
        CadSolidEditor.CheckGeometry(body,cancel);
        Need(ModelParameterSnapshot.Equivalent(binding.Root,CadSolidStockEditor353.RootState(body,cancel)),
            "OBJECT_IDENTITY_MISMATCH","Native imprint changed the retained body's root identity, appearance, XData or dependency identities.");
    }
    static Result Outcome(ModifyArguments args,Binding binding,SolidImprintTopology393 after,int completed,
        WriteJob job,ReadContext context,IEnumerable<string> warnings)
    {
        var target=new EntityRef(args.BridgeId,args.DocumentId,Id(binding.Target));
        var sources=binding.Sources.Select(value=>new SolidImprintSource393(new(args.BridgeId,args.DocumentId,Id(value.Id)),value.Input.CurveToken)).ToArray();
        var change=new SolidImprintChange393(0,target,sources,binding.Topology,after,
            binding.Topology.snapshot_token!=after.snapshot_token,completed,
            "Actual native topology of the retained body before imprint and following commit/documentation refresh. Counts and token change are observations, not a geometric delta guarantee. No persistent source association or feature is created; reacquire topology before selecting later faces.");
        return Result.Ok(job.OriginalRequestId,new ModifiedEntities(args.OperationId,"committed",false,[target],[],[],[],"not_guaranteed",solid_imprint_changes:[change]),context,warnings.ToArray());
    }
    internal static Result Execute(ModifyArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        Transaction? tr=null;var copies=new List<Curve>();Binding? binding=null;bool started=false,commitAttempted=false;
        int completed=0;string stage="source_read";System.Exception? failure=null;var warnings=new List<string>{WriteRecovery.Warning,Scope};
        try
        {
            var db=state.Document.Database;tr=db.TransactionManager.StartTransaction();binding=Resolve(args,state,tr,job.CheckCancellation);
            DirectSolidDocumentation.Snapshot? documentation=binding.Documented?DirectSolidDocumentation.Capture(state.Document,[Id(binding.Target)],job.CheckCancellation):null;
            var body=Body(binding.Target,binding.Model,db,tr,job.CheckCancellation);
            // Full actual serialized source/result bytes, never a source-count cap.
            SerializedCapacity357.Check(Outcome(args,binding,binding.Topology,binding.Sources.Length,job,context,warnings),Wire.PayloadBudget-16384,job.CheckCancellation);
            var forbidden=new HashSet<IntPtr>{body.UnmanagedObject};
            foreach(var source in binding.Sources)forbidden.Add(Curve(source.Id,binding.Model,db,tr,job.CheckCancellation).UnmanagedObject);
            stage="detached_curve_copies";
            foreach(var source in binding.Sources)
            {
                job.CheckCancellation();var curve=Curve(source.Id,binding.Model,db,tr,job.CheckCancellation);
                var copy=(Curve)CadContourBudget344.Detached(curve);
                // An unsafe cross-source/duplicate alias is not adopted/disposed.
                Need(!forbidden.Contains(copy.UnmanagedObject),"UNSAFE_NATIVE_CLONE","Native imprint curve copies must have actual distinct detached native addresses.");
                forbidden.Add(copy.UnmanagedObject);copies.Add(copy);
                CadCurveCopyGeometry417.Require(source.Before.Shape,CadRetainedSolidPath392.StableShape(copy,job.CheckCancellation),
                    curve.GetType()==typeof(Spline)&&copy.GetType()==typeof(Spline),ModelParameterSnapshot.Equivalent,
                    "NATIVE_PATH_CLONE_MISMATCH","The detached imprint curve does not retain complete observed native source geometry.",
                    "resident_imprint_path",job.CheckCancellation,warnings);
            }
            Sources(binding,db,tr,job.CheckCancellation);RetainedBody(binding,body,job.CheckCancellation);
            job.CheckCancellation();stage="native_imprint";started=true;
            body=(Solid3d)tr.GetObject(binding.Target,OpenMode.ForWrite);
            foreach(var copy in copies)
            {
                job.CheckCancellation();body.ImprintEntity(copy);completed++;
                Sources(binding,db,tr,job.CheckCancellation);RetainedBody(binding,body,job.CheckCancellation);
            }
            _=Topology(body,new(args.BridgeId,args.DocumentId,Id(binding.Target)),job.CheckCancellation);
            stage="native_commit";job.CheckCancellation();job.BeginCommit();commitAttempted=true;tr.Commit();tr.Dispose();tr=null;
            if(documentation!=null){stage="documentation_refresh";DirectSolidDocumentation.RefreshAndVerify(state.Document,documentation);}
            stage="committed_readback";SolidImprintTopology393 after;
            using(var read=db.TransactionManager.StartTransaction())
            {
                var current=Body(binding.Target,binding.Model,db,read,()=>{});RetainedBody(binding,current,()=>{});Sources(binding,db,read,()=>{});
                after=Topology(current,new(args.BridgeId,args.DocumentId,Id(binding.Target)),()=>{});
            }
            if(after.snapshot_token==binding.Topology.snapshot_token)
                warnings.Add("IMPRINT_TOPOLOGY_UNCHANGED: Native calls completed, but the observed topology token is unchanged. Do not infer a new face region; inspect actual contact and current faces before a later edit.");
            var result=Outcome(args,binding,after,completed,job,context,warnings);
            SerializedCapacity357.Check(result,Wire.PayloadBudget-8192,()=>{});job.RecordCommit(result);
        }
        catch(System.Exception error)
        {
            failure=error;
            // Detected original-source changes are resident uncertainty even
            // when discovered during native detached-copy construction.
            if(error is DomainException changed&&changed.Error.code=="NATIVE_IMPRINT_SOURCE_CHANGED")started=true;
        }
        System.Exception? cleanup=null;
        for(int index=copies.Count-1;index>=0;index--)try{copies[index].Dispose();}catch(System.Exception error){cleanup??=error;}
        try{tr?.Dispose();}catch(System.Exception error){cleanup??=error;}
        if(job.CommittedResult is {} committed)
        {
            if(cleanup!=null)
            {
                committed=committed with{warnings=committed.warnings.Append("POST_COMMIT_CLEANUP_FAILED: Native imprint commit is recorded; inspect the retained body and sources before further mutation.").ToArray()};job.RecordCommit(committed);
            }
            return committed;
        }
        string code=failure is DomainException domain?domain.Error.code:failure is OperationCanceledException?"CANCELLED":"NATIVE_IMPRINT_FAILED";
        bool unknown=started||commitAttempted||cleanup!=null;
        log.Write(job.Operation,job.OriginalRequestId,"write_checkpoint",transaction:stage,operationId:job.OperationId);
        return job.Error(unknown?"WRITE_OUTCOME_UNKNOWN":code,
            unknown?"Native imprint started, a retained source changed, commit was attempted or cleanup failed; preserve this exact request and inspect actual state before further writes. Do not repeat with a new operation_id.":failure?.Message??"Native imprint did not start.",
            unknown?"unknown":"not_started",context,new{cause=code,stage,native_calls_completed=completed,
                target=binding==null?null:new EntityRef(args.BridgeId,args.DocumentId,Id(binding.Target)),
                source_handles=args.Operations.OfType<SolidImprintEditSpec393>().SelectMany(value=>value.SourceCurves).Select(value=>value.Handle).ToArray(),
                native_error_status=failure is Teigha.Runtime.Exception native?native.ErrorStatus.ToString():null,
                native_details=failure is DomainException failed?failed.Error.details:null,cleanup_error=cleanup?.GetType().FullName}) with{warnings=warnings.ToArray()};
    }
}
