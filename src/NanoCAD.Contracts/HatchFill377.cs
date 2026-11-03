using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record HatchGradientColor377(float Value,RgbColor Color);
public sealed record HatchGradient377(string Type,string Name,double Angle,float Shift,bool OneColorMode,float ShadeTint,HatchGradientColor377[] Colors);
public sealed record HatchFillPattern377(string Type,string Name,double Angle,double Scale,Point2 Origin,bool Double,double? Space);
public sealed record SetHatchFill377(string[] Targets,string FillToken,string Kind,HatchGradient377? Gradient,HatchFillPattern377? Pattern)
    :EditSpec("set_hatch_fill",Targets);
public sealed record HatchFillChange377(int operation_index,EntityRef hatch_ref,string fill_token,string fill_kind,string[] source_handles,string[] warnings);
public sealed record HatchFillOutput377(string operation_id,string status,bool replayed,EntityRef[] modified,EntityRef[] erased,
    EntityRef[] created,CopyMapping[] copies,string undo_history,HatchFillChange377[] hatch_fill_changes);

public static class HatchFillValidation377
{
    public static readonly string[] GradientTypes=["predefined","user_defined"];
    public static readonly string[] PatternTypes=["predefined","user_defined","custom_defined"];
    static DomainException Bad(string path,string message)=>new("INVALID_ARGUMENT",message,false,new{path});
    static void Shape(JsonElement item,string path,params string[] names)
    {
        if(item.ValueKind!=JsonValueKind.Object)throw Bad(path,"Expected an object.");
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var field in item.EnumerateObject())if(!seen.Add(field.Name)||!names.Contains(field.Name))throw Bad(path+"."+field.Name,"Unknown or duplicate field.");
        foreach(string name in names)if(!item.TryGetProperty(name,out _))throw Bad(path+"."+name,"Required field.");
    }
    static string Text(JsonElement item,string name,string path)
    {
        var value=item.GetProperty(name);
        if(value.ValueKind!=JsonValueKind.String)throw Bad(path+"."+name,"Expected a native name.");
        string result=value.GetString()!;
        if(string.IsNullOrWhiteSpace(result)||result.Trim()!=result||result.Any(char.IsControl)||result.IndexOfAny(['\\','/',':','|'])>=0)
            throw Bad(path+"."+name,"Use a nonempty native name, without a path, qualifier or control characters.");
        return result;
    }
    static double Number(JsonElement value,string path)
    {
        if(value.ValueKind!=JsonValueKind.Number||!value.TryGetDouble(out double result)||!double.IsFinite(result))throw Bad(path,"Expected a finite native number.");
        return result==0?0:result;
    }
    static float Single(JsonElement value,string path)
    {
        double result=Number(value,path);float native=(float)result;
        if(!float.IsFinite(native))throw Bad(path,"Native gradient values must fit a finite32-bit float.");
        return native==0?0:native;
    }
    static bool Boolean(JsonElement item,string name,string path)
    {
        var value=item.GetProperty(name);
        if(value.ValueKind is not(JsonValueKind.True or JsonValueKind.False))throw Bad(path+"."+name,"Expected Boolean.");
        return value.GetBoolean();
    }
    static double Angle(JsonElement value,string path)
    {double result=Number(value,path);if(result<0||result>=2*Math.PI)throw Bad(path,"Use native OCS radians in[0,2pi).");return result;}
    static RgbColor Rgb(JsonElement value,string path)
    {
        if(value.ValueKind!=JsonValueKind.Array||value.GetArrayLength()!=3)throw Bad(path,"Use three RGB bytes.");
        byte Channel(JsonElement item){if(item.ValueKind!=JsonValueKind.Number||!item.TryGetInt32(out int result)||result<0||result>255)throw Bad(path,"RGB channels must be integer0..255.");return (byte)result;}
        return new(Channel(value[0]),Channel(value[1]),Channel(value[2]));
    }
    public static SetHatchFill377 Parse(JsonElement item,string path,string[] handles)
    {
        Shape(item,path,"operation","handles","fill_token","fill");
        if(handles.Length!=1)throw Bad(path+".handles","Address one current resident native Hatch per operation.");
        string token=Text(item,"fill_token",path);
        if(token.Length!=64||token.Any(value=>!Uri.IsHexDigit(value)))throw Bad(path+".fill_token","Read the fresh geometry.fill_editing.fill_token.");
        var fill=item.GetProperty("fill");string fp=path+".fill";
        if(fill.ValueKind!=JsonValueKind.Object||!fill.TryGetProperty("kind",out var kindValue)||kindValue.ValueKind!=JsonValueKind.String)throw Bad(fp,"Specify solid, pattern or gradient fill.");
        string kind=kindValue.GetString()!;
        HatchGradient377? gradient=null;HatchFillPattern377? pattern=null;
        if(kind=="solid")Shape(fill,fp,"kind");
        else if(kind=="gradient")
        {
            Shape(fill,fp,"kind","gradient_type","gradient_name","angle","shift","one_color_mode","shade_tint","colors");
            string type=Text(fill,"gradient_type",fp);
            if(!GradientTypes.Contains(type))throw Bad(fp+".gradient_type","Use the actual native predefined or user_defined gradient type.");
            var colors=fill.GetProperty("colors");
            if(colors.ValueKind!=JsonValueKind.Array||colors.GetArrayLength()==0)throw Bad(fp+".colors","Supply the complete nonempty native gradient color/value array; no fixed color-count ceiling is imposed.");
            var values=colors.EnumerateArray().Select((color,index)=>
            {
                string cp=fp+".colors["+index+"]";Shape(color,cp,"value","color_rgb");
                return new HatchGradientColor377(Single(color.GetProperty("value"),cp+".value"),Rgb(color.GetProperty("color_rgb"),cp+".color_rgb"));
            }).ToArray();
            gradient=new(type,Text(fill,"gradient_name",fp),Angle(fill.GetProperty("angle"),fp+".angle"),
                Single(fill.GetProperty("shift"),fp+".shift"),Boolean(fill,"one_color_mode",fp),Single(fill.GetProperty("shade_tint"),fp+".shade_tint"),values);
        }
        else if(kind=="pattern")
        {
            Shape(fill,fp,"kind","pattern_type","pattern_name","angle","scale","origin_ocs","pattern_double","spacing");
            string type=Text(fill,"pattern_type",fp);
            if(!PatternTypes.Contains(type))throw Bad(fp+".pattern_type","Use predefined, user_defined or custom_defined native pattern type.");
            double scale=Number(fill.GetProperty("scale"),fp+".scale");if(scale<=0)throw Bad(fp+".scale","Use a positive native pattern scale.");
            var origin=fill.GetProperty("origin_ocs");
            if(origin.ValueKind!=JsonValueKind.Array||origin.GetArrayLength()!=2)throw Bad(fp+".origin_ocs","Use a native hatch-plane OCS XY pair.");
            var spacing=fill.GetProperty("spacing");double? space=spacing.ValueKind==JsonValueKind.Null?null:Number(spacing,fp+".spacing");
            if(type=="user_defined"?space is not >0:space!=null)throw Bad(fp+".spacing","User-defined patterns require positive independent spacing; other types require null.");
            string name=Text(fill,"pattern_name",fp);
            if(name.Equals("SOLID",StringComparison.OrdinalIgnoreCase))throw Bad(fp+".pattern_name","Use fill.kind=solid for a native solid fill.");
            pattern=new(type,name,Angle(fill.GetProperty("angle"),fp+".angle"),scale,
                new(Number(origin[0],fp+".origin_ocs[0]"),Number(origin[1],fp+".origin_ocs[1]")),Boolean(fill,"pattern_double",fp),space);
        }
        else throw Bad(fp+".kind","Use solid, pattern or gradient.");
        return new(handles,token.ToUpperInvariant(),kind,gradient,pattern);
    }
    public static void Batch(IReadOnlyCollection<EditSpec> edits)
    {
        if(!edits.Any(edit=>edit is SetHatchFill377))return;
        if(edits.Any(edit=>edit is not SetHatchFill377)||edits.SelectMany(edit=>edit.Handles).Distinct(StringComparer.Ordinal).Count()!=edits.Count)
            throw new DomainException("INVALID_OPERATION_ORDER","Use a dedicated native fill batch with distinct Hatch targets. It changes no boundary geometry or association.");
    }
}
