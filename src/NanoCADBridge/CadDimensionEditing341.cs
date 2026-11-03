using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CadDimensionEditing341
{
    internal const string Warning="Native dimension editing does not drive CAD source geometry or create automatic associations. Automatic label placement may reflow; existing explicit text can mask display settings. Explicit/inherited override behavior after later style changes is not certified. Recovery/Undo is not guaranteed.";
    private static void Need(bool condition,string message,string code="NATIVE_DIMENSION_MISMATCH")
    {if(!condition)throw new DomainException(code,message);}
    private static string? Id(ObjectId id)=>id.IsNull?null:Validation.NormalizeHandle(id.Handle.ToString());
    private static double[] Point(Point3d p)=>new[]{p.X,p.Y,p.Z};
    private static bool Exact(JsonElement a,JsonElement b)=>a.GetRawText()==b.GetRawText();
    private static double Finite(double n)=>double.IsFinite(n)?n:throw new DomainException("DIMENSION_DISPLAY_UNAVAILABLE","A native display value is nonfinite.");
    internal static object ReadDisplay(Dimension d)=>new{
        linear_factor=Finite(d.Dimlfac),linear_unit_format=d.Dimlunit,angular_unit_format=d.Dimaunit,rounding=Finite(d.Dimrnd),
        linear_zero_suppression=d.Dimzin,angular_zero_suppression=d.Dimazin,primary_template=d.Dimpost,
        alternate_enabled=d.Dimalt,alternate_factor=Finite(d.Dimaltf),alternate_unit_format=d.Dimaltu,alternate_precision=d.Dimaltd,
        alternate_rounding=Finite(d.Dimaltrnd),alternate_zero_suppression=d.Dimaltz,alternate_template=d.Dimapost,
        extension_offset=Finite(d.Dimexo),extension_beyond=Finite(d.Dimexe),text_gap=Finite(d.Dimgap),
        suppress_extension1=d.Dimse1,suppress_extension2=d.Dimse2,suppress_dimension1=d.Dimsd1,suppress_dimension2=d.Dimsd2,
        text_movement=d.Dimtmove,automatic_text_position=d.UsingDefaultTextPosition,
        units="native_DIM_variables_no_implicit_conversion_measurement_separate"};
    internal static (object? Display,string? Reason) TryReadDisplay(Dimension d,Action cancel)
    {
        cancel();try{var result=ReadDisplay(d);new ModelInventoryBudget(cancel).Add(result);cancel();return(result,null);}
        catch(DomainException e)when(e.Error.code is "DIMENSION_DISPLAY_UNAVAILABLE" or "RESPONSE_LIMIT_EXCEEDED"){return(null,e.Error.message);}
        catch(Teigha.Runtime.Exception e){return(null,"Native dimension display is unavailable: "+e.ErrorStatus);}
    }
    private static JsonElement Metadata(Dimension d,Transaction tr,Action cancel)
    {
        var owned=NativeOwnedMetadata.Capture(d,tr,cancel);
        foreach(var item in owned.EnumerateArray())
        {cancel();Need(item.GetProperty("common").GetProperty("reactors").GetArrayLength()==0,"Owned dimension dependency data requires its owning adapter.","UNSUPPORTED_DIMENSION_CONTENT");}
        using var data=d.XData;var reservation=new ModelInventoryBudget(cancel);
        using var color=d.Color;
        var value=new{handle=Id(d.ObjectId),owner=Id(d.OwnerId),layer=Id(d.LayerId),d.Visible,
            color=new{method=color.ColorMethod.ToString(),color.ColorIndex,color.Red,color.Green,color.Blue,color.HasColorName,color.ColorName,color.BookName},
            linetype=Id(d.LinetypeId),d.LinetypeScale,weight=(int)d.LineWeight,
            transparency=new{d.Transparency.Alpha,d.Transparency.IsByAlpha,d.Transparency.IsByLayer,d.Transparency.IsByBlock},
            extension=Id(d.ExtensionDictionary),xdata=data==null?null:NativeOwnedMetadata.Buffer(data,cancel,reservation.Add),owned};
        new ModelInventoryBudget(cancel).Add(value);return Wire.Element(value);
    }
    private static JsonElement Style(Dimension d,Database db,Transaction tr,Action cancel)
    {
        cancel();var acquired=d.GetDimstyleData();
        // Installed IL constructs a fresh native record. Prove that ownership
        // before entering disposal; a resident alias is never disposed.
        Need(acquired!=null&&acquired.GetType()==typeof(DimStyleTableRecord)&&acquired.ObjectId.IsNull,
            "Dimension.GetDimstyleData did not return an exact detached record.","UNSAFE_NATIVE_CLONE");
        using var data=acquired!;
        T G<T>(Func<T> native){cancel();T value=native();cancel();return value;}
        object Color(Func<Teigha.Colors.Color> acquire)
        {
            cancel();using var color=acquire();cancel();
            return new{method=color.ColorMethod.ToString(),color.ColorIndex,color.Red,color.Green,color.Blue,
                color.HasColorName,color.ColorName,color.BookName};
        }
        var variables = new DimensionVariables
        {
            dimadec = G(() => data.Dimadec),
            dimalt = G(() => data.Dimalt),
            dimaltd = G(() => data.Dimaltd),
            dimaltf = G(() => data.Dimaltf),
            dimaltrnd = G(() => data.Dimaltrnd),
            dimalttd = G(() => data.Dimalttd),
            dimalttz = G(() => data.Dimalttz),
            dimaltu = G(() => data.Dimaltu),
            dimaltz = G(() => data.Dimaltz),
            dimapost = G(() => data.Dimapost),
            dimarcsym = G(() => data.Dimarcsym),
            dimatfit = G(() => data.Dimatfit),
            dimaunit = G(() => data.Dimaunit),
            dimazin = G(() => data.Dimazin),
            dimcen = G(() => data.Dimcen),
            dimdec = G(() => data.Dimdec),
            dimdle = G(() => data.Dimdle),
            dimdli = G(() => data.Dimdli),
            dimdsep = G(() => data.Dimdsep),
            dimexe = G(() => data.Dimexe),
            dimexo = G(() => data.Dimexo),
            dimfrac = G(() => data.Dimfrac),
            dimfxlen = G(() => data.Dimfxlen),
            dimfxlenon = G(() => data.DimfxlenOn),
            dimgap = G(() => data.Dimgap),
            dimjogang = G(() => data.Dimjogang),
            dimjust = G(() => data.Dimjust),
            dimlfac = G(() => data.Dimlfac),
            dimlim = G(() => data.Dimlim),
            dimlunit = G(() => data.Dimlunit),
            dimpost = G(() => data.Dimpost),
            dimrnd = G(() => data.Dimrnd),
            dimsah = G(() => data.Dimsah),
            dimsd1 = G(() => data.Dimsd1),
            dimsd2 = G(() => data.Dimsd2),
            dimse1 = G(() => data.Dimse1),
            dimse2 = G(() => data.Dimse2),
            dimsoxd = G(() => data.Dimsoxd),
            dimtad = G(() => data.Dimtad),
            dimtdec = G(() => data.Dimtdec),
            dimtfac = G(() => data.Dimtfac),
            dimtfill = G(() => data.Dimtfill),
            dimtih = G(() => data.Dimtih),
            dimtix = G(() => data.Dimtix),
            dimtm = G(() => data.Dimtm),
            dimtmove = G(() => data.Dimtmove),
            dimtofl = G(() => data.Dimtofl),
            dimtoh = G(() => data.Dimtoh),
            dimtol = G(() => data.Dimtol),
            dimtolj = G(() => data.Dimtolj),
            dimtp = G(() => data.Dimtp),
            dimtsz = G(() => data.Dimtsz),
            dimtvp = G(() => data.Dimtvp),
            dimtzin = G(() => data.Dimtzin),
            dimupt = G(() => data.Dimupt),
            dimzin = G(() => data.Dimzin),
        };
        var result=Wire.Element(new{
            dimension_scale=G(()=>data.Dimscale),text_height=G(()=>data.Dimtxt),arrow_size=G(()=>data.Dimasz),variables,
            references=new{arrow=Id(G(()=>data.Dimblk)),first_arrow=Id(G(()=>data.Dimblk1)),second_arrow=Id(G(()=>data.Dimblk2)),
                leader_arrow=Id(G(()=>data.Dimldrblk)),extension1_linetype=Id(G(()=>data.Dimltex1)),extension2_linetype=Id(G(()=>data.Dimltex2)),
                dimension_linetype=Id(G(()=>data.Dimltype)),text_style=Id(G(()=>data.Dimtxsty))},
            colors=new{dimension=Color(()=>data.Dimclrd),extension=Color(()=>data.Dimclre),text=Color(()=>data.Dimclrt),fill=Color(()=>data.Dimtfillclr)},
            lineweights=new{dimension=(int)G(()=>data.Dimlwd),extension=(int)G(()=>data.Dimlwe)}});
        new ModelInventoryBudget(cancel).Add(result);return result;
    }
    // Read-only reuse of the exact existing effective DIM snapshot.
    internal static JsonElement RetainedStyleSnapshot(Dimension dimension,Database db,Transaction tr,Action cancel)
        =>Style(dimension,db,tr,cancel);
    private sealed record State(JsonElement Metadata,JsonElement Style,JsonElement Definition,ObjectId StyleId,Vector3d Normal,
        double Elevation,string Text,double TextRotation,int Movement,bool Automatic,Point3d TextPosition,JsonElement NativeDetails);
    private static State Capture(Dimension d,Database db,Transaction tr,Action cancel,bool metadata)
        =>new(metadata?Metadata(d,tr,cancel):default,Style(d,db,tr,cancel),Wire.Element(DimensionGeometryReader.Definition(d)),d.DimensionStyle,d.Normal,
            d.Elevation,d.DimensionText,d.TextRotation,d.Dimtmove,d.UsingDefaultTextPosition,d.TextPosition,CadExtendedDimensions356.Controls(d));
    private static void Source(Entity source,Transaction tr,Action cancel)
    {
        cancel();Need(source is Dimension&&DimensionGeometryReader.Supported(source.GetType()),"Use one of the nine exact ordinary native dimension kinds.","UNSUPPORTED_DIMENSION_CONTENT");
        var d=(Dimension)source;using var reactors=d.GetPersistentReactorIds();
        Need(!d.HasFields&&reactors.Count==0&&d.Annotative!=AnnotativeStates.True,"Associated, field-bearing or contextual dimensions need their owning adapter.","UNSUPPORTED_DIMENSION_CONTENT");
        Need(!d.DimensionStyle.IsNull&&d.DimensionStyle.IsValid&&!d.DimensionStyle.IsErased,"Dimension style is unavailable.","DIMENSION_STYLE_UNAVAILABLE");
        var record=tr.GetObject(d.DimensionStyle,OpenMode.ForRead);
        Need(record.GetType()==typeof(DimStyleTableRecord)&&!((DimStyleTableRecord)record).IsDependent,"Use a native local dimension style.","DIMENSION_STYLE_UNAVAILABLE");
        Metadata(d,tr,cancel); // Classified exact ownership; no blanket extension-dictionary refusal.
    }
    private static void Fonts(Dimension d,EditSpec edit,Transaction tr,Action cancel,List<string>? warnings)
    {
        if(edit is not DimensionDisplayEdit341 display)return;
        string literal="";foreach(var p in display.Properties.EnumerateObject())if(p.Name.EndsWith("_prefix",StringComparison.Ordinal)||p.Name.EndsWith("_suffix",StringComparison.Ordinal))literal+=p.Value.GetString();
        // Check the ordinary degree/ASCII notation glyphs, never invent a
        // Unicode-prime requirement. Native generated/inline notation still
        // needs visual review; these do not claim complete glyph expansion.
        if((d is LineAngularDimension2 or Point3AngularDimension)&&display.Properties.TryGetProperty("angular_unit_format",out var angular))
            literal+=angular.GetInt32() switch{0=>"°",1=>"°'\"",4=>"NSEW°'\"",_=>""};
        if(display.Properties.TryGetProperty("linear_unit_format",out var linear)&&linear.GetInt32() is 3 or 4)literal+="'\"";
        if(display.Properties.TryGetProperty("alternate_unit_format",out var alternate)&&alternate.GetInt32() is 3 or 4 or 6)literal+="'\"";
        if(display.Properties.TryGetProperty("angular_unit_format",out _)||display.Properties.TryGetProperty("linear_unit_format",out _)||display.Properties.TryGetProperty("alternate_unit_format",out _))
            warnings?.Add("DIMENSION_NOTATION_VISUAL_REVIEW: Some generated engineering symbols and retained inline font runs cannot be completely verified automatically; inspect the rendered dimensions.");
        if(literal.Length==0)return;
        cancel();var id=FontCoverage.DimensionStyle(d);var style=(TextStyleTableRecord)tr.GetObject(id,OpenMode.ForRead);
        if(style.IsShapeFile||!string.IsNullOrEmpty(style.BigFontFileName)||string.IsNullOrWhiteSpace(style.Font.TypeFace))
        {warnings?.Add("FONT_CHECK_UNAVAILABLE: Native SHX/big-font glyphs for requested dimension affixes/notation need visual review.");return;}
        try{FontCoverage.Require(tr,id,literal);}
        catch(DomainException e)when(e.Error.code=="FONT_CHECK_UNAVAILABLE")
        {warnings?.Add("FONT_CHECK_UNAVAILABLE: Requested dimension affixes/notation could not be completely verified; inspect actual rendered text.");}
        cancel();
    }
    internal static void Resolve(Entity source,EditSpec edit,Database db,Transaction tr,Action cancel)
    {
        using var nativeBinding=CadNativeDimensionBinding357.Bind(source);source=nativeBinding.Value;
        Source(source,tr,cancel);var d=(Dimension)source;
        if(edit is DimensionDefinitionEdit341 definition)
        {
            Need(d.GetType().Name==definition.DimensionKind,"dimension_kind differs from the actual native subtype.","PRECONDITION_FAILED");
            Need(d.Normal==Vector3d.ZAxis&&d.Elevation==0,"Definition editing retains an ordinary WCS XY/Z0 plane.","UNSUPPORTED_DIMENSION_CONTENT");
            Need(d is not DiametricDimension||d.UsingDefaultTextPosition||d.Dimtmove==2,"Manual diametric DIMTMOVE0/1 remains outside this route.","UNSUPPORTED_DIMENSION_CONTENT");
            Need(SameDefinition(definition.ExpectedDefinition,Wire.Element(DimensionGeometryReader.Definition(d))),"Read the complete current geometry.definition before editing its anchors.","PRECONDITION_FAILED");
        }
        else Need(edit is DimensionDisplayEdit341,"Unknown native dimension editing operation.","INVALID_ARGUMENT");
        Capture(d,db,tr,cancel,true);Fonts(d,edit,tr,cancel,null);
    }
    internal static void Preview(Entity source,EditSpec edit,Database db,Transaction tr,Action cancel)
    {
        using var nativeBinding=CadNativeDimensionBinding357.Bind(source);source=nativeBinding.Value;
        Resolve(source,edit,db,tr,cancel);var d=(Dimension)source;var before=Capture(d,db,tr,cancel,true);
        using var clone=CadNativeDimensionBinding357.Clone(d);using var cloneBinding=CadNativeDimensionBinding357.Bind(clone);Run((Dimension)cloneBinding.Value,edit,db,tr,cancel,false,false,null);
        Need(Exact(before.Metadata,Metadata(d,tr,cancel))&&Exact(before.Style,Style(d,db,tr,cancel))&&SameDefinition(before.Definition,Wire.Element(DimensionGeometryReader.Definition(d))),"Dimension preview changed its resident source.");
    }
    internal static void Apply(Entity source,EditSpec edit,Database db,Transaction tr,Action cancel,bool regenerate,List<string>? warnings=null)
    {
        using var nativeBinding=CadNativeDimensionBinding357.Bind(source);source=nativeBinding.Value;
        Resolve(source,edit,db,tr,cancel);Fonts((Dimension)source,edit,tr,cancel,warnings);
        Run((Dimension)source,edit,db,tr,cancel,regenerate,true,warnings);
        warnings?.Add(Warning);
    }
    private static string NativeField(string name)=>name switch{
        "linear_factor"=>"dimlfac","linear_unit_format"=>"dimlunit","angular_unit_format"=>"dimaunit","rounding"=>"dimrnd",
        "linear_zero_suppression"=>"dimzin","angular_zero_suppression"=>"dimazin","alternate_enabled"=>"dimalt","alternate_factor"=>"dimaltf",
        "alternate_unit_format"=>"dimaltu","alternate_precision"=>"dimaltd","alternate_rounding"=>"dimaltrnd","alternate_zero_suppression"=>"dimaltz",
        "extension_offset"=>"dimexo","extension_beyond"=>"dimexe","text_gap"=>"dimgap","suppress_extension1"=>"dimse1",
        "suppress_extension2"=>"dimse2","suppress_dimension1"=>"dimsd1","suppress_dimension2"=>"dimsd2",_=>throw new DomainException("INVALID_ARGUMENT","Unknown display field.")};
    private static void Display(Dimension d,DimensionDisplayEdit341 edit,JsonObject expected,Action cancel)
    {
        foreach(var p in edit.Properties.EnumerateObject())
        {
            cancel();var v=p.Value;
            if(p.Name.EndsWith("_prefix",StringComparison.Ordinal)||p.Name.EndsWith("_suffix",StringComparison.Ordinal))continue;
            switch(p.Name)
            {
                case "linear_factor":d.Dimlfac=v.GetDouble();break;case "linear_unit_format":d.Dimlunit=v.GetInt32();break;case "angular_unit_format":d.Dimaunit=v.GetInt32();break;
                case "rounding":d.Dimrnd=v.GetDouble();break;case "linear_zero_suppression":d.Dimzin=v.GetInt32();break;case "angular_zero_suppression":d.Dimazin=v.GetInt32();break;
                case "alternate_enabled":d.Dimalt=v.GetBoolean();break;case "alternate_factor":d.Dimaltf=v.GetDouble();break;case "alternate_unit_format":d.Dimaltu=v.GetInt32();break;
                case "alternate_precision":d.Dimaltd=v.GetInt32();break;case "alternate_rounding":d.Dimaltrnd=v.GetDouble();break;case "alternate_zero_suppression":d.Dimaltz=v.GetInt32();break;
                case "extension_offset":d.Dimexo=v.GetDouble();break;case "extension_beyond":d.Dimexe=v.GetDouble();break;case "text_gap":d.Dimgap=v.GetDouble();break;
                case "suppress_extension1":d.Dimse1=v.GetBoolean();break;case "suppress_extension2":d.Dimse2=v.GetBoolean();break;case "suppress_dimension1":d.Dimsd1=v.GetBoolean();break;case "suppress_dimension2":d.Dimsd2=v.GetBoolean();break;
                default:throw new DomainException("INVALID_ARGUMENT","Unknown dimension display field.");
            }
            expected["variables"]![NativeField(p.Name)]=JsonSerializer.SerializeToNode(DimensionEditingValidation341.NativeDisplayValue(p.Name,v),Wire.Json);
        }
        if(edit.Properties.TryGetProperty("primary_prefix",out var prefix))
        {cancel();string text=prefix.GetString()+"<>"+edit.Properties.GetProperty("primary_suffix").GetString();d.Dimpost=text;expected["variables"]!["dimpost"]=text;}
        if(edit.Properties.TryGetProperty("alternate_prefix",out var alternate))
        {cancel();string text=alternate.GetString()+"<>"+edit.Properties.GetProperty("alternate_suffix").GetString();d.Dimapost=text;expected["variables"]!["dimapost"]=text;}
    }
    private static void Definition(Dimension d,JsonElement value,Action cancel)
    {
        Point3d P(string name){cancel();var p=value.GetProperty(name);return new(p[0].GetDouble(),p[1].GetDouble(),p[2].GetDouble());}
        switch(d)
        {
            case AlignedDimension a:a.XLine1Point=P("xline1_point");a.XLine2Point=P("xline2_point");a.DimLinePoint=P("dimline_point");break;
            case RotatedDimension r:r.XLine1Point=P("xline1_point");r.XLine2Point=P("xline2_point");r.Rotation=value.GetProperty("rotation").GetDouble();r.DimLinePoint=P("dimline_point");break;
            case RadialDimension r:r.Center=P("center");r.ChordPoint=P("chord_point");r.LeaderLength=value.GetProperty("leader_length").GetDouble();break;
            case DiametricDimension a:a.ChordPoint=P("chord_point");a.FarChordPoint=P("far_chord_point");a.LeaderLength=value.GetProperty("leader_length").GetDouble();break;
            case LineAngularDimension2 a:a.XLine1Start=P("xline1_start");a.XLine1End=P("xline1_end");a.XLine2Start=P("xline2_start");a.XLine2End=P("xline2_end");a.ArcPoint=P("arc_point");break;
            case Point3AngularDimension a:a.CenterPoint=P("center_point");a.XLine1Point=P("xline1_point");a.XLine2Point=P("xline2_point");a.ArcPoint=P("arc_point");break;
            case OrdinateDimension o:o.Origin=P("origin");o.DefiningPoint=P("defining_point");o.LeaderEndPoint=P("leader_end_point");o.UsingXAxis=value.GetProperty("using_x_axis").GetBoolean();break;
            default:if(CadExtendedDimensions356.Supported(d.GetType()))CadExtendedDimensions356.SetDefinition(d,value,cancel);else throw new DomainException("UNSUPPORTED_DIMENSION_CONTENT","Unsupported native subtype.");break;
        }
        cancel();
    }
    private sealed record MetadataFingerprint(string path,string? expected_sha256,string? current_sha256);
    private static object MetadataDetails(JsonElement before,JsonElement after,Action cancel)
    {
        // Completed managed snapshots only. No native getters or admission changes.
        const int detailBytes=8192;
        try
        {
            var fields=new List<MetadataFingerprint>();int different=0,bytes=1024;
            bool Equal(JsonElement? first,JsonElement? second)=>first.HasValue==second.HasValue&&
                (!first.HasValue||first.Value.GetRawText()==second!.Value.GetRawText());
            string? Hash(JsonElement? value)
            {
                cancel();if(!value.HasValue)return null;
                var raw=System.Text.Encoding.UTF8.GetBytes(value.Value.GetRawText());cancel();
                var result=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(raw));cancel();return result;
            }
            void Add(string path,JsonElement? first,JsonElement? second)
            {
                cancel();if(Equal(first,second))return;different++;
                var field=new MetadataFingerprint(path,Hash(first),Hash(second));
                int size=JsonSerializer.SerializeToUtf8Bytes(field,Wire.Json).Length+1;
                if((long)bytes+size<=detailBytes){fields.Add(field);bytes+=size;}
            }
            void Walk(string path,JsonElement? first,JsonElement? second)
            {
                cancel();if(Equal(first,second))return;
                Add(path,first,second);
                if(!first.HasValue||!second.HasValue||first.Value.ValueKind!=second.Value.ValueKind)return;
                if(first.Value.ValueKind==JsonValueKind.Object)
                {
                    foreach(var property in first.Value.EnumerateObject())
                    {cancel();Walk(path+"/"+property.Name,property.Value,second.Value.TryGetProperty(property.Name,out var current)?current:(JsonElement?)null);}
                    foreach(var property in second.Value.EnumerateObject())
                    {cancel();if(!first.Value.TryGetProperty(property.Name,out _))Walk(path+"/"+property.Name,null,property.Value);}
                }
                else if(first.Value.ValueKind==JsonValueKind.Array)
                {
                    int count=Math.Max(first.Value.GetArrayLength(),second.Value.GetArrayLength());
                    for(int index=0;index<count;index++)
                    {cancel();Walk(path+"/"+index,index<first.Value.GetArrayLength()?first.Value[index]:null,index<second.Value.GetArrayLength()?second.Value[index]:null);}
                }
            }
            Walk("/metadata",before,after);
            var result=new{diagnostic_scope="completed_dimension_metadata_component_fingerprints_only",
                differing_component_count=different,changed_component_fingerprints=fields.ToArray(),
                omitted_differing_component_count=different-fields.Count,xdata_change=CadDimensionMetadataDiagnostics358.XData(before,after,cancel)};
            if(JsonSerializer.SerializeToUtf8Bytes(result,Wire.Json).Length>32768)
                return new{diagnostic_unavailable_reason="Captured metadata diagnostics exceed their byte budget; no complete changed-field list is claimed."};
            return result;
        }
        catch(OperationCanceledException){throw;}
        catch(System.Exception error)
        {return new{diagnostic_unavailable_reason="Captured metadata diagnostics unavailable: "+error.GetType().Name};}
    }
    private static void Run(Dimension d,EditSpec edit,Database db,Transaction tr,Action cancel,bool regenerate,bool metadata,List<string>? warnings)
    {
        cancel();var before=Capture(d,db,tr,cancel,metadata);var expected=JsonNode.Parse(before.Style.GetRawText())!.AsObject();
        if(edit is DimensionDisplayEdit341 display)Display(d,display,expected,cancel);else Definition(d,((DimensionDefinitionEdit341)edit).Definition,cancel);
        if(regenerate){cancel();d.RecomputeDimensionBlock(true);d.RecordGraphicsModified(true);cancel();}
        Need(Exact(before.NativeDetails,CadExtendedDimensions356.Controls(d)),"Native editing changed unrequested stored arc partial/leader/symbol controls.");
        Need(Exact(Wire.Element(expected),Style(d,db,tr,cancel)),"Native dimension style readback changed a requested or retained effective field.");
        Need(d.DimensionStyle==before.StyleId&&d.Normal==before.Normal&&d.Elevation==before.Elevation&&d.DimensionText==before.Text&&d.TextRotation==before.TextRotation&&d.Dimtmove==before.Movement&&d.UsingDefaultTextPosition==before.Automatic,
            "Native edit changed retained style, plane, literal text override or text movement policy.");
        if(!before.Automatic)Need(d.TextPosition.DistanceTo(before.TextPosition)<=1e-8,"Native edit moved the retained manual text anchor.");
        var actual=Wire.Element(DimensionGeometryReader.Definition(d));
        if(edit is DimensionDefinitionEdit341 requiredGeometry)Need(SameDefinition(requiredGeometry.Definition,actual),"Native dimension edit changed specified defining references or physical dimension placement.");
        if(metadata)
        {
            var actualMetadata=Metadata(d,tr,cancel);
            bool preserved=edit is DimensionDisplayEdit341 requestedDisplay?
                CadDimensionDStyleXData349.MetadataPreserved(before.Metadata,actualMetadata,requestedDisplay.Properties,cancel):
                Exact(before.Metadata,actualMetadata);
            if(!preserved&&regenerate&&edit is DimensionDefinitionEdit341&&DimensionAddedXData358.MetadataPreserved(before.Metadata,actualMetadata,cancel,out var added))
            {preserved=true;if(warnings!=null)DimensionAddedXDataWarning358.Append(warnings,d.Handle.ToString(),added,cancel);}
            if(!preserved)throw new DomainException("NATIVE_DIMENSION_MISMATCH",
                "Native dimension edit changed unrelated root or owned metadata.",false,MetadataDetails(before.Metadata,actualMetadata,cancel));
        }
        if(edit is DimensionDefinitionEdit341 geometry)
        {
            Need(SameDefinition(geometry.Definition,actual),"Native dimension edit changed specified defining references or physical dimension placement.");
            if(!Exact(geometry.Definition,actual))warnings?.Add("DIMENSION_PLACEMENT_NORMALIZED: Native readback normalized the stored point along the same physical dimension line/arc; defining references and physical offset/radius/sector remain.");
        }
        else
        {
            // Display changes can reflow derived annotation points in any
            // retained plane. Protect the actual measured references; the XY
            // physical-placement matcher belongs only to definition editing.
            JsonElement Measured(JsonElement definition)=>Wire.Element(definition.EnumerateObject()
                .Where(p=>p.Name is not("dimline_point" or "arc_point" or "leader_end_point" or "leader_length"))
                .ToDictionary(p=>p.Name,p=>p.Value.Clone()));
            Need(Exact(Measured(before.Definition),Measured(actual)),"Display editing changed a measured native dimension reference.");
            if(!Exact(before.Definition,actual))warnings?.Add("DIMENSION_AUTOMATIC_LAYOUT_OBSERVED: Native dimension annotation placement changed after display formatting; measured references and retained plane remain. Inspect the useful result.");
        }
        if(edit is DimensionDisplayEdit341&&d.DimensionText.Length>0)warnings?.Add("DIMENSION_TEXT_OVERRIDE: Existing explicit native DimensionText is retained and may mask primary/alternate display settings.");
    }
    internal static bool SameDefinition(JsonElement expected,JsonElement actual)=>DimensionEditingValidation341.SameDefinition(expected,actual);
}
