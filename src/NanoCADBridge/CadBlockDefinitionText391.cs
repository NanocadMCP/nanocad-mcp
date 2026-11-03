using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// Existing native literal definition labels. The shared source graph is the
// same actual Model/Paper/nested graph observed by the geometry member route.
internal static class CadBlockDefinitionText391
{
    private const string Warning="SHARED_BLOCK_TEXT: Literal definition labels change in every actual Model/Paper/nested insertion. Existing members, insertion placement, literal occurrence attributes and observed source metadata remain; native graphics are marked modified. Text glyph origin and MText evaluated layout can reflow. Unselected opaque payloads and private evaluated graphics are not exhaustively observed. Refresh inspect_block and inspect a useful titleblock/symbol view. Undo/rollback is not guaranteed.";
    internal static bool Handles(StructureWriteArguments args)=>args.Operations is [SetBlockDefinitionText391];
    private static void Need([DoesNotReturnIf(false)] bool value,string message,string code="UNSUPPORTED_BLOCK_TEXT")
    {if(!value)throw new DomainException(code,message);}
    private static string Id(ObjectId id)=>Validation.NormalizeHandle(id.Handle.ToString());
    private static string Kind(Entity entity)=>entity.GetType()==typeof(DBText)?"Text":entity.GetType()==typeof(MText)?"MText":
        throw new DomainException("UNSUPPORTED_BLOCK_MEMBER","Select direct native Text/MText definition labels; AttributeDefinition/occurrence values have a separate scope.");
    private static string Contents(Entity entity)=>entity is MText mtext?mtext.Contents:((DBText)entity).TextString;
    private static ObjectId Style(Entity entity,Database db,Transaction tr)
    {
        var id=entity is MText mtext?mtext.TextStyleId:((DBText)entity).TextStyleId;
        Need(!id.IsNull&&id.IsValid&&!id.IsErased&&!id.IsEffectivelyErased&&tr.GetObject(id,OpenMode.ForRead) is TextStyleTableRecord,
            "Selected label has no current native text-style identity.","TEXT_STYLE_UNAVAILABLE");
        var style=(TextStyleTableRecord)tr.GetObject(id,OpenMode.ForRead);
        Need(style.GetType()==typeof(TextStyleTableRecord)&&style.ObjectId==id&&style.OwnerId==db.TextStyleTableId&&style.Database?.UnmanagedObject==db.UnmanagedObject,
            "Selected label's native text style has a foreign owner/database.","TEXT_STYLE_UNAVAILABLE");return id;
    }
    internal static void RequireLiteral(Entity entity,Action cancel)
    {
        cancel();_=Kind(entity);
        Need(!entity.HasFields&&entity.Annotative!=AnnotativeStates.True,"Selected labels must be native literal nonannotative Text/MText without Fields.");
        using var reactors=entity.GetPersistentReactorIds();Need(reactors.Count==0,"Selected labels have persistent dependencies requiring their owning route.");
        if(entity is DBText text)
            Need(StructureWriteValidation.IsPlainAttribute(text.TextString)&&(text.HorizontalMode is TextHorizontalMode.TextLeft or TextHorizontalMode.TextCenter or TextHorizontalMode.TextRight)&&
                Enum.IsDefined(typeof(TextVerticalMode),text.VerticalMode)&&double.IsFinite(text.Oblique)&&Math.Abs(text.Oblique)<Math.PI/2,
                "Text labels require literal single-line content with left/center/right justification; fit/aligned labels need their native placement/format route.");
        else Need(MTextEditContent.IsCanonical(((MText)entity).Contents),"MText labels require canonical literal native paragraph Contents; formatted/Field content needs its owning route.");
        CadModifier.CheckGeometry(entity,cancel);
    }
    private static string Reason(System.Exception error)
    {string value=(error is DomainException domain?domain.Error.code:error.GetType().Name)+": "+error.Message;return value.Length<=1024?value:value[..1024];}
    private static JsonObject StoredInvariant(Entity entity,Action cancel)
    {
        var value=JsonNode.Parse(Wire.Element(CadBlockDefinitionGeometry388.StoredGeometry(entity,cancel)).GetRawText())!.AsObject();
        RemoveRequested(value);return value;
    }
    private static void RemoveRequested(JsonObject value)
    {
        if(value["type"]!.GetValue<string>()=="Text")
        {
            value.Remove("TextString");
            // Position is the glyph-derived origin for nondefault alignment.
            // AlignmentPoint remains the fixed anchor; default Position stays.
            if(value["horizontal"]!.GetValue<string>()!=TextHorizontalMode.TextLeft.ToString()||
                value["vertical"]!.GetValue<string>()!=TextVerticalMode.TextBase.ToString())value.Remove("position");
        }
        else
        {
            value.Remove("Contents");
            // For native columns the overall Width is a layout envelope,
            // derived from the current column count. Keep explicit column
            // widths/gutter/heights/flow and the native Location anchor.
            if(value["column_type"]!.GetValue<string>()!=ColumnType.NoColumns.ToString())value.Remove("Width");
        }
    }
    private static JsonElement Invariants(CadBlockDefinitionGeometry388.State state,SetBlockDefinitionText391 edit)
    {
        var value=JsonNode.Parse(state.Value.GetRawText())!.AsObject();var ids=edit.Members.Select(e=>e.Handle).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var row in value["contents"]!.AsArray())
            if(row?["header"]?["root"]?["handle"] is JsonValue handle&&ids.Contains(handle.GetValue<string>()))RemoveRequested(row!["geometry"]!.AsObject());
        return Wire.Element(value);
    }
    private static void SetContents(Entity entity,BlockMemberText391 member,Database db,Action cancel)
    {
        cancel();RequireLiteral(entity,cancel);
        Need(Kind(entity)==member.Type,"Requested type differs from the actual native label.","UNSUPPORTED_BLOCK_MEMBER");
        Need(Contents(entity)==member.ExpectedText,"Stored label differs from expected_text; refresh inspect_block.definition_text_editing.","BLOCK_TEXT_MISMATCH");
        var before=StoredInvariant(entity,cancel);
        if(entity is MText mtext)mtext.Contents=member.Text;
        else
        {
            var text=(DBText)entity;text.TextString=member.Text;
            if(text.HorizontalMode!=TextHorizontalMode.TextLeft||text.VerticalMode!=TextVerticalMode.TextBase)
            {cancel();text.AdjustAlignment(db);}
        }
        cancel();RequireLiteral(entity,cancel);
        Need(Contents(entity)==member.Text,"Native label did not retain the exact requested stored content.","NATIVE_BLOCK_TEXT_MISMATCH");
        Need(before.ToJsonString()==StoredInvariant(entity,cancel).ToJsonString(),"Native label replacement changed its retained alignment anchor, frame, style or appearance.","NATIVE_BLOCK_TEXT_MISMATCH");
    }
    private static CadBlockDefinitionGeometry388.State Resolve(StructureWriteArguments args,DocumentState state,Transaction tr,Action cancel,Action<string>? warning=null)
    {
        Need(Handles(args),"Edit one shared definition's labels in a separate request.","INVALID_ARGUMENT");var edit=(SetBlockDefinitionText391)args.Operations[0];var db=state.Document.Database;
        var target=CadStructureWriter.Find(db,edit.DefinitionHandle);var selected=edit.Members.Select(e=>CadStructureWriter.Find(db,e.Handle)).ToHashSet();
        var before=CadBlockDefinitionGeometry388.Capture(target,selected,db,tr,cancel,true,textSelected:true);
        Need(CadBlockDefinitionGeometry388.Token(before)==edit.ExpectedDefinitionToken,"Definition labels, source data or referring insertions changed; refresh inspect_block.definition_text_editing.","STALE_BLOCK_DEFINITION_TOKEN");
        foreach(var member in edit.Members)
        {
            cancel();var entity=(Entity)tr.GetObject(CadStructureWriter.Find(db,member.Handle),OpenMode.ForRead);
            Need(Kind(entity)==member.Type,"Requested label type differs from the actual native member.","UNSUPPORTED_BLOCK_MEMBER");
            Need(Contents(entity)==member.ExpectedText,"Stored label differs from expected_text; refresh inspect_block.definition_text_editing.","BLOCK_TEXT_MISMATCH");
            _=Style(entity,db,tr);
            // Clone is a detached native preview; neither original member nor
            // any insertion is opened for write during preflight.
            using var clone=(Entity)entity.Clone();
            Need(clone.GetType()==entity.GetType()&&clone.ObjectId.IsNull&&clone.UnmanagedObject!=entity.UnmanagedObject,
                "Native label preview did not yield an independent detached entity.","BLOCK_TEXT_PREVIEW_FAILED");
            SetContents(clone,member,db,cancel);
            // The existing coverage adapter inspects the actual requested
            // literal preview. Proven missing fonts/glyphs still fail; an
            // unavailable inspection is an explicit warning, not a refusal.
            FontCoverage.RequireEntity(tr,clone,warning,cancel);
        }
        return before;
    }
    internal static void Preflight(StructureWriteArguments args,DocumentState state,Transaction tr,Action cancel)=>_=Resolve(args,state,tr,cancel);
    internal static object TryRead(ObjectId target,string bridge,string document,Database db,Transaction tr,Action cancel)
    {
        var rows=new List<object>();var refs=Array.Empty<EntityRef>();string? token=null;string? unavailable=null;string name="";var budget=new ModelInventoryBudget(cancel);
        EntityRef Ref(ObjectId id)=>new(bridge,document,Id(id));
        try
        {
            if(!target.IsNull&&target.IsValid&&tr.GetObject(target,OpenMode.ForRead) is BlockTableRecord actual)name=actual.Name;
            var block=CadBlockDefinitionGeometry388.Definition(target,db,tr,false);name=block.Name;var selected=new HashSet<ObjectId>();
            foreach(var id in CadBlockDefinitionGeometry388.Members(block,db,tr,cancel))
            {
                cancel();var entity=(Entity)tr.GetObject(id,OpenMode.ForRead);if(entity.GetType()!=typeof(DBText)&&entity.GetType()!=typeof(MText))continue;
                string? reason=null;
                try{RequireLiteral(entity,cancel);CadBlockDefinitionGeometry388.Writable(entity,db,tr);_=Style(entity,db,tr);_=NativeOwnedMetadata.Capture(entity,tr,cancel);selected.Add(id);}
                catch(OperationCanceledException){throw;}catch(System.Exception error){reason=Reason(error);}
                string contents=Contents(entity);var row=new{@ref=Ref(id),type=Kind(entity),contents,plain_text=entity is MText?MTextContent.PlainText(contents):contents,eligible=reason==null,unavailable_reason=reason};
                budget.Add(row);rows.Add(row);
            }
            var capture=CadBlockDefinitionGeometry388.Capture(target,selected,db,tr,cancel,true,textSelected:true);refs=capture.References.Select(Ref).ToArray();token=CadBlockDefinitionGeometry388.Token(capture);
            if(selected.Count==0)unavailable="This definition has no currently eligible direct native literal Text/MText label.";
        }
        catch(OperationCanceledException){throw;}catch(System.Exception error){unavailable=Reason(error);}
        var result=new{definition_ref=Ref(target),definition_name=name,coordinate_system="parent_definition",definition_token=token,can_edit=unavailable==null,
            unavailable_reason=unavailable,members=rows.ToArray(),referencing_insertions=refs};SerializedCapacity357.Check(result,Wire.PayloadBudget-8192,cancel);return result;
    }
    private static void Requested(SetBlockDefinitionText391 edit,Database db,Transaction tr,Action cancel)
    {
        foreach(var member in edit.Members)
        {
            cancel();var entity=(Entity)tr.GetObject(CadStructureWriter.Find(db,member.Handle),OpenMode.ForRead);RequireLiteral(entity,cancel);
            Need(Kind(entity)==member.Type&&Contents(entity)==member.Text,"An existing native definition label did not retain the exact requested content.","NATIVE_BLOCK_TEXT_MISMATCH");
        }
    }
    private static void Retained(JsonElement before,JsonElement after)
    {Need(before.GetRawText()==after.GetRawText(),"Shared label editing changed retained native members, source data, owners, insertion placement or occurrence attributes.","NATIVE_BLOCK_TEXT_MISMATCH");}
    internal static Result Execute(StructureWriteArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        Transaction? transaction=null;bool started=false,commitAttempted=false;string stage="resolve";var warnings=new List<string>{Warning,WriteRecovery.Warning};
        try
        {
            var db=state.Document.Database;var edit=(SetBlockDefinitionText391)args.Operations[0];transaction=db.TransactionManager.StartTransaction();
            var before=Resolve(args,state,transaction,job.CheckCancellation,warnings.Add);var selected=edit.Members.Select(e=>CadStructureWriter.Find(db,e.Handle)).ToHashSet();var invariant=Invariants(before,edit);
            var children=edit.Members.Select((e,i)=>new StructureChild(new(args.BridgeId,args.DocumentId,e.Handle),e.Type,i,null))
                .Concat(before.References.Select(id=>new StructureChild(new(args.BridgeId,args.DocumentId,Id(id)),"block_reference",0,null))).ToArray();
            var data=new ModifiedStructure(args.OperationId,"committed",false,[new(0,edit.Operation,new(args.BridgeId,args.DocumentId,edit.DefinitionHandle),"block_definition",before.Name,false,children)],"not_guaranteed");
            SerializedCapacity357.Check(data,Wire.PayloadBudget-8192,job.CheckCancellation);job.CheckCancellation();stage="native_content_setters";started=true;
            log.Write(job.Operation,job.OriginalRequestId,"block_definition_text_started",operationId:job.OperationId);
            foreach(var member in edit.Members)
            {
                job.CheckCancellation();var entity=(Entity)transaction.GetObject(CadStructureWriter.Find(db,member.Handle),OpenMode.ForWrite);
                SetContents(entity,member,db,job.CheckCancellation);entity.RecordGraphicsModified(true);
            }
            stage="native_graphics_notification";foreach(var id in before.References){job.CheckCancellation();((Entity)transaction.GetObject(id,OpenMode.ForWrite)).RecordGraphicsModified(true);}
            stage="native_readback";Requested(edit,db,transaction,job.CheckCancellation);
            Retained(invariant,Invariants(CadBlockDefinitionGeometry388.Capture(before.Definition,selected,db,transaction,job.CheckCancellation,true,textSelected:true),edit));
            job.CheckCancellation();stage="commit";job.BeginCommit();commitAttempted=true;transaction.Commit();transaction.Dispose();transaction=null;
            stage="postcommit_readback";using(var read=db.TransactionManager.StartTransaction())
            {
                Requested(edit,db,read,job.CheckCancellation);
                Retained(invariant,Invariants(CadBlockDefinitionGeometry388.Capture(before.Definition,selected,db,read,job.CheckCancellation,true,textSelected:true),edit));
            }
            var result=Result.Ok(job.OriginalRequestId,data,context,warnings.Distinct(StringComparer.Ordinal).ToArray());job.RecordCommit(result);return result;
        }
        catch(System.Exception error)
        {
            if(job.CommittedResult is {} committed)return committed;System.Exception? cleanup=null;try{transaction?.Dispose();}catch(System.Exception caught){cleanup=caught;}
            string cause=error is DomainException domain?domain.Error.code:error is OperationCanceledException?"CANCELLED":"BLOCK_TEXT_FAILED";
            return job.Error(started?"WRITE_OUTCOME_UNKNOWN":cause,started?"Native shared label editing started but the complete outcome is unverified; refresh the definition and its actual insertions before further dependent writes.":error.Message,
                started?"unknown":"not_started",context,new{cause,stage,commit_attempted=commitAttempted,message=Reason(error),native_details=(error as DomainException)?.Error.details,cleanup_error=cleanup?.GetType().Name})with{warnings=warnings.Distinct(StringComparer.Ordinal).ToArray()};
        }
    }
}
