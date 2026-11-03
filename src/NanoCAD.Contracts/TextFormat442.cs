using System.Text.Json;
using System.Text.Json.Serialization;

namespace NanoCAD.Contracts;

public sealed record TextFormatChanges442(string? TextStyleHandle=null,double? TextHeight=null,double? WidthFactor=null,
    double? Rotation=null,double? Oblique=null,string? HorizontalMode=null,string? VerticalMode=null,Point3? AnchorWcs=null,Point3? SecondAnchorWcs=null);
public sealed record SetTextFormat442(string[] Targets,string TextToken,TextFormatChanges442 Properties):EditSpec("set_text_format",Targets);
public sealed record TextEditing442(string? text_token,bool editable,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]string? unavailable_reason=null);
public sealed record TextFormatChange442(EntityRef source_ref,string text_token,EntityRef text_style_ref,double text_height,
    double width_factor,double rotation,double oblique,string horizontal_mode,string vertical_mode,double[] anchor_wcs,double[]? second_anchor_wcs,string[] warnings);
public sealed record TextCreateFormat442(double WidthFactor,double Rotation,double Oblique,string HorizontalMode,string VerticalMode,Point2? SecondAnchor);

public static class TextFormatValidation442
{
    public static readonly string[] Horizontal=["left","center","right","aligned","middle","fit"];
    public static readonly string[] Vertical=["baseline","bottom","middle","top"];
    static DomainException Bad(string path,string message)=>new("INVALID_ARGUMENT",path+": "+message);
    static void Shape(JsonElement value,string path,params string[] fields)
    {if(value.ValueKind!=JsonValueKind.Object)throw Bad(path,"Expected object.");var seen=new HashSet<string>();foreach(var p in value.EnumerateObject())if(!seen.Add(p.Name)||!fields.Contains(p.Name))throw Bad(path+"."+p.Name,"Unknown or duplicate field.");}
    static string Text(JsonElement value,string name,string path)=>value.TryGetProperty(name,out var p)&&p.ValueKind==JsonValueKind.String?p.GetString()!:throw Bad(path+"."+name,"Expected string.");
    static double Number(JsonElement value,string path)
    {if(value.ValueKind!=JsonValueKind.Number||!value.TryGetDouble(out double n)||!double.IsFinite(n))throw Bad(path,"Expected finite number.");return n;}
    static double? Optional(JsonElement value,string name,string path,bool positive=false)
    {if(!value.TryGetProperty(name,out var p))return null;double n=Number(p,path+"."+name);if(positive&&n<=0)throw Bad(path+"."+name,"Expected positive native value.");return n;}
    static string? Mode(JsonElement value,string name,string path,string[] allowed)
    {if(!value.TryGetProperty(name,out _))return null;string s=Text(value,name,path);if(!allowed.Contains(s))throw Bad(path+"."+name,"Unknown native mode.");return s;}
    static Point3? Point(JsonElement value,string name,string path)
    {if(!value.TryGetProperty(name,out var p))return null;if(p.ValueKind!=JsonValueKind.Array||p.GetArrayLength()!=3)throw Bad(path+"."+name,"Expected WCS XYZ drawing-unit point.");return new(Number(p[0],path),Number(p[1],path),Number(p[2],path));}
    public static SetTextFormat442 Parse(JsonElement value,string path,string[] handles)
    {
        Shape(value,path,"operation","handles","text_token","properties");if(handles.Length!=1)throw Bad(path,"Use one fresh token per native TEXT; batch different targets as separate operations.");
        string token=Text(value,"text_token",path);if(token.Length!=64||token.Any(c=>!Uri.IsHexDigit(c)))throw Bad(path,"Read geometry.formatting.editing.text_token.");
        if(!value.TryGetProperty("properties",out var p))throw Bad(path,"properties is required.");
        Shape(p,path,"text_style_handle","text_height","width_factor","rotation","oblique","horizontal_mode","vertical_mode","anchor_wcs","second_anchor_wcs");
        if(!p.EnumerateObject().Any())throw Bad(path,"At least one native format property is required.");
        string? style=p.TryGetProperty("text_style_handle",out _)?Validation.NormalizeHandle(Text(p,"text_style_handle",path)):null;
        var result=new SetTextFormat442(handles,token.ToUpperInvariant(),new(style,Optional(p,"text_height",path,true),Optional(p,"width_factor",path,true),
            Optional(p,"rotation",path),Optional(p,"oblique",path),Mode(p,"horizontal_mode",path,Horizontal),Mode(p,"vertical_mode",path,Vertical),Point(p,"anchor_wcs",path),Point(p,"second_anchor_wcs",path)));
        if(result.Properties.Oblique is {} angle&&Math.Abs(angle)>=Math.PI/2)throw Bad(path,"Oblique must be strictly between -pi/2 and pi/2 radians.");return result;
    }
    public static TextCreateFormat442 Create(JsonElement value,string path)
    {
        Shape(value,path,"width_factor","rotation","oblique","horizontal_mode","vertical_mode","second_anchor");
        string h=Mode(value,"horizontal_mode",path,Horizontal)??"left",v=Mode(value,"vertical_mode",path,Vertical)??"baseline";
        double oblique=Optional(value,"oblique",path)??0;if(Math.Abs(oblique)>=Math.PI/2)throw Bad(path,"Oblique must be between -pi/2 and pi/2.");
        Point2? second=null;if(value.TryGetProperty("second_anchor",out var p))
        {if(p.ValueKind!=JsonValueKind.Array||p.GetArrayLength()!=2)throw Bad(path,"second_anchor is local XY.");second=new(Number(p[0],path),Number(p[1],path));}
        if(h is "aligned" or "fit"){if(v!="baseline"||second==null)throw Bad(path,"Aligned/Fit require baseline and explicit second_anchor.");if(value.TryGetProperty("width_factor",out _)||value.TryGetProperty("rotation",out _))throw Bad(path,"Native Aligned/Fit derive width and rotation from their two anchors; change those anchors.");}
        else if(second!=null)throw Bad(path,"second_anchor belongs only to Aligned/Fit.");
        if(h=="middle"&&v!="baseline")throw Bad(path,"Native middle horizontal mode uses baseline vertical mode.");
        return new(Optional(value,"width_factor",path,true)??1,Optional(value,"rotation",path)??0,oblique,h,v,second);
    }
    public static void Batch(IReadOnlyList<EditSpec> edits)
    {
        if(!edits.Any(e=>e is SetTextFormat442))return;
        if(edits.Any(e=>e is not SetTextFormat442)||edits.SelectMany(e=>e.Handles).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=edits.Count)
            throw new DomainException("INVALID_OPERATION_ORDER","Batch native TEXT formatting on distinct targets; combine each target's properties in its one token-bound operation.");
    }
}
