using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// Retain-only native definition creation from actual source entities. The
// origin establishes the insertion base; no source transform/replacement.
internal static class CadBlockFromObjects407
{
    const string Warning="BLOCK_FROM_OBJECTS: Native cloning preserves actual geometry/display and owned attributes; user metadata follows its native cloning adapter without exhaustive private-payload verification. Creates a reusable definition only; all selected originals and shared definitions remain. Undo/rollback is not guaranteed; unknown never means success or rollback.";
    sealed record Source(ObjectId Id,ObjectId Owner,JsonElement Geometry,JsonElement Properties,JsonElement Metadata,CadBlockMover.Resolved? Block);
    sealed record Selection(CreateBlockFromObjects407 Edit,ObjectId Space,Source[] Sources);
    static void Need([DoesNotReturnIf(false)]bool condition,string message,string code="UNSUPPORTED_BLOCK_SOURCE")
    {if(!condition)throw new DomainException(code,message);}
    static bool Live(ObjectId id)=>!id.IsNull&&id.IsValid&&!id.IsErased&&!id.IsEffectivelyErased;
    static string Id(ObjectId id)=>Validation.NormalizeHandle(id.Handle.ToString());
    internal static bool Handles(StructureWriteArguments args)=>args.Operations is [CreateBlockFromObjects407];
    static JsonElement Geometry(Entity entity,Transaction tr,Action cancel)
    {
        if(entity is BlockReference reference)
        {
            var attributes=reference.AttributeCollection.Cast<ObjectId>().Select(id=>(AttributeReference)tr.GetObject(id,OpenMode.ForRead)).ToArray();
            return CadBlockMover.Snapshot(new(reference,attributes),cancel);
        }
        return Wire.Element(CadBlockDefinitionGeometry388.StoredGeometry(entity,cancel));
    }
    static JsonElement Metadata(Entity entity,Transaction tr,Action cancel)
    {
        object Item(DBObject value)
        {
            cancel();using var data=value.XData;using var links=value.GetPersistentReactorIds();
            Need(!value.HasFields&&links.Count==0,"Field-bearing/persistently associated source objects need their owning route.");
            var budget=new ModelInventoryBudget(cancel);var owned=NativeOwnedMetadata.Capture(value,tr,cancel);
            foreach(var record in owned.EnumerateArray()){cancel();Need(record.GetProperty("common").GetProperty("reactors").GetArrayLength()==0,"Owned dependency metadata needs its owning route.");}
            return new{extension=value.ExtensionDictionary.IsNull?null:Id(value.ExtensionDictionary),xdata=data==null?null:NativeOwnedMetadata.Buffer(data,cancel,budget.Add),owned};
        }
        return Wire.Element(new{root=Item(entity),attributes=entity is BlockReference reference?
            reference.AttributeCollection.Cast<ObjectId>().Select(id=>new{handle=Id(id),owner=Id(((AttributeReference)tr.GetObject(id,OpenMode.ForRead)).OwnerId),data=Item(tr.GetObject(id,OpenMode.ForRead))}).ToArray():null});
    }
    static Selection Resolve(StructureWriteArguments args,DocumentState state,Transaction tr,Action cancel)
    {
        Need(Handles(args),"Create a block from existing objects in one separate structural request.","INVALID_ARGUMENT");
        var edit=(CreateBlockFromObjects407)args.Operations[0];var db=state.Document.Database;
        Need((int)db.Insunits==4,"Block creation from existing sources requires Millimeters; source units are not converted.","UNSUPPORTED_UNITS");
        var table=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead);Need(!table.Has(edit.Name),"This block name already exists; choose a new name.","BLOCK_EXISTS");
        var space=CadBlockSpace.RequireWriteSpace(db,tr,args.EffectiveTargetSpace,cancel);var sources=new List<Source>();var budget=new ModelInventoryBudget(cancel);
        foreach(string handle in edit.Handles)
        {
            cancel();var id=CadStructureWriter.Find(db,handle);Need(Live(id)&&tr.GetObject(id,OpenMode.ForRead) is Entity,"A selected existing entity is unavailable.","OBJECT_NOT_FOUND");
            var entity=(Entity)tr.GetObject(id,OpenMode.ForRead);Need(entity.OwnerId==space&&entity.Database?.UnmanagedObject==db.UnmanagedObject,"Select top-level source entities in the requested active Model/Paper space.","OUT_OF_SCOPE");
            Need(entity.Annotative!=AnnotativeStates.True,"Annotative objects need their owning context route.");
            CadIndependentCurve.Require(entity,db,tr,cancel);CadBlockDefinitionGeometry388.Writable(entity,db,tr);
            CadBlockMover.Resolved? block=null;
            if(entity.GetType()==typeof(BlockReference)||entity.GetType()==typeof(MInsertBlock))block=CadBlockMover.Resolve((BlockReference)entity,db,tr,space,cancel);
            else CadBlockPlacementGraph.CheckLeaf(entity,db,tr,cancel);
            var source=new Source(id,space,Geometry(entity,tr,cancel),Wire.Element(CadBlockMover.PreservedProperties(entity)),Metadata(entity,tr,cancel),block);
            budget.Add(new{source.Geometry,source.Properties,source.Metadata});if(block?.Graph is {} graph)foreach(var captured in graph.States){cancel();budget.Add(captured.Value);}
            sources.Add(source);
        }
        return new(edit,space,sources.ToArray());
    }
    internal static void Preflight(StructureWriteArguments args,DocumentState state,Transaction tr,Action cancel)=>_=Resolve(args,state,tr,cancel);
    static void Retained(Selection selection,Transaction tr,Action cancel)
    {
        foreach(var source in selection.Sources)
        {
            cancel();Need(Live(source.Id),"A selected original is no longer live.","NATIVE_BLOCK_CLONE_MISMATCH");var entity=(Entity)tr.GetObject(source.Id,OpenMode.ForRead);
            Need(entity.OwnerId==source.Owner&&entity.ObjectId==source.Id,"A selected original changed native identity or owner.","NATIVE_BLOCK_CLONE_MISMATCH");
            Need(BlockMoveGeometry.SameProperties(source.Geometry,Geometry(entity,tr,cancel))&&BlockMoveGeometry.SameProperties(source.Properties,Wire.Element(CadBlockMover.PreservedProperties(entity)))&&
                BlockMoveGeometry.SameProperties(source.Metadata,Metadata(entity,tr,cancel)),"Native cloning changed a retained original.","NATIVE_BLOCK_CLONE_MISMATCH");
            source.Block?.Graph?.Verify(tr,cancel);
        }
    }
    internal static Result Execute(StructureWriteArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        Transaction? tr=null;BlockTableRecord? createdDefinition=null;bool started=false;string stage="source_read";
        try
        {
            var db=state.Document.Database;tr=db.TransactionManager.StartTransaction();var selection=Resolve(args,state,tr,job.CheckCancellation);var edit=selection.Edit;
            var table=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForWrite);
            createdDefinition=new(){Name=edit.Name,Origin=new(edit.BasePoint.X,edit.BasePoint.Y,0),Units=UnitsValue.Millimeters};
            job.CheckCancellation();started=true;stage="new_native_definition";log.Write(job.Operation,job.OriginalRequestId,"block_from_objects_started",operationId:job.OperationId);
            var definitionId=table.Add(createdDefinition);tr.AddNewlyCreatedDBObject(createdDefinition,true);
            stage="native_deep_clone";using var ids=new ObjectIdCollection(selection.Sources.Select(source=>source.Id).ToArray());using var mapping=new IdMapping();
            db.DeepCloneObjects(ids,definitionId,mapping,false);job.CheckCancellation();
            var cloned=new List<(Source Source,ObjectId Id)>();
            foreach(var source in selection.Sources)
            {
                job.CheckCancellation();Need(mapping.Contains(source.Id),"DeepClone omitted a requested source mapping.","NATIVE_BLOCK_CLONE_MISMATCH");var pair=mapping.Lookup(source.Id);
                Need(pair.IsCloned&&pair.Key==source.Id&&Live(pair.Value)&&pair.Value!=source.Id,"DeepClone did not create a distinct requested native root.","NATIVE_BLOCK_CLONE_MISMATCH");
                var original=(Entity)tr.GetObject(source.Id,OpenMode.ForRead);var actual=tr.GetObject(pair.Value,OpenMode.ForRead) as Entity;
                Need(actual!=null&&actual.GetType()==original.GetType()&&actual.OwnerId==definitionId,"A cloned root has a different type or destination definition owner.","NATIVE_BLOCK_CLONE_MISMATCH");
                Need(BlockMoveGeometry.SameProperties(source.Geometry,Geometry(actual,tr,job.CheckCancellation))&&BlockMoveGeometry.SameProperties(source.Properties,Wire.Element(CadBlockMover.PreservedProperties(actual))),
                    "Native cloned geometry/display/owned attribute values differ from the source.","NATIVE_BLOCK_CLONE_MISMATCH");
                if(original is BlockReference oldBlock&&actual is BlockReference newBlock)
                {
                    var oldAttributes=oldBlock.AttributeCollection.Cast<ObjectId>().ToArray();var newAttributes=newBlock.AttributeCollection.Cast<ObjectId>().ToArray();
                    Need(oldAttributes.Length==newAttributes.Length&&oldAttributes.Select(id=>mapping.Contains(id)?mapping.Lookup(id).Value:ObjectId.Null).SequenceEqual(newAttributes)&&
                        newAttributes.All(id=>((AttributeReference)tr.GetObject(id,OpenMode.ForRead)).OwnerId==newBlock.ObjectId),"Cloned native attributes have incomplete mapping or ownership.","NATIVE_BLOCK_CLONE_MISMATCH");
                }
                cloned.Add((source,pair.Value));
            }
            var cloneIds=cloned.Select(item=>item.Id).ToHashSet();
            foreach(IdPair pair in mapping)
            {
                job.CheckCancellation();if(!pair.IsCloned)continue;
                if(tr.GetObject(pair.Value,OpenMode.ForRead) is Entity entity&&entity.OwnerId==definitionId)
                    Need(cloneIds.Contains(pair.Value),"DeepClone produced an additional unrequested definition entity.","NATIVE_BLOCK_CLONE_MISMATCH");
            }
            Need(createdDefinition.Cast<ObjectId>().Where(Live).ToHashSet().SetEquals(cloneIds),"The actual new definition membership differs from the mapped requested roots.","NATIVE_BLOCK_CLONE_MISMATCH");
            stage="retained_source_readback";Retained(selection,tr,job.CheckCancellation);
            EntityRef Ref(ObjectId id)=>new(args.BridgeId,args.DocumentId,Id(id));
            var changes=new StructureChange[]{new(0,edit.Operation,Ref(definitionId),"block_definition",edit.Name,true,
                cloned.Select((item,index)=>new StructureChild(Ref(item.Id),((Entity)tr.GetObject(item.Id,OpenMode.ForRead)).GetType().Name,index,null)).ToArray())};
            var output=new ModifiedStructure(args.OperationId,"committed",false,changes,"not_guaranteed");SerializedCapacity357.Check(output,Wire.PayloadBudget-8192,job.CheckCancellation);
            var result=Result.Ok(job.OriginalRequestId,output,context,Warning,WriteRecovery.Warning);
            job.CheckCancellation();stage="commit";job.BeginCommit();tr.Commit();job.RecordCommit(result);
            stage="committed_cleanup";
            try{tr.Dispose();}
            catch(System.Exception cleanup)
            {result=result with{warnings=[Warning,WriteRecovery.Warning,"POST_COMMIT_CLEANUP_FAILED: The native block creation committed, but transaction cleanup reported "+cleanup.GetType().Name+". Refresh the named definition before dependent writes."]};job.RecordCommit(result);}
            tr=null;return result;
        }
        catch(System.Exception error)
        {
            if(job.CommittedResult is {} result)return result;System.Exception? cleanup=null;try{tr?.Dispose();}catch(System.Exception caught){cleanup=caught;}
            string cause=error is DomainException domain?domain.Error.code:error is OperationCanceledException?"CANCELLED":"BLOCK_FROM_OBJECTS_FAILED";
            return job.Error(started?"WRITE_OUTCOME_UNKNOWN":cause,started?"Native block creation started but the final outcome is unverified; inspect the named definition and selected originals before further dependent writes.":error.Message,
                started?"unknown":"not_started",context,new{cause,stage,message=error.Message,native_details=(error as DomainException)?.Error.details,cleanup_error=cleanup?.GetType().Name})with{warnings=[Warning,WriteRecovery.Warning]};
        }
        finally{createdDefinition?.Dispose();}
    }
}
