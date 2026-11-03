using System.Security.Cryptography;
using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// Real OdDbFcf: native codes are retained, never interpreted as a standard or
// replaced by line/text primitives. Read and mutation have separate admission.
internal static class CadToleranceFrame356
{
    internal const string Warning="Native feature-control-frame codes and glyphs are retained as supplied; this adapter does not select a GD&T standard or validate design tolerances. Inspect native glyphs, readability and placement. No source association is created. Recovery/Undo is not guaranteed.";
    static void Need(bool ok,string code,string message){if(!ok)throw new DomainException(code,message);}
    static string? Id(ObjectId id)=>id.IsNull?null:Validation.NormalizeHandle(id.Handle.ToString());
    static double N(double n)=>double.IsFinite(n)?n:throw new DomainException("INVALID_GEOMETRY","Nonfinite native tolerance-frame geometry.");
    static double[] P(Point3d p)=>[N(p.X),N(p.Y),N(p.Z)];
    static double[] V(Vector3d v)=>[N(v.X),N(v.Y),N(v.Z)];
    static bool Equal(JsonElement a,JsonElement b)=>ModelParameterSnapshot.Equivalent(a,b);
    static object Color(Teigha.Colors.Color c)=>new{method=c.ColorMethod.ToString(),c.ColorIndex,c.Red,c.Green,c.Blue,c.HasColorName,c.ColorName,c.BookName};
    static JsonElement Geometry(FeatureControlFrame f,Action cancel)
    {
        cancel();using var border=f.Dimclrd;using var text=f.Dimclrt;
        var value=Wire.Element(new{codes=f.Text,position=P(f.Location),normal=V(f.Normal),direction=V(f.Direction),
            dimension_style_handle=Id(f.DimensionStyle),text_style_handle=Id(f.TextStyleId),text_height=N(f.Dimtxt),dimension_scale=N(f.Dimscale),text_gap=N(f.Dimgap),
            border_color=Color(border),text_color=Color(text)});
        new ModelInventoryBudget(cancel).Add(value);return value;
    }
    static JsonElement Metadata(FeatureControlFrame f,Transaction tr,Action cancel)
    {
        cancel();using var data=f.XData;using var reactors=f.GetPersistentReactorIds();var budget=new ModelInventoryBudget(cancel);var links=new List<string?>();
        foreach(ObjectId id in reactors){cancel();links.Add(Id(id));budget.Add(links[^1]);}
        var value=Wire.Element(new{handle=Id(f.ObjectId),owner=Id(f.OwnerId),layer=Id(f.LayerId),runtime_type=f.GetType().FullName,
            properties=EntityAdapter.StoredProperties(f),f.HasFields,annotative=f.Annotative.ToString(),
            extension=Id(f.ExtensionDictionary),xdata=data==null?null:NativeOwnedMetadata.Buffer(data,cancel,budget.Add),reactors=links.OrderBy(id=>id,StringComparer.Ordinal),owned=NativeOwnedMetadata.Capture(f,tr,cancel)});
        budget.Add(value);return value;
    }
    static DimStyleTableRecord LocalStyle(ObjectId id,Database db,Transaction tr,Action cancel)
    {
        cancel();Need(!id.IsNull&&id.IsValid&&!id.IsErased&&!id.IsEffectivelyErased,"DIMENSION_STYLE_UNAVAILABLE","Native tolerance dimension style is unavailable.");
        var raw=tr.GetObject(id,OpenMode.ForRead);Need(raw.GetType()==typeof(DimStyleTableRecord)&&raw.OwnerId==db.DimStyleTableId&&raw.Database==db,
            "DIMENSION_STYLE_UNAVAILABLE","Use a resident local dimension style in this drawing.");
        var style=(DimStyleTableRecord)raw;Need(!style.IsDependent,"DIMENSION_STYLE_UNAVAILABLE","Dependent dimension styles need their owning document.");return style;
    }
    static JsonElement NativeStyle(DimStyleTableRecord data,Action cancel)
    {
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
    static JsonElement StyleData(DimStyleTableRecord style,Database db,Transaction tr,Action cancel,bool detached)
    {
        using var xdata=style.XData;using var reactors=style.GetPersistentReactorIds();var budget=new ModelInventoryBudget(cancel);var links=new List<string?>();
        foreach(ObjectId id in reactors){cancel();links.Add(Id(id));}
        var result=Wire.Element(new{defaults=NativeStyle(style,cancel),native_type=style.GetType().FullName,name=style.Name,owner=Id(style.OwnerId),style.IsDependent,style.HasFields,annotative=style.Annotative.ToString(),
            extension=Id(style.ExtensionDictionary),xdata=xdata==null?null:NativeOwnedMetadata.Buffer(xdata,cancel,budget.Add),reactors=links.OrderBy(id=>id,StringComparer.Ordinal),owned=NativeOwnedMetadata.Capture(style,tr,cancel)});
        budget.Add(result);return result;
    }
    static JsonElement Effective(FeatureControlFrame f,Database db,Transaction tr,Action cancel)
    {
        cancel();var acquired=f.GetDimstyleData();
        // Installed PE constructs a fresh record before the actual FCF getter.
        Need(acquired!=null&&acquired.GetType()==typeof(DimStyleTableRecord)&&acquired.ObjectId.IsNull,"UNSAFE_NATIVE_CLONE","FCF.GetDimstyleData must return a detached record.");
        using var data=acquired!;return NativeStyle(data,cancel);
    }
    static JsonElement Snapshot(FeatureControlFrame f,Database db,Transaction tr,Action cancel)
    {
        var result=Wire.Element(new{geometry=Geometry(f,cancel),effective=Effective(f,db,tr,cancel),metadata=Metadata(f,tr,cancel),
            shared_style=StyleData(LocalStyle(f.DimensionStyle,db,tr,cancel),db,tr,cancel,false)});new ModelInventoryBudget(cancel).Add(result);return result;
    }
    static void Source(FeatureControlFrame f,Database db,Transaction tr,Action cancel)
    {
        cancel();Need(f.GetType()==typeof(FeatureControlFrame),"UNSUPPORTED_TOLERANCE_CONTENT","Use the exact native FeatureControlFrame.");
        using var reactors=f.GetPersistentReactorIds();Need(!f.HasFields&&reactors.Count==0&&f.Annotative!=AnnotativeStates.True,"UNSUPPORTED_TOLERANCE_CONTENT","Associated, field-bearing or annotative frames need their owning adapter.");
        Need(f.Normal==Vector3d.ZAxis&&Math.Abs(f.Location.Z)<=1e-8&&Math.Abs(f.Direction.Z)<=1e-8&&Math.Abs(f.Direction.Length-1)<=1e-8,
            "UNSUPPORTED_TOLERANCE_CONTENT","This edit retains the ordinary WCS/sheet XY oriented frame.");
        LocalStyle(f.DimensionStyle,db,tr,cancel);var owned=NativeOwnedMetadata.Capture(f,tr,cancel);
        foreach(var item in owned.EnumerateArray()){cancel();Need(item.GetProperty("common").GetProperty("reactors").GetArrayLength()==0,"UNSUPPORTED_TOLERANCE_CONTENT","Owned dependency data needs its owning adapter.");}
    }
    static string Token(JsonElement snapshot)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(snapshot,Wire.Json)));
    internal static ToleranceFrameRead356 Read(FeatureControlFrame f,EntityRef reference,Transaction tr,Action cancel)
    {
        var geometry=Geometry(f,cancel);string? token=null,reason=null;
        try{Source(f,f.Database,tr,cancel);token=Token(Snapshot(f,f.Database,tr,cancel));}
        catch(DomainException error)when(error.Error.code is "UNSUPPORTED_TOLERANCE_CONTENT" or "UNSUPPORTED_OWNED_METADATA" or "DIMENSION_STYLE_UNAVAILABLE")
        {reason=error.Error.message;}
        var result=new ToleranceFrameRead356("FeatureControlFrame",geometry,token!=null,token,reason,
            "stored_native_codes_oriented_frame_and_effective_settings; no_standard_interpretation_or_association");new ModelInventoryBudget(cancel).Add(result);return result;
    }
    internal static FeatureControlFrame Create(ToleranceSpec356 spec,ObjectId style)
    {
        var frame=new FeatureControlFrame(spec.Codes,new(spec.Position.X,spec.Position.Y,0),Vector3d.ZAxis,new(Math.Cos(spec.Rotation),Math.Sin(spec.Rotation),0));
        try{Need(frame.ObjectId.IsNull,"UNSAFE_NATIVE_CLONE","New FCF must be detached.");frame.DimensionStyle=style;return frame;}
        catch{frame.Dispose();throw;}
    }
    internal static void VerifyCreated(FeatureControlFrame f,ToleranceSpec356 spec,ObjectId style,Action cancel)
    {
        cancel();Need(f.GetType()==typeof(FeatureControlFrame)&&f.Text==spec.Codes&&f.DimensionStyle==style&&f.Normal==Vector3d.ZAxis&&
            f.Location.DistanceTo(new(spec.Position.X,spec.Position.Y,0))<=1e-8&&(f.Direction-new Vector3d(Math.Cos(spec.Rotation),Math.Sin(spec.Rotation),0)).Length<=1e-8,
            "NATIVE_TOLERANCE_MISMATCH","Native tolerance frame did not retain supplied codes/style/oriented placement.");
    }
    internal static void Resolve(FeatureControlFrame f,ToleranceEdit356 edit,Database db,Transaction tr,Action cancel)
    {
        Source(f,db,tr,cancel);Need(Token(Snapshot(f,db,tr,cancel))==edit.Token,"TOLERANCE_STATE_CHANGED","Read the complete current tolerance_token before editing.");
        if(edit.Properties.TryGetProperty("dimension_style_handle",out var style))LocalStyle(CadStructureWriter.Find(db,style.GetString()!),db,tr,cancel);
    }
    static void Set(FeatureControlFrame f,ToleranceEdit356 edit,Database db,Transaction tr,Action cancel)
    {
        if(edit.Properties.TryGetProperty("dimension_style_handle",out var requestedStyle))f.DimensionStyle=LocalStyle(CadStructureWriter.Find(db,requestedStyle.GetString()!),db,tr,cancel).ObjectId;
        foreach(var item in edit.Properties.EnumerateObject())
        {
            cancel();var v=item.Value;
            switch(item.Name){case "codes":f.Text=v.GetString()!;break;case "position":f.Location=new(v[0].GetDouble(),v[1].GetDouble(),0);break;
                case "rotation":double a=v.GetDouble();f.SetOrientation(Vector3d.ZAxis,new(Math.Cos(a),Math.Sin(a),0));break;
                case "dimension_style_handle":break;
                case "text_height":f.Dimtxt=v.GetDouble();break;case "dimension_scale":f.Dimscale=v.GetDouble();break;case "text_gap":f.Dimgap=v.GetDouble();break;
                default:throw new DomainException("INVALID_ARGUMENT","Unknown native tolerance-frame setting.");}
        }
    }
    static JsonElement Styles(FeatureControlFrame f,ToleranceEdit356 edit,Database db,Transaction tr,Action cancel)
    {
        var ids=new HashSet<ObjectId>{f.DimensionStyle};if(edit.Properties.TryGetProperty("dimension_style_handle",out var h))ids.Add(CadStructureWriter.Find(db,h.GetString()!));
        return Wire.Element(ids.OrderBy(id=>Id(id),StringComparer.Ordinal).Select(id=>new{handle=Id(id),state=StyleData(LocalStyle(id,db,tr,cancel),db,tr,cancel,false)}).ToArray());
    }
    static bool StylesPreserved(JsonElement styles,Database db,Transaction tr,Action cancel)
    {
        foreach(var style in styles.EnumerateArray()){cancel();if(!Equal(style.GetProperty("state"),StyleData(LocalStyle(CadStructureWriter.Find(db,style.GetProperty("handle").GetString()!),db,tr,cancel),db,tr,cancel,false)))return false;}
        return true;
    }
    static void Requested(FeatureControlFrame f,ToleranceEdit356 edit,Database db,Action cancel)
    {
        bool Near(double a,double b)=>double.IsFinite(a)&&double.IsFinite(b)&&Math.Abs(a-b)<=1e-8;
        foreach(var item in edit.Properties.EnumerateObject())
        {
            cancel();var v=item.Value;bool same=item.Name switch{
                "codes"=>f.Text==v.GetString(),"position"=>f.Location.DistanceTo(new(v[0].GetDouble(),v[1].GetDouble(),0))<=1e-8,
                "rotation"=>(f.Direction-new Vector3d(Math.Cos(v.GetDouble()),Math.Sin(v.GetDouble()),0)).Length<=1e-8,
                "dimension_style_handle"=>f.DimensionStyle==CadStructureWriter.Find(db,v.GetString()!),
                "text_height"=>Near(f.Dimtxt,v.GetDouble()),"dimension_scale"=>Near(f.Dimscale,v.GetDouble()),"text_gap"=>Near(f.Dimgap,v.GetDouble()),_=>false};
            Need(same,"NATIVE_TOLERANCE_MISMATCH","Native frame did not retain the requested "+item.Name+".");
        }
    }
    internal static ToleranceFrameChange356 Preview(FeatureControlFrame source,ToleranceEdit356 edit,Database db,Transaction tr,Action cancel)
    {
        Resolve(source,edit,db,tr,cancel);var before=Snapshot(source,db,tr,cancel);var styles=Styles(source,edit,db,tr,cancel);
        using var clone=CadAnnotationMirrorEditor340.Detached(source);Set((FeatureControlFrame)clone,edit,db,tr,cancel);
        Requested((FeatureControlFrame)clone,edit,db,cancel);Geometry((FeatureControlFrame)clone,cancel);Effective((FeatureControlFrame)clone,db,tr,cancel);
        Need(Equal(before,Snapshot(source,db,tr,cancel))&&StylesPreserved(styles,db,tr,cancel),"NATIVE_TOLERANCE_MISMATCH","Detached preview changed its resident source or shared style.");
        // Private byte-size estimate only; these placeholder refs/tokens are
        // never published as native identities or committed observations.
        var reference=new EntityRef("00000000-0000-0000-0000-000000000001","00000000-0000-0000-0000-000000000001",Id(source.ObjectId)!);
        return new(0,reference,Read(source,reference,tr,cancel),new("FeatureControlFrame",Geometry((FeatureControlFrame)clone,cancel),true,new string('0',64),null,"stored_native_codes_oriented_frame_and_effective_settings; no_standard_interpretation_or_association"));
    }
    internal static ToleranceFrameChange356 Apply(int index,FeatureControlFrame f,ToleranceEdit356 edit,Database db,Transaction tr,EntityRef reference,Action cancel)
    {
        Resolve(f,edit,db,tr,cancel);var before=Snapshot(f,db,tr,cancel);var beforeRead=Read(f,reference,tr,cancel);var styles=Styles(f,edit,db,tr,cancel);
        using var clone=CadAnnotationMirrorEditor340.Detached(f);Set((FeatureControlFrame)clone,edit,db,tr,cancel);
        Requested((FeatureControlFrame)clone,edit,db,cancel);var expectedGeometry=Geometry((FeatureControlFrame)clone,cancel);var expectedEffective=Effective((FeatureControlFrame)clone,db,tr,cancel);
        Need(Equal(before,Snapshot(f,db,tr,cancel))&&StylesPreserved(styles,db,tr,cancel),"NATIVE_TOLERANCE_MISMATCH","Native preview changed its source.");
        Set(f,edit,db,tr,cancel);f.RecordGraphicsModified(true);Requested(f,edit,db,cancel);var after=Snapshot(f,db,tr,cancel);
        Need(Equal(expectedGeometry,after.GetProperty("geometry"))&&Equal(expectedEffective,after.GetProperty("effective"))&&
            Equal(before.GetProperty("metadata"),after.GetProperty("metadata"))&&StylesPreserved(styles,db,tr,cancel),"NATIVE_TOLERANCE_MISMATCH","Native frame editing changed unrequested geometry/effective settings/root/owned metadata.");
        var previousStyle=LocalStyle(CadStructureWriter.Find(db,before.GetProperty("geometry").GetProperty("dimension_style_handle").GetString()!),db,tr,cancel);
        Need(Equal(before.GetProperty("shared_style"),StyleData(previousStyle,db,tr,cancel,false)),"NATIVE_TOLERANCE_MISMATCH","Frame editing changed its shared source style.");
        return new(index,reference,beforeRead,Read(f,reference,tr,cancel));
    }
    internal sealed record CommitState(ToleranceFrameChange356[] Changes,JsonElement Styles);
    internal static CommitState CaptureCommitted(ToleranceFrameChange356[] changes,Database db,Transaction tr,Action cancel)
    {
        var ids=changes.SelectMany(c=>new[]{c.before.geometry.GetProperty("dimension_style_handle").GetString()!,c.after.geometry.GetProperty("dimension_style_handle").GetString()!}).Distinct(StringComparer.OrdinalIgnoreCase);
        var styles=Wire.Element(ids.OrderBy(h=>h,StringComparer.Ordinal).Select(h=>new{handle=h,state=StyleData(LocalStyle(CadStructureWriter.Find(db,h),db,tr,cancel),db,tr,cancel,false)}).ToArray());
        new ModelInventoryBudget(cancel).Add(styles);return new(changes,styles);
    }
    internal static void VerifyCommitted(CommitState expected,Database db,Action cancel)
    {
        using var tr=db.TransactionManager.StartTransaction();
        Need(StylesPreserved(expected.Styles,db,tr,cancel),"NATIVE_TOLERANCE_MISMATCH","Committed frame editing changed an observed shared style.");
        foreach(var change in expected.Changes){cancel();var f=tr.GetObject(CadStructureWriter.Find(db,change.source_ref.handle),OpenMode.ForRead) as FeatureControlFrame;
            Need(f!=null,"NATIVE_TOLERANCE_MISMATCH","Committed native frame is unavailable.");var after=Read(f!,change.source_ref,tr,cancel);
            Need(after.tolerance_token!=null&&after.tolerance_token==change.after.tolerance_token,"NATIVE_TOLERANCE_MISMATCH","Committed native frame differs from its checked exact state.");}
    }
}
