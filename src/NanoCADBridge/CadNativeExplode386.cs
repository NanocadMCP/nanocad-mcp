using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// One actual native explosion. Outputs are neither sampled nor reconstructed.
// Shared definitions stay resident; source retirement is an explicit mode.
internal static class CadNativeExplode386
{
    const string Warning="NATIVE_EXPLODE: Generated objects retain the native exploder's geometry/properties, without a promise to migrate source-owned XData, extension records or associations. retain leaves the original source; replace intentionally retires only that selected source and its own data. Shared definitions and drawing source files are not purged. Undo/rollback is not guaranteed; inspect the generated group. Unknown never means success or rollback.";
    sealed record Stored(ObjectId Id,JsonElement Content,JsonElement Metadata);
    sealed record Binding(int Index,NativeExplode386 Edit,ObjectId Id,ObjectId Owner,Stored[] Before);
    sealed class Prepared:IDisposable
    {
        internal Binding Binding {get;}
        internal DBObjectCollection Native {get;}=new();
        internal List<DBObject> Wrappers {get;}=new();
        internal List<Entity> Outputs {get;}=new();
        internal Prepared(Binding binding){Binding=binding;}
        public void Dispose()
        {
            System.Exception? failure=null;
            try{Native.Dispose();}catch(System.Exception error){failure??=error;}
            // Wrappers contains only uniquely proved detached native outputs,
            // never a refused resident/source alias or a duplicate pointer.
            foreach(var output in Wrappers)try{output.Dispose();}catch(System.Exception error){failure??=error;}
            if(failure!=null)throw new InvalidOperationException("Native explode output cleanup failed.",failure);
        }
    }
    static void Need([DoesNotReturnIf(false)]bool value,string message,string code="UNSUPPORTED_EXPLODE_SOURCE")
    {if(!value)throw new DomainException(code,message);}
    static bool Live(ObjectId id)=>!id.IsNull&&id.IsValid&&!id.IsErased&&!id.IsEffectivelyErased;
    static string Id(ObjectId id)=>Validation.NormalizeHandle(id.Handle.ToString());
    static string? OptionalId(ObjectId id)=>id.IsNull?null:Id(id);
    internal static bool Handles(ModifyArguments args)=>args.Operations.Any(value=>value is NativeExplode386);

