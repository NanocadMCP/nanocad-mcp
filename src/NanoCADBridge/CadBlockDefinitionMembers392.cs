using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// Composition changes use real existing BTR ownership. Existing shared graph
// observation supplies the token and retained sources, not a second private
// payload oracle. Retired nested occurrences do not retire their definitions.
internal static class CadBlockDefinitionMembers392
{
    private const string Warning="SHARED_BLOCK_MEMBERSHIP: Native definition members are appended/retired in-place in every actual Model/Paper/nested consumer. Definition identity, retained member IDs, consumer placement and retained occurrence attributes stay unchanged. Retiring a nested occurrence retires its owned attributes, retaining its shared child definition. Native graphics are marked modified. Unselected opaque payloads/private evaluated graphics are not exhaustively observed. Refresh inspect_block and inspect the useful shared symbol/titleblock view; Undo/rollback is not guaranteed.";
    internal static bool Handles(StructureWriteArguments args)=>args.Operations is [SetBlockDefinitionMembers392];
    private static void Need([DoesNotReturnIf(false)] bool value,string message,string code="UNSUPPORTED_BLOCK_MEMBERSHIP")
    {if(!value)throw new DomainException(code,message);}
    private static string Id(ObjectId id)=>Validation.NormalizeHandle(id.Handle.ToString());
    private static string Reason(System.Exception e)
    {string text=(e is DomainException d?d.Error.code:e.GetType().Name)+": "+e.Message;return text.Length<=1024?text:text[..1024];}
    private static void Independent(DBObject value)
    {using var reactors=value.GetPersistentReactorIds();Need(!value.HasFields&&reactors.Count==0,"The selected object has Fields/persistent associations requiring its owning lifecycle.");}
    private static void DefinitionWrite(BlockTableRecord value)
    {Independent(value);Need(value.Annotative!=AnnotativeStates.True,"An annotative definition needs its context lifecycle.");}
    internal static void RequireRetirement(Entity entity,Database db,Transaction tr,Action cancel)
    {
        cancel();var type=entity.GetType();
        Need(type==typeof(Line)||type==typeof(Circle)||type==typeof(Arc)||type==typeof(Ellipse)||type==typeof(Spline)||type==typeof(Polyline)||
            type==typeof(DBText)||type==typeof(MText)||type==typeof(Hatch)||type==typeof(BlockReference),
            "Retire direct ordinary curve/Text/MText/independent Hatch/static reference members; AttributeDefinition, array/custom and linked families need their owning lifecycle.","UNSUPPORTED_BLOCK_MEMBER");
        Need(entity.Annotative!=AnnotativeStates.True,"An annotative member needs its context lifecycle.");Independent(entity);
        CadBlockDefinitionGeometry388.Writable(entity,db,tr);
        if(entity is Hatch hatch)CadBlockTemplate391.RequireHatch(hatch,cancel);
        CadIndependentCurve.Require(entity,db,tr,cancel);
        if(entity is BlockReference reference)
        {
            Need(!reference.IsDynamicBlock,"A dynamic member needs its owning lifecycle.");
            var seen=new HashSet<ObjectId>();
            foreach(ObjectId id in reference.AttributeCollection)
            {
                cancel();Need(!id.IsNull&&id.IsValid&&!id.IsErased&&!id.IsEffectivelyErased&&seen.Add(id)&&tr.GetObject(id,OpenMode.ForRead) is AttributeReference,
                    "A retired nested occurrence has unavailable/repeated owned attributes.");
                var attribute=(AttributeReference)tr.GetObject(id,OpenMode.ForRead);
                Need(attribute.GetType()==typeof(AttributeReference)&&attribute.ObjectId==id&&attribute.OwnerId==reference.ObjectId&&attribute.Database?.UnmanagedObject==db.UnmanagedObject,
                    "A retired nested attribute has a foreign native type/owner/database.");
                Independent(attribute);CadBlockDefinitionGeometry388.Writable(attribute,db,tr);
                CadIndependentCurve.Require(attribute,db,tr,cancel);
            }
        }
    }
    private sealed record Plan(CadBlockDefinitionGeometry388.State Before,ObjectId[] Removed,ObjectId[] RetainedDefinitions,
        DefineBlockEdit? Addition,CadBlockWriter.Resolved? Source);
    private static ObjectId[] Definitions(CadBlockDefinitionGeometry388.State state,Database db)=>state.Value.GetProperty("definitions").EnumerateArray()
        .Select(row=>CadStructureWriter.Find(db,row.GetProperty("header").GetProperty("root").GetProperty("handle").GetString()!)).ToArray();
    private static Plan Resolve(StructureWriteArguments args,DocumentState state,Transaction tr,Action cancel,Action<string>? warning=null)
    {
        Need(Handles(args),"Edit one shared definition's composition in a separate request.","INVALID_ARGUMENT");
        var edit=(SetBlockDefinitionMembers392)args.Operations[0];var db=state.Document.Database;var target=CadStructureWriter.Find(db,edit.DefinitionHandle);
        var block=CadBlockDefinitionGeometry388.Definition(target,db,tr,edit.AddEntities.Length!=0);DefinitionWrite(block);
        var removed=edit.RemoveHandles.Select(handle=>CadStructureWriter.Find(db,handle)).ToArray();
        var before=CadBlockDefinitionGeometry388.Capture(target,removed.ToHashSet(),db,tr,cancel,true,membershipSelected:true,requireMembershipUnits:edit.AddEntities.Length!=0);
        Need(CadBlockDefinitionGeometry388.Token(before)==edit.ExpectedDefinitionToken,"Definition composition, sources or actual consumers changed; refresh inspect_block.definition_membership.","STALE_BLOCK_DEFINITION_TOKEN");
        DefineBlockEdit? addition=null;CadBlockWriter.Resolved? source=null;
        if(edit.AddEntities.Length!=0)
        {
            addition=new("MCP_MEMBER_TEMPLATE",new(0,0),edit.AddEntities,[]);
            LayerWriteState Layer(ObjectId id)
            {var layer=(LayerTableRecord)tr.GetObject(id,OpenMode.ForRead);return new(layer.Name,id==db.Clayer,layer.IsDependent,layer.IsOff,layer.IsFrozen,layer.IsLocked);}
            var table=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead);
            // The selected existing definition is the eventual owner. The
            // detached template only reuses native member construction; its
            // name/basepoint are never installed or assigned to the target.
            source=CadBlockWriter.Resolve(addition,db,tr,table[BlockTableRecord.ModelSpace],Layer,cancel,warning,memberAddition:true);
            foreach(var child in source.Nested.Values)
                Need(child.Definition!=target&&!(child.Graph?.Definitions.Contains(target)??false),"The added child definition already contains the target; this would introduce a nesting cycle.","CYCLIC_BLOCK_DEFINITION");
        }
        return new(before,removed,Definitions(before,db),addition,source);
    }
    internal static void Preflight(StructureWriteArguments args,DocumentState state,Transaction tr,Action cancel)=>_=Resolve(args,state,tr,cancel);
    internal static object TryRead(ObjectId target,string bridge,string document,Database db,Transaction tr,Action cancel)
    {
        EntityRef Ref(ObjectId id)=>new(bridge,document,Id(id));
        string name="";string? token=null,unavailable=null,addUnavailable=null;var rows=new List<object>();var references=Array.Empty<EntityRef>();var budget=new ModelInventoryBudget(cancel);
        try
        {
            if(!target.IsNull&&target.IsValid&&tr.GetObject(target,OpenMode.ForRead) is BlockTableRecord actual)name=actual.Name;
            var block=CadBlockDefinitionGeometry388.Definition(target,db,tr,false);name=block.Name;DefinitionWrite(block);
            foreach(var id in CadBlockDefinitionGeometry388.Members(block,db,tr,cancel))
            {
                cancel();var entity=(Entity)tr.GetObject(id,OpenMode.ForRead);string? reason=null;
                try{RequireRetirement(entity,db,tr,cancel);}catch(OperationCanceledException){throw;}catch(System.Exception error){reason=Reason(error);}
                var row=new{@ref=Ref(id),type=entity.GetType()==typeof(DBText)?"Text":entity.GetType().Name,can_remove=reason==null,unavailable_reason=reason};budget.Add(row);rows.Add(row);
            }
            // The token includes all actual members/consumers and does not
            // depend on which subset the following request chooses to retire.
            var capture=CadBlockDefinitionGeometry388.Capture(target,[],db,tr,cancel,true,membershipSelected:true,requireMembershipUnits:false);
            token=CadBlockDefinitionGeometry388.Token(capture);references=capture.References.Select(Ref).ToArray();
            try
            {
                _=CadBlockDefinitionGeometry388.Definition(target,db,tr,true);
                var layers=(LayerTable)tr.GetObject(db.LayerTableId,OpenMode.ForRead);var zero=(LayerTableRecord)tr.GetObject(layers["0"],OpenMode.ForRead);
                new LayerWriteState(zero.Name,zero.ObjectId==db.Clayer,zero.IsDependent,zero.IsOff,zero.IsFrozen,zero.IsLocked).RequireAttributeWrite();
            }
            catch(OperationCanceledException){throw;}catch(System.Exception error){addUnavailable=Reason(error);}
        }
        catch(OperationCanceledException){throw;}catch(System.Exception error){unavailable=Reason(error);addUnavailable=unavailable;}
        var result=new{definition_ref=Ref(target),definition_name=name,coordinate_system="parent_definition",definition_token=token,can_edit=unavailable==null,
            unavailable_reason=unavailable,can_add=unavailable==null&&addUnavailable==null,add_unavailable_reason=addUnavailable,members=rows.ToArray(),referencing_insertions=references};
        SerializedCapacity357.Check(result,Wire.PayloadBudget-8192,cancel);return result;
    }
    private static JsonElement Retained(CadBlockDefinitionGeometry388.State state,HashSet<string> changed,HashSet<string> originalDefinitions)
    {
        var value=JsonNode.Parse(state.Value.GetRawText())!.AsObject();var definitions=value["definitions"]!.AsArray();
        for(int i=definitions.Count-1;i>=0;i--)
        {
            var row=definitions[i]!.AsObject();string handle=row["header"]!["root"]!["handle"]!.GetValue<string>();
            if(!originalDefinitions.Contains(handle)){definitions.RemoveAt(i);continue;}
            if(handle==Id(state.Definition))
            {var members=row["members"]!.AsArray();for(int j=members.Count-1;j>=0;j--)if(changed.Contains(members[j]!.GetValue<string>()))members.RemoveAt(j);}
        }
        var contents=value["contents"]!.AsArray();
        for(int i=contents.Count-1;i>=0;i--)if(changed.Contains(contents[i]!["header"]!["root"]!["handle"]!.GetValue<string>()))contents.RemoveAt(i);
        return Wire.Element(value);
    }
    private static void Same(JsonElement before,JsonElement after,string code="NATIVE_BLOCK_MEMBERSHIP_MISMATCH")
    {Need(before.GetRawText()==after.GetRawText(),"Composition editing changed retained native definitions, members, owners, consumers or occurrence attributes.",code);}
    private sealed record Retired(ObjectId Id,ObjectId Owner,ObjectId? ChildDefinition,ObjectId[] Attributes);
    private static void Erased(Retired item,Database db,Transaction tr,Action cancel)
    {
        cancel();Need(item.Id.IsErased||item.Id.IsEffectivelyErased,"A selected member remains live after native retirement.","NATIVE_BLOCK_MEMBERSHIP_MISMATCH");
        var root=tr.GetObject(item.Id,OpenMode.ForRead,true);
        Need(root.ObjectId==item.Id&&root.OwnerId==item.Owner&&root.Database?.UnmanagedObject==db.UnmanagedObject,"Native retirement changed the selected identity/owner/database.","NATIVE_BLOCK_MEMBERSHIP_MISMATCH");
        if(item.ChildDefinition is {} child)Need(!child.IsErased&&!child.IsEffectivelyErased,"Native retirement affected a shared child definition.","NATIVE_BLOCK_MEMBERSHIP_MISMATCH");
        foreach(var id in item.Attributes)
        {
            cancel();Need(id.IsErased||id.IsEffectivelyErased,"Native retirement retained a live owned nested attribute.","NATIVE_BLOCK_MEMBERSHIP_MISMATCH");
            Need(tr.GetObject(id,OpenMode.ForRead,true) is AttributeReference attribute&&attribute.OwnerId==item.Id,"A retired nested attribute changed its native owner.","NATIVE_BLOCK_MEMBERSHIP_MISMATCH");
        }
    }
    private static void VerifyAdded(Plan plan,ObjectId[] added,JsonElement?[] geometry,Dictionary<int,ObjectId[]> nestedAttributes,Transaction tr,Action cancel)
    {
        for(int i=0;i<added.Length;i++)
        {
            cancel();var entity=(Entity)tr.GetObject(added[i],OpenMode.ForRead);
            Need(entity.ObjectId==added[i]&&entity.OwnerId==plan.Before.Definition&&!added[i].IsErased&&!added[i].IsEffectivelyErased,"An appended member has a different native identity/owner.","NATIVE_BLOCK_MEMBERSHIP_MISMATCH");
            CadBlockTemplate391.VerifyMember(entity,plan.Addition!.Entities[i],plan.Source!.EntityStyles[i],cancel);
            if(geometry[i] is {} expected)Same(expected,Wire.Element(CadBlockDefinitionGeometry388.StoredGeometry(entity,cancel)));
            if(nestedAttributes.TryGetValue(i,out var ownedIds))
            {
                var reference=(BlockReference)entity;
                Need(reference.AttributeCollection.Cast<ObjectId>().SequenceEqual(ownedIds),"A new nested occurrence changed its owned attribute membership.","NATIVE_BLOCK_MEMBERSHIP_MISMATCH");
                var attributes=reference.AttributeCollection.Cast<ObjectId>().Select(id=>(AttributeReference)tr.GetObject(id,OpenMode.ForRead)).ToArray();
                CadBlockWriter.VerifyInsertion(plan.Source.Nested[i],new(null,[],reference,attributes),tr,cancel);
            }
        }
    }
    internal static Result Execute(StructureWriteArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        Transaction? transaction=null;bool started=false,commitAttempted=false;string stage="resolve";var warnings=new List<string>{Warning,WriteRecovery.Warning};var owned=new List<DBObject>();
        try
        {
            var db=state.Document.Database;var edit=(SetBlockDefinitionMembers392)args.Operations[0];transaction=db.TransactionManager.StartTransaction();
            var plan=Resolve(args,state,transaction,job.CheckCancellation,warnings.Add);var target=plan.Before.Definition;
            var original=CadBlockDefinitionGeometry388.Members((BlockTableRecord)transaction.GetObject(target,OpenMode.ForRead),db,transaction,job.CheckCancellation);
            var definitions=plan.RetainedDefinitions.Select(Id).ToHashSet(StringComparer.OrdinalIgnoreCase);var changed=plan.Removed.Select(Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var invariant=Retained(plan.Before,changed,definitions);CadBlockWriter.Prepared? prepared=null;
            if(plan.Addition!=null)prepared=CadBlockWriter.Preview(plan.Addition,plan.Source!,transaction,owned,job.CheckCancellation);
            stage="preview_source_readback";
            Same(plan.Before.Value,CadBlockDefinitionGeometry388.Capture(target,plan.Removed.ToHashSet(),db,transaction,job.CheckCancellation,true,membershipSelected:true,requireMembershipUnits:edit.AddEntities.Length!=0).Value,"BLOCK_DEFINITION_CHANGED");
            // Existing native creators verify requested MText/Hatch/nested,
            // Ellipse and Spline parameters. Do not add a second fit/layout
            // metadata oracle to those adapters. Ordinary primitive stored
            // coordinates/content supplement their creation checks.
            var geometry=prepared?.Members.Select(entity=>entity is Hatch or MText or BlockReference or Ellipse or Spline?(JsonElement?)null:Wire.Element(CadBlockDefinitionGeometry388.StoredGeometry(entity,job.CheckCancellation))).ToArray()??[];
            var children=new List<StructureChild>();var retired=new List<Retired>();var added=new List<ObjectId>();var nestedAttributes=new Dictionary<int,ObjectId[]>();
            EntityRef Ref(ObjectId id)=>new(args.BridgeId,args.DocumentId,Id(id));
            // Charge the projected receipt before any resident mutation.
            EntityRef Dummy()=>new(args.BridgeId,args.DocumentId,"FFFFFFFFFFFFFFFF");
            var projected=edit.AddEntities.Select((_,i)=>new StructureChild(Dummy(),"added_member",i,null))
                .Concat(plan.Removed.Select((_,i)=>new StructureChild(Dummy(),"retired_member",i,null)))
                .Concat(plan.Before.References.Select(id=>new StructureChild(Ref(id),"block_reference",0,null))).ToList();
            if(prepared!=null)foreach(var pair in prepared.Nested)projected.AddRange(pair.Value.Attributes.Select(a=>new StructureChild(Dummy(),"added_nested_attribute",pair.Key,a.Tag)));
            foreach(var id in plan.Removed)if(transaction.GetObject(id,OpenMode.ForRead) is BlockReference retiredReference)projected.AddRange(retiredReference.AttributeCollection.Cast<ObjectId>().Select(a=>new StructureChild(Ref(a),"retired_nested_attribute",Array.IndexOf(plan.Removed,id),((AttributeReference)transaction.GetObject(a,OpenMode.ForRead)).Tag)));
            SerializedCapacity357.Check(new ModifiedStructure(args.OperationId,"committed",false,[new(0,edit.Operation,Ref(target),"block_definition",plan.Before.Name,false,projected.ToArray())],"not_guaranteed"),Wire.PayloadBudget-8192,job.CheckCancellation);
            job.CheckCancellation();stage="native_membership";started=true;log.Write(job.Operation,job.OriginalRequestId,"block_definition_membership_started",operationId:job.OperationId);
            var block=(BlockTableRecord)transaction.GetObject(target,OpenMode.ForWrite);
            // Erase only explicitly selected native members. Retained source
            // IDs are never transferred, replaced or rebuilt.
            for(int i=0;i<plan.Removed.Length;i++)
            {
                job.CheckCancellation();var entity=(Entity)transaction.GetObject(plan.Removed[i],OpenMode.ForWrite);RequireRetirement(entity,db,transaction,job.CheckCancellation);
                var attributes=entity is BlockReference retiredAttributeOwner?retiredAttributeOwner.AttributeCollection.Cast<ObjectId>().ToArray():[];
                var item=new Retired(entity.ObjectId,entity.OwnerId,entity is BlockReference nested?nested.BlockTableRecord:null,attributes);retired.Add(item);
                foreach(var id in attributes)children.Add(new(Ref(id),"retired_nested_attribute",i,((AttributeReference)transaction.GetObject(id,OpenMode.ForRead)).Tag));
                entity.Erase(true);Erased(item,db,transaction,job.CheckCancellation);children.Add(new(Ref(item.Id),"retired_member",i,null));
            }
            if(prepared!=null)for(int i=0;i<prepared.Members.Length;i++)
            {
                job.CheckCancellation();var entity=prepared.Members[i];var id=block.AppendEntity(entity);transaction.AddNewlyCreatedDBObject(entity,true);added.Add(id);changed.Add(Id(id));
                if(edit.AddEntities[i] is HatchSpec)AnnotationCreator.Complete(entity,edit.AddEntities[i]);
                if(prepared.Nested.TryGetValue(i,out var nested))
                {
                    var appendedReference=(BlockReference)entity;var attributes=new List<ObjectId>();
                    foreach(var attribute in nested.Attributes)
                    {
                        job.CheckCancellation();var attributeId=appendedReference.AttributeCollection.AppendAttribute(attribute);transaction.AddNewlyCreatedDBObject(attribute,true);
                        Need(attribute.ObjectId==attributeId&&attribute.OwnerId==id,"A new nested attribute has a different native identity/owner.","NATIVE_BLOCK_MEMBERSHIP_MISMATCH");
                        attributes.Add(attributeId);children.Add(new(Ref(attributeId),"added_nested_attribute",i,attribute.Tag));
                    }
                    nestedAttributes.Add(i,attributes.ToArray());
                }
                children.Add(new(Ref(id),"added_member",i,null));
            }
            stage="native_graphics_notification";
            foreach(var id in plan.Before.References){job.CheckCancellation();((Entity)transaction.GetObject(id,OpenMode.ForWrite)).RecordGraphicsModified(true);children.Add(new(Ref(id),"block_reference",0,null));}
            var expected=original.Where(id=>!plan.Removed.Contains(id)).Concat(added).ToArray();var addedIds=added.ToArray();
            void Readback(Transaction read)
            {
                job.CheckCancellation();var actual=(BlockTableRecord)read.GetObject(target,OpenMode.ForRead);DefinitionWrite(actual);
                Need(CadBlockDefinitionGeometry388.Members(actual,db,read,job.CheckCancellation).SequenceEqual(expected),"Actual native definition membership differs from retained-minus-retired-plus-added order.","NATIVE_BLOCK_MEMBERSHIP_MISMATCH");
                foreach(var item in retired)Erased(item,db,read,job.CheckCancellation);
                VerifyAdded(plan,addedIds,geometry,nestedAttributes,read,job.CheckCancellation);
                foreach(var child in plan.Source?.Nested.Values.AsEnumerable()??Enumerable.Empty<CadBlockWriter.Resolved>())child.Graph?.Verify(read,job.CheckCancellation);
                var after=CadBlockDefinitionGeometry388.Capture(target,[],db,read,job.CheckCancellation,true,membershipSelected:true,requireMembershipUnits:edit.AddEntities.Length!=0,retainedDefinitions:plan.RetainedDefinitions);
                Same(invariant,Retained(after,changed,definitions));
            }
            stage="native_readback";Readback(transaction);
            var data=new ModifiedStructure(args.OperationId,"committed",false,[new(0,edit.Operation,Ref(target),"block_definition",plan.Before.Name,false,children.ToArray())],"not_guaranteed");
            SerializedCapacity357.Check(data,Wire.PayloadBudget-8192,job.CheckCancellation);
            job.CheckCancellation();stage="commit";job.BeginCommit();commitAttempted=true;transaction.Commit();transaction.Dispose();transaction=null;
            stage="postcommit_readback";using(var read=db.TransactionManager.StartTransaction())Readback(read);
            var result=Result.Ok(job.OriginalRequestId,data,context,warnings.Distinct(StringComparer.Ordinal).ToArray());job.RecordCommit(result);return result;
        }
        catch(System.Exception error)
        {
            if(job.CommittedResult is {} committed)return committed;
            if(error is DomainException {Error.code:"BLOCK_DEFINITION_CHANGED"})started=true;
            System.Exception? cleanup=null;try{transaction?.Dispose();}catch(System.Exception caught){cleanup=caught;}
            string cause=error is DomainException domain?domain.Error.code:error is OperationCanceledException?"CANCELLED":"BLOCK_MEMBERSHIP_FAILED";
            return job.Error(started?"WRITE_OUTCOME_UNKNOWN":cause,started?"Native shared composition work started or an original source changed, but the complete outcome is unverified; refresh the definition and its consumers before dependent writes.":error.Message,
                started?"unknown":"not_started",context,new{cause,stage,commit_attempted=commitAttempted,message=Reason(error),native_details=(error as DomainException)?.Error.details,cleanup_error=cleanup?.GetType().Name})with{warnings=warnings.Distinct(StringComparer.Ordinal).ToArray()};
        }
        finally
        {
            foreach(var item in owned.AsEnumerable().Reverse())try{item.Dispose();}catch(System.Exception){log.Write(job.Operation,job.OriginalRequestId,"block_membership_wrapper_cleanup",operationId:job.OperationId);}
        }
    }
}
