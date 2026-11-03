using System.Security.Cryptography;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Color=Teigha.Colors.Color;

namespace NanoCADBridge;

// Native style defaults only: no replacement leader/template, per-entity setter,
// guessed override mask or analytic text/landing envelope oracle.
internal static class CadMLeaderStyleWriter337
{
    internal sealed record Snapshot(JsonElement Values,JsonElement Metadata,string Token);
    internal sealed record Consumer(ObjectId Id,JsonElement Preserved,JsonElement Effective,JsonElement PreviewLayout,string? Plain,string Raw);
    internal sealed record BorrowedConsumer(ObjectId Id,JsonElement State);
    internal sealed record Resolved(MLeaderStyleEdit337 Edit,ObjectId Dictionary,ObjectId Source,string SourceName,string Name,
        EntityRef Reference,Snapshot Before,JsonElement Defaults,JsonElement Inventory,Consumer[] Consumers,BorrowedConsumer[] BorrowedConsumers)
    {internal List<string> Warnings {get;}=new();}
    private static void Need([DoesNotReturnIf(false)] bool value,string message,string code="UNSUPPORTED_MLEADER_STYLE")
    {if(!value)throw new DomainException(code,message);}
    private static string? Id(ObjectId id)=>id.IsNull?null:Validation.NormalizeHandle(id.Handle.ToString());
    private static double N(double value)
    {Need(double.IsFinite(value),"Native style contains nonfinite stored defaults.","INVALID_GEOMETRY");return value;}
    private static double[] P(Point3d p)=>new[]{N(p.X),N(p.Y),N(p.Z)};
    private static double[] V(Vector3d p)=>new[]{N(p.X),N(p.Y),N(p.Z)};
    private static bool Exact(object? a,object? b)=>JsonSerializer.Serialize(a,Wire.Json)==JsonSerializer.Serialize(b,Wire.Json);
    private static string Text(string value,Action cancel)
    {
        cancel();try{Need(new UTF8Encoding(false,true).GetByteCount(value)<=8*1024*1024,"Native style text exceeds the actual8MiB snapshot budget.","RESPONSE_LIMIT_EXCEEDED");}
        catch(EncoderFallbackException){throw new DomainException("UNSUPPORTED_MLEADER_STYLE","Native template text contains malformed Unicode; no normalized token is produced.");}
        return value;
    }
    private static object ColorValue(Color color)=>new{method=color.IsByLayer?"by_layer":color.IsByBlock?"by_block":color.IsByAci?"aci":color.IsByColor?"true_color":color.ColorMethod.ToString(),
        aci=color.IsByAci?(int?)color.ColorIndex:null,rgb=color.IsByColor?new[]{(int)color.Red,(int)color.Green,(int)color.Blue}:null};
    private static MText? OwnedText(MLeader leader,Action cancel)
    {
        cancel();var text=leader.MText;
        // The installed getter transfers the native smart pointer into an
        // auto-delete wrapper. Never register/dispose an unexpected resident.
        Need(text==null||text.ObjectId.IsNull,"MLeader.MText returned a resident wrapper; native ownership is unresolved.","API_EXCEPTION");
        return text;
    }
    private static object? DefaultText(MLeaderStyle style,Action cancel)
    {
        cancel();var text=style.DefaultMText;if(text==null)return null;
        // PE: native smart pointer is transferred into MText(ptr,true), as in
        // MLeader.MText. Reject unexpected resident ownership before disposal.
        Need(text.ObjectId.IsNull,"DefaultMText getter returned a resident template; no ownership/disposal is assumed.","API_EXCEPTION");
        using(text)
        {
            using var color=text.Color;using var background=text.BackgroundFillColor;using var xdata=text.XData;
            var budget=new ModelInventoryBudget(cancel);
            var data=xdata==null?null:NativeOwnedMetadata.Buffer(xdata,cancel,item=>budget.Add(item));
            bool columns=text.ColumnType!=ColumnType.NoColumns;
            Need(text.ColumnType is ColumnType.NoColumns or ColumnType.StaticColumns or ColumnType.DynamicColumns,"Native default template column kind is unsupported.");
            var heights=new List<double>();int? staticCount=null;if(text.ColumnType==ColumnType.StaticColumns)
            {
                int count=text.ColumnCount;Need(count>=0,"Native default template column count is invalid.","INVALID_GEOMETRY");
                staticCount=count;
                for(int i=0;i<count;i++){cancel();double height=N(text.GetColumnHeight(i));budget.Add(height);heights.Add(height);}
            }
            string raw=Text(text.Contents,cancel);cancel();string? plain=MTextContent.PlainText(raw);cancel();
            var transparency=text.BackgroundTransparency;
            var result=new{contents=raw,plain_text=plain,has_fields=text.HasFields,text_style_handle=Id(text.TextStyleId),text_height=N(text.TextHeight),
                width=N(text.Width),location=P(text.Location),normal=V(text.Normal),direction=V(text.Direction),attachment=text.Attachment.ToString(),
                line_spacing=N(text.LineSpacingFactor),line_spacing_style=text.LineSpacingStyle.ToString(),flow=text.FlowDirection.ToString(),color=ColorValue(color),
                background=new{text.BackgroundFill,text.UseBackgroundColor,scale=N(text.BackgroundScaleFactor),color=ColorValue(background),
                    transparency=new{alpha=transparency.Alpha,by_alpha=transparency.IsByAlpha,by_layer=transparency.IsByLayer,by_block=transparency.IsByBlock}},
                column_type=text.ColumnType.ToString(),column_width=columns?(double?)N(text.ColumnWidth):null,
                column_gutter=columns?(double?)N(text.ColumnGutterWidth):null,column_auto_height=columns?(bool?)text.ColumnAutoHeight:null,
                column_reversed=columns?(bool?)text.ColumnFlowReversed:null,column_static_count=staticCount,column_heights=heights.ToArray(),
                column_fixed_height=text.ColumnType==ColumnType.DynamicColumns&&!text.ColumnAutoHeight?(double?)N(text.Height):null,
                extension_handle=Id(text.ExtensionDictionary),xdata=data};
            new ModelInventoryBudget(cancel).Add(result);return result;
        }
    }
    internal static JsonElement Values(MLeaderStyle s,Action cancel)
    {
        cancel();using var leader=s.LeaderLineColor;using var text=s.TextColor;using var block=s.BlockColor;
        var value=Wire.Element(new{text_style_handle=Id(s.TextStyleId),text_height=N(s.TextHeight),arrow_size=N(s.ArrowSize),landing_gap=N(s.LandingGap),dogleg_length=N(s.DoglegLength),
            enable_landing=s.EnableLanding,enable_dogleg=s.EnableDogleg,scale=N(s.Scale),leader_linetype_handle=Id(s.LeaderLineTypeId),leader_lineweight_hundredth_mm=(int)s.LeaderLineWeight,
            leader_color=ColorValue(leader),text_color=ColorValue(text),arrow_symbol_handle=Id(s.ArrowSymbolId),content_type=s.ContentType.ToString(),annotative=s.Annotative.ToString(),
            leader_line_type=s.LeaderLineType.ToString(),text_alignment=s.TextAlignmentType.ToString(),text_angle_type=s.TextAngleType.ToString(),
            text_attachment_direction=s.TextAttachmentDirection.ToString(),text_attachment_type=s.TextAttachmentType.ToString(),
            text_attachment_left=s.GetTextAttachmentType(LeaderDirectionType.LeftLeader).ToString(),text_attachment_right=s.GetTextAttachmentType(LeaderDirectionType.RightLeader).ToString(),
            text_align_always_left=s.TextAlignAlwaysLeft,enable_frame_text=s.EnableFrameText,extend_leader_to_text=s.ExtendLeaderToText,break_size=N(s.BreakSize),max_leader_segment_points=s.MaxLeaderSegmentsPoints,
            first_segment_angle=s.FirstSegmentAngleConstraint.ToString(),second_segment_angle=s.SecondSegmentAngleConstraint.ToString(),draw_leader_order=s.DrawLeaderOrderType.ToString(),draw_mleader_order=s.DrawMLeaderOrderType.ToString(),
            block_handle=Id(s.BlockId),block_color=ColorValue(block),block_rotation=N(s.BlockRotation),block_scale=new[]{N(s.BlockScale.X),N(s.BlockScale.Y),N(s.BlockScale.Z)},
            enable_block_rotation=s.EnableBlockRotation,enable_block_scale=s.EnableBlockScale,block_connection=s.BlockConnectionType.ToString(),default_mtext=DefaultText(s,cancel)});
        new ModelInventoryBudget(cancel).Add(value);return value;
    }
    private static JsonElement Metadata(DBObject item,Transaction tr,Action cancel,bool includeOwned=true)
    {
        using var xdata=item.XData;using var reactors=item.GetPersistentReactorIds();var budget=new ModelInventoryBudget(cancel);
        var links=new List<string?>();foreach(ObjectId link in reactors){cancel();budget.Add(Id(link));links.Add(Id(link));}
        var value=Wire.Element(new{handle=Id(item.ObjectId),owner_handle=Id(item.OwnerId),extension_handle=Id(item.ExtensionDictionary),has_fields=item.HasFields,
            xdata=xdata==null?null:NativeOwnedMetadata.Buffer(xdata,cancel,row=>budget.Add(row)),reactors=links.OrderBy(link=>link,StringComparer.Ordinal).ToArray(),
            owned=includeOwned?NativeOwnedMetadata.Capture(item,tr,cancel):Wire.Element(Array.Empty<object>())});
        new ModelInventoryBudget(cancel).Add(value);return value;
    }
    private static Snapshot Capture(MLeaderStyle style,Transaction tr,Action cancel)
    {
        var values=Values(style,cancel);var metadata=Metadata(style,tr,cancel);
        var state=Wire.Element(new{style.Name,values,metadata});new ModelInventoryBudget(cancel).Add(state);
        return new(values,metadata,Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(state,Wire.Json))));
    }
    private static JsonElement Compact(JsonElement values)
    {
        var fields=new Dictionary<string,JsonElement>(StringComparer.Ordinal);
        foreach(string name in new[]{"text_style_handle","text_height","arrow_size","landing_gap","dogleg_length","enable_landing","enable_dogleg","scale",
            "leader_linetype_handle","leader_lineweight_hundredth_mm","leader_color","text_color","arrow_symbol_handle","content_type","annotative","leader_line_type",
            "text_alignment","text_angle_type","text_attachment_direction","text_attachment_type","text_attachment_left","text_attachment_right","enable_frame_text"})fields.Add(name,values.GetProperty(name).Clone());
        var template=values.GetProperty("default_mtext");
        fields.Add("default_mtext",template.ValueKind==JsonValueKind.Null?template.Clone():Wire.Element(new{has_fields=template.GetProperty("has_fields").GetBoolean(),
            column_type=template.GetProperty("column_type").GetString(),content_utf8_bytes=Encoding.UTF8.GetByteCount(template.GetProperty("contents").GetString()!)}));
        return Wire.Element(fields);
    }
    internal static MLeaderStyleRead337 ReadProperties(MLeaderStyle style,string name,Database db,Transaction tr,Action cancel)
    {
        bool local=!name.Contains('|'),native=style.GetType()==typeof(MLeaderStyle)&&style.OwnerId==db.MLeaderStyleDictionaryId;
        bool current=style.ObjectId==db.MLeaderstyle;
        try
        {
            var snapshot=Capture(style,tr,cancel);SourceScope(style,db,tr,cancel);
            var template=snapshot.Values.GetProperty("default_mtext");Need(template.ValueKind==JsonValueKind.Null||!template.GetProperty("has_fields").GetBoolean(),"Field-bearing default template requires its owning adapter.");
            return new("MLeaderStyle",local,native&&local,current,Compact(snapshot.Values),new(snapshot.Token),lifecycle:CadNativeStyleLifecycle352.Describe(style,db,tr,cancel),
                creation_candidate:style.ExtensionDictionary.IsNull);
        }
        catch(OperationCanceledException){throw;}
        catch(Exception error)
        {
            string reason=error is DomainException domain?domain.Error.code+": "+domain.Error.message:"Native complete editing metadata unavailable: "+error.GetType().Name;
            return new("MLeaderStyle",local,native&&local,current,editing_unavailable_reason:reason,lifecycle:CadNativeStyleLifecycle352.Describe(style,db,tr,cancel));
        }
    }
    private static void SourceScope(MLeaderStyle style,Database db,Transaction tr,Action cancel)
    {
        Need(style.GetType()==typeof(MLeaderStyle)&&style.Database==db&&!style.ObjectId.IsNull&&style.OwnerId==db.MLeaderStyleDictionaryId,
            "Use an exact resident local native MLeaderStyle.","STYLE_UNAVAILABLE");
        Need(BlockWriteValidation.OrdinaryName(style.Name)&&style.ContentType==ContentType.MTextContent&&style.Annotative!=AnnotativeStates.True&&!style.HasFields,
            "This default editor retains ordinary non-annotative MText-content styles without fields.");
        using var reactors=style.GetPersistentReactorIds();var seen=new HashSet<ObjectId>();
        foreach(ObjectId reactor in reactors)
        {
            cancel();Need(seen.Add(reactor),"Native style has a repeated persistent dependency.");
            if(CadNativeStyleDependencies349.OwningDictionary(style,style.Name,reactor,db.MLeaderStyleDictionaryId,db,tr,cancel))continue;
            if(!reactor.IsNull&&reactor.IsValid&&!reactor.IsErased&&!reactor.IsEffectivelyErased&&
                tr.GetObject(reactor,OpenMode.ForRead) is MLeader leader&&leader.MLeaderStyle==style.ObjectId&&
                CadNativeStyleDependencies349.OrdinaryConsumer(leader,typeof(MLeader),db,tr,cancel))continue;
            throw DependencyFailure(style,reactor,db,tr,cancel);
        }
        var dictionary=(DBDictionary)tr.GetObject(db.MLeaderStyleDictionaryId,OpenMode.ForRead);
        Need(dictionary.Contains(style.Name)&&dictionary.GetAt(style.Name)==style.ObjectId,"Native style name index/owner is inconsistent.","STYLE_UNAVAILABLE");
    }
    private static DomainException DependencyFailure(MLeaderStyle style,ObjectId reactor,Database db,Transaction tr,Action cancel)
    {
        cancel();bool live=!reactor.IsNull&&reactor.IsValid&&!reactor.IsErased&&!reactor.IsEffectivelyErased;
        DBObject? item=live?tr.GetObject(reactor,OpenMode.ForRead):null;
        string classification=reactor.IsNull?"null":!reactor.IsValid?"invalid":!live?"erased":"unclassified_native_dependency";
        if(item is DBDictionary&&reactor==db.MLeaderStyleDictionaryId)classification="style_dictionary_owner";
        else if(item is MLeader leader&&item.GetType()==typeof(MLeader)&&leader.MLeaderStyle==style.ObjectId&&item.Database?.UnmanagedObject==db.UnmanagedObject)
        {
            var ownerId=item.OwnerId;DBObject? owner=!ownerId.IsNull&&ownerId.IsValid&&!ownerId.IsErased&&!ownerId.IsEffectivelyErased?tr.GetObject(ownerId,OpenMode.ForRead):null;
            classification=owner is BlockTableRecord block&&block.GetType()==typeof(BlockTableRecord)&&block.OwnerId==db.BlockTableId&&
                !block.IsDependent&&!block.IsFromExternalReference&&!block.IsFromOverlayReference?"ordinary_local_mleader_consumer_requires_owning_adapter":"mleader_consumer_with_unclassified_owner";
        }
        cancel();var diagnostic=Wire.Element(new{reactor_handle=reactor.IsNull||!reactor.IsValid?null:Id(reactor),
            runtime_type=item?.GetType().FullName,owner_handle=item==null?null:Id(item.OwnerId),classification});
        if(JsonSerializer.SerializeToUtf8Bytes(diagnostic,Wire.Json).Length>2048)
            diagnostic=Wire.Element(new{classification,details_unavailable_reason="Native dependency descriptor exceeds diagnostic byte allowance."});
        return new DomainException("UNSUPPORTED_MLEADER_STYLE","Unclassified persistent style dependencies require their owning adapter. "+diagnostic.GetRawText(),false,diagnostic);
    }
    private static JsonElement Defaults(Database db)=>Wire.Element(new{layer=Id(db.Clayer),text_style=Id(db.Textstyle),dimension_style=Id(db.Dimstyle),table_style=Id(db.Tablestyle),mleader_style=Id(db.MLeaderstyle),
        db.TileMode,space=Id(db.CurrentSpaceId)});
    private static JsonElement Inventory(ObjectId dictionaryId,Transaction tr,Action cancel)
    {
        var rows=new List<object>();var budget=new ModelInventoryBudget(cancel);
        foreach(DBDictionaryEntry entry in (DBDictionary)tr.GetObject(dictionaryId,OpenMode.ForRead))
        {cancel();var item=new{name=entry.Key,handle=Id(entry.Value)};budget.Add(item);rows.Add(item);}
        return Wire.Element(rows);
    }
    private static ObjectId Reference(string handle,Type type,ObjectId owner,Database db,Transaction tr)
    {
        var id=CadStructureWriter.Find(db,handle);var item=tr.GetObject(id,OpenMode.ForRead);
        Need(item.GetType()==type&&item.OwnerId==owner&&item is SymbolTableRecord symbol&&!symbol.IsDependent&&!symbol.Name.Contains('|'),"Select an existing local native font/linetype record.","STYLE_UNAVAILABLE");
        return id;
    }
    internal static JsonElement Effective(MLeader leader,Action cancel)
    {
        cancel();using var line=leader.LeaderLineColor;using var color=leader.TextColor;using var text=leader.ContentType==ContentType.MTextContent?OwnedText(leader,cancel):null;
        return Wire.Element(new{text_style_handle=Id(text?.TextStyleId??leader.TextStyleId),text_height=N(leader.TextHeight),arrow_size=N(leader.ArrowSize),landing_gap=N(leader.LandingGap),dogleg_length=N(leader.DoglegLength),
            enable_landing=leader.EnableLanding,enable_dogleg=leader.EnableDogleg,scale=N(leader.Scale),leader_linetype_handle=Id(leader.LeaderLineTypeId),leader_lineweight_hundredth_mm=(int)leader.LeaderLineWeight,
            leader_color=ColorValue(line),text_color=ColorValue(color),enable_frame_text=leader.EnableFrameText,text_alignment=leader.TextAlignmentType.ToString(),text_angle_type=leader.TextAngleType.ToString(),
            text_attachment_direction=leader.TextAttachmentDirection.ToString(),text_attachment_type=leader.TextAttachmentType.ToString(),
            text_attachment_left=leader.GetTextAttachmentType(LeaderDirectionType.LeftLeader).ToString(),text_attachment_right=leader.GetTextAttachmentType(LeaderDirectionType.RightLeader).ToString()});
    }
    internal static (MLeaderStyleLeaderRead337? Value,string? Reason) TryReadLeaderProperties(MLeader leader,EntitySummary summary,Action cancel)
    {
        try
        {
            cancel();Need(!leader.MLeaderStyle.IsNull,"Native leader has no resolved style link.","STYLE_UNAVAILABLE");
            return(new(new(summary.@ref.bridge_instance_id,summary.@ref.document_id,Id(leader.MLeaderStyle)!),Effective(leader,cancel)),null);
        }
        catch(OperationCanceledException){throw;}
        catch(Exception error){return(null,error is DomainException domain?domain.Error.code+": "+domain.Error.message:"Native leader style read unavailable: "+error.GetType().Name);}
    }
    internal static object EmbeddedText(MText text,Action cancel)
    {
        using var background=text.BackgroundFillColor;using var xdata=text.XData;var budget=new ModelInventoryBudget(cancel);
        var data=xdata==null?null:NativeOwnedMetadata.Buffer(xdata,cancel,item=>budget.Add(item));var transparency=text.BackgroundTransparency;
        Need(text.ColumnType is ColumnType.NoColumns or ColumnType.StaticColumns or ColumnType.DynamicColumns,"Native embedded MText column kind is unsupported.");
        bool columns=text.ColumnType!=ColumnType.NoColumns;int? staticCount=null;var heights=new List<double>();
        if(text.ColumnType==ColumnType.StaticColumns)
        {
            int count=text.ColumnCount;Need(count>=0,"Native embedded MText column count is invalid.","INVALID_GEOMETRY");staticCount=count;
            for(int index=0;index<count;index++){cancel();double height=N(text.GetColumnHeight(index));budget.Add(height);heights.Add(height);}
        }
        // Font, height, width/location and automatic column heights can derive
        // from style defaults and reflow. Raw content and unrelated stored
        // paragraph/background/column modes remain exact; no template setter.
        var result=new{normal=V(text.Normal),direction=V(text.Direction),attachment=text.Attachment.ToString(),
            line_spacing=N(text.LineSpacingFactor),line_spacing_style=text.LineSpacingStyle.ToString(),flow=text.FlowDirection.ToString(),
            background=new{enabled=text.BackgroundFill,drawing_color=text.UseBackgroundColor,scale=N(text.BackgroundScaleFactor),color=ColorValue(background),
                transparency=new{alpha=transparency.Alpha,by_alpha=transparency.IsByAlpha,by_layer=transparency.IsByLayer,by_block=transparency.IsByBlock}},
            columns=new{type=text.ColumnType.ToString(),automatic_height=columns?(bool?)text.ColumnAutoHeight:null,
                reversed=columns?(bool?)text.ColumnFlowReversed:null,width=columns?(double?)N(text.ColumnWidth):null,gutter=columns?(double?)N(text.ColumnGutterWidth):null,
                static_count=staticCount,static_heights=heights.ToArray(),fixed_height=text.ColumnType==ColumnType.DynamicColumns&&!text.ColumnAutoHeight?(double?)N(text.Height):null},
            extension_handle=Id(text.ExtensionDictionary),xdata=data};
        new ModelInventoryBudget(cancel).Add(result);return result;
    }
    internal static Consumer ConsumerState(MLeader leader,Transaction tr,Action cancel)
    {
        Need(leader.GetType()==typeof(MLeader)&&leader.ContentType==ContentType.MTextContent&&!leader.HasFields&&!leader.EnableAnnotationScale,
            "Used style includes a leader requiring its field/context/content adapter.");
        using var text=OwnedText(leader,cancel);Need(text!=null&&!text.HasFields,"Used native leader field content requires its owning adapter.");
        var topology=new List<object>();var budget=new ModelInventoryBudget(cancel);
        foreach(int branch in leader.GetLeaderIndexes())foreach(int line in leader.GetLeaderLineIndexes(branch))
        {
            cancel();int count=leader.VerticesCount(line);Need(count>=0,"Native leader vertex count is invalid.","INVALID_GEOMETRY");
            var anchors=new List<double[]>();var anchorBudget=new ModelInventoryBudget(cancel);int fixedCount=count>1?count-1:count;
            // Target and explicit middle vertices precede the terminal vertex,
            // whose landing/content connection may reflow under native defaults.
            for(int index=0;index<fixedCount;index++){cancel();var point=P(leader.GetVertex(line,index));anchorBudget.Add(point);anchors.Add(point);}
            var value=new{branch,line,count,anchors};budget.Add(value);topology.Add(value);
        }
        var preserved=Wire.Element(new{handle=Id(leader.ObjectId),owner=Id(leader.OwnerId),style_handle=Id(leader.MLeaderStyle),normal=V(leader.Normal),content_type=leader.ContentType.ToString(),
            contents=Text(text.Contents,cancel),embedded=EmbeddedText(text,cancel),properties=EntityAdapter.StoredProperties(leader),metadata=Metadata(leader,tr,cancel),topology});
        string raw=Text(text.Contents,cancel);cancel();var plain=MTextContent.PlainText(raw);cancel();
        new ModelInventoryBudget(cancel).Add(preserved);return new(leader.ObjectId,preserved,Effective(leader,cancel),CadMLeaderStyleConsumerLayout349.Capture(leader,text,cancel),plain,raw);
    }
    private static Consumer[] Consumers(ObjectId style,Database db,Transaction tr,Action cancel)
    {
        var consumers=new List<Consumer>();var budget=new ModelInventoryBudget(cancel);
        foreach(ObjectId blockId in (BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead))
        {
            cancel();if(blockId.IsErased||blockId.IsEffectivelyErased)continue;
            var block=(BlockTableRecord)tr.GetObject(blockId,OpenMode.ForRead);
            foreach(ObjectId id in block)
            {
                cancel();if(id.IsErased||id.IsEffectivelyErased)continue;
                if(tr.GetObject(id,OpenMode.ForRead) is not MLeader leader||leader.MLeaderStyle!=style)continue;
                Need(leader.OwnerId==blockId&&CadNativeStyleDependencies349.OrdinaryConsumer(leader,typeof(MLeader),db,tr,cancel),
                    "A referring MLeader has custom/external/inconsistent ownership; its owning adapter is required.");
                var consumer=ConsumerState(leader,tr,cancel);budget.Add(new{consumer.Preserved,consumer.Effective,consumer.PreviewLayout,consumer.Plain});consumers.Add(consumer);
            }
        }
        return consumers.ToArray();
    }
    private static BorrowedConsumer[] Borrowed(ObjectId style,Database db,Transaction tr,Action cancel)
    {
        // Creation does not set, recompute, format or clone these leaders. Do
        // not require their Field/content/context edit eligibility just to copy
        // a local source style. Native borrowed links still need actual proof.
        var values=new List<BorrowedConsumer>();var budget=new ModelInventoryBudget(cancel);
        foreach(ObjectId blockId in (BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead))
        {
            cancel();if(blockId.IsErased||blockId.IsEffectivelyErased)continue;
            foreach(ObjectId id in (BlockTableRecord)tr.GetObject(blockId,OpenMode.ForRead))
            {
                cancel();if(id.IsErased||id.IsEffectivelyErased)continue;
                if(tr.GetObject(id,OpenMode.ForRead) is not MLeader leader||leader.MLeaderStyle!=style)continue;
                Need(leader.OwnerId==blockId&&CadNativeStyleDependencies349.OrdinaryConsumer(leader,typeof(MLeader),db,tr,cancel),
                    "A borrowed style consumer lacks its actual exact local owner/style binding.");
                var state=Wire.Element(new{handle=Id(id),owner=Id(leader.OwnerId),style_handle=Id(leader.MLeaderStyle),
                    content_type=leader.ContentType.ToString(),annotation_scale=leader.EnableAnnotationScale,normal=V(leader.Normal),
                    properties=EntityAdapter.StoredProperties(leader),metadata=CadNativeStyleIdentityMetadata352.Metadata(leader,db,tr,cancel),
                    observation_scope="actual_consumer_identity_style_link_properties_and_readable_owned_metadata; content_layout_fields_context_and_opaque_native_internals_not_read_for_style_copy"});
                budget.Add(state);values.Add(new(id,state));
            }
        }
        return values.ToArray();
    }
    private static bool Inline(string contents,Action cancel)
    {
        for(int i=0;i<contents.Length-1;i++){cancel();if(contents[i]!='\\')continue;char c=contents[++i];if(c=='\\'||c is not ('f' or 'F'))continue;
            for(int j=i+1;j<contents.Length;j++){cancel();if(contents[j]==';')return true;if(contents[j] is '{' or '}' or '\\')break;}}
        return false;
    }
    private static void Font(ObjectId id,string? plain,string raw,Transaction tr,Action cancel,List<string> warnings,bool uncertain=false)
    {
        if(id.IsNull){warnings.Add("MLEADER_FONT_COVERAGE_UNVERIFIED: Native default font is unresolved; review rendered text.");return;}
        var style=(TextStyleTableRecord)tr.GetObject(id,OpenMode.ForRead);
        if(style.IsShapeFile||!string.IsNullOrEmpty(style.BigFontFileName)||string.IsNullOrWhiteSpace(style.Font.TypeFace))
        {warnings.Add("MLEADER_FONT_COVERAGE_UNVERIFIED: SHX/big-font coverage requires its native font adapter; review rendered text.");return;}
        bool inline=Inline(raw,cancel);
        try
        {
            FontCoverage.Require(tr,id,string.Empty);cancel();
            if(plain==null){warnings.Add("MLEADER_VISIBLE_TEXT_UNVERIFIED: Retained raw MText cannot be fully decoded by the safe content decoder; raw format codes are not treated as engineering glyphs. Review rendered text.");return;}
            FontCoverage.Require(tr,id,plain);
        }
        catch(DomainException error) when(error.Error.code=="FONT_CHECK_UNAVAILABLE"||(inline||uncertain)&&error.Error.code=="FONT_GLYPHS_MISSING")
        {warnings.Add(error.Error.code+": Changed MLeader default font coverage is not fully verified for retained inline/unknown override runs; review rendered text.");}
        if(inline||uncertain)warnings.Add("MLEADER_FONT_PROVENANCE_UNVERIFIED: Visible text/base-font checks do not prove retained inline fonts or an equal-valued explicit override mask; inspect actual rendered text.");
    }
    internal static Resolved Resolve(StructureWriteArguments args,MLeaderStyleEdit337 edit,Database db,Transaction tr,Action cancel)
    {
        cancel();var id=CadStructureWriter.Find(db,edit.SourceHandle);var source=tr.GetObject(id,OpenMode.ForRead) as MLeaderStyle;
        Need(source!=null,"Expected a native MLeaderStyle base/target.","STYLE_UNAVAILABLE");SourceScope(source!,db,tr,cancel);
        var before=Capture(source!,tr,cancel);Need(before.Token==edit.SourceToken,"Native style changed; refresh its complete editing.style_token.","STYLE_STATE_CHANGED");
        var dictionary=(DBDictionary)tr.GetObject(db.MLeaderStyleDictionaryId,OpenMode.ForRead);bool creating=edit.Operation=="create_mleader_style";
        string name=creating?edit.Name!:source!.Name;
        if(creating){Need(!dictionary.Contains(name),"MLeaderStyle creation does not overwrite an existing name.","STYLE_EXISTS");Need(source!.ExtensionDictionary.IsNull,"Creating a base-owned extension dictionary needs its native ownership-cloning adapter.");}
        else Need(source!.Name==edit.ExpectedName,"Native style name differs from expected_name.","STYLE_NAME_MISMATCH");
        var template=before.Values.GetProperty("default_mtext");Need(template.ValueKind==JsonValueKind.Null||!template.GetProperty("has_fields").GetBoolean(),"Field-bearing default template requires its owning adapter.");
        if(edit.Properties.TextStyleHandle is {} font)Reference(font,typeof(TextStyleTableRecord),db.TextStyleTableId,db,tr);
        if(edit.Properties.LeaderLinetypeHandle is {} linetype)Reference(linetype,typeof(LinetypeTableRecord),db.LinetypeTableId,db,tr);
        return new(edit,db.MLeaderStyleDictionaryId,id,source!.Name,name,new(args.BridgeId,args.DocumentId,edit.SourceHandle),before,Defaults(db),Inventory(db.MLeaderStyleDictionaryId,tr,cancel),
            creating?Array.Empty<Consumer>():Consumers(id,db,tr,cancel),creating?Borrowed(id,db,tr,cancel):Array.Empty<BorrowedConsumer>());
    }
    private static void VerifySource(Resolved source,Database db,Transaction tr,Action cancel)
    {
        var style=(MLeaderStyle)tr.GetObject(source.Source,OpenMode.ForRead);SourceScope(style,db,tr,cancel);
        Need(style.Name==source.SourceName&&Capture(style,tr,cancel).Token==source.Before.Token&&Exact(source.Defaults,Defaults(db))&&
            Exact(source.Inventory,Inventory(source.Dictionary,tr,cancel)),"Native source style/inventory/current defaults changed before apply.","STYLE_STATE_CHANGED");
        VerifyBorrowed(source,db,tr,cancel);
        if(source.Edit.Operation!="create_mleader_style")
        {
            var consumers=Consumers(source.Source,db,tr,cancel);
            Need(consumers.Length==source.Consumers.Length,"Used leader inventory changed before style apply.","STYLE_STATE_CHANGED");
            var current=consumers.ToDictionary(value=>value.Id);
            foreach(var before in source.Consumers)
            {cancel();Need(current.TryGetValue(before.Id,out var actual)&&Exact(before.Preserved,actual.Preserved)&&Exact(before.Effective,actual.Effective)&&Exact(before.PreviewLayout,actual.PreviewLayout),"Used leader state changed before style apply.","STYLE_STATE_CHANGED");}
        }
    }
    private static void VerifyBorrowed(Resolved source,Database db,Transaction tr,Action cancel)
    {
        if(source.Edit.Operation!="create_mleader_style")return;
        var actual=Borrowed(source.Source,db,tr,cancel).ToDictionary(value=>value.Id);
        Need(actual.Count==source.BorrowedConsumers.Length,"Borrowed base-style leader inventory changed.","STYLE_STATE_CHANGED");
        foreach(var before in source.BorrowedConsumers)
        {
            cancel();Need(actual.TryGetValue(before.Id,out var current)&&Exact(before.State,current.State),
                "Native style clone changed a borrowed base leader's identity/style link/properties/readable owned metadata.","STYLE_STATE_CHANGED");
        }
    }
    private static JsonElement DetachBorrowed(MLeaderStyle clone,Resolved source,Database db,Transaction tr,Action cancel)
    {
        var before=Metadata(clone,tr,cancel,includeOwned:false);
        var original=(MLeaderStyle)tr.GetObject(source.Source,OpenMode.ForRead);var allowed=new HashSet<ObjectId>();
        if(CadNativeStyleDependencies349.OwningDictionary(original,original.Name,source.Dictionary,source.Dictionary,db,tr,cancel))
            allowed.Add(source.Dictionary);
        foreach(var value in source.BorrowedConsumers){cancel();allowed.Add(value.Id);}
        foreach(var value in source.Consumers){cancel();allowed.Add(value.Id);}
        using(var links=clone.GetPersistentReactorIds())
        {
            var remove=new List<ObjectId>();var seen=new HashSet<ObjectId>();
            foreach(ObjectId id in links)
            {
                cancel();Need(allowed.Contains(id)&&seen.Add(id),"Detached MLeaderStyle clone contains an unclassified/repeated borrowed reactor.","API_EXCEPTION");
                remove.Add(id);
            }
            foreach(var id in remove){cancel();clone.RemovePersistentReactor(id);}
        }
        var metadata=Metadata(clone,tr,cancel,includeOwned:false);var expected=JsonNode.Parse(before.GetRawText())!;expected["reactors"]=new JsonArray();
        Need(Exact(Wire.Element(expected),metadata)&&!metadata.GetProperty("has_fields").GetBoolean()&&
            Exact(metadata.GetProperty("xdata"),source.Before.Metadata.GetProperty("xdata"))&&
            (source.Edit.Operation!="create_mleader_style"||metadata.GetProperty("extension_handle").ValueKind==JsonValueKind.Null),
            "Detached MLeaderStyle changed non-reactor metadata or retained unclassified create ownership.","API_EXCEPTION");
        VerifySource(source,db,tr,cancel);return metadata;
    }
    private static void Assign(MLeaderStyle s,MLeaderStyleChanges337 p,Database db,Transaction tr,Action cancel)
    {
        Need(p is {TextAttachmentType:null,TextAttachmentLeft:null,TextAttachmentRight:null}||s.TextAttachmentDirection==TextAttachmentDirection.AttachmentHorizontal,
            "Common side attachment editing applies to the actual horizontal MText attachment direction; vertical direction is retained.","OUT_OF_SCOPE");
        if(p.EnableFrameText is {} frame){cancel();s.EnableFrameText=frame;}
        if(p.TextAlignment is {} alignment){cancel();s.TextAlignmentType=Enum.Parse<TextAlignmentType>(alignment);}
        if(p.TextAngleType is {} angle){cancel();s.TextAngleType=Enum.Parse<TextAngleType>(angle);}
        if(p.TextAttachmentType is {} attachment){cancel();s.TextAttachmentType=Enum.Parse<TextAttachmentType>(attachment);}
        if(p.TextAttachmentLeft is {} left){cancel();s.SetTextAttachmentType(Enum.Parse<TextAttachmentType>(left),LeaderDirectionType.LeftLeader);}
        if(p.TextAttachmentRight is {} right){cancel();s.SetTextAttachmentType(Enum.Parse<TextAttachmentType>(right),LeaderDirectionType.RightLeader);}
        if(p.TextStyleHandle is {} font){cancel();s.TextStyleId=Reference(font,typeof(TextStyleTableRecord),db.TextStyleTableId,db,tr);}
        if(p.TextHeight is {} height){cancel();s.TextHeight=height;}if(p.ArrowSize is {} arrow){cancel();s.ArrowSize=arrow;}
        if(p.LandingGap is {} gap){cancel();s.LandingGap=gap;}if(p.DoglegLength is {} dogleg){cancel();s.DoglegLength=dogleg;}
        if(p.EnableLanding is {} landing){cancel();s.EnableLanding=landing;}if(p.EnableDogleg is {} enabled){cancel();s.EnableDogleg=enabled;}
        if(p.Scale is {} scale){cancel();s.Scale=scale;}if(p.LeaderLineweight is {} weight){cancel();s.LeaderLineWeight=(LineWeight)weight;}
        if(p.LeaderLinetypeHandle is {} line){cancel();s.LeaderLineTypeId=Reference(line,typeof(LinetypeTableRecord),db.LinetypeTableId,db,tr);}
        if(p.LeaderColorAci!=null||p.LeaderColorRgb!=null){cancel();using var color=NativeColor(p.LeaderColorAci,p.LeaderColorRgb);s.LeaderLineColor=color;}
        if(p.TextColorAci!=null||p.TextColorRgb!=null){cancel();using var color=NativeColor(p.TextColorAci,p.TextColorRgb);s.TextColor=color;}
    }
    private static Color NativeColor(int? aci,RgbColor? rgb)=>rgb is {} color?Color.FromRgb(color.Red,color.Green,color.Blue):Color.FromColorIndex(Teigha.Colors.ColorMethod.ByAci,(short)aci!.Value);
    private static void VerifyValues(JsonElement before,JsonElement after,MLeaderStyleChanges337 edit)
    {
        var expected=JsonNode.Parse(before.GetRawText())!;
        void Set(string field,object? value){if(value!=null)expected[field]=JsonSerializer.SerializeToNode(value,Wire.Json);}
        Set("enable_frame_text",edit.EnableFrameText);Set("text_alignment",edit.TextAlignment);
        Set("text_angle_type",edit.TextAngleType);
        Set("text_attachment_type",edit.TextAttachmentType);Set("text_attachment_left",edit.TextAttachmentType);Set("text_attachment_right",edit.TextAttachmentType);
        // Installed getter IL selects LeftLeader for horizontal direction.
        Set("text_attachment_type",edit.TextAttachmentLeft);Set("text_attachment_left",edit.TextAttachmentLeft);Set("text_attachment_right",edit.TextAttachmentRight);
        Set("text_style_handle",edit.TextStyleHandle);Set("text_height",edit.TextHeight);Set("arrow_size",edit.ArrowSize);Set("landing_gap",edit.LandingGap);Set("dogleg_length",edit.DoglegLength);
        Set("enable_landing",edit.EnableLanding);Set("enable_dogleg",edit.EnableDogleg);Set("scale",edit.Scale);Set("leader_linetype_handle",edit.LeaderLinetypeHandle);Set("leader_lineweight_hundredth_mm",edit.LeaderLineweight);
        if(edit.LeaderColorAci!=null||edit.LeaderColorRgb!=null){using var color=NativeColor(edit.LeaderColorAci,edit.LeaderColorRgb);Set("leader_color",ColorValue(color));}
        if(edit.TextColorAci!=null||edit.TextColorRgb!=null){using var color=NativeColor(edit.TextColorAci,edit.TextColorRgb);Set("text_color",ColorValue(color));}
        // Native template font/height/color can follow changed style defaults;
        // raw template contents, ownership references and unrequested settings stay.
        if(expected["default_mtext"] is JsonObject template&&after.GetProperty("default_mtext").ValueKind!=JsonValueKind.Null)
        {
            var current=after.GetProperty("default_mtext");
            void Alias(string field,object requested)
            {
                var observed=current.GetProperty(field);var prior=before.GetProperty("default_mtext").GetProperty(field);
                Need(Exact(observed,prior)||Exact(observed,Wire.Element(requested)),"Native template default must retain its prior value or the exact requested style value.","NATIVE_STYLE_MISMATCH");
                template[field]=JsonNode.Parse(observed.GetRawText());
            }
            if(edit.TextStyleHandle!=null)Alias("text_style_handle",edit.TextStyleHandle);
            if(edit.TextHeight!=null)Alias("text_height",edit.TextHeight.Value);
            if(edit.TextColorAci!=null||edit.TextColorRgb!=null){using var color=NativeColor(edit.TextColorAci,edit.TextColorRgb);Alias("color",ColorValue(color));}
            // These are native rendered template attributes associated with
            // the explicitly requested presentation modes, not raw contents,
            // paragraph/background/column controls, ownership or metadata.
            bool Changed(string field,object? request)=>request!=null&&!Exact(before.GetProperty(field),after.GetProperty(field));
            if(Changed("text_angle_type",edit.TextAngleType))template["direction"]=JsonNode.Parse(current.GetProperty("direction").GetRawText());
            if(Changed("text_alignment",edit.TextAlignment)||Changed("text_attachment_left",edit.TextAttachmentType??edit.TextAttachmentLeft)||
                Changed("text_attachment_right",edit.TextAttachmentType??edit.TextAttachmentRight))
                template["attachment"]=JsonNode.Parse(current.GetProperty("attachment").GetRawText());
        }
        Need(Exact(Wire.Element(expected),after),"Native style setter changed unrequested stored defaults/template or failed requested readback.","NATIVE_STYLE_MISMATCH");
    }
    internal static MLeaderStyle Preview(Resolved source,Transaction tr,Database db,List<DBObject> owned,Action cancel)
    {
        VerifySource(source,db,tr,cancel);
        if(source.Consumers.Any(value=>value.PreviewLayout.GetProperty("embedded").GetProperty("columns").GetProperty("type").GetString()=="DynamicColumns"))
            source.Warnings.Add("MLEADER_DYNAMIC_COLUMN_LAYOUT_UNVERIFIED: Stored dynamic column controls and aggregate text placement/size are retained; individual automatic column heights are not independently observed. Inspect the inherited presentation.");
        var style=(MLeaderStyle)tr.GetObject(source.Source,OpenMode.ForRead);var native=style.Clone();
        Need(!ReferenceEquals(native,style),"Native Clone returned the resident source; no setter was called.","API_EXCEPTION");
        if(native is not DBObject detached)throw new DomainException("API_EXCEPTION","Native style clone is not a database object; unproved ownership is not disposed.");
        Need(detached.ObjectId.IsNull&&detached.OwnerId.IsNull&&detached.UnmanagedObject!=IntPtr.Zero&&
            detached.UnmanagedObject!=style.UnmanagedObject&&!owned.Any(value=>value.UnmanagedObject==detached.UnmanagedObject),
            "Native style clone returned a resident/source/owned alias; no setter/disposal was performed.","API_EXCEPTION");
        owned.Add(detached);
        Need(detached.GetType()==typeof(MLeaderStyle),"Native style clone is not the exact detached type.","API_EXCEPTION");
        var clone=(MLeaderStyle)detached;
        Need(Exact(source.Before.Values,Values(clone,cancel)),"Native clone changed unselected source defaults.","API_EXCEPTION");
        var cloneMetadata=DetachBorrowed(clone,source,db,tr,cancel);
        // Name belongs to the resident style dictionary. Keep this preview detached;
        // the native PostMLeaderStyleToDb(db, source.Name) route installs/verifies
        // the requested name once during Apply. A detached name setter can raise OwnerNotSet.
        Assign(clone,source.Edit.Properties,db,tr,cancel);Need(Exact(cloneMetadata,Metadata(clone,tr,cancel,includeOwned:false)),"Preview setter changed detached non-reactor metadata.","API_EXCEPTION");VerifyValues(source.Before.Values,Values(clone,cancel),source.Edit.Properties);
        Coverage(source,clone,tr,cancel,source.Warnings);
        if(source.Edit.Properties is not {TextAngleType:null,TextAlignment:null,TextAttachmentType:null,TextAttachmentLeft:null,TextAttachmentRight:null})
            source.Warnings.Add("MLEADER_NATIVE_PRESENTATION_UNVERIFIED: Exact requested native mode/side defaults are checked; derived template/text attachment and direction are native rendered presentation, not a proved independent layout formula. Raw contents/unrelated stored controls remain protected; review actual callouts.");
        if(source.Consumers.Length>0&&source.Edit.Properties is not {TextHeight:null,ArrowSize:null,LandingGap:null,DoglegLength:null,Scale:null})
            source.Warnings.Add("MLEADER_NUMERIC_INHERITANCE_UNVERIFIED: Existing effective size/scale getters are observed separately from stored defaults; numerical inequality does not prove a literal entity override or its units. Native inherited presentation may change; inspect the useful result.");
        VerifySource(source,db,tr,cancel);return clone;
    }
    private static void Coverage(Resolved source,MLeaderStyle prepared,Transaction tr,Action cancel,List<string> warnings)
    {
        if(source.Edit.Properties.TextStyleHandle==null)return;
        if(Id(prepared.TextStyleId)==source.Before.Values.GetProperty("text_style_handle").GetString())return;
        var font=prepared.TextStyleId;Font(font,string.Empty,string.Empty,tr,cancel,warnings);
        var template=Values(prepared,cancel).GetProperty("default_mtext");
        var oldTemplate=source.Before.Values.GetProperty("default_mtext");
        if(template.ValueKind!=JsonValueKind.Null&&template.GetProperty("text_style_handle").ValueKind==JsonValueKind.String&&
            (oldTemplate.ValueKind==JsonValueKind.Null||!Exact(template.GetProperty("text_style_handle"),oldTemplate.GetProperty("text_style_handle"))))
            Font(CadStructureWriter.Find(prepared.Database??tr.GetObject(source.Source,OpenMode.ForRead).Database,template.GetProperty("text_style_handle").GetString()!),
                template.GetProperty("plain_text").GetString()!,template.GetProperty("contents").GetString()!,tr,cancel,warnings);
        var old=source.Before.Values.GetProperty("text_style_handle");
        foreach(var consumer in source.Consumers)
        {
            cancel();if(old.ValueKind!=JsonValueKind.Null&&!Exact(consumer.Effective.GetProperty("text_style_handle"),old))continue;
            Font(font,consumer.Plain,consumer.Raw,tr,cancel,warnings,uncertain:true);
        }
    }
    internal static StructureChange Apply(int index,Resolved source,MLeaderStyle prepared,Transaction tr,Database db,WriteJob job,List<string>? warnings=null)
    {
        VerifySource(source,db,tr,job.CheckCancellation);var notices=source.Warnings.ToList();Coverage(source,prepared,tr,job.CheckCancellation,notices);
        bool creating=source.Edit.Operation=="create_mleader_style";MLeaderStyle actual;
        if(creating)
        {
            Need(prepared.ObjectId.IsNull&&prepared.OwnerId.IsNull&&prepared.ExtensionDictionary.IsNull,"Prepared new style must be detached without copied foreign ownership.","API_EXCEPTION");
            using(var links=prepared.GetPersistentReactorIds())Need(links.Count==0,"Prepared new style retains borrowed reactors.","API_EXCEPTION");
            using(var data=prepared.XData)
            {
                var budget=new ModelInventoryBudget(job.CheckCancellation);var xdata=data==null?null:NativeOwnedMetadata.Buffer(data,job.CheckCancellation,item=>budget.Add(item));
                Need(Exact(source.Before.Metadata.GetProperty("xdata"),JsonSerializer.SerializeToElement(xdata,Wire.Json)),"Native creation clone lost/changed base XData.","NATIVE_STYLE_MISMATCH");
            }
            job.CheckCancellation();var id=prepared.PostMLeaderStyleToDb(db,source.Name);tr.AddNewlyCreatedDBObject(prepared,true);actual=prepared;
            Need(!id.IsNull&&actual.ObjectId==id&&actual.OwnerId==source.Dictionary&&actual.Name==source.Name,"Native style insertion returned unexpected identity/name/owner.","NATIVE_STYLE_MISMATCH");
            var resident=(MLeaderStyle)tr.GetObject(source.Source,OpenMode.ForRead);Need(Capture(resident,tr,job.CheckCancellation).Token==source.Before.Token,"Creation mutated its base native style.","NATIVE_STYLE_MISMATCH");
            VerifyBorrowed(source,db,tr,job.CheckCancellation);
        }
        else
        {
            actual=(MLeaderStyle)tr.GetObject(source.Source,OpenMode.ForWrite);Assign(actual,source.Edit.Properties,db,tr,job.CheckCancellation);
            // Native graphics invalidation requests a fresh representation;
            // it neither assigns the style link nor resets entity overrides.
            foreach(var consumer in source.Consumers)
            {job.CheckCancellation();((MLeader)tr.GetObject(consumer.Id,OpenMode.ForWrite)).RecordGraphicsModified(true);}
            Need(Exact(source.Before.Metadata,Metadata(actual,tr,job.CheckCancellation)),"Native defaults edit changed style identity/owned metadata.","NATIVE_STYLE_MISMATCH");
            var consumers=Consumers(source.Source,db,tr,job.CheckCancellation);Need(consumers.Length==source.Consumers.Length,"Style edit changed its native consumer inventory.","NATIVE_STYLE_MISMATCH");
            var current=consumers.ToDictionary(value=>value.Id);
            var numericChanges=new Dictionary<string,(int Count,string Handle,JsonElement Before,JsonElement After)>(StringComparer.Ordinal);
            int reflowCount=0;JsonElement? reflowSample=null;
            foreach(var old in source.Consumers)
            {
                job.CheckCancellation();Need(current.TryGetValue(old.Id,out var now),"Style edit lost an actual consumer.","NATIVE_STYLE_MISMATCH");
                foreach(string field in new[]{"text_style_handle","enable_landing","enable_dogleg",
                    "leader_color","text_color","leader_linetype_handle","leader_lineweight_hundredth_mm",
                    "enable_frame_text","text_alignment","text_angle_type","text_attachment_direction","text_attachment_type","text_attachment_left","text_attachment_right"})
                {
                    var baseline=source.Before.Values.GetProperty(field);if(baseline.ValueKind==JsonValueKind.Null||Exact(old.Effective.GetProperty(field),baseline))continue;
                    Need(Exact(old.Effective.GetProperty(field),now!.Effective.GetProperty(field)),"Style edit changed a distinct effective native entity override.","NATIVE_STYLE_MISMATCH");
                }
                // Sparse presentation edits admit only the exact prior effective
                // value or the requested stored value for a consumer matching the
                // prior default. This observes equal-valued overrides honestly;
                // it neither clears them nor invents a complete override mask.
                void Presentation(string field,object? requested)
                {
                    var prior=old.Effective.GetProperty(field);var actual=now!.Effective.GetProperty(field);
                    if(requested==null){Need(Exact(actual,prior),"Native consumer changed an unrequested presentation setting.","NATIVE_STYLE_MISMATCH");return;}
                    if(!Exact(prior,source.Before.Values.GetProperty(field)))return;
                    Need(Exact(actual,prior)||Exact(actual,Wire.Element(requested)),"Native consumer presentation is neither retained nor the exact requested inherited default.","NATIVE_STYLE_MISMATCH");
                }
                var edit=source.Edit.Properties;string? left=edit.TextAttachmentType??edit.TextAttachmentLeft,right=edit.TextAttachmentType??edit.TextAttachmentRight;
                Presentation("enable_frame_text",edit.EnableFrameText);Presentation("text_alignment",edit.TextAlignment);Presentation("text_angle_type",edit.TextAngleType);
                Presentation("text_attachment_direction",null);
                Presentation("text_attachment_type",old.Effective.GetProperty("text_attachment_direction").GetString()=="AttachmentHorizontal"?left:null);
                Presentation("text_attachment_left",left);Presentation("text_attachment_right",right);
                bool Changed(string field,object? requested)=>requested!=null&&!Exact(old.Effective.GetProperty(field),now!.Effective.GetProperty(field))&&
                    Exact(now.Effective.GetProperty(field),Wire.Element(requested));
                bool angleReflow=Changed("text_angle_type",edit.TextAngleType);
                bool attachmentReflow=Changed("text_alignment",edit.TextAlignment)||old.Effective.GetProperty("text_attachment_direction").GetString()=="AttachmentHorizontal"&&
                    (Changed("text_attachment_left",left)||Changed("text_attachment_right",right));
                var retained=JsonNode.Parse(old.Preserved.GetRawText())!;
                if(angleReflow)retained["embedded"]!["direction"]=JsonNode.Parse(now!.Preserved.GetProperty("embedded").GetProperty("direction").GetRawText());
                if(attachmentReflow)retained["embedded"]!["attachment"]=JsonNode.Parse(now!.Preserved.GetProperty("embedded").GetProperty("attachment").GetRawText());
                Need(Exact(Wire.Element(retained),now!.Preserved),"Style edit changed retained content/identity/owner/links/target anchors or unrelated embedded data.","NATIVE_STYLE_MISMATCH");
                if(angleReflow||attachmentReflow)
                {
                    reflowCount++;reflowSample??=Wire.Element(new{handle=Id(old.Id),angle_mode_changed=angleReflow,attachment_default_changed=attachmentReflow,
                        direction_before=old.Preserved.GetProperty("embedded").GetProperty("direction"),direction_after=now.Preserved.GetProperty("embedded").GetProperty("direction"),
                        attachment_before=old.Preserved.GetProperty("embedded").GetProperty("attachment"),attachment_after=now.Preserved.GetProperty("embedded").GetProperty("attachment")});
                }
                foreach(string field in new[]{"text_height","arrow_size","landing_gap","dogleg_length","scale"})
                {
                    var beforeValue=old.Effective.GetProperty(field);var afterValue=now!.Effective.GetProperty(field);if(Exact(beforeValue,afterValue))continue;
                    if(numericChanges.TryGetValue(field,out var observed))numericChanges[field]=(observed.Count+1,observed.Handle,observed.Before,observed.After);
                    else numericChanges.Add(field,(1,Id(old.Id)!,beforeValue.Clone(),afterValue.Clone()));
                }
                if(source.Edit.Properties.TextStyleHandle!=null&&now!.Effective.GetProperty("text_style_handle").ValueKind==JsonValueKind.String&&
                    !Exact(old.Effective.GetProperty("text_style_handle"),now.Effective.GetProperty("text_style_handle")))
                    Font(CadStructureWriter.Find(db,now.Effective.GetProperty("text_style_handle").GetString()!),now.Plain,now.Raw,tr,job.CheckCancellation,notices);
            }
            foreach(var change in numericChanges)
                notices.Add("MLEADER_NUMERIC_INHERITANCE_UNVERIFIED: "+Wire.Element(new{field=change.Key,changed_consumer_count=change.Value.Count,
                    first_observed_handle=change.Value.Handle,before=change.Value.Before,after=change.Value.After}).GetRawText()+
                    "; first actual changed pair is a sample, not a literal override-mask/loss proof. Review inherited rendering.");
            if(reflowCount>0)notices.Add("MLEADER_NATIVE_TEXT_REFLOW_OBSERVED: "+Wire.Element(new{changed_consumer_count=reflowCount,first_sample=reflowSample}).GetRawText()+
                "; native text direction/attachment follow the requested effective presentation mode; no independent layout formula is claimed. Contents/metadata/anchors remain exact; review actual callout.");
            if(source.Consumers.Length>0)notices.Add("MLEADER_OVERRIDE_MASK_UNVERIFIED: Native style links, content and distinct effective overrides remain observed; the managed SDK has no complete stored per-entity override-mask getter. Inherited layout may reflow; inspect the rendered result.");
        }
        VerifyValues(source.Before.Values,Values(actual,job.CheckCancellation),source.Edit.Properties);
        Need(Exact(source.Defaults,Defaults(db)),"Native style operation changed current document defaults/context.","NATIVE_STYLE_MISMATCH");
        var dictionary=(DBDictionary)tr.GetObject(source.Dictionary,OpenMode.ForRead);Need(dictionary.GetAt(source.Name)==actual.ObjectId,"Native style dictionary name index is inconsistent.","NATIVE_STYLE_MISMATCH");
        var expected=JsonNode.Parse(source.Inventory.GetRawText())!.AsArray();if(creating)expected.Add(JsonSerializer.SerializeToNode(new{name=source.Name,handle=Id(actual.ObjectId)},Wire.Json));
        // Native dictionaries may reorder insertion enumeration; names/IDs are
        // exact identities and are compared as complete sorted inventories.
        string[] Order(JsonElement rows)=>rows.EnumerateArray().Select(row=>row.GetRawText()).OrderBy(row=>row,StringComparer.Ordinal).ToArray();
        Need(Exact(Order(Wire.Element(expected)),Order(Inventory(source.Dictionary,tr,job.CheckCancellation))),"Native operation changed unrelated style dictionary entries.","NATIVE_STYLE_MISMATCH");
        warnings?.AddRange(notices.Distinct(StringComparer.Ordinal));
        return new(index,source.Edit.Operation,new(source.Reference.bridge_instance_id,source.Reference.document_id,Id(actual.ObjectId)!),"mleader_style",source.Name,creating);
    }
}