    static Stored Capture(DBObject item,Database db,Transaction tr,Action cancel)
    {
        cancel();Need(Live(item.ObjectId)&&item.Database?.UnmanagedObject==db.UnmanagedObject,"An explode source/definition member has inconsistent native document identity.");
        using var data=item.XData;using var reactors=item.GetPersistentReactorIds();
        var budget=new ModelInventoryBudget(cancel);
        var metadata=Wire.Element(new{extension=OptionalId(item.ExtensionDictionary),item.HasFields,annotative=item.Annotative.ToString(),
            xdata=data==null?null:NativeOwnedMetadata.Buffer(data,cancel,budget.Add),
            reactors=reactors.Cast<ObjectId>().Select(OptionalId).OrderBy(value=>value,StringComparer.Ordinal).ToArray(),
            owned=NativeOwnedMetadata.Capture(item,tr,cancel)});
        object content;
        if(item is BlockTableRecord definition)
            content=new{handle=Id(item.ObjectId),owner=Id(item.OwnerId),runtime_type=item.GetType().FullName,definition.Name,
                origin=new[]{definition.Origin.X,definition.Origin.Y,definition.Origin.Z},units=(int)definition.Units,
                definition.Explodable,definition.IsAnonymous,definition.IsDynamicBlock,definition.IsLayout,definition.IsDependent,
                definition.IsFromExternalReference,definition.IsFromOverlayReference,
                members=definition.Cast<ObjectId>().Where(Live).Select(Id).ToArray()};
        else
        {
            Need(item is Entity,"Native block definitions must contain inspectable native entities.");var entity=(Entity)item;
            var summary=EntityAdapter.Summary(entity,ObjectId.Null,"00000000-0000-0000-0000-000000000000","00000000-0000-0000-0000-000000000000");
            var geometry=EntityAdapter.Detail(entity,summary,CancellationToken.None,includeNativeCurve:true).geometry;
            content=new{handle=Id(item.ObjectId),owner=Id(item.OwnerId),runtime_type=item.GetType().FullName,
                layer=Id(entity.LayerId),linetype=Id(entity.LinetypeId),properties=CadBlockMover.PreservedProperties(entity),geometry};
        }
        cancel();var snapshot=Wire.Element(content);budget.Add(snapshot);budget.Add(metadata);return new(item.ObjectId,snapshot,metadata);
    }
    static BlockTableRecord Definition(ObjectId id,Database db,Transaction tr,bool selected)
    {
        Need(Live(id)&&tr.GetObject(id,OpenMode.ForRead) is BlockTableRecord,"Native shared block definition is unavailable.");
        var definition=(BlockTableRecord)tr.GetObject(id,OpenMode.ForRead);
        Need(definition.GetType()==typeof(BlockTableRecord)&&definition.OwnerId==db.BlockTableId&&definition.Database?.UnmanagedObject==db.UnmanagedObject&&
            !definition.IsAnonymous&&!definition.IsDynamicBlock&&!definition.IsLayout&&!definition.IsDependent&&!definition.IsFromExternalReference&&!definition.IsFromOverlayReference,
            "Native explode currently selects ordinary static local block definitions, excluding dynamic/anonymous/Xref/layout definitions.");
        // This is one explosion. Nested insertions may remain insertions; their
        // own Explodable=false is not a refusal of the selected parent.
        Need(!selected||definition.Explodable,"The selected native block definition disallows explosion.","BLOCK_NOT_EXPLODABLE");return definition;
    }
    static void StaticReference(BlockReference reference)
    {
        Need((reference.GetType()==typeof(BlockReference)||reference.GetType()==typeof(MInsertBlock))&&!reference.IsDynamicBlock&&reference.Annotative!=AnnotativeStates.True,
            "Select an exact non-annotative static BlockReference or native MInsertBlock.");
        Need(!reference.AttributeCollection.Cast<ObjectId>().Any(),"Attribute-bearing insertion explosion needs an explicit occurrence-value flattening adapter; no attribute values are silently substituted by definition defaults.");
    }
    static Stored[] Source(Entity entity,Database db,Transaction tr,Action cancel)
    {
        var states=new List<Stored>{Capture(entity,db,tr,cancel)};var capacity=new ModelInventoryBudget(cancel);capacity.Add(new{states[0].Content,states[0].Metadata});
        if(entity.GetType()==typeof(Region))return states.ToArray();
        Need(entity is BlockReference,"Native explode accepts exact native BlockReference or Region sources.","UNSUPPORTED_ENTITY_TYPE");
        var root=(BlockReference)entity;StaticReference(root);var pending=new Queue<ObjectId>();pending.Enqueue(root.BlockTableRecord);
        var seen=new HashSet<ObjectId>();
        while(pending.Count>0)
        {
            cancel();var id=pending.Dequeue();if(!seen.Add(id))continue;var definition=Definition(id,db,tr,id==root.BlockTableRecord);
            var state=Capture(definition,db,tr,cancel);capacity.Add(new{state.Content,state.Metadata});states.Add(state);
            foreach(ObjectId member in definition)
            {
                cancel();if(!Live(member))continue;var value=tr.GetObject(member,OpenMode.ForRead);
                Need(value is Entity&&value.OwnerId==definition.ObjectId,"Block definition contains a non-entity or foreign-owned member.");
                Need(value is not AttributeDefinition and not AttributeReference,"Attribute definitions/occurrences require explicit attribute flattening; this static block branch is attribute-free.");
                if(value is BlockReference nested){StaticReference(nested);pending.Enqueue(nested.BlockTableRecord);}
                state=Capture(value,db,tr,cancel);capacity.Add(new{state.Content,state.Metadata});states.Add(state);
            }
        }
        return states.ToArray();
    }
    static void Retained(Stored[] before,Database db,Transaction tr,Action cancel,ObjectId omitted=default)
    {
        foreach(var prior in before)
        {
            cancel();if(prior.Id==omitted)continue;Need(Live(prior.Id),"Native explode lost a retained source or shared definition member.","NATIVE_EXPLODE_MISMATCH");
            var actual=Capture(tr.GetObject(prior.Id,OpenMode.ForRead),db,tr,cancel);
            Need(BlockMoveGeometry.SameProperties(prior.Content,actual.Content)&&prior.Metadata.GetRawText()==actual.Metadata.GetRawText(),
                "Native explode changed retained source geometry/identity, shared definition contents or user-owned metadata.","NATIVE_EXPLODE_MISMATCH");
        }
    }
    static Binding[] Resolve(ModifyArguments args,DocumentState state,Transaction tr,Action cancel)
    {
        NativeExplodeValidation386.Batch(args.Operations);WriteSpace.Check(args);var db=state.Document.Database;
        var owner=CadBlockSpace.RequireWriteSpace(db,tr,args.TargetSpace,cancel);var bindings=new List<Binding>();var capacity=new ModelInventoryBudget(cancel);
        for(int index=0;index<args.Operations.Length;index++)
        {
            var edit=(NativeExplode386)args.Operations[index];foreach(string handle in edit.Handles)
            {
                cancel();var id=CadStructureWriter.Find(db,handle);Need(Live(id),"Selected native explode source is unavailable.","OBJECT_NOT_FOUND");
                Need(tr.GetObject(id,OpenMode.ForRead) is Entity,"Selected source is not an Entity.","UNSUPPORTED_ENTITY_TYPE");var entity=(Entity)tr.GetObject(id,OpenMode.ForRead);
                Need((entity.GetType()==typeof(BlockReference)||entity.GetType()==typeof(MInsertBlock)||entity.GetType()==typeof(Region))&&entity.OwnerId==owner&&entity.Database?.UnmanagedObject==db.UnmanagedObject,
                    "Select an exact top-level BlockReference, MInsertBlock or Region in the requested active Model/Paper space.","OUT_OF_SCOPE");
                Need(entity.Annotative!=AnnotativeStates.True,"Annotative explosion requires its owning context adapter.");
                var layer=(LayerTableRecord)tr.GetObject(entity.LayerId,OpenMode.ForRead);
                Need(layer.OwnerId==db.LayerTableId&&!layer.IsDependent&&!layer.IsLocked,"Selected source layer must be local and unlocked.","LAYER_UNAVAILABLE");
                if(edit.SourceMode=="replace")
                {
                    CadIndependentCurve.Require(entity,db,tr,cancel);
                    if(entity is Region region)CadRegionBooleanEditor.CheckSource(region,db,tr,cancel);
                }
                var before=Source(entity,db,tr,cancel);foreach(var item in before)capacity.Add(new{item.Content,item.Metadata});
                bindings.Add(new(index,edit,id,owner,before));
            }
        }
        return bindings.ToArray();
    }
    internal static void Preflight(ModifyArguments args,DocumentState state,Transaction tr,Action? cancel=null)=>Resolve(args,state,tr,cancel??(()=>{}));
    static NativeExplodeObject386 Observation(Entity entity,EntityRef reference,ObjectId owner)=>new(reference,entity.GetType().FullName??entity.GetType().Name,Id(owner),entity.Layer);
    static Result Outcome(ModifyArguments args,List<Prepared> prepared,bool placeholder,WriteJob job,ReadContext context)
    {
        var changes=new List<NativeExplodeChange386>();var created=new List<EntityRef>();var erased=new List<EntityRef>();
        foreach(var item in prepared)
        {
            var binding=item.Binding;var source=new EntityRef(args.BridgeId,args.DocumentId,Id(binding.Id));var outputs=new List<NativeExplodeObject386>();
            foreach(var entity in item.Outputs)
            {
                var reference=new EntityRef(args.BridgeId,args.DocumentId,placeholder?"FFFFFFFFFFFFFFFF":Id(entity.ObjectId));
                outputs.Add(Observation(entity,reference,binding.Owner));created.Add(reference);
            }
            bool removed=binding.Edit.SourceMode=="replace";if(removed)erased.Add(source);
            changes.Add(new(binding.Index,source,binding.Edit.SourceMode,removed,outputs.ToArray(),[Warning]));
        }
        return Result.Ok(job.OriginalRequestId,new NativeExplodeOutput386(args.OperationId,"committed",false,[],erased.ToArray(),created.ToArray(),[],"not_guaranteed",changes.ToArray()),context,[WriteRecovery.Warning,Warning]);
    }
    static void Bytes(Result result,Action cancel)=>SerializedCapacity357.Check(result,Wire.PayloadBudget-8192,cancel);
    internal static Result Execute(ModifyArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        Transaction? tr=null;var prepared=new List<Prepared>();var createdRefs=new List<EntityRef>();bool started=false;string stage="source_read";System.Exception? failure=null;
        try
        {
            var db=state.Document.Database;tr=db.TransactionManager.StartTransaction();var bindings=Resolve(args,state,tr,job.CheckCancellation);
            var forbidden=new HashSet<IntPtr>();
            foreach(var binding in bindings)foreach(var prior in binding.Before)
            {job.CheckCancellation();forbidden.Add(tr.GetObject(prior.Id,OpenMode.ForRead).UnmanagedObject);}
            foreach(var binding in bindings)
            {
                job.CheckCancellation();var item=new Prepared(binding);prepared.Add(item);var source=(Entity)tr.GetObject(binding.Id,OpenMode.ForRead);
                stage="native_detached_explode";source.Explode(item.Native);
                System.Exception? acquisitionError=null;int count=item.Native.Count;
                for(int index=0;index<count;index++)try
                {
                    var value=item.Native[index];
                    Need(value!=null&&value.UnmanagedObject!=IntPtr.Zero&&!forbidden.Contains(value.UnmanagedObject)&&value.ObjectId.IsNull&&value.OwnerId.IsNull,
                        "Native explosion returned an unavailable, resident/source/shared or repeated native alias; unsafe wrapper was not adopted/disposed.","NATIVE_EXPLODE_OUTPUT_UNAVAILABLE");
                    forbidden.Add(value.UnmanagedObject);item.Wrappers.Add(value);
                }
                catch(System.Exception error){acquisitionError??=error;}
                // Acquire every safe detached output even when an earlier slot
                // fails, so cleanup neither leaks later items nor adopts aliases.
                if(acquisitionError!=null)throw acquisitionError;
                foreach(var value in item.Wrappers)
                {
                    job.CheckCancellation();Need(value is Entity,"Native exploder returned a non-entity; no output was silently dropped.","NATIVE_EXPLODE_OUTPUT_UNAVAILABLE");
                    var entity=(Entity)value;item.Outputs.Add(entity);
                    Need(Live(entity.LayerId)&&tr.GetObject(entity.LayerId,OpenMode.ForRead) is LayerTableRecord actual&&actual.OwnerId==db.LayerTableId,
                        "Native output layer identity is not in the current drawing.","NATIVE_EXPLODE_OUTPUT_UNAVAILABLE");
                }
                Need(item.Outputs.Count>0,"Native explosion produced no entities; the source has not been erased.","EMPTY_GEOMETRY");
                Retained(binding.Before,db,tr,job.CheckCancellation);
            }
            // The complete native output count/types are known before the first
            // resident mutation. Reserve maximum-width handles, not a count cap.
            Bytes(Outcome(args,prepared,true,job,context),job.CheckCancellation);
            foreach(var item in prepared)
            {
                var binding=item.Binding;Retained(binding.Before,db,tr,job.CheckCancellation);
                var owner=(BlockTableRecord)tr.GetObject(binding.Owner,OpenMode.ForWrite);
                foreach(var entity in item.Outputs)
                {
                    job.CheckCancellation();stage="append_native_output";started=true;var id=owner.AppendEntity(entity);
                    if(!id.IsNull)createdRefs.Add(new(args.BridgeId,args.DocumentId,Id(id)));
                    tr.AddNewlyCreatedDBObject(entity,true);
                    Need(Live(id)&&entity.ObjectId==id&&entity.OwnerId==binding.Owner&&entity.Database?.UnmanagedObject==db.UnmanagedObject,
                        "Native output append did not retain actual identity/current owner.","NATIVE_EXPLODE_MISMATCH");
                }
                Retained(binding.Before,db,tr,job.CheckCancellation);
                if(binding.Edit.SourceMode=="replace")
                {
                    job.CheckCancellation();stage="erase_selected_source";var source=(Entity)tr.GetObject(binding.Id,OpenMode.ForWrite);source.Erase(true);
                    Need(binding.Id.IsErased,"Native erase did not retire the selected source.","NATIVE_EXPLODE_MISMATCH");
                    Retained(binding.Before,db,tr,job.CheckCancellation,binding.Id);
                }
            }
            foreach(var item in prepared)
            {
                foreach(var output in item.Outputs)Need(Live(output.ObjectId)&&output.OwnerId==item.Binding.Owner,
                    "A generated native output was lost before commit.","NATIVE_EXPLODE_MISMATCH");
                Retained(item.Binding.Before,db,tr,job.CheckCancellation,item.Binding.Edit.SourceMode=="replace"?item.Binding.Id:ObjectId.Null);
            }
            var result=Outcome(args,prepared,false,job,context);Bytes(result,job.CheckCancellation);
            stage="native_commit";job.BeginCommit();tr.Commit();job.RecordCommit(result);
        }
        catch(System.Exception error){failure=error;}
        System.Exception? cleanup=null;
        foreach(var item in prepared)try{item.Dispose();}catch(System.Exception error){cleanup??=error;}
        try{tr?.Dispose();}catch(System.Exception error){cleanup??=error;}
        if(job.CommittedResult is {} committed)
        {
            if(cleanup!=null){committed=committed with{warnings=committed.warnings.Append("POST_COMMIT_CLEANUP_FAILED: Native explode commit is recorded; inspect current outputs before further mutation.").ToArray()};job.RecordCommit(committed);}return committed;
        }
        string code=failure is DomainException domain?domain.Error.code:failure is OperationCanceledException?"CANCELLED":"NATIVE_EXPLODE_FAILED";bool unknown=started||cleanup!=null;
        return job.Error(unknown?"WRITE_OUTCOME_UNKNOWN":code,unknown?"Native explode append/erase started or cleanup failed; preserve this exact request and inspect current state. Do not repeat with a new operation_id.":failure?.Message??"Native explosion did not complete.",
            unknown?"unknown":"not_started",context,new{cause=code,stage,native_error_status=failure is Teigha.Runtime.Exception native?native.ErrorStatus.ToString():null,
                native_details=failure is DomainException failed?failed.Error.details:null,created_refs=createdRefs.ToArray(),
                selected_sources=args.Operations.SelectMany(value=>value.Handles).Select(handle=>new EntityRef(args.BridgeId,args.DocumentId,handle)).ToArray(),
                cleanup_error=cleanup?.GetType().FullName}) with{warnings=[WriteRecovery.Warning,Warning]};
    }
}
