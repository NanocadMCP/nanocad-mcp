using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// One shared definition's actual nested occurrences. Reuse the reviewed
// consumer graph and the existing literal attribute setters/ownership rules.
internal static class CadNestedAttributeEditor393
{
    const string Warning="SHARED_NESTED_ATTRIBUTE: The stored value of a selected nested insertion changes in every actual Model/Paper/nested consumer of its owner definition. Definition/member/attribute identities, insertion placement, defaults and other occurrence values remain. Native graphics are marked modified; aligned glyph origins and multiline layout may reflow. Opaque owned payloads/private evaluated graphics are not exhaustively observed. Refresh inspect_block and view the useful symbol/titleblock. Undo/rollback is not guaranteed.";
    sealed record Item(SetAttributeEdit Edit,ObjectId Block,ObjectId Attribute,string Tag);
    sealed record Plan(CadBlockDefinitionGeometry388.State Before,Item[] Items);
    internal static bool Handles(StructureWriteArguments args)=>args.Operations.Any(e=>e is SetAttributeEdit{OwnerDefinitionHandle:not null});
    static void Need([DoesNotReturnIf(false)]bool value,string message,string code="UNSUPPORTED_NESTED_ATTRIBUTE")
    {if(!value)throw new DomainException(code,message);}
    static string Id(ObjectId id)=>Validation.NormalizeHandle(id.Handle.ToString());
    static bool Live(ObjectId id)=>!id.IsNull&&id.IsValid&&!id.IsErased&&!id.IsEffectivelyErased;
    static void Independent(DBObject value)
    {using var reactors=value.GetPersistentReactorIds();Need(!value.HasFields&&reactors.Count==0&&value.Annotative!=AnnotativeStates.True,"The selected definition/insertion has Fields, persistent associations or annotative context requiring its owning route.");}
    static CadBlockDefinitionGeometry388.State Capture(ObjectId target,Database db,Transaction tr,Action cancel)
        =>CadBlockDefinitionGeometry388.Capture(target,[],db,tr,cancel,true,membershipSelected:true,requireMembershipUnits:false);
    static AttributeReference Attribute(ObjectId definition,ObjectId block,ObjectId attribute,Database db,Transaction tr,Action cancel)
    {
        var owner=CadBlockDefinitionGeometry388.Definition(definition,db,tr,false);
        Need(CadBlockDefinitionGeometry388.Members(owner,db,tr,cancel).Contains(block),"block_handle must be an actual direct nested insertion in owner_definition_handle.","ATTRIBUTE_NOT_IN_BLOCK");
        Need(Live(block)&&tr.GetObject(block,OpenMode.ForRead) is BlockReference,"The nested insertion is unavailable.");
        var reference=(BlockReference)tr.GetObject(block,OpenMode.ForRead);
        Need(reference.GetType()==typeof(BlockReference)&&reference.ObjectId==block&&reference.OwnerId==definition&&reference.Database?.UnmanagedObject==db.UnmanagedObject&&!reference.IsDynamicBlock,
            "Select an exact local static nested insertion with its actual native owner.");
        Independent(reference);_=CadBlockDefinitionGeometry388.Definition(reference.BlockTableRecord,db,tr,false);CadBlockDefinitionGeometry388.Writable(reference,db,tr);
        var seen=new HashSet<ObjectId>();bool found=false;
        foreach(ObjectId id in reference.AttributeCollection)
        {
            cancel();Need(Live(id)&&seen.Add(id)&&tr.GetObject(id,OpenMode.ForRead) is AttributeReference,"The nested insertion has unavailable/repeated/custom attributes.");
            var value=(AttributeReference)tr.GetObject(id,OpenMode.ForRead);
            Need(value.GetType()==typeof(AttributeReference)&&value.ObjectId==id&&value.OwnerId==block&&value.Database?.UnmanagedObject==db.UnmanagedObject,"A nested attribute has a foreign native owner/type/document.");
            found|=id==attribute;
        }
        Need(found,"attribute_handle is not owned by the specified actual nested insertion.","ATTRIBUTE_NOT_IN_BLOCK");
        var selected=(AttributeReference)tr.GetObject(attribute,OpenMode.ForRead);CadBlockDefinitionGeometry388.Writable(selected,db,tr);return selected;
    }
    static Plan Resolve(StructureWriteArguments args,DocumentState state,Transaction tr,Action cancel,Action<string>? warning=null)
    {
        Need(Handles(args)&&args.Operations.All(e=>e is SetAttributeEdit{OwnerDefinitionHandle:not null,ExpectedDefinitionToken:not null}),"Use a separate batch of nested attribute values for one shared definition.","INVALID_OPERATION_ORDER");
        var edits=args.Operations.Cast<SetAttributeEdit>().ToArray();var first=edits[0];
        Need(edits.All(e=>e.OwnerDefinitionHandle==first.OwnerDefinitionHandle&&e.ExpectedDefinitionToken==first.ExpectedDefinitionToken)&&edits.Select(e=>e.AttributeHandle).Distinct(StringComparer.Ordinal).Count()==edits.Length,
            "Use distinct owned attributes of one shared definition and one current token.","INVALID_OPERATION_ORDER");
        var db=state.Document.Database;_=CadBlockSpace.RequireWriteSpace(db,tr,args.EffectiveTargetSpace,cancel);
        var target=CadStructureWriter.Find(db,first.OwnerDefinitionHandle!);Independent(CadBlockDefinitionGeometry388.Definition(target,db,tr,false));
        var before=Capture(target,db,tr,cancel);Need(CadBlockDefinitionGeometry388.Token(before)==first.ExpectedDefinitionToken,"Shared membership, native sources or actual consumers changed; refresh inspect_block.","STALE_BLOCK_DEFINITION_TOKEN");
        var items=new List<Item>();
        foreach(var edit in edits)
        {
            cancel();var block=CadStructureWriter.Find(db,edit.BlockHandle);var id=CadStructureWriter.Find(db,edit.AttributeHandle);
            var value=Attribute(target,block,id,db,tr,cancel);CadBlockAttributeEditor.RequireLiteral(value,edit.ContentType,cancel);
            Need(CadBlockAttributeEditor.Contents(value,cancel)==edit.ExpectedText,"Stored nested occurrence contents differ from expected_text; refresh inspect_block.","ATTRIBUTE_TEXT_MISMATCH");
            try{NativeAttributeMText377.CheckReplacementFont(value,edit,tr,cancel);}
            catch(DomainException error)when(error.Error.code=="FONT_CHECK_UNAVAILABLE"){warning?.Invoke("FONT_CHECK_UNAVAILABLE: Requested nested attribute glyph coverage is unverified; "+error.Message);}
            if(edit.ContentType=="mtext")NativeAttributeMText377.Preview(value,edit,null,cancel);
            else using(var copy=CadContourBudget344.Detached(value))CadBlockAttributeEditor.Preview((AttributeReference)copy,edit,db,tr,cancel);
            items.Add(new(edit,block,id,value.Tag));
        }
        Need(before.Value.GetRawText()==Capture(target,db,tr,cancel).Value.GetRawText(),"Detached attribute preparation changed the retained native definition or source.","NATIVE_BLOCK_ATTRIBUTE_SOURCE_CHANGED");
        return new(before,items.ToArray());
    }
    internal static void Preflight(StructureWriteArguments args,DocumentState state,Transaction tr,Action cancel)=>_=Resolve(args,state,tr,cancel);
    internal static object TryRead(ObjectId target,string bridge,string document,Database db,Transaction tr,Action cancel)
    {
        EntityRef Ref(ObjectId id)=>new(bridge,document,Id(id));string? token=null,reason=null;int eligible=0;var rows=new List<object>();var refs=Array.Empty<EntityRef>();var budget=new ModelInventoryBudget(cancel);
        try
        {
            var owner=CadBlockDefinitionGeometry388.Definition(target,db,tr,false);Independent(owner);
            foreach(var block in CadBlockDefinitionGeometry388.Members(owner,db,tr,cancel))
            {
                cancel();if(tr.GetObject(block,OpenMode.ForRead) is not BlockReference reference||reference.GetType()!=typeof(BlockReference))continue;
                foreach(ObjectId id in reference.AttributeCollection)
                {
                    cancel();if(!Live(id)||tr.GetObject(id,OpenMode.ForRead) is not AttributeReference value)continue;
                    string kind=value.IsMTextAttribute?"mtext":"single_line";string? unavailable=null;
                    try{_=Attribute(target,block,id,db,tr,cancel);CadBlockAttributeEditor.RequireLiteral(value,kind,cancel);}
                    catch(OperationCanceledException){throw;}catch(System.Exception error){unavailable=Reason(error);}
                    if(unavailable==null)eligible++;
                    var row=new{block_ref=Ref(block),attribute_ref=Ref(id),tag=value.Tag,content_type=kind,contents=CadBlockAttributeEditor.Contents(value,cancel),can_edit=unavailable==null,unavailable_reason=unavailable};budget.Add(row);rows.Add(row);
                }
            }
            var capture=Capture(target,db,tr,cancel);token=CadBlockDefinitionGeometry388.Token(capture);refs=capture.References.Select(Ref).ToArray();
        }
        catch(OperationCanceledException){throw;}catch(System.Exception error){reason=Reason(error);}
        if(reason==null&&eligible==0)reason="No currently eligible direct nested variable literal attributes in this definition.";
        var result=new{definition_ref=Ref(target),definition_token=token,coordinate_system="parent_definition",can_edit=reason==null&&eligible>0,unavailable_reason=reason,
            members=rows.ToArray(),referencing_insertions=refs};SerializedCapacity357.Check(result,Wire.PayloadBudget-8192,cancel);return result;
    }
    static string Reason(System.Exception error)=>(error is DomainException d?d.Error.code:error.GetType().Name)+": "+error.Message;
    static JsonElement Invariant(CadBlockDefinitionGeometry388.State state,IEnumerable<Item> items)
    {
        var value=JsonNode.Parse(state.Value.GetRawText())!.AsObject();var selected=items.Select(i=>Id(i.Attribute)).ToHashSet(StringComparer.Ordinal);
        void Reference(JsonNode? row)
        {
            if(row?["attributes"] is not JsonArray attributes)return;
            foreach(var attribute in attributes)
            {
                string? handle=attribute?["header"]?["root"]?["handle"]?.GetValue<string>();if(handle==null||!selected.Contains(handle))continue;
                var stored=attribute!["stored"]!.AsObject();
                if(stored["dbtext"] is JsonObject text)
                {
                    text.Remove("text");if(text["horizontal"]!.GetValue<string>()!=TextHorizontalMode.TextLeft.ToString()||text["vertical"]!.GetValue<string>()!=TextVerticalMode.TextBase.ToString())text.Remove("position");
                }
                if(stored["multiline"] is JsonObject multiline){multiline.Remove("contents");multiline.Remove("plain_text");}
            }
        }
        foreach(var row in value["contents"]!.AsArray())Reference(row);
        foreach(var row in value["references"]!.AsArray())Reference(row);
        foreach(var definition in value["definitions"]!.AsArray())foreach(var row in definition!["unselected_contents"]!.AsArray())Reference(row);
        return Wire.Element(value);
    }
    static void Requested(Plan plan,Database db,Transaction tr,Action cancel)
    {
        foreach(var item in plan.Items)
        {cancel();var value=Attribute(plan.Before.Definition,item.Block,item.Attribute,db,tr,cancel);CadBlockAttributeEditor.RequireLiteral(value,item.Edit.ContentType,cancel);Need(value.Tag==item.Tag&&CadBlockAttributeEditor.Contents(value,cancel)==item.Edit.Text,"The actual nested attribute did not retain its requested literal value/kind/tag.","NATIVE_BLOCK_ATTRIBUTE_MISMATCH");}
    }
    internal static Result Execute(StructureWriteArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        Transaction? tr=null;bool started=false,commitAttempted=false;string stage="resolve";var warnings=new List<string>{Warning,WriteRecovery.Warning};
        try
        {
            var db=state.Document.Database;tr=db.TransactionManager.StartTransaction();var plan=Resolve(args,state,tr,job.CheckCancellation,warnings.Add);var invariant=Invariant(plan.Before,plan.Items);
            EntityRef Ref(ObjectId id)=>new(args.BridgeId,args.DocumentId,Id(id));
            var changes=plan.Items.Select((item,index)=>new StructureChange(index,item.Edit.Operation,Ref(item.Attribute),"attribute",item.Tag,false,
                new[]{new StructureChild(Ref(item.Block),"nested_block_reference",index,item.Tag),new StructureChild(Ref(plan.Before.Definition),"owner_definition",index,null)}
                    .Concat(index==0?plan.Before.References.Select(id=>new StructureChild(Ref(id),"block_reference",index,null)):Enumerable.Empty<StructureChild>()).ToArray())).ToArray();
            var data=new ModifiedStructure(args.OperationId,"committed",false,changes,"not_guaranteed");SerializedCapacity357.Check(data,Wire.PayloadBudget-8192,job.CheckCancellation);
            job.CheckCancellation();stage="native_attribute_setters";started=true;log.Write(job.Operation,job.OriginalRequestId,"nested_attribute_started",operationId:job.OperationId);
            foreach(var item in plan.Items)
            {
                job.CheckCancellation();var value=(AttributeReference)tr.GetObject(item.Attribute,OpenMode.ForWrite);CadBlockAttributeEditor.ApplyValue(value,item.Edit,db,tr,job.CheckCancellation);value.RecordGraphicsModified(true);
                ((Entity)tr.GetObject(item.Block,OpenMode.ForWrite)).RecordGraphicsModified(true);
            }
            stage="native_graphics_notification";foreach(var id in plan.Before.References){job.CheckCancellation();((Entity)tr.GetObject(id,OpenMode.ForWrite)).RecordGraphicsModified(true);}
            void Readback(Transaction transaction)
            {Requested(plan,db,transaction,job.CheckCancellation);Need(invariant.GetRawText()==Invariant(Capture(plan.Before.Definition,db,transaction,job.CheckCancellation),plan.Items).GetRawText(),"Shared attribute editing changed retained native members, sources, owners, consumer placement/defaults or other occurrence values.","NATIVE_BLOCK_ATTRIBUTE_MISMATCH");}
            stage="native_readback";Readback(tr);job.CheckCancellation();stage="commit";job.BeginCommit();commitAttempted=true;tr.Commit();tr.Dispose();tr=null;
            stage="postcommit_readback";using(var read=db.TransactionManager.StartTransaction())Readback(read);
            var result=Result.Ok(job.OriginalRequestId,data,context,warnings.Distinct(StringComparer.Ordinal).ToArray());job.RecordCommit(result);return result;
        }
        catch(System.Exception error)
        {
            if(job.CommittedResult is {} committed)return committed;System.Exception? cleanup=null;try{tr?.Dispose();}catch(System.Exception caught){cleanup=caught;}
            string cause=error is DomainException domain?domain.Error.code:error is OperationCanceledException?"CANCELLED":"NESTED_ATTRIBUTE_FAILED";if(cause=="NATIVE_BLOCK_ATTRIBUTE_SOURCE_CHANGED")started=true;
            bool unknown=started||commitAttempted||cleanup!=null;
            return job.Error(unknown?"WRITE_OUTCOME_UNKNOWN":cause,unknown?"Native shared attribute editing started, source preservation changed or transaction cleanup failed; its complete outcome is unverified. Refresh the definition and actual insertions before dependent writes.":error.Message,
                unknown?"unknown":"not_started",context,new{cause,stage,commit_attempted=commitAttempted,message=Reason(error),native_details=(error as DomainException)?.Error.details,cleanup_error=cleanup?.GetType().Name})with{warnings=warnings.Distinct(StringComparer.Ordinal).ToArray()};
        }
    }
}
