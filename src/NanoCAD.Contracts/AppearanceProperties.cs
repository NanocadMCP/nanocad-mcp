using System.Text;
using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record RgbColor(byte Red,byte Green,byte Blue)
{
    public int[] Channels()=>new[]{(int)Red,(int)Green,(int)Blue};
}
public sealed record TransparencyChange(string Mode,double? Percent=null)
{
    // Preserve the requested percentage in the fingerprint; the native alpha
    // byte is an explicit quantized result, not a different requested payload.
    public byte? Alpha=>Percent is {} percent?(byte)Math.Round(255*(1-percent/100),MidpointRounding.AwayFromZero):null;
}
public sealed record AppearanceChanges(RgbColor? ColorRgb,TransparencyChange? Transparency,string? Description);
public sealed record StoredTransparency(string mode,int? alpha,double? percent);

public static class AppearanceValidation
{
    public static readonly string[] EntityFields={"color_rgb","transparency"};
    public static readonly string[] LayerFields={"color_rgb","transparency","description"};
    private static DomainException Invalid(string path,string message)=>new("INVALID_ARGUMENT",path+": "+message,false,new{path});
    private static JsonElement Required(JsonElement value,string name,string path)=>value.TryGetProperty(name,out var field)?field:throw Invalid(path+"."+name,"Required field.");
    private static void Shape(JsonElement value,string path,params string[] fields)
    {
        if(value.ValueKind!=JsonValueKind.Object)throw Invalid(path,"Expected object.");
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var field in value.EnumerateObject())
            if(!seen.Add(field.Name)||!fields.Contains(field.Name,StringComparer.Ordinal))throw Invalid(path+"."+field.Name,"Unknown or duplicate field.");
    }
    private static RgbColor Rgb(JsonElement value,string path)
    {
        if(value.ValueKind!=JsonValueKind.Array||value.GetArrayLength()!=3)throw Invalid(path,"Expected [red,green,blue], three integer channels0..255.");
        var channels=new byte[3];int index=0;
        foreach(var channel in value.EnumerateArray())
        {
            if(channel.ValueKind!=JsonValueKind.Number||!channel.TryGetInt32(out int number)||number is <0 or >255)throw Invalid(path+"["+index+"]","Expected integer0..255.");
            channels[index++]=(byte)number;
        }
        return new(channels[0],channels[1],channels[2]);
    }
    private static TransparencyChange Transparency(JsonElement value,string path,bool layer)
    {
        if(value.ValueKind!=JsonValueKind.Object)throw Invalid(path,"Expected a transparency object.");
        var modeValue=Required(value,"mode",path);
        if(modeValue.ValueKind!=JsonValueKind.String)throw Invalid(path+".mode","Expected explicit, by_layer or by_block.");
        string mode;
        try{mode=modeValue.GetString()!;}
        catch(InvalidOperationException){throw Invalid(path+".mode","Expected valid Unicode scalar text.");}
        if(mode=="explicit")
        {
            Shape(value,path,"mode","percent");var percentValue=Required(value,"percent",path);
            if(percentValue.ValueKind!=JsonValueKind.Number||!percentValue.TryGetDouble(out double percent)||!double.IsFinite(percent)||percent is <0 or >100)
                throw Invalid(path+".percent","Expected finite percentage0..100;0 opaque,100 clear.");
            return new(mode,percent);
        }
        Shape(value,path,"mode");
        if(layer||mode is not ("by_layer" or "by_block"))throw Invalid(path+".mode",layer?"A layer requires its own explicit transparency.":"Expected explicit, by_layer or by_block.");
        return new(mode);
    }
    public static string Description(JsonElement value,string path)
    {
        if(value.ValueKind!=JsonValueKind.String)throw Invalid(path,"Expected a Unicode description; an empty string clears it.");
        string text;
        try{text=value.GetString()!;}
        catch(InvalidOperationException){throw Invalid(path,"Expected valid Unicode scalar text.");}
        if(text.Contains('\0'))throw Invalid(path,"A native string cannot contain NUL.");
        try{_ = new UTF8Encoding(false,true).GetByteCount(text);}
        catch(EncoderFallbackException){throw Invalid(path,"Expected valid Unicode scalar text.");}
        if(JsonSerializer.SerializeToUtf8Bytes(text,Wire.Json).Length>Wire.PayloadBudget-8192)
            throw new DomainException("REQUEST_LIMIT_EXCEEDED",path+": Description exceeds the actual serialized readback-byte budget.",false,new{path});
        return text;
    }
    // The caller owns the complete property shape and existing ACI validation.
    public static AppearanceChanges Parse(JsonElement properties,string path,bool layer=false)
    {
        if(properties.ValueKind!=JsonValueKind.Object)throw Invalid(path,"Expected object.");
        RgbColor? rgb=properties.TryGetProperty("color_rgb",out var rgbValue)?Rgb(rgbValue,path+".color_rgb"):null;
        if(rgb!=null&&properties.TryGetProperty("color_aci",out _))throw Invalid(path,"Choose color_aci or color_rgb, not both.");
        TransparencyChange? transparency=properties.TryGetProperty("transparency",out var value)?Transparency(value,path+".transparency",layer):null;
        string? description=layer&&properties.TryGetProperty("description",out var descriptionValue)?Description(descriptionValue,path+".description"):null;
        return new(rgb,transparency,description);
    }
    public static StoredTransparency Expected(TransparencyChange change)=>new(change.Mode,change.Alpha,
        change.Alpha is {} alpha?100d*(255-alpha)/255:null);
}
