using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// SDK nrxdbgate/database.h:8014-8022 and AttributeReference.MTextAttribute /
// UpdateMTextAttribute. Const definition reads never request regeneration.
internal static class NativeAttributeMText377
{
    internal static bool IsMultiline(DBText source) => source is AttributeReference a ? a.IsMTextAttribute :
        source is AttributeDefinition d && d.IsMTextAttributeDefinition;
    internal static MText? Acquire(DBText source)
    {
        var value=source switch
        {
            AttributeReference a when a.IsMTextAttribute => a.MTextAttribute,
            AttributeDefinition d when d.IsMTextAttributeDefinition => d.MTextAttributeDefinition,
            _ => null
        };
        // Installed getter IL constructs MText(IntPtr,false). ObjectId.IsNull
        // describes database residence, not native ownership of that pointer.
        if(value!=null&&(value.AutoDelete||value.UnmanagedObject==IntPtr.Zero))
        {
            GC.SuppressFinalize(value);
            throw new DomainException("UNSAFE_NATIVE_BINDING","The native attribute getter did not return a live nonowning MText wrapper; it was not adopted or disposed.");
        }
        return value;
    }
    internal static void Release(MText? value)
    {
        if(value==null)return;
        // These are only Acquire wrappers. AutoDelete=false disposal detaches
        // the managed wrapper without inspecting or deleting embedded storage.
        if(value.AutoDelete)
        {
            GC.SuppressFinalize(value);
            throw new DomainException("UNSAFE_NATIVE_BINDING","Borrowed native attribute MText ownership changed; the wrapper was not disposed.");
        }
        value.Dispose();
    }
    private static MText CloneDefinition(MText source)
    {
        var candidate=source.Clone();var clone=candidate as MText;
        if(clone==null||clone.GetType()!=typeof(MText)||!clone.AutoDelete||clone.UnmanagedObject==IntPtr.Zero||
            clone.UnmanagedObject==source.UnmanagedObject||!clone.ObjectId.IsNull||!clone.OwnerId.IsNull)
        {
            if(candidate is Teigha.Runtime.DisposableWrapper rejected&&!ReferenceEquals(rejected,source))GC.SuppressFinalize(rejected);
            throw new DomainException("UNSAFE_NATIVE_CLONE","Native multiline clone did not return a distinct owned detached MText; an unproved alias was not adopted or disposed.");
        }
        return clone;
    }
    private static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)]bool value,string message)
    { if(!value)throw new DomainException("UNSUPPORTED_ATTRIBUTE_TYPE",message); }
    private static double[] Point(Teigha.Geometry.Point3d value)=>[value.X,value.Y,value.Z];
    private static double[] Vector(Teigha.Geometry.Vector3d value)=>[value.X,value.Y,value.Z];
    private static object Color(Teigha.Colors.Color value)=>new{method=value.IsByLayer?"by_layer":value.IsByBlock?"by_block":value.IsByAci?"aci":value.IsByColor?"true_color":value.ColorMethod.ToString(),
        aci=value.IsByAci?(int?)value.ColorIndex:null,rgb=value.IsByColor?new[]{(int)value.Red,(int)value.Green,(int)value.Blue}:null};
    private static object Appearance(MText text,Action check)
    {
        // Same supported const surface as CadAnnotationMirrorEditor340.TextStored.
        // ColumnCount/Height may be derived for auto columns; snapshot only the
        // stored static count/heights or explicit dynamic fixed height.
        using var background=text.BackgroundFillColor;var transparency=text.BackgroundTransparency;
        bool staticColumns=text.ColumnType==ColumnType.StaticColumns;var heights=new List<double>();var budget=new ModelInventoryBudget(check);
        if(staticColumns)for(int index=0;index<text.ColumnCount;index++){check();double height=text.GetColumnHeight(index);budget.Add(height);heights.Add(height);}
        var result=new{background_fill=text.BackgroundFill,use_background_color=text.UseBackgroundColor,background_scale_factor=text.BackgroundScaleFactor,
            background_color=Color(background),background_transparency=new{alpha=transparency.Alpha,by_alpha=transparency.IsByAlpha,by_layer=transparency.IsByLayer,by_block=transparency.IsByBlock},
            column_type=text.ColumnType.ToString(),column_width=text.ColumnWidth,column_gutter_width=text.ColumnGutterWidth,column_auto_height=text.ColumnAutoHeight,
            column_flow_reversed=text.ColumnFlowReversed,explicit_column_count=staticColumns?(int?)text.ColumnCount:null,explicit_column_heights=heights,
            dynamic_fixed_height=text.ColumnType==ColumnType.DynamicColumns&&!text.ColumnAutoHeight?(double?)text.Height:null,
            properties=EntityAdapter.StoredProperties(text)};
        budget.Add(result);return result;
    }
    private static JsonElement Definition(MText text,Action check)
    {
        using var xdata=text.XData;using var reactors=text.GetPersistentReactorIds();var budget=new ModelInventoryBudget(check);
        var links=new List<string?>();foreach(ObjectId link in reactors){check();links.Add(link.IsNull?null:link.Handle.ToString());budget.Add(links[^1]);}
        var result=Wire.Element(new{position=Point(text.Location),normal=Vector(text.Normal),direction=Vector(text.Direction),width=text.Width,text_height=text.TextHeight,
            attachment=text.Attachment.ToString(),line_spacing_style=text.LineSpacingStyle.ToString(),line_spacing_factor=text.LineSpacingFactor,
            flow_direction=text.FlowDirection.ToString(),text_style=text.TextStyleId.IsNull?null:text.TextStyleId.Handle.ToString(),appearance=Appearance(text,check),stored_properties=CadBlockMover.PreservedProperties(text),
            xdata=xdata==null?null:NativeOwnedMetadata.Buffer(xdata,check,budget.Add),reactors=links.OrderBy(value=>value,StringComparer.Ordinal).ToArray(),
            extension=text.ExtensionDictionary.IsNull?null:text.ExtensionDictionary.Handle.ToString()});
        budget.Add(result);return result;
    }
    internal static object? Read(DBText source,Func<ObjectId,EntityRef> reference,Action check)
    {
        if(!IsMultiline(source))return null;
        check();var text=Acquire(source);
        try
        {
            Need(text != null && text.GetType()==typeof(MText),"Native multiline definition is unavailable or custom; no truncated text is returned.");
            new ModelInventoryBudget(check).Add(text.Contents);CadModifier.CheckGeometry(text,check);
            var style=text.TextStyleId;bool styleAvailable=!style.IsNull&&style.IsValid&&!style.IsErased&&!style.IsEffectivelyErased&&source.Database!=null&&
                source.Database.TryGetObjectId(style.Handle,out var nativeStyle)&&nativeStyle==style;
            using var sourceLinks=source.GetPersistentReactorIds();using var textLinks=text.GetPersistentReactorIds();
            return new
            {
                contents=text.Contents,plain_text=MTextContent.PlainText(text.Contents),position=Point(text.Location),normal=Vector(text.Normal),direction=Vector(text.Direction),
                width=text.Width,text_height=text.TextHeight,attachment=text.Attachment.ToString(),line_spacing_style=text.LineSpacingStyle.ToString(),
                line_spacing_factor=text.LineSpacingFactor,flow_direction=text.FlowDirection.ToString(),text_style_ref=styleAvailable?reference(style):null,
                text_style_unavailable_reason=styleAvailable?null:"Native MText style identity is absent or unavailable; no font/effective inheritance is guessed.",appearance=Appearance(text,check),has_fields=source.HasFields||text.HasFields,
                can_replace_literal_contents=source is AttributeReference a && !a.IsConstant && !source.HasFields && !text.HasFields &&
                    source.Annotative!=AnnotativeStates.True && sourceLinks.Count==0 && textLinks.Count==0 && text.ObjectId.IsNull && text.ExtensionDictionary.IsNull && styleAvailable && text.ColumnType==ColumnType.NoColumns && MTextEditContent.IsCanonical(text.Contents)
            };
        }
        finally { Release(text); }
    }
    internal static string Contents(AttributeReference source,Action check)
    {
        check();var text=Acquire(source);try{Need(text != null,"Native multiline definition is unavailable.");return text!.Contents;}
        finally{Release(text);}
    }
    internal static void RequireLiteral(AttributeReference source,Action check)
    {
        check();Need(source.GetType()==typeof(AttributeReference)&&source.IsMTextAttribute&&!source.IsConstant&&!source.HasFields&&source.Annotative!=AnnotativeStates.True,
            "Literal multiline edits require an existing native variable nonannotative occurrence without Fields; no conversion is performed.");
        using var reactors=source.GetPersistentReactorIds();Need(reactors.Count==0,"The occurrence has persistent associations requiring its owning edit route.");
        var text=Acquire(source);
        try
        {
            Need(text != null&&text.GetType()==typeof(MText)&&text.ObjectId.IsNull&&!text.HasFields&&text.ExtensionDictionary.IsNull&&text.ColumnType==ColumnType.NoColumns&&MTextEditContent.IsCanonical(text.Contents),
                "The native multiline definition is not ordinary literal paragraphs; it remains readable, but replacement needs its formatted/Field/column route.");
            using var links=text.GetPersistentReactorIds();Need(links.Count==0,"Embedded MText has persistent dependencies requiring its owning content route.");
            CadModifier.CheckGeometry(source,check);CadModifier.CheckGeometry(text!,check);
        }
        finally { Release(text); }
    }
    internal static void CheckReplacementFont(AttributeReference source,SetAttributeEdit edit,Transaction tx,Action check)
    {
        if(edit.ContentType!="mtext"){FontCoverage.Require(tx,source.TextStyleId,edit.Text);return;}
        var text=Acquire(source);
        try{Need(text != null,"Native multiline font definition is unavailable.");check();FontCoverage.Require(tx,text!.TextStyleId,MTextContent.PlainText(edit.Text)!);}
        finally{Release(text);}
    }
    private static JsonElement Root(AttributeReference source,Transaction tx,Action check)
    {
        using var xdata=source.XData;var budget=new ModelInventoryBudget(check);
        var result=Wire.Element(new{source.Tag,source.Invisible,source.IsConstant,source.IsMTextAttribute,source.LockPositionInBlock,source.IsMirroredInX,source.IsMirroredInY,
            properties=CadBlockMover.PreservedProperties(source),layer=source.LayerId.Handle.ToString(),linetype=source.LinetypeId.Handle.ToString(),
            extension=source.ExtensionDictionary.IsNull?null:source.ExtensionDictionary.Handle.ToString(),xdata=xdata==null?null:NativeOwnedMetadata.Buffer(xdata,check,budget.Add),
            owned_metadata=source.ObjectId.IsNull?(JsonElement?)null:NativeOwnedMetadata.Capture(source,tx,check)});
        budget.Add(result);return result;
    }
    internal static void Preview(AttributeReference source,SetAttributeEdit edit,string? preceding,Action check)
    {
        RequireLiteral(source,check);var current=Acquire(source);MText? replacement=null;
        string original;JsonElement definition;
        try
        {
            Need(current!=null,"Native multiline definition is unavailable.");
            original=current.Contents;
            Need((preceding??original)==edit.ExpectedText,"Stored or preceding multiline Contents differ from expected_text.");
            definition=Definition(current,check);replacement=CloneDefinition(current);
            Need(Definition(replacement,check).GetRawText()==definition.GetRawText(),"Native MText clone changed its stored definition.");
            Release(current);current=null;
            // Native UpdateMTextAttribute runs only on the resident occurrence.
            // Prepare literal Contents on an owned MText copy; never replace or
            // update a detached AttributeReference with database-linked styles.
            replacement.Contents=edit.Text;check();CadModifier.CheckGeometry(replacement,check);
            Need(replacement.Contents==edit.Text&&Definition(replacement,check).GetRawText()==definition.GetRawText(),
                "Native multiline preparation changed unrequested definition/style/placement.");
        }
        finally{Release(current);replacement?.Dispose();GC.KeepAlive(source);}
        var after=Acquire(source);
        try{Need(after!=null&&after.Contents==original&&Definition(after,check).GetRawText()==definition.GetRawText(),
            "Multiline preparation changed the retained source definition.");}
        finally{Release(after);GC.KeepAlive(source);}
    }
    internal static void Apply(AttributeReference source,SetAttributeEdit edit,Transaction tx,Action check)
    {
        Need(!source.ObjectId.IsNull&&source.IsWriteEnabled,"Native multiline replacement requires the actual resident write-open occurrence.");
        RequireLiteral(source,check);var id=source.ObjectId;var owner=source.OwnerId;var root=Root(source,tx,check);
        var current=Acquire(source);JsonElement definition;
        try
        {
            Need(current!=null&&current.Contents==edit.ExpectedText,"Stored multiline Contents differ from expected_text.");
            definition=Definition(current,check);check();
            // The installed getter binds the mutable native MText with
            // AutoDelete=false. Edit only its Contents, then detach that
            // wrapper before update can regenerate embedded native storage.
            // No replacement object is handed to the attribute setter, so
            // no undocumented setter copy/ownership transfer is required.
            current.Contents=edit.Text;
        }
        finally{Release(current);GC.KeepAlive(source);}
        check();source.UpdateMTextAttribute();GC.KeepAlive(source);
        check();RequireLiteral(source,check);var after=Acquire(source);
        try
        {
            Need(after!=null&&after.Contents==edit.Text&&source.ObjectId==id&&source.OwnerId==owner&&
                Root(source,tx,check).GetRawText()==root.GetRawText()&&Definition(after,check).GetRawText()==definition.GetRawText(),
                "Native multiline replacement did not retain its requested Contents, root identity/owner/tag or native MText definition/style/placement.");
        }
        finally{Release(after);GC.KeepAlive(source);}
    }
}
