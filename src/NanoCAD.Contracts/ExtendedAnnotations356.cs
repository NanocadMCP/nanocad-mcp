using System.Text;
using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record ArcLengthDimensionSpec356(Point2 Center,Point2 First,Point2 Second,Point2 ArcPoint,string DimensionStyle,string TextOverride,string LayerName)
    :EntitySpec("Dimension",LayerName),IDimensionCreation;
public sealed record JoggedRadiusDimensionSpec356(Point2 Center,Point2 ChordPoint,Point2 OverrideCenter,Point2 JogPoint,double JogAngle,string DimensionStyle,string TextOverride,string LayerName)
    :EntitySpec("Dimension",LayerName),IDimensionCreation;
public sealed record ToleranceSpec356(string Codes,Point2 Position,double Rotation,string DimensionStyle,string LayerName):EntitySpec("Tolerance",LayerName);
public sealed record ToleranceEdit356(string[] Targets,string Token,JsonElement Properties):EditSpec("set_tolerance",Targets);

public sealed record ToleranceFrameRead356(string annotation_kind,JsonElement geometry,bool editable,string? tolerance_token,string? unavailable_reason,string observation_scope);
public sealed record ToleranceFrameChange356(int operation_index,EntityRef source_ref,ToleranceFrameRead356 before,ToleranceFrameRead356 after);

public static class ExtendedAnnotationValidation356
{
    private static DomainException Bad(string path,string message)=>new("INVALID_ARGUMENT",path+": "+message);
    private static JsonElement R(JsonElement v,string n,string p)=>v.TryGetProperty(n,out var r)?r:throw Bad(p+"."+n,"Required field.");
    private static void Shape(JsonElement v,string p,params string[] fields)
    {if(v.ValueKind!=JsonValueKind.Object)throw Bad(p,"Expected object.");var seen=new HashSet<string>();foreach(var f in v.EnumerateObject())if(!seen.Add(f.Name)||!fields.Contains(f.Name,StringComparer.Ordinal))throw Bad(p+"."+f.Name,"Unknown/duplicate field.");}
    public static string Codes(JsonElement value,string path)
    {
        if(value.ValueKind!=JsonValueKind.String)throw Bad(path,"Expected native feature-control-frame codes.");
        string text=value.GetString()!;try{new UTF8Encoding(false,true).GetByteCount(text);}catch(EncoderFallbackException){throw Bad(path,"Invalid Unicode.");}
        if(text.Length==0||text.Contains("%<")||text.Any(c=>char.IsControl(c)&&c is not '\n' and not '\r'))throw Bad(path,"Use nonempty native feature-control-frame text/codes without fields or control bytes; native rows/newlines are permitted.");
        return text;
    }
    private static string Text(JsonElement v,string p){if(v.ValueKind!=JsonValueKind.String||string.IsNullOrWhiteSpace(v.GetString())||v.GetString()!.Any(char.IsControl))throw Bad(p,"Expected visible native style/token text.");return v.GetString()!;}
    private static double Number(JsonElement v,string p,bool positive=false)
    {if(v.ValueKind!=JsonValueKind.Number||!v.TryGetDouble(out var n)||!double.IsFinite(n)||Math.Abs(n)>1e9||positive&&n<=0)throw Bad(p,"Expected finite native value within +/-1e9 and declared sign.");return n;}
    private static Point2 Point(JsonElement v,string p)
    {if(v.ValueKind!=JsonValueKind.Array||v.GetArrayLength()!=2)throw Bad(p,"Use WCS/sheet XY millimeters.");return new(Number(v[0],p),Number(v[1],p));}
    public static void ArcGeometry(Point2 center,Point2 first,Point2 second,Point2 arc,string path)
    {
        double radius=AngularDimensionGeometry.Length(center,first),other=AngularDimensionGeometry.Length(center,second);
        double angle=AngularDimensionGeometry.Turn(AngularDimensionGeometry.Difference(first,center),AngularDimensionGeometry.Difference(second,center));
        if(radius<=1e-7||Math.Abs(radius-other)>1e-7||angle<=1e-8||angle>=2*Math.PI-1e-8||AngularDimensionGeometry.Length(center,arc)<=1e-7||
           !AngularDimensionGeometry.InSector(center,AngularDimensionGeometry.Difference(first,center),arc,angle))throw Bad(path,"Use distinct equal-radius circular endpoints ordered counterclockwise and an arc placement point strictly inside that selected sector.");
    }
    public static void JogGeometry(Point2 center,Point2 chord,Point2 fake,Point2 jog,double angle,string path)
    {if(AngularDimensionGeometry.Length(center,chord)<=1e-7||AngularDimensionGeometry.Length(fake,jog)<=1e-7||angle<=0||angle>=Math.PI)throw Bad(path,"Use a nonzero native radius, distinct override-center/jog points and jog_angle radians strictly between0 and pi.");}
    public static EditSpec ParseTolerance(JsonElement v,string path,string[] handles)
    {
        Shape(v,path,"operation","handles","tolerance_token","properties");if(handles.Length!=1)throw Bad(path,"Address one exact native feature-control frame.");
        string token=Text(R(v,"tolerance_token",path),path);if(token.Length!=64||token.Any(c=>!Uri.IsHexDigit(c)))throw Bad(path,"Read the complete current tolerance_token.");
        var p=R(v,"properties",path);Shape(p,path+".properties","codes","position","rotation","dimension_style_handle","text_height","dimension_scale","text_gap");
        if(!p.EnumerateObject().Any())throw Bad(path,"Request at least one native frame property.");
        foreach(var f in p.EnumerateObject())switch(f.Name)
        {case "codes":Codes(f.Value,path);break;case "position":Point(f.Value,path);break;case "dimension_style_handle":Validation.NormalizeHandle(Text(f.Value,path));break;
         case "text_height":case "dimension_scale":Number(f.Value,path,true);break;default:Number(f.Value,path);break;}
        return new ToleranceEdit356(handles,token.ToUpperInvariant(),p.Clone());
    }
}

