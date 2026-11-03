using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// Native object formatting, separate from replacement of literal paragraphs.
// No Contents setter, coordinate recreation or analytic text-bound-box oracle.
internal static class CadMTextFormatEditor
{
    internal sealed record Values(ObjectId Style,double Height,double Width,AttachmentPoint Attachment,
        LineSpacingStyle Spacing,double SpacingFactor,Vector3d Direction,Point3d Location,Vector3d Normal);
    internal sealed record Snapshot(Values Values,JsonElement Preserved,JsonElement Columns,JsonElement Style,string Token,string[]? Warnings=null);
    private static void Need([DoesNotReturnIf(false)] bool value,string message,string code="UNSUPPORTED_MTEXT_FORMAT")
    {if(!value)throw new DomainException(code,message);}
    private static double Number(double value)
    {Need(double.IsFinite(value),"Native MText contains a nonfinite format or placement value.","INVALID_GEOMETRY");return value;}
    private static double[] Vector(Vector3d v)=>new[]{Number(v.X),Number(v.Y),Number(v.Z)};
    private static double[] Point(Point3d p)=>new[]{Number(p.X),Number(p.Y),Number(p.Z)};
    private static string Handle(ObjectId id)
    {Need(!id.IsNull&&id.IsValid&&!id.IsErased&&!id.IsEffectivelyErased,"Native MText/style identity is unavailable.");return Validation.NormalizeHandle(id.Handle.ToString());}
    private static string Text(string text,Action cancel)
    {
        cancel();try
        {
            if(new UTF8Encoding(false,true).GetByteCount(text)>8*1024*1024)
                throw new DomainException("RESPONSE_LIMIT_EXCEEDED","Native MText text exceeds the actual8MiB serialized snapshot budget.");
        }
        catch(EncoderFallbackException){throw new DomainException("UNSUPPORTED_MTEXT_FORMAT","Stored native text has malformed Unicode; no replacement or token normalization is made.");}
        return text;
    }
    private static bool Close(double a,double b)=>double.IsFinite(a)&&double.IsFinite(b)&&Math.Abs(a-b)<=1e-12+Math.Max(Math.Abs(a),Math.Abs(b))*1e-12;
    private static bool Same(Vector3d a,Vector3d b)=>Close(a.X,b.X)&&Close(a.Y,b.Y)&&Close(a.Z,b.Z);
    private static bool DirectionMatches(Vector3d a,Vector3d b)=>
        double.IsFinite(a.Length)&&double.IsFinite(b.Length)&&Math.Abs(a.X-b.X)<=1e-10&&Math.Abs(a.Y-b.Y)<=1e-10&&Math.Abs(a.Z-b.Z)<=1e-10;
    private static bool Same(Point3d a,Point3d b)=>Close(a.X,b.X)&&Close(a.Y,b.Y)&&Close(a.Z,b.Z);
    private static object Color(Teigha.Colors.Color color)=>new{method=color.ColorMethod.ToString(),index=color.ColorIndex,
        red=color.Red,green=color.Green,blue=color.Blue};
    private static Values Stored(MText t)=>new(t.TextStyleId,Number(t.TextHeight),Number(t.Width),t.Attachment,
        t.LineSpacingStyle,Number(t.LineSpacingFactor),t.Direction,t.Location,t.Normal);
    internal static Snapshot Capture(MText text,Database db,Transaction tr,Action cancel)
    {
        cancel();Need(text.GetType()==typeof(MText)&&text.Database==db&&!text.ObjectId.IsNull,"Use an exact resident native MText in this drawing.");
        var owner=tr.GetObject(text.OwnerId,OpenMode.ForRead) as BlockTableRecord;
        Need(owner!=null&&owner.IsLayout&&!owner.IsFromExternalReference,"MText must belong to a local Model/Paper layout, not a block definition or external reference.");
        Need(!text.HasFields,"Field-bearing MText needs its owning field snapshot adapter; stored field links are not replaced.");
        Need(text.Annotative!=AnnotativeStates.True,"Annotative MText needs its object-context format adapter; alternate contexts are not overwritten.");
        var values=Stored(text);var budget=new ModelInventoryBudget(cancel);
        _=Vector(values.Normal);_=Vector(values.Direction);_=Point(values.Location);
        Need(values.Height>0&&values.Width>=0&&values.SpacingFactor>0,"Native MText size/spacing is not a valid stored format.");
        Need(double.IsFinite(values.Normal.Length)&&double.IsFinite(values.Direction.Length)&&values.Normal.Length>0&&values.Direction.Length>0&&
            Close(values.Normal.DotProduct(values.Direction),0),"Native text direction is not tangent to its retained plane.","INVALID_GEOMETRY");
        var style=(TextStyleTableRecord)tr.GetObject(values.Style,OpenMode.ForRead);
        Need(style.GetType()==typeof(TextStyleTableRecord)&&style.OwnerId==db.TextStyleTableId,"Native text style owner/type is unavailable.");
        var styleData=Wire.Element(TextStyleMetadataReader.Read(style,cancel));new ModelInventoryBudget(cancel).Add(styleData);
        using var xdata=text.XData;using var reactors=text.GetPersistentReactorIds();
        var dependencies=new List<string>();foreach(ObjectId id in reactors){cancel();budget.Add(Handle(id));dependencies.Add(Handle(id));}
        Need(dependencies.Count==0,"Persistent MText dependencies need their owning adapter; formatting cannot promise their unselected external state.");
        using var background=text.BackgroundFillColor;using var rootColor=text.Color;var transparency=text.BackgroundTransparency;
        var owned=NativeOwnedMetadata.Capture(text,tr,cancel);
        var preserved=Wire.Element(new{handle=Handle(text.ObjectId),owner=Handle(text.OwnerId),runtime_type=text.GetType().FullName,
            layer_handle=Handle(text.LayerId),linetype_handle=Handle(text.LinetypeId),color=Color(rootColor),
            contents=Text(text.Contents,cancel),properties=CadBlockMover.PreservedProperties(text),annotative=(int)text.Annotative,
            xdata=xdata==null?null:NativeOwnedMetadata.Buffer(xdata,cancel,item=>budget.Add(item)),reactors=dependencies.ToArray(),owned,
            flow=text.FlowDirection.ToString(),background=new{enabled=text.BackgroundFill,drawing_color=text.UseBackgroundColor,
                scale=Number(text.BackgroundScaleFactor),color=Color(background),transparency=new{alpha=transparency.Alpha,
                    by_alpha=transparency.IsByAlpha,by_layer=transparency.IsByLayer,by_block=transparency.IsByBlock}}});
        new ModelInventoryBudget(cancel).Add(preserved);
        JsonElement columns;
        if(text.ColumnType==ColumnType.NoColumns)columns=Wire.Element(new{type=text.ColumnType.ToString(),layout_height=Number(text.Height)});
        else
        {
            Need(text.ColumnType is ColumnType.StaticColumns or ColumnType.DynamicColumns,"Unknown column kind requires its native column adapter.");
            int count=text.ColumnCount;Need(count>=0,"Native MText column count is invalid.");
            var heights=new List<double>();for(int i=0;i<count;i++){cancel();double h=Number(text.GetColumnHeight(i));budget.Add(h);heights.Add(h);}
            columns=Wire.Element(new{type=text.ColumnType.ToString(),width=Number(text.ColumnWidth),gutter=Number(text.ColumnGutterWidth),
                automatic_height=text.ColumnAutoHeight,reversed=text.ColumnFlowReversed,
                fixed_height=text.ColumnType==ColumnType.DynamicColumns&&!text.ColumnAutoHeight?(double?)Number(text.Height):null,
                count,heights=heights.ToArray()});
        }
        new ModelInventoryBudget(cancel).Add(columns);
        // Exact native scalar bits in the token, including the whole observed
        // state; rendering/actual bbox is not an availability or equality gate.
        var tokenState=Wire.Element(new{preserved,columns,style=styleData,style_handle=Handle(values.Style),
            height=NativeOwnedMetadata.Value(values.Height),width=NativeOwnedMetadata.Value(values.Width),attachment=values.Attachment.ToString(),
            spacing=values.Spacing.ToString(),spacing_factor=NativeOwnedMetadata.Value(values.SpacingFactor),
            direction=NativeOwnedMetadata.Value(values.Direction),location=NativeOwnedMetadata.Value(values.Location),normal=NativeOwnedMetadata.Value(values.Normal)});
        // Account the complete aggregate once. Child snapshots already have
        // construction checks; counting them again as unrelated records would
        // introduce a smaller accidental owned-metadata/content size limit.
        new ModelInventoryBudget(cancel).Add(tokenState);cancel();string token=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(tokenState,Wire.Json)));
        return new(values,preserved,columns,styleData,token);
    }
    internal static MTextEditingMetadata TryRead(MText text,Action cancel)
    {
        try
        {
            var db=text.Database;var tr=db?.TransactionManager.TopTransaction;
            if(db==null||tr==null)return new(null,false,"Resident MText transaction is unavailable for a complete editing token.");
            return new(Capture(text,db,tr,cancel).Token,true);
        }
        catch(OperationCanceledException){throw;}
        catch(Exception error)
        {
            string reason=error is DomainException domain?domain.Error.code+": "+error.Message:"Native editing snapshot unavailable: "+error.Message;
            // Optional diagnostics must not turn an otherwise readable MText
            // into an oversized protocol frame through a native error string.
            try
            {
                if(new UTF8Encoding(false,true).GetByteCount(reason)>2048)
                    reason=(error is DomainException failure?failure.Error.code:"NATIVE_SNAPSHOT_UNAVAILABLE")+
                        ": Detailed diagnostic exceeds the optional metadata byte budget; base MText geometry remains readable.";
            }
            catch(EncoderFallbackException){reason="Native editing snapshot unavailable: diagnostic contains malformed Unicode.";}
            cancel();return new(null,false,reason);
        }
    }
    private static AttachmentPoint Attachment(string value)=>value switch{
        "top_left"=>AttachmentPoint.TopLeft,"top_center"=>AttachmentPoint.TopCenter,"top_right"=>AttachmentPoint.TopRight,
        "middle_left"=>AttachmentPoint.MiddleLeft,"middle_center"=>AttachmentPoint.MiddleCenter,"middle_right"=>AttachmentPoint.MiddleRight,
        "bottom_left"=>AttachmentPoint.BottomLeft,"bottom_center"=>AttachmentPoint.BottomCenter,"bottom_right"=>AttachmentPoint.BottomRight,
        _=>throw new DomainException("INVALID_ARGUMENT","Unsupported MText attachment anchor.")};
    private static Values Expected(Snapshot before,SetMTextFormatSpec edit,Database db,Transaction tr,Action cancel)
    {
        var p=edit.Properties;var values=before.Values;var style=values.Style;
        if(p.TextStyleHandle is {} handle)
        {
            style=CadStructureWriter.Find(db,handle);
            Need(tr.GetObject(style,OpenMode.ForRead) is TextStyleTableRecord record&&record.GetType()==typeof(TextStyleTableRecord)&&
                record.OwnerId==db.TextStyleTableId&&!record.IsDependent,"Choose an existing local text style by its current handle.","TEXT_STYLE_UNAVAILABLE");
        }
        if(p.Width.HasValue)Need(before.Columns.GetProperty("type").GetString()=="NoColumns","Width editing of column MText needs its column-layout adapter; its other stored format fields remain available.");
        var direction=p.DirectionWcs is {} d?new Vector3d(d.X,d.Y,d.Z):values.Direction;
        Need(Close(direction.DotProduct(values.Normal),0),"direction_wcs must lie in the retained native MText plane.","INVALID_GEOMETRY");
        cancel();return values with{Style=style,Height=p.TextHeight??values.Height,Width=p.Width??values.Width,
            Attachment=p.Attachment==null?values.Attachment:Attachment(p.Attachment),
            Spacing=p.LineSpacingStyle==null?values.Spacing:p.LineSpacingStyle=="exactly"?LineSpacingStyle.Exactly:LineSpacingStyle.AtLeast,
            SpacingFactor=p.LineSpacingFactor??values.SpacingFactor,Direction=direction};
    }
    private static bool InlineFont(string contents,Action cancel)
    {
        for(int i=0;i<contents.Length-1;i++)
        {
            cancel();if(contents[i]!='\\')continue;
            char code=contents[++i];if(code=='\\')continue;
            if(code is not ('f' or 'F'))continue;
            // Native font controls terminate with ';'. Escaped backslashes
            // above remain literal text, not a font-control admission flag.
            int start=i+1;
            for(int j=start;j<contents.Length;j++)
            {
                cancel();if(contents[j]==';')return j>start;
                if(contents[j] is '{' or '}' or '\\')break;
            }
        }
        return false;
    }
    private static string[]? Coverage(MText text,Values expected,Values before,Transaction tr,Action cancel)
    {
        if(expected.Style==before.Style)return null;
        var record=(TextStyleTableRecord)tr.GetObject(expected.Style,OpenMode.ForRead);
        // The native Text getter is prohibited because it can lose whitespace. Decode
        // exact stored Contents without invoking a lossy native text getter.
        string raw=Text(text.Contents,cancel);new ModelInventoryBudget(cancel).Add(raw);
        cancel();string? plain=MTextContent.PlainText(raw);cancel();
        // Prove availability independently of glyph/run analysis; the latter
        // cannot use characters forced into retained inline fonts as evidence
        // that the changed base style will corrupt those characters.
        var notices=new List<string>();bool availabilityVerified=false;
        if(record.IsShapeFile||!string.IsNullOrEmpty(record.BigFontFileName)||string.IsNullOrWhiteSpace(record.Font.TypeFace))
            notices.Add("FONT_CHECK_UNAVAILABLE: SHX/big-font availability and glyph coverage need their native font adapter; review actual rendered text.");
        else
        {
            try{FontCoverage.Require(tr,expected.Style,string.Empty);availabilityVerified=true;}
            catch(DomainException error) when(error.Error.code=="FONT_CHECK_UNAVAILABLE")
            {notices.Add("FONT_CHECK_UNAVAILABLE: Base font availability could not be verified; review actual rendered text.");}
        }
        cancel();bool inline=InlineFont(raw,cancel);
        if(plain==null)
            notices.Add("MTEXT_VISIBLE_TEXT_UNVERIFIED: Stored Contents cannot be fully decoded by the conservative text adapter; raw control codes were not checked as glyphs. Review actual rendered text.");
        else if(!inline&&availabilityVerified)
        {
            try{FontCoverage.Require(tr,expected.Style,plain);}
            catch(DomainException error) when(error.Error.code=="FONT_CHECK_UNAVAILABLE")
            {notices.Add("FONT_CHECK_UNAVAILABLE: Base-style glyph coverage is not fully verified; native contents remain retained. Review actual rendered text.");}
        }
        cancel();
        if(inline)notices.Add("MTEXT_INLINE_FONT_COVERAGE_UNVERIFIED: Retained inline font runs are not verified as base-style glyphs. Review actual rendered text.");
        return notices.Count==0?null:notices.ToArray();
    }
    internal static Snapshot CheckSource(MText text,SetMTextFormatSpec edit,Database db,Transaction tr,Action cancel)
    {
        var before=Capture(text,db,tr,cancel);Need(before.Token==edit.MTextToken,"Stored MText format/content differs from mtext_token; refresh the reader.","MTEXT_STATE_CHANGED");
        var expected=Expected(before,edit,db,tr,cancel);return before with{Warnings=Coverage(text,expected,before.Values,tr,cancel)};
    }
    private static JsonElement StructuralColumns(JsonElement columns)
    {
        if(columns.GetProperty("type").GetString()=="NoColumns")
            return Wire.Element(new{type="NoColumns"}); // Height is an observed reflow envelope here.
        // Dynamic columns determine their count/heights from content/layout.
        // Preserve their explicit width/gutter/height-mode/fixed-height/flow, and observe
        // the new derived layout rather than making count an equality oracle.
        if(columns.GetProperty("type").GetString()!="DynamicColumns")return columns;
        return Wire.Element(columns.EnumerateObject().Where(p=>p.Name is not ("count" or "heights")).ToDictionary(p=>p.Name,p=>p.Value.Clone()));
    }
    internal static MTextFormatChange Apply(MText text,SetMTextFormatSpec edit,Database db,Transaction tr,Action cancel,EntityRef reference)
    {
        var before=CheckSource(text,edit,db,tr,cancel);var target=Expected(before,edit,db,tr,cancel);cancel();
        var expectedStyle=Wire.Element(TextStyleMetadataReader.Read((TextStyleTableRecord)tr.GetObject(target.Style,OpenMode.ForRead),cancel));
        bool noColumns=before.Columns.GetProperty("type").GetString()=="NoColumns";
        if(target.Style!=before.Values.Style)text.TextStyleId=target.Style;
        // A style may supply defaults; restore each unrequested stored entity
        // setting explicitly, retaining its original WCS Location anchor.
        if(!Close(text.TextHeight,target.Height))text.TextHeight=target.Height;
        // Overall column Width follows the native column count. Retain the
        // explicit per-column widths without resetting this derived envelope.
        if(noColumns&&!Close(text.Width,target.Width))text.Width=target.Width;
        if(text.Attachment!=target.Attachment)text.Attachment=target.Attachment;
        if(text.LineSpacingStyle!=target.Spacing)text.LineSpacingStyle=target.Spacing;
        if(!Close(text.LineSpacingFactor,target.SpacingFactor))text.LineSpacingFactor=target.SpacingFactor;
        if(!Same(text.Direction,target.Direction))text.Direction=target.Direction;
        if(!Same(text.Location,target.Location))text.Location=target.Location;
        cancel();var after=Capture(text,db,tr,cancel);var actual=after.Values;
        Need(actual.Style==target.Style&&actual.Height>0&&Close(actual.Height,target.Height)&&(!noColumns||Close(actual.Width,target.Width))&&actual.Attachment==target.Attachment&&
            actual.Spacing==target.Spacing&&actual.SpacingFactor>0&&Close(actual.SpacingFactor,target.SpacingFactor)&&DirectionMatches(actual.Direction,target.Direction)&&
            Same(actual.Normal,target.Normal)&&Same(actual.Location,target.Location),"Native MText setter did not retain requested and unselected format/placement values.","NATIVE_MTEXT_FORMAT_MISMATCH");
        Need(before.Preserved.GetRawText()==after.Preserved.GetRawText()&&
            StructuralColumns(before.Columns).GetRawText()==StructuralColumns(after.Columns).GetRawText()&&expectedStyle.GetRawText()==after.Style.GetRawText(),
            "Native formatting changed raw content, root/owned metadata, background/flow or explicit column settings.","NATIVE_MTEXT_FORMAT_MISMATCH");
        return new(reference,after.Token,new(reference.bridge_instance_id,reference.document_id,Handle(actual.Style)),actual.Height,actual.Width,
            actual.Attachment.ToString(),actual.Spacing.ToString(),actual.SpacingFactor,Vector(actual.Direction),before.Warnings);
    }
}
