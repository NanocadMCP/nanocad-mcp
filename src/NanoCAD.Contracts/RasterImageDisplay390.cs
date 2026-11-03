using System.Text.Json;
using static NanoCAD.Contracts.StructureWriteValidation;

namespace NanoCAD.Contracts;

public sealed record RasterImageDisplay390(int? Brightness,int? Contrast,int? Fade,bool? ShowImage,bool? ImageTransparency,bool? ShowUnaligned);
public sealed record SetRasterImageDisplay390(string Handle,string ReferenceToken,RasterImageDisplay390 Properties)
    : ExternalReferenceEdit385("set_raster_image_display");

public static class RasterImageDisplayValidation390
{
    public static SetRasterImageDisplay390 Parse(JsonElement value,string path)
    {
        Shape(value,path,"operation","reference_kind","handle","reference_token","properties");
        if(Text(value,"reference_kind",path,16)!="raster_image")throw Invalid(path+".reference_kind","Use raster_image for an actual native image occurrence.");
        string handle=Validation.NormalizeHandle(Text(value,"handle",path,16));
        string token=Text(value,"reference_token",path,64);
        if(token.Length!=64||token.Any(c=>!Uri.IsHexDigit(c)))throw Invalid(path+".reference_token","Use the current image occurrence reference_token from inspect_objects.");
        var properties=Required(value,"properties",path);
        Shape(properties,path+".properties","brightness","contrast","fade","show_image","image_transparency","show_unaligned");
        if(!properties.EnumerateObject().Any())throw Invalid(path+".properties","At least one image display property is required.");
        int? Percent(string name)
        {
            if(!properties.TryGetProperty(name,out var field))return null;
            if(field.ValueKind!=JsonValueKind.Number||!field.TryGetInt32(out int result)||result<0||result>100)
                throw Invalid(path+".properties."+name,"Expected a native image adjustment integer from0 to100.");
            return result;
        }
        bool? Flag(string name)
        {
            if(!properties.TryGetProperty(name,out var field))return null;
            if(field.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw Invalid(path+".properties."+name,"Expected boolean.");
            return field.GetBoolean();
        }
        return new(handle,token.ToUpperInvariant(),new(Percent("brightness"),Percent("contrast"),Percent("fade"),Flag("show_image"),Flag("image_transparency"),Flag("show_unaligned")));
    }
}
