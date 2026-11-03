using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// Native instance/content setters. Shared styles and source block definitions
// are read and retained, never rewritten to emulate an instance change.
internal static class CadMLeaderInstance442
{
    internal const string Warning="NATIVE_MLEADER_INSTANCE: The retained native callout content/layout was changed. Arrow targets and actual branches remain; native text/block/dogleg layout may reflow. Read geometry.instance_editing and inspect the useful callout. No source association is created. Native Undo/recovery is not guaranteed.";
    internal sealed record State(JsonElement Properties,JsonElement Lines,JsonElement Branches,JsonElement Content,JsonElement Definition,JsonElement Metadata);
    sealed record Definition(ObjectId Id,JsonElement State,AttributeDefinition[] Attributes);
    static void Need([DoesNotReturnIf(false)]bool ok,string message,string code="UNSUPPORTED_MLEADER_INSTANCE")
    {if(!ok)throw new DomainException(code,message);}
    static string? Id(ObjectId id)=>id.IsNull?null:Validation.NormalizeHandle(id.Handle.ToString());
    static bool Same(JsonElement a,JsonElement b)=>MLeaderTextVerification.Unchanged(a,b);
    static Point3d P(Point2 p)=>new(p.X,p.Y,0);
    static Point3d P(JsonElement p)=>new(p[0].GetDouble(),p[1].GetDouble(),p[2].GetDouble());
    static double N(double n){Need(double.IsFinite(n),"Native MLeader contains nonfinite geometry.","INVALID_GEOMETRY");return n;}
    static double[] P(Point3d p)=>[N(p.X),N(p.Y),N(p.Z)];
    static double[] V(Vector3d p)=>[N(p.X),N(p.Y),N(p.Z)];
    static bool Near(Point3d a,Point3d b)=>a.DistanceTo(b)<=1e-7;
    static JsonElement Metadata(DBObject value,Transaction tr,Action cancel)
    {
        using var data=value.XData;using var links=value.GetPersistentReactorIds();var budget=new SerializedCapacity357(cancel:cancel);var reactors=new List<string?>();
        foreach(ObjectId id in links){cancel();budget.Add(Id(id));reactors.Add(Id(id));}
        return Wire.Element(new{handle=Id(value.ObjectId),owner=Id(value.OwnerId),runtime_type=value.GetType().FullName,extension=Id(value.ExtensionDictionary),
            has_fields=value.HasFields,annotative=value.Annotative.ToString(),xdata=data==null?null:NativeOwnedMetadata.Buffer(data,cancel,v=>budget.Add(v)),reactors,
            owned=NativeOwnedMetadata.Capture(value,tr,cancel)});
    }
    static ObjectId Symbol(string handle,Type type,ObjectId owner,Database db,Transaction tr)
    {
        var id=CadStructureWriter.Find(db,handle);var item=tr.GetObject(id,OpenMode.ForRead);
        Need(item.GetType()==type&&item.OwnerId==owner&&item.Database?.UnmanagedObject==db.UnmanagedObject,"Select a native local symbol from this drawing.","STYLE_UNAVAILABLE");
        if(item is SymbolTableRecord symbol)Need(!symbol.IsDependent&&!symbol.Name.Contains('|'),"A dependent symbol needs its owning document.","STYLE_UNAVAILABLE");return id;
    }
    static Definition Block(string handle,Database db,Transaction tr,Action cancel)
    {
        var id=Symbol(handle,typeof(BlockTableRecord),db.BlockTableId,db,tr);var block=(BlockTableRecord)tr.GetObject(id,OpenMode.ForRead);
        Need(!block.IsAnonymous&&!block.IsLayout&&!block.IsDynamicBlock&&!block.IsFromExternalReference&&!block.IsFromOverlayReference&&!block.HasFields&&block.Annotative!=AnnotativeStates.True,
            "Block balloons use an ordinary local static nonannotative definition without Fields.");
        Need((int)block.Units is 0 or 4,"Block balloon definition must be Unitless or Millimeters; no implicit unit conversion.","BLOCK_UNITS_MISMATCH");
        var members=new List<object>();var attributes=new List<AttributeDefinition>();var budget=new SerializedCapacity357(cancel:cancel);
        foreach(ObjectId member in block)
        {
            cancel();Need(!member.IsNull&&member.IsValid&&!member.IsErased&&!member.IsEffectivelyErased,"Native block membership is unavailable.");
            var raw=tr.GetObject(member,OpenMode.ForRead);Need(raw is Entity&&raw.OwnerId==id&&raw.Database?.UnmanagedObject==db.UnmanagedObject,"Native block member ownership differs.");var entity=(Entity)raw;
            Need(!entity.HasFields&&entity.Annotative!=AnnotativeStates.True,"Block members with Fields or annotation contexts need their owning adapter.");
            var type=entity.GetType();Need(type==typeof(Line)||type==typeof(Circle)||type==typeof(Arc)||type==typeof(Ellipse)||type==typeof(Polyline)||type==typeof(Spline)||type==typeof(DBText)||type==typeof(MText)||type==typeof(AttributeDefinition),
                "This block-content route uses native ordinary curve/Text/MText/AttributeDefinition members; nested/custom members need their own adapter.");
            object? attribute=null;
            if(entity is AttributeDefinition definition)
            {
                Need(!definition.IsMTextAttributeDefinition&&MLeaderTextLiteral.Supported(definition.TextString,0,4096),"Block balloon attributes use native single-line literal definitions.");
                attributes.Add(definition);attribute=new{definition.Tag,definition.Constant,definition.Invisible,definition.Preset,definition.Verifiable,definition.LockPositionInBlock};
            }
            var summary=EntityAdapter.Summary(entity,ObjectId.Null,"00000000-0000-0000-0000-000000000000","00000000-0000-0000-0000-000000000000");
            var state=new{metadata=Metadata(entity,tr,cancel),geometry=EntityAdapter.Detail(entity,summary,CancellationToken.None),properties=EntityAdapter.StoredProperties(entity),attribute};budget.Add(state);members.Add(state);
        }
        Need(members.Count>0,"A native block balloon needs a nonempty symbol definition.");
        var result=Wire.Element(new{metadata=Metadata(block,tr,cancel),block.Name,origin=P(block.Origin),units=(int)block.Units,members});SerializedCapacity357.Check(result,cancel:cancel);
        return new(id,result,attributes.ToArray());
    }
    static AttributeReference? Attribute(MLeader leader,ObjectId definition)
    {
        var value=leader.GetBlockAttribute(definition);
        Need(value==null||value.ObjectId.IsNull,"Native block attribute getter returned a resident alias.","UNSAFE_NATIVE_CLONE");return value;
    }
    static JsonElement AttributeState(AttributeReference value,Action cancel)
    {
        cancel();Need(!value.HasFields&&!value.IsMTextAttribute&&MLeaderTextLiteral.Supported(value.TextString,0,4096),"Existing block balloon attribute needs a nonliteral/multiline content adapter.");
        using var data=value.XData;using var links=value.GetPersistentReactorIds();Need(value.ExtensionDictionary.IsNull&&links.Count==0,"Embedded attribute-owned dependencies need their owning adapter.");
        var budget=new SerializedCapacity357(cancel:cancel);
        return Wire.Element(new{value.Tag,text=value.TextString,style=Id(value.TextStyleId),height=N(value.Height),width_factor=N(value.WidthFactor),oblique=N(value.Oblique),
            rotation=N(value.Rotation),position=P(value.Position),alignment=P(value.AlignmentPoint),normal=V(value.Normal),horizontal=value.HorizontalMode.ToString(),vertical=value.VerticalMode.ToString(),
            value.Invisible,value.IsConstant,value.LockPositionInBlock,value.IsMirroredInX,value.IsMirroredInY,properties=EntityAdapter.StoredProperties(value),xdata=data==null?null:NativeOwnedMetadata.Buffer(data,cancel,v=>budget.Add(v))});
    }
    static JsonElement BlockValues(MLeader leader,Definition source,Action cancel)
    {
        var rows=new List<object>();var budget=new SerializedCapacity357(cancel:cancel);
        foreach(var definition in source.Attributes)
        {
            cancel();using var value=definition.Constant?null:Attribute(leader,definition.ObjectId);
            var row=new{attribute_definition_handle=Id(definition.ObjectId),tag=definition.Tag,constant=definition.Constant,value=value?.TextString??definition.TextString,
                has_override=value!=null,stored=value==null?default(JsonElement?):AttributeState(value,cancel)};budget.Add(row);rows.Add(row);
        }
        return Wire.Element(rows);
    }
    static JsonElement Properties(MLeader leader)
    {
        bool block=leader.ContentType==ContentType.BlockContent;
        using var lineColor=leader.LeaderLineColor;using var textColor=leader.TextColor;
        object Color(Teigha.Colors.Color c)=>new{method=c.ColorMethod.ToString(),aci=c.ColorIndex,rgb=new[]{(int)c.Red,c.Green,c.Blue}};
        return Wire.Element(new{content_type=leader.ContentType.ToString(),content_position_wcs=P(block?leader.BlockPosition:leader.TextLocation),
            text_height=N(leader.TextHeight),text_style_handle=Id(leader.TextStyleId),arrow_size=N(leader.ArrowSize),arrow_symbol_handle=Id(leader.ArrowSymbolId),
            landing_gap=N(leader.LandingGap),dogleg_length=N(leader.DoglegLength),enable_landing=leader.EnableLanding,enable_dogleg=leader.EnableDogleg,enable_frame_text=leader.EnableFrameText,
            text_alignment=leader.TextAlignmentType.ToString(),text_angle_type=leader.TextAngleType.ToString(),
            text_attachment_left=leader.GetTextAttachmentType(LeaderDirectionType.LeftLeader).ToString(),text_attachment_right=leader.GetTextAttachmentType(LeaderDirectionType.RightLeader).ToString(),
            block_scale=block?new[]{N(leader.BlockScale.X),N(leader.BlockScale.Y),N(leader.BlockScale.Z)}:null,block_rotation=block?(double?)N(leader.BlockRotation):null,
            block_connection_type=block?leader.BlockConnectionType.ToString():null,block_handle=block?Id(leader.BlockContentId):null,style_handle=Id(leader.MLeaderStyle),
            normal=V(leader.Normal),scale=N(leader.Scale),line_type=leader.LeaderLineType.ToString(),text_attachment_direction=leader.TextAttachmentDirection.ToString(),
            root_properties=EntityAdapter.StoredProperties(leader),leader_color=Color(lineColor),text_color=Color(textColor),line_type_handle=Id(leader.LeaderLineTypeId),lineweight=(int)leader.LeaderLineWeight});
    }
    static State Capture(MLeader leader,Database db,Transaction tr,Action cancel,bool metadata=true)
    {
        Need(leader.GetType()==typeof(MLeader)&&(leader.ContentType is ContentType.MTextContent or ContentType.BlockContent)&&!leader.HasFields&&!leader.EnableAnnotationScale,
            "Use an ordinary native MText- or block-content MLeader without Fields/annotation scales.");
        Need(leader.Normal.IsEqualTo(Vector3d.ZAxis,Tolerance.Global),"MLeader instance edits retain WCS XY/Z0.","OUT_OF_SCOPE");
        var position=leader.ContentType==ContentType.BlockContent?leader.BlockPosition:leader.TextLocation;Need(Math.Abs(position.Z)<=1e-7,"Content must lie in WCS XY/Z0.","OUT_OF_SCOPE");
        var lines=new List<object>();var branches=new List<object>();var budget=new SerializedCapacity357(cancel:cancel);var seen=new HashSet<int>();
        foreach(int branch in leader.GetLeaderIndexes())
        {
            cancel();var item=new{leader_index=branch,dogleg=V(leader.GetDogleg(branch)),dogleg_length=N(leader.GetDoglegLength(branch))};budget.Add(item);branches.Add(item);
            foreach(int line in leader.GetLeaderLineIndexes(branch))
            {
                cancel();Need(seen.Add(line),"Native leader line inventory repeats.");int count=leader.VerticesCount(line);Need(count>=2,"A retained leader line requires at least two vertices.");
                var vertices=new List<double[]>();for(int i=0;i<count;i++){cancel();var point=P(leader.GetVertex(line,i));Need(Math.Abs(point[2])<=1e-7,"Leader geometry must lie in WCS XY/Z0.","OUT_OF_SCOPE");budget.Add(point);vertices.Add(point);}
                using var color=leader.GetLeaderLineColor(line);
                var state=new{leader_index=branch,leader_line_index=line,vertices_wcs=vertices,line_type=leader.GetLeaderLineType(line).ToString(),linetype_handle=Id(leader.GetLeaderLineTypeId(line)),lineweight=(int)leader.GetLeaderLineWeight(line),
                    color=new{method=color.ColorMethod.ToString(),aci=color.ColorIndex,rgb=new[]{(int)color.Red,color.Green,color.Blue}},arrow_size=N(leader.GetArrowSize(line)),arrow_symbol_handle=Id(leader.GetArrowSymbolId(line))};budget.Add(state);lines.Add(state);
            }
        }
        Need(lines.Count>0,"The retained MLeader requires native leader lines.");
        JsonElement content,definition=Wire.Element(new{});
        if(leader.ContentType==ContentType.MTextContent)
        {
            using var text=CadAnnotationTransformEditor.OwnedText(leader);Need(text!=null&&!text.HasFields&&text.ColumnType==ColumnType.NoColumns,"Embedded text with Fields/columns needs its owning adapter.");
            content=Wire.Element(new{kind="mtext",raw=text!.Contents,font=Id(text.TextStyleId),text_height=N(text.TextHeight),width=N(text.Width),normal=V(text.Normal),direction=V(text.Direction),
                attachment=text.Attachment.ToString(),paragraph=CadMLeaderStyleWriter337.EmbeddedText(text,cancel)});
        }
        else
        {
            var source=Block(Id(leader.BlockContentId)!,db,tr,cancel);definition=source.State;content=Wire.Element(new{kind="block",attributes=BlockValues(leader,source,cancel)});
        }
        var result=new State(Properties(leader),Wire.Element(lines),Wire.Element(branches),content,definition,metadata?CadMLeaderGeometry388.Metadata(leader,tr,cancel):Wire.Element(new{}));
        SerializedCapacity357.Check(result,cancel:cancel);return result;
    }
    static void Ownership(MLeader source,Transaction tr,Action cancel)
    {
        Need(!source.ObjectId.IsNull&&source.ObjectId.IsValid&&!source.ObjectId.IsErased,"Select a current native MLeader.","OBJECT_NOT_FOUND");
        var db=source.Database;Need(HostMgd.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Database==db,"MLeader document is not active.","DOCUMENT_CONTEXT_MISMATCH");
        var space=CadBlockSpace.RequireSpace(db,tr,source.OwnerId);Need(CadBlockSpace.RequireWriteSpace(db,tr,space.Kind,cancel)==source.OwnerId,"MLeader is outside current ordinary Model/Paper.","OUT_OF_SCOPE");
        CadAnnotationTransformEditor.CheckSource(source,tr,cancel);
        Need(tr.GetObject(source.MLeaderStyle,OpenMode.ForRead) is MLeaderStyle style&&style.GetType()==typeof(MLeaderStyle)&&style.OwnerId==db.MLeaderStyleDictionaryId&&!style.Name.Contains('|'),"Native MLeader style ownership is unavailable.","STYLE_UNAVAILABLE");
    }
    static State Prepare(MLeader source,SetMLeaderInstance442 edit,Transaction tr,Action cancel)
    {
        Ownership(source,tr,cancel);var before=Capture(source,source.Database,tr,cancel);
        Need(MLeaderGeometryValidation388.Token(before,cancel)==edit.InstanceToken,"MLeader/source definition/layout changed; read its fresh instance_token.","MLEADER_STATE_CHANGED");
        bool block=source.ContentType==ContentType.BlockContent;
        foreach(var field in edit.Properties.EnumerateObject())
        {
            Need(!field.Name.StartsWith("block_",StringComparison.Ordinal)||block,"Block properties require actual native BlockContent.");
            Need(!field.Name.StartsWith("text_",StringComparison.Ordinal)&&field.Name!="enable_frame_text"||!block,"Text properties require actual native MTextContent.");
            if(field.Name=="text_style_handle")
            {
                var font=Symbol(field.Value.GetString()!,typeof(TextStyleTableRecord),source.Database.TextStyleTableId,source.Database,tr);
                using var text=CadAnnotationTransformEditor.OwnedText(source);string? plain=MTextContent.PlainText(text!.Contents);
                Need(plain!=null,"A changed font needs readable retained literal glyphs; mixed/unknown controls require their content adapter.");FontCoverage.Require(tr,font,plain!);
            }
            if(field.Name=="arrow_symbol_handle")_=Symbol(field.Value.GetString()!,typeof(BlockTableRecord),source.Database.BlockTableId,source.Database,tr);
        }
        Need(block||edit.Attributes.Length==0,"Attribute value edits require actual BlockContent.");
        if(block)
        {
            var definition=Block(Id(source.BlockContentId)!,source.Database,tr,cancel);
            foreach(var change in edit.Attributes)
            {
                var attribute=definition.Attributes.SingleOrDefault(a=>Id(a.ObjectId)==change.AttributeDefinitionHandle);
                Need(attribute!=null&&!attribute.Constant,"Select an actual variable attribute definition of this callout's block.");
                using var value=Attribute(source,attribute!.ObjectId);Need((value?.TextString??attribute.TextString)==change.ExpectedValue,"Block balloon attribute differs from expected_value.","ATTRIBUTE_TEXT_MISMATCH");
                FontCoverage.Require(tr,value?.TextStyleId??attribute.TextStyleId,change.Value);
            }
        }
        return before;
    }
    internal static (object? Value,string? Reason) TryRead(MLeader leader,Action cancel)
    {
        try
        {
            var tr=leader.Database.TransactionManager.TopTransaction??throw new DomainException("OBJECT_READ_FAILED","Native instance token requires an active read transaction.");
            var state=Capture(leader,leader.Database,tr,cancel);var result=new{instance_token=MLeaderGeometryValidation388.Token(state,cancel),complete=true,properties=state.Properties,
                lines=state.Lines,branches=state.Branches,content=state.Content,scope="actual_native_instance_content_layout_branches_and_source_definition; no_Field_or_annotation_context_adapter; ordinary_Model_or_active_Paper_write_admission_checked_separately"};
            SerializedCapacity357.Check(result,Wire.PayloadBudget-8192,cancel);return(result,null);
        }
        catch(OperationCanceledException){throw;}
        catch(DomainException error){return(null,error.Error.code+": "+error.Error.message);}
        catch(Exception error){return(null,"NATIVE_MLEADER_INSTANCE_READ_UNAVAILABLE: "+error.GetType().Name);}
    }
    static void SetAttribute(MLeader leader,AttributeDefinition definition,string text,Action cancel)
    {
        cancel();var acquired=Attribute(leader,definition.ObjectId);
        using var value=acquired??new AttributeReference();
        if(acquired==null)value.SetAttributeFromBlock(definition,Matrix3d.Identity);
        value.TextString=text;
        // Installed signature is (ObjectId, AttributeReference), not the CHM's
        // erroneous ref spelling. Public native setter copies attribute data.
        leader.SetBlockAttribute(definition.ObjectId,value);
    }
    static void SetProperties(MLeader source,SetMLeaderInstance442 edit,Database db,Transaction tr,Action cancel)
    {
        foreach(var p in edit.Properties.EnumerateObject())
        {
            cancel();switch(p.Name)
            {
                case "content_position_wcs":break;
                case "text_height":source.TextHeight=p.Value.GetDouble();break;
                case "text_style_handle":source.TextStyleId=Symbol(p.Value.GetString()!,typeof(TextStyleTableRecord),db.TextStyleTableId,db,tr);break;
                case "arrow_size":source.ArrowSize=p.Value.GetDouble();break;
                case "arrow_symbol_handle":source.ArrowSymbolId=Symbol(p.Value.GetString()!,typeof(BlockTableRecord),db.BlockTableId,db,tr);break;
                case "landing_gap":source.LandingGap=p.Value.GetDouble();break;
                case "dogleg_length":source.DoglegLength=p.Value.GetDouble();break;
                case "enable_landing":source.EnableLanding=p.Value.GetBoolean();break;
                case "enable_dogleg":source.EnableDogleg=p.Value.GetBoolean();break;
                case "enable_frame_text":source.EnableFrameText=p.Value.GetBoolean();break;
                case "text_alignment":source.TextAlignmentType=Enum.Parse<TextAlignmentType>(p.Value.GetString()!);break;
                case "text_angle_type":source.TextAngleType=Enum.Parse<TextAngleType>(p.Value.GetString()!);break;
                case "text_attachment_left":source.SetTextAttachmentType(Enum.Parse<TextAttachmentType>(p.Value.GetString()!),LeaderDirectionType.LeftLeader);break;
                case "text_attachment_right":source.SetTextAttachmentType(Enum.Parse<TextAttachmentType>(p.Value.GetString()!),LeaderDirectionType.RightLeader);break;
                case "block_scale":source.BlockScale=new Scale3d(p.Value.GetDouble());break;
                case "block_rotation":source.BlockRotation=p.Value.GetDouble();break;
                case "block_connection_type":source.BlockConnectionType=Enum.Parse<BlockConnectionType>(p.Value.GetString()!);break;
                default:throw new DomainException("INVALID_ARGUMENT","Unrecognized MLeader instance property.");
            }
        }
        if(edit.Attributes.Length>0)
        {
            var definition=Block(Id(source.BlockContentId)!,db,tr,cancel);
            foreach(var change in edit.Attributes)SetAttribute(source,definition.Attributes.Single(a=>Id(a.ObjectId)==change.AttributeDefinitionHandle),change.Value,cancel);
        }
        if(edit.Properties.TryGetProperty("content_position_wcs",out var target))
        {
            var current=source.ContentType==ContentType.BlockContent?source.BlockPosition:source.TextLocation;
            cancel();source.MoveMLeader(P(target)-current,MoveType.MoveContentAndDoglegPoints);
        }
        if(!source.ObjectId.IsNull)source.RecordGraphicsModified(true);
    }
    static JsonElement Without(JsonElement value,params string[] names)
    {var node=JsonNode.Parse(value.GetRawText())!.AsObject();foreach(string name in names)node.Remove(name);return Wire.Element(node);}
    static void Verify(State before,State after,SetMLeaderInstance442 edit,Action cancel)
    {
        Need(Same(before.Definition,after.Definition),"Instance editing changed the source block definition.","NATIVE_MLEADER_INSTANCE_MISMATCH");
        var changed=edit.Properties.EnumerateObject().Select(p=>p.Name).ToArray();
        Need(Same(Without(before.Properties,changed.Concat(new[]{"content_position_wcs"}).ToArray()),Without(after.Properties,changed.Concat(new[]{"content_position_wcs"}).ToArray())),"Instance editing changed an unrequested native property.","NATIVE_MLEADER_INSTANCE_MISMATCH");
        foreach(var p in edit.Properties.EnumerateObject())
        {
            var actual=after.Properties.GetProperty(p.Name);bool equal=p.Name=="block_scale"?actual.EnumerateArray().All(n=>Math.Abs(n.GetDouble()-p.Value.GetDouble())<=1e-7):Same(p.Value,actual);
            if(p.Name=="block_rotation")equal=Math.Abs(Math.Sin(actual.GetDouble()-p.Value.GetDouble()))<=1e-9&&Math.Cos(actual.GetDouble()-p.Value.GetDouble())>0;
            Need(equal,"Native MLeader did not retain requested "+p.Name+".","NATIVE_MLEADER_INSTANCE_MISMATCH");
        }
        Need(before.Lines.GetArrayLength()==after.Lines.GetArrayLength(),"Instance editing changed leader topology.","NATIVE_MLEADER_INSTANCE_MISMATCH");
        Need(before.Branches.EnumerateArray().Select(b=>b.GetProperty("leader_index").GetInt32()).OrderBy(i=>i).SequenceEqual(after.Branches.EnumerateArray().Select(b=>b.GetProperty("leader_index").GetInt32()).OrderBy(i=>i)),
            "Instance editing changed native branch membership.","NATIVE_MLEADER_INSTANCE_MISMATCH");
        foreach(var prior in before.Lines.EnumerateArray())
        {
            cancel();int line=prior.GetProperty("leader_line_index").GetInt32();var next=after.Lines.EnumerateArray().SingleOrDefault(v=>v.GetProperty("leader_line_index").GetInt32()==line);
            Need(next.ValueKind==JsonValueKind.Object&&prior.GetProperty("leader_index").GetInt32()==next.GetProperty("leader_index").GetInt32(),"Instance editing changed native branch/line identity.","NATIVE_MLEADER_INSTANCE_MISMATCH");
            var oldPoints=prior.GetProperty("vertices_wcs");var newPoints=next.GetProperty("vertices_wcs");
            Need(oldPoints.GetArrayLength()==newPoints.GetArrayLength()&&Near(P(oldPoints[0]),P(newPoints[0])),"Instance editing changed retained arrow targets or line vertex counts.","NATIVE_MLEADER_INSTANCE_MISMATCH");
            var omit=new List<string>{"vertices_wcs"};if(changed.Contains("arrow_size"))omit.Add("arrow_size");if(changed.Contains("arrow_symbol_handle"))omit.Add("arrow_symbol_handle");
            Need(Same(Without(prior,omit.ToArray()),Without(next,omit.ToArray())),"Instance editing changed an unrequested per-line property.","NATIVE_MLEADER_INSTANCE_MISMATCH");
        }
        if(before.Content.GetProperty("kind").GetString()=="mtext")
        {
            var omit=new List<string>();if(changed.Contains("text_style_handle"))omit.Add("font");if(changed.Contains("text_height"))omit.Add("text_height");
            // Direction/attachment may follow explicitly changed native text
            // alignment; paragraph/background/raw Contents remain exact.
            if(changed.Length>0)omit.AddRange(new[]{"direction","attachment","paragraph"});
            Need(Same(Without(before.Content,omit.ToArray()),Without(after.Content,omit.ToArray())),"Instance editing changed retained raw text or embedded content controls.","NATIVE_MLEADER_INSTANCE_MISMATCH");
            Need(Same(before.Content.GetProperty("paragraph"),after.Content.GetProperty("paragraph"))||Same(Without(before.Content.GetProperty("paragraph"),"direction","attachment"),Without(after.Content.GetProperty("paragraph"),"direction","attachment")),"Instance editing changed retained paragraph/background data.","NATIVE_MLEADER_INSTANCE_MISMATCH");
        }
        else
        {
            var old=before.Content.GetProperty("attributes");var current=after.Content.GetProperty("attributes");Need(old.GetArrayLength()==current.GetArrayLength(),"Block attribute membership changed.","NATIVE_MLEADER_INSTANCE_MISMATCH");
            foreach(var prior in old.EnumerateArray())
            {
                string id=prior.GetProperty("attribute_definition_handle").GetString()!;var next=current.EnumerateArray().Single(v=>v.GetProperty("attribute_definition_handle").GetString()==id);
                var change=edit.Attributes.SingleOrDefault(a=>a.AttributeDefinitionHandle==id);
                Need(next.GetProperty("value").GetString()==(change?.Value??prior.GetProperty("value").GetString()),"Native block attribute value differs.","NATIVE_MLEADER_INSTANCE_MISMATCH");
                Need(Same(Without(prior,"value","stored","has_override"),Without(next,"value","stored","has_override")),"Native block attribute tag/definition/constant state changed.","NATIVE_MLEADER_INSTANCE_MISMATCH");
                if(prior.GetProperty("stored").ValueKind==JsonValueKind.Object&&next.GetProperty("stored").ValueKind==JsonValueKind.Object)
                    Need(Same(Without(prior.GetProperty("stored"),"text","position","alignment","rotation","height"),Without(next.GetProperty("stored"),"text","position","alignment","rotation","height")),"Native block attribute changed unrequested stored data.","NATIVE_MLEADER_INSTANCE_MISMATCH");
            }
        }
    }
    internal static void CheckSource(Entity source,SetMLeaderInstance442 edit,Transaction tr,Action cancel)
    {Need(source.GetType()==typeof(MLeader),"Select an exact native MLeader.","UNSUPPORTED_ENTITY_TYPE");_=Prepare((MLeader)source,edit,tr,cancel);}
    static ModifiedEntities Result(ModifyArguments args,SetMLeaderInstance442 edit)=>new(args.OperationId,"committed",false,[new(args.BridgeId,args.DocumentId,edit.Handles.Single())],[],[],[],"not_guaranteed");
    internal static void Preview(ModifyArguments args,CadModifier.Resolved resolved,Transaction tr,Action cancel)
    {
        var edit=(SetMLeaderInstance442)args.Operations.Single();var source=(MLeader)tr.GetObject(resolved.Sources[edit.Handles.Single()],OpenMode.ForRead);var before=Prepare(source,edit,tr,cancel);
        using var clone=CadContourBudget344.Detached(source);var detached=(MLeader)clone;var initial=Capture(detached,source.Database,tr,cancel,false);
        SetProperties(detached,edit,source.Database,tr,cancel);Verify(initial,Capture(detached,source.Database,tr,cancel,false),edit,cancel);
        Need(MLeaderGeometryValidation388.Token(before,cancel)==MLeaderGeometryValidation388.Token(Capture(source,source.Database,tr,cancel),cancel),"Detached instance preview changed the resident callout.","NATIVE_MLEADER_INSTANCE_MISMATCH");
        SerializedCapacity357.Check(new{data=Result(args,edit),warnings=new[]{Warning}},Wire.PayloadBudget-16384,cancel);
    }
    internal static ModifiedEntities Apply(ModifyArguments args,CadModifier.Resolved resolved,Transaction tr,WriteJob job,List<string>? warnings)
    {
        var edit=(SetMLeaderInstance442)args.Operations.Single();var source=(MLeader)tr.GetObject(resolved.Sources[edit.Handles.Single()],OpenMode.ForRead);var before=Prepare(source,edit,tr,job.CheckCancellation);
        Need(source.OwnerId==resolved.Model,"MLeader differs from requested active space.","OUT_OF_SCOPE");source.UpgradeOpen();SetProperties(source,edit,source.Database,tr,job.CheckCancellation);var after=Capture(source,source.Database,tr,job.CheckCancellation);
        Verify(before,after,edit,job.CheckCancellation);Need(Same(before.Metadata,after.Metadata),"Native instance edit changed root identity/owner or unrelated owned data.","NATIVE_MLEADER_INSTANCE_MISMATCH");
        warnings?.Add(Warning);return Result(args,edit);
    }
    internal static ObjectId ResolveCreation(MLeaderBlockSpec442 spec,Database db,Transaction tr,Action cancel)
    {
        var source=Block(spec.BlockHandle,db,tr,cancel);var styles=(DBDictionary)tr.GetObject(db.MLeaderStyleDictionaryId,OpenMode.ForRead);
        Need(styles.Contains(spec.MLeaderStyle),"Explicit native MLeaderStyle does not exist.","ANNOTATION_STYLE_UNAVAILABLE");var styleId=styles.GetAt(spec.MLeaderStyle);
        Need(tr.GetObject(styleId,OpenMode.ForRead) is MLeaderStyle style&&style.GetType()==typeof(MLeaderStyle)&&style.OwnerId==db.MLeaderStyleDictionaryId&&!style.HasFields&&style.Annotative!=AnnotativeStates.True,"Use an ordinary local nonannotative MLeaderStyle.","ANNOTATION_STYLE_UNAVAILABLE");
        foreach(var change in spec.Attributes)
        {
            var definition=source.Attributes.SingleOrDefault(a=>Id(a.ObjectId)==change.AttributeDefinitionHandle);Need(definition!=null&&!definition.Constant,"Creation attributes must identify variable members of this block.");
        }
        foreach(var attribute in source.Attributes)
            FontCoverage.Require(tr,attribute.TextStyleId,spec.Attributes.SingleOrDefault(a=>a.AttributeDefinitionHandle==Id(attribute.ObjectId))?.Value??attribute.TextString);
        return styleId;
    }
    internal static MLeader Create(MLeaderBlockSpec442 spec,ObjectId style,Database db,Transaction tr,Action cancel)
    {
        _=ResolveCreation(spec,db,tr,cancel);var definition=Block(spec.BlockHandle,db,tr,cancel);var result=new MLeader();
        try
        {
            result.MLeaderStyle=style;result.ContentType=ContentType.BlockContent;result.BlockContentId=definition.Id;result.BlockConnectionType=Enum.Parse<BlockConnectionType>(spec.BlockConnection);
            result.BlockScale=new Scale3d(spec.BlockScale);result.BlockRotation=spec.BlockRotation;result.BlockPosition=P(spec.BlockPosition);
            int line=result.AddLeaderLine(result.AddLeader());int count=result.VerticesCount(line);
            while(count<spec.Vertices.Length){cancel();result.AddLastVertex(line,P(spec.Vertices[Math.Min(count,spec.Vertices.Length-1)]));int next=result.VerticesCount(line);Need(next>count,"Native block leader did not append a vertex.");count=next;}
            while(count>spec.Vertices.Length){cancel();result.RemoveLastVertex(line);int next=result.VerticesCount(line);Need(next<count,"Native block leader did not remove its initializer vertex.");count=next;}
            for(int i=0;i<spec.Vertices.Length;i++){cancel();result.SetVertex(line,i,P(spec.Vertices[i]));}
            foreach(var attribute in definition.Attributes.Where(a=>!a.Constant))
                SetAttribute(result,attribute,spec.Attributes.SingleOrDefault(v=>v.AttributeDefinitionHandle==Id(attribute.ObjectId))?.Value??attribute.TextString,cancel);
            Need(Same(definition.State,Block(spec.BlockHandle,db,tr,cancel).State),"Creating a block balloon changed its source definition.","NATIVE_MLEADER_INSTANCE_MISMATCH");
            VerifyCreated(result,spec,db,tr,cancel);return result;
        }
        catch{result.Dispose();throw;}
    }
    internal static void VerifyCreated(MLeader result,MLeaderBlockSpec442 spec,Database db,Transaction tr,Action cancel)
    {
        Need(result.ContentType==ContentType.BlockContent&&Id(result.BlockContentId)==spec.BlockHandle&&Near(result.BlockPosition,P(spec.BlockPosition)),"Created native block balloon does not retain requested source/content placement.","NATIVE_MLEADER_INSTANCE_MISMATCH");
        Need(Math.Abs(result.BlockScale.X-spec.BlockScale)<=1e-7&&Math.Abs(result.BlockScale.Y-spec.BlockScale)<=1e-7&&Math.Abs(result.BlockScale.Z-spec.BlockScale)<=1e-7&&
            Math.Abs(Math.Sin(result.BlockRotation-spec.BlockRotation))<=1e-9&&Math.Cos(result.BlockRotation-spec.BlockRotation)>0&&result.BlockConnectionType.ToString()==spec.BlockConnection,
            "Created native block balloon scale/rotation/connection differs.","NATIVE_MLEADER_INSTANCE_MISMATCH");
        var lines=result.GetLeaderIndexes().Cast<int>().SelectMany(b=>result.GetLeaderLineIndexes(b).Cast<int>()).ToArray();Need(lines.Length==1&&result.VerticesCount(lines[0])==spec.Vertices.Length,"Created block balloon topology differs.","NATIVE_MLEADER_INSTANCE_MISMATCH");
        for(int i=0;i<spec.Vertices.Length;i++)Need(Near(result.GetVertex(lines[0],i),P(spec.Vertices[i])),"Created block balloon vertex differs.","NATIVE_MLEADER_INSTANCE_MISMATCH");
        var block=Block(spec.BlockHandle,db,tr,cancel);foreach(var attribute in block.Attributes.Where(a=>!a.Constant))
        {
            using var actual=Attribute(result,attribute.ObjectId);string expected=spec.Attributes.SingleOrDefault(v=>v.AttributeDefinitionHandle==Id(attribute.ObjectId))?.Value??attribute.TextString;
            Need(actual!=null&&actual.TextString==expected,"Created block balloon attribute did not read back.","NATIVE_MLEADER_INSTANCE_MISMATCH");
        }
    }
}