public static partial class CreateValidation
{
    private static EntitySpec ParseExtendedDimension356(JsonElement item,string kind,string path,string style,string text,string layer)
    {
        if(!DimensionTextContent.PreservesMeasurement(text))throw Invalid(path,"Retain one <> measurement marker with ordinary literal affixes; no fields/formatting.");
        if(kind=="arc_length")
        {
            Shape(item,path,"type","kind","layer","dimension_style","text_override","center","xline1","xline2","arc_point");
            var c=Point(item,"center",path);var a=Point(item,"xline1",path);var b=Point(item,"xline2",path);var arc=Point(item,"arc_point",path);
            ExtendedAnnotationValidation356.ArcGeometry(c,a,b,arc,path);return new ArcLengthDimensionSpec356(c,a,b,arc,style,text,layer);
        }
        Shape(item,path,"type","kind","layer","dimension_style","text_override","center","chord_point","override_center","jog_point","jog_angle");
        var center=Point(item,"center",path);var chord=Point(item,"chord_point",path);var fake=Point(item,"override_center",path);var jog=Point(item,"jog_point",path);
        double angle=Number(Required(item,"jog_angle",path),path,0,Math.PI);ExtendedAnnotationValidation356.JogGeometry(center,chord,fake,jog,angle,path);
        return new JoggedRadiusDimensionSpec356(center,chord,fake,jog,angle,style,text,layer);
    }
    private static EntitySpec ParseTolerance356(JsonElement item,string path,string layer)
    {
        Shape(item,path,"type","layer","codes","position","rotation","dimension_style");
        double rotation=item.TryGetProperty("rotation",out var r)?Number(r,path+".rotation",-1e9,1e9):0;
        return new ToleranceSpec356(ExtendedAnnotationValidation356.Codes(Required(item,"codes",path),path+".codes"),Point(item,"position",path),rotation,Text(item,"dimension_style",path,255),layer);
    }
}
