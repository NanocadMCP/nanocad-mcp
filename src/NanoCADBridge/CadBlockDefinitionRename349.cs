using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// A native shared symbol rename, with no member setter, reconstruction,
// definition replacement, occurrence reassignment or attribute synchronization.
internal static class CadBlockDefinitionRename349
{
    private sealed record State(ObjectId Target,JsonElement Value,string[] Warnings);
    private const string ZeroId="00000000-0000-0000-0000-000000000000";
    internal static bool Handles(StructureWriteArguments args)=>args.Operations is [RenameBlockDefinition349];
    private static void Need(bool value,string message,string code="BLOCK_DEFINITION_STATE_CHANGED")
    {if(!value)throw new DomainException(code,message);}
    private static bool Live(ObjectId id)=>!id.IsNull&&id.IsValid&&!id.IsErased&&!id.IsEffectivelyErased;
    private static string? Id(ObjectId id)=>id.IsNull?null:Validation.NormalizeHandle(id.Handle.ToString());
    private static bool Exact(JsonElement first,JsonElement second)=>first.GetRawText()==second.GetRawText();
    private static double N(double value)=>double.IsFinite(value)?value:throw new DomainException("INVALID_GEOMETRY","Native block geometry is nonfinite.");
    private static double[] P(Point3d point)=>new[]{N(point.X),N(point.Y),N(point.Z)};
    private static double[] V(Vector3d vector)=>new[]{N(vector.X),N(vector.Y),N(vector.Z)};
    private static double[] Matrix(Matrix3d matrix)
    {var result=new double[16];for(int row=0;row<4;row++)for(int column=0;column<4;column++)result[row*4+column]=N(matrix[row,column]);return result;}
    private static bool SameDatabase(DBObject value,Database db)=>db.UnmanagedObject!=IntPtr.Zero&&value.Database?.UnmanagedObject==db.UnmanagedObject;
    private static object[] Buffer(ResultBuffer data,Action cancel)
    {
        var values=new List<object>();var budget=new ModelInventoryBudget(cancel);
        foreach(var typed in data.AsArray())
        {
            cancel();object? value;
            try{value=NativeOwnedMetadata.Value(typed.Value);}
            catch(DomainException error) when(error.Error.code=="UNSUPPORTED_OWNED_METADATA")
            {value=new{native_type=typed.Value?.GetType().FullName,value_scope="opaque_typed_value_unobserved"};}
            var item=new{code=typed.TypeCode,value};budget.Add(item);values.Add(item);
        }
        return values.ToArray();
    }
    private static BlockTableRecord Definition(ObjectId id,Database db,Transaction tr,bool selected=true)
    {
        Need(Live(id),"Native block definition is unavailable.","OBJECT_NOT_FOUND");
        Need(tr.GetObject(id,OpenMode.ForRead) is BlockTableRecord,"Expected a block definition handle.","OUT_OF_SCOPE");
        var block=(BlockTableRecord)tr.GetObject(id,OpenMode.ForRead);
        Need(block.GetType()==typeof(BlockTableRecord)&&block.OwnerId==db.BlockTableId&&SameDatabase(block,db),"Native definition owner/database is inconsistent.","OUT_OF_SCOPE");
        if(selected)Need(!block.IsAnonymous&&!block.IsDynamicBlock&&!block.IsLayout&&!block.IsDependent&&!block.IsFromExternalReference&&!block.IsFromOverlayReference&&
            BlockWriteValidation.OrdinaryName(block.Name),"Select an ordinary local static shared block definition.","UNSUPPORTED_BLOCK_TYPE");
        var table=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead);
        Need(table.Has(block.Name)&&table[block.Name]==id,"Native block name index and definition identity disagree.");
        return block;
    }
    private static JsonElement Metadata(DBObject item,Database db,Transaction tr,Action cancel)
    {
        cancel();Need(Live(item.ObjectId)&&Live(item.OwnerId)&&SameDatabase(item,db),"Block member identity/database/owner is unavailable.");
        using var reactors=item.GetPersistentReactorIds();var links=new List<string?>();
        foreach(ObjectId id in reactors){cancel();links.Add(Id(id));}
        JsonElement owned;
        try{owned=NativeOwnedMetadata.Capture(item,tr,cancel);}
        catch(DomainException error) when(error.Error.code=="UNSUPPORTED_OWNED_METADATA"&&
            (error.Error.message.StartsWith("Field-bearing owned metadata",StringComparison.Ordinal)||
             error.Error.message.StartsWith("Native owned data needs",StringComparison.Ordinal)||
             error.Error.message.StartsWith("A native metadata value needs",StringComparison.Ordinal)))
        {owned=OpaqueOwned(item,db,tr,cancel);}
        using var data=item.XData;
        var result=Wire.Element(new{handle=Id(item.ObjectId),owner=Id(item.OwnerId),native_type=item.GetType().FullName,
            item.HasFields,extension=Id(item.ExtensionDictionary),reactors=links.OrderBy(value=>value,StringComparer.OrdinalIgnoreCase).ToArray(),
            xdata=data==null?null:Buffer(data,cancel),owned});
        new ModelInventoryBudget(cancel).Add(result);return result;
    }
    private static JsonElement OpaqueOwned(DBObject root,Database db,Transaction tr,Action cancel)
    {
        var values=new List<object>();var seen=new HashSet<ObjectId>();var pending=new Stack<(ObjectId Id,ObjectId Owner)>();
        var budget=new ModelInventoryBudget(cancel);if(!root.ExtensionDictionary.IsNull)pending.Push((root.ExtensionDictionary,root.ObjectId));
        while(pending.Count>0)
        {
            cancel();var next=pending.Pop();Need(Live(next.Id)&&seen.Add(next.Id),"Owned metadata identity is unavailable/repeated.");
            var item=tr.GetObject(next.Id,OpenMode.ForRead);Need(item.OwnerId==next.Owner&&SameDatabase(item,db),"Owned metadata has inconsistent actual owner/database.");
            using var reactors=item.GetPersistentReactorIds();using var data=item.XData;var links=new List<string?>();
            foreach(ObjectId id in reactors){cancel();links.Add(Id(id));}
            var entries=new List<object>();var entryBudget=new ModelInventoryBudget(cancel);
            if(item is DBDictionary dictionary)
                foreach(DBDictionaryEntry entry in dictionary)
                {
                    cancel();Need(Live(entry.Value),"Owned dictionary entry is unavailable.");var child=tr.GetObject(entry.Value,OpenMode.ForRead);
                    bool owns=child.OwnerId==item.ObjectId&&SameDatabase(child,db);
                    var entryValue=new{name=entry.Key,handle=Id(entry.Value),owner=Id(child.OwnerId),type=child.GetType().FullName,owned=owns};
                    entryBudget.Add(entryValue);entries.Add(entryValue);
                    if(owns)pending.Push((entry.Value,item.ObjectId));
                }
            object? stored=null;
            if(item.GetType()==typeof(Xrecord)&&item is Xrecord record)
            {using var buffer=record.Data;stored=new{record.XlateReferences,merge_style=record.MergeStyle.ToString(),data=buffer==null?null:Buffer(buffer,cancel)};}
            var value=new{handle=Id(item.ObjectId),owner=Id(item.OwnerId),type=item.GetType().FullName,item.HasFields,
                extension=Id(item.ExtensionDictionary),reactors=links.OrderBy(link=>link,StringComparer.OrdinalIgnoreCase).ToArray(),
                xdata=data==null?null:Buffer(data,cancel),entries,stored,
                inner_state_scope=stored==null&&item is not DBDictionary?"opaque_native_inner_state_unobserved":"readable_stored_metadata"};
            budget.Add(value);values.Add(value);if(!item.ExtensionDictionary.IsNull)pending.Push((item.ExtensionDictionary,item.ObjectId));
        }
        return Wire.Element(new{observation_scope="generic_owned_identity_and_readable_data_opaque_native_internals_unobserved",records=values});
    }
    private static ObjectId[] Members(IEnumerable<ObjectId> input,Action cancel)
    {
        var values=new List<ObjectId>();var seen=new HashSet<ObjectId>();var budget=new ModelInventoryBudget(cancel);
        foreach(var id in input)
        {cancel();if(id.IsErased||id.IsEffectivelyErased)continue;Need(Live(id)&&seen.Add(id),"Native block member identity is invalid/repeated.");budget.Add(Id(id));values.Add(id);}
        return values.ToArray();
    }
    private static bool ContainsMember(BlockTableRecord owner,ObjectId selected,Action cancel)
    {
        foreach(ObjectId id in owner){cancel();if(id==selected)return true;}
        return false;
    }
    private static JsonElement Attribute(AttributeReference attribute,Database db,Transaction tr,Action cancel)
    {
        return Entity(attribute,db,tr,cancel);
    }
    private static JsonElement Reference(BlockReference reference,Database db,Transaction tr,Action cancel)
    {
        cancel();_=Definition(reference.BlockTableRecord,db,tr,false);
        Need(Live(reference.OwnerId)&&tr.GetObject(reference.OwnerId,OpenMode.ForRead) is BlockTableRecord,"Insertion owner is not a native block record.");
        var owner=(BlockTableRecord)tr.GetObject(reference.OwnerId,OpenMode.ForRead);
        if(owner.IsLayout)_=CadBlockSpace.RequireSpace(db,tr,owner.ObjectId);else _=Definition(owner.ObjectId,db,tr,false);
        Need(ContainsMember(owner,reference.ObjectId,cancel),"Insertion is absent from its actual native owner membership.");
        var attributeIds=Members(reference.AttributeCollection.Cast<ObjectId>(),cancel);var attributes=new List<JsonElement>();var budget=new ModelInventoryBudget(cancel);
        foreach(var id in attributeIds)
        {
            cancel();Need(tr.GetObject(id,OpenMode.ForRead) is AttributeReference,"Insertion owns a custom/non-attribute member.","UNSUPPORTED_BLOCK_CONTENT");
            var attribute=(AttributeReference)tr.GetObject(id,OpenMode.ForRead);Need(attribute.OwnerId==reference.ObjectId,"Occurrence attribute has a foreign owner.");
            var captured=Attribute(attribute,db,tr,cancel);budget.Add(captured);attributes.Add(captured);
        }
        var scale=reference.ScaleFactors;
        var result=Wire.Element(new{metadata=Metadata(reference,db,tr,cancel),definition=Id(reference.BlockTableRecord),
            position=P(reference.Position),normal=V(reference.Normal),rotation=N(reference.Rotation),scale=new[]{N(scale.X),N(scale.Y),N(scale.Z)},
            transform=Matrix(reference.BlockTransform),properties=EntityAdapter.StoredProperties(reference),layer=Id(reference.LayerId),
            reference.IsDynamicBlock,annotative=reference.Annotative.ToString(),
            attributes});new ModelInventoryBudget(cancel).Add(result);return result;
    }
    private static JsonElement Entity(Entity entity,Database db,Transaction tr,Action cancel)
    {
        cancel();
        var summary=EntityAdapter.Summary(entity,ObjectId.Null,ZeroId,ZeroId);
        JsonElement? detail=null;string? unobserved=null;
        // A symbol rename is independent of child content-edit support. Fields
        // can legitimately evaluate/reflow after their shared name changes;
        // their derived glyph/layout geometry is not a stored-content oracle.
        if(entity.HasFields)unobserved="field_derived_content_and_layout_unobserved";
        else try{detail=Wire.Element(EntityAdapter.Detail(entity,summary,CancellationToken.None));cancel();}
        catch(DomainException error){unobserved=error.Error.code;}
        catch(Teigha.Runtime.Exception){unobserved="native_typed_geometry_getter_unavailable";}
        catch(NotSupportedException){unobserved="native_typed_geometry_getter_unavailable";}
        var result=Wire.Element(new{metadata=Metadata(entity,db,tr,cancel),detail,geometry_unobserved_reason=unobserved,
            properties=EntityAdapter.StoredProperties(entity),layer=Id(entity.LayerId),annotative=entity.Annotative.ToString(),
            definition=entity is AttributeDefinition a?new{a.Tag,a.Prompt,a.Preset,a.Verifiable,a.LockPositionInBlock,a.Invisible,a.Constant,a.IsMTextAttributeDefinition}:null,
            occurrence=entity is AttributeReference b?new{b.Tag,b.LockPositionInBlock,b.Invisible,b.IsConstant,b.IsMirroredInX,b.IsMirroredInY,b.IsMTextAttribute}:null});
        new ModelInventoryBudget(cancel).Add(result);return result;
    }
    private static State Capture(ObjectId target,Database db,Transaction tr,Action cancel)
    {
        var root=Definition(target,db,tr);var table=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead);
        var budget=new ModelInventoryBudget(cancel);var inventory=new List<(string Handle,string Name)>();
        foreach(ObjectId id in table)
        {cancel();if(id.IsErased||id.IsEffectivelyErased)continue;Need(Live(id),"Native block table contains an invalid identity.");var block=(BlockTableRecord)tr.GetObject(id,OpenMode.ForRead);
            var row=(Handle:Id(id)!,Name:block.Name);budget.Add(new{handle=row.Handle,name=row.Name});inventory.Add(row);}
        var graph=new List<object>();var seen=new HashSet<ObjectId>();var pending=new Stack<ObjectId>();pending.Push(target);
        while(pending.Count>0)
        {
            cancel();var id=pending.Pop();if(!seen.Add(id))continue;var block=Definition(id,db,tr,id==target);
            var members=Members(block.Cast<ObjectId>(),cancel);var contents=new List<JsonElement>();var contentsBudget=new ModelInventoryBudget(cancel);
            foreach(var memberId in members)
            {
                cancel();Need(tr.GetObject(memberId,OpenMode.ForRead) is Entity,"Block has a non-entity member.","UNSUPPORTED_BLOCK_CONTENT");
                var entity=(Entity)tr.GetObject(memberId,OpenMode.ForRead);Need(entity.OwnerId==block.ObjectId,"Block member has a foreign actual owner.");
                JsonElement captured;
                if(entity is BlockReference reference){captured=Reference(reference,db,tr,cancel);pending.Push(reference.BlockTableRecord);}
                else captured=Entity(entity,db,tr,cancel);
                contentsBudget.Add(captured);contents.Add(captured);
            }
            var node=new{handle=Id(id),name=block.Name,metadata=Metadata(block,db,tr,cancel),origin=P(block.Origin),units=(int)block.Units,
                scaling=(int)block.BlockScaling,block.Explodable,block.IsAnonymous,block.IsDynamicBlock,block.IsLayout,block.IsDependent,
                block.IsFromExternalReference,block.IsFromOverlayReference,members=members.Select(Id).ToArray(),contents};
            budget.Add(node);graph.Add(node);
        }
        // Scan actual resident membership rather than accepting a possibly
        // incomplete cached block-reference index. No model/occurrence count cap.
        var references=new List<JsonElement>();
        foreach(ObjectId ownerId in table)
        {
            cancel();if(ownerId.IsErased||ownerId.IsEffectivelyErased)continue;var owner=(BlockTableRecord)tr.GetObject(ownerId,OpenMode.ForRead);
            foreach(ObjectId memberId in owner)
            {
                cancel();if(memberId.IsErased||memberId.IsEffectivelyErased)continue;
                if(tr.GetObject(memberId,OpenMode.ForRead) is not BlockReference reference||reference.BlockTableRecord!=target)continue;
                var captured=Reference(reference,db,tr,cancel);budget.Add(captured);references.Add(captured);
            }
        }
        var result=Wire.Element(new{target=Id(root.ObjectId),
            inventory=inventory.OrderBy(row=>row.Handle,StringComparer.OrdinalIgnoreCase).Select(row=>new{handle=row.Handle,name=row.Name}).ToArray(),
            graph=graph.Select(value=>Wire.Element(value)).OrderBy(value=>value.GetProperty("handle").GetString(),StringComparer.OrdinalIgnoreCase).ToArray(),
            references=references.OrderBy(value=>value.GetProperty("metadata").GetProperty("handle").GetString(),StringComparer.OrdinalIgnoreCase).ToArray()});
        new ModelInventoryBudget(cancel).Add(result);
        return new(target,result,new[]{"BLOCK_RENAME_OBSERVATION_SCOPE: Native definition/member/reference identities, owners, observed appearance and readable metadata/typed geometry are retained. Field-derived layout and opaque native content/owned internals are not exhaustively observed; their content is not edited by this shared Name setter. Application data and external text containing the old name are not rewritten. Inspect the relevant shared block graphics."});
    }
    private static State Resolve(StructureWriteArguments args,DocumentState state,Transaction tr,Action cancel)
    {
        Need(Handles(args),"Rename one block definition in a separate structural request.","INVALID_ARGUMENT");
        var edit=(RenameBlockDefinition349)args.Operations[0];var db=state.Document.Database;
        var id=CadStructureWriter.Find(db,edit.Handle);var block=Definition(id,db,tr);var table=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead);
        Need(block.Name==edit.ExpectedName,"Stored block name differs from expected_name; refresh get_structure blocks.","BLOCK_NAME_MISMATCH");
        Need(!table.Has(edit.Name),"Destination block name already exists; rename does not overwrite.","BLOCK_NAME_EXISTS");
        return Capture(id,db,tr,cancel);
    }
    internal static void Preflight(StructureWriteArguments args,DocumentState state,Transaction tr,Action cancel)=>_=Resolve(args,state,tr,cancel);
    private static JsonElement Expected(State before,RenameBlockDefinition349 edit)
    {
        var expected=JsonNode.Parse(before.Value.GetRawText())!.AsObject();
        foreach(var item in expected["inventory"]!.AsArray())if(item!["handle"]!.GetValue<string>()==edit.Handle)item["name"]=edit.Name;
        foreach(var item in expected["graph"]!.AsArray())if(item!["handle"]!.GetValue<string>()==edit.Handle)item["name"]=edit.Name;
        return Wire.Element(expected);
    }
    internal static Result Execute(StructureWriteArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        Transaction? transaction=null;bool started=false,commitAttempted=false;
        try
        {
            var db=state.Document.Database;var edit=(RenameBlockDefinition349)args.Operations[0];
            transaction=db.TransactionManager.StartTransaction();var before=Resolve(args,state,transaction,job.CheckCancellation);var expected=Expected(before,edit);
            var change=new StructureChange(0,edit.Operation,new(args.BridgeId,args.DocumentId,edit.Handle),"block_definition",edit.Name,false);
            var data=new ModifiedStructure(args.OperationId,"committed",false,new[]{change},"not_guaranteed");
            Need(JsonSerializer.SerializeToUtf8Bytes(data,Wire.Json).Length<=Wire.PayloadBudget-8192,"Projected rename receipt exceeds the actual response budget.","RESPONSE_LIMIT_EXCEEDED");
            job.CheckCancellation();started=true;log.Write(job.Operation,job.OriginalRequestId,"block_definition_rename_started",operationId:job.OperationId);
            var block=(BlockTableRecord)transaction.GetObject(before.Target,OpenMode.ForWrite);block.Name=edit.Name;job.CheckCancellation();
            var table=(BlockTable)transaction.GetObject(db.BlockTableId,OpenMode.ForRead);
            Need(block.ObjectId==before.Target&&block.OwnerId==db.BlockTableId&&block.Name==edit.Name&&table.Has(edit.Name)&&table[edit.Name]==before.Target&&!table.Has(edit.ExpectedName),
                "Native rename failed its retained identity or name-index proof.","NATIVE_BLOCK_RENAME_MISMATCH");
            Need(Exact(expected,Capture(before.Target,db,transaction,job.CheckCancellation).Value),"Native rename changed retained definition/geometry/occurrence/attribute metadata.","NATIVE_BLOCK_RENAME_MISMATCH");
            job.CheckCancellation();job.BeginCommit();commitAttempted=true;transaction.Commit();transaction.Dispose();transaction=null;
            using(var readback=db.TransactionManager.StartTransaction())
                Need(Exact(expected,Capture(before.Target,db,readback,job.CheckCancellation).Value),"Postcommit rename did not retain its full observed shared state.","NATIVE_BLOCK_RENAME_MISMATCH");
            var result=Result.Ok(job.OriginalRequestId,data,context,before.Warnings.Append(WriteRecovery.Warning).ToArray());job.RecordCommit(result);return result;
        }
        catch(System.Exception error)
        {
            if(job.CommittedResult is {} committed)return committed;
            System.Exception? cleanup=null;try{transaction?.Dispose();}catch(System.Exception caught){cleanup=caught;}
            string cause=error is DomainException domain?domain.Error.code:error is OperationCanceledException?"CANCELLED":"BLOCK_RENAME_FAILED";
            return job.Error(started?"WRITE_OUTCOME_UNKNOWN":cause,started?"Native block rename started but complete outcome was not verified. Inspect its name, definition and existing insertions before further writes.":"Block rename failed before changes started.",
                started?"unknown":"not_started",context,new{cause,commit_attempted=commitAttempted,error_type=error.GetType().Name,cleanup_error=cleanup?.GetType().Name});
        }
    }
}
