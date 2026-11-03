using System.Text.Json;
using static NanoCAD.Contracts.StructureWriteValidation;

namespace NanoCAD.Contracts;

public sealed record SetRasterImageClip391(string Handle,string ReferenceToken,string Mode,Point3[]? Boundary)
    : ExternalReferenceEdit385("set_raster_image_clip");
public static class RasterImageClipValidation391
{
    public static SetRasterImageClip391 Parse(JsonElement value,string path)
    {
        var mode=Text(value,"mode",path,16);
        if(mode is not ("polygon" or "clear"))throw Invalid(path+".mode","Use polygon for a WCS boundary or clear for the full unchanged image.");
        Shape(value,path,mode=="polygon"?["operation","reference_kind","handle","reference_token","mode","boundary"]:["operation","reference_kind","handle","reference_token","mode"]);
        if(Text(value,"reference_kind",path,16)!="raster_image")throw Invalid(path+".reference_kind","Use raster_image for the actual selected native occurrence.");
        string token=Text(value,"reference_token",path,64);
        if(token.Length!=64||token.Any(c=>!Uri.IsHexDigit(c)))throw Invalid(path+".reference_token","Use the current image occurrence reference_token.");
        Point3[]? points=null;
        if(mode=="polygon")
        {
            var boundary=Required(value,"boundary",path);
            if(boundary.ValueKind!=JsonValueKind.Array||boundary.GetArrayLength()<3)throw Invalid(path+".boundary","Use at least three ordered WCS vertices of one simple image-plane polygon.");
            points=boundary.EnumerateArray().Select((point,index)=>{
                string at=path+".boundary["+index+"]";
                if(point.ValueKind!=JsonValueKind.Array||point.GetArrayLength()!=3)throw Invalid(at,"Expected a finite WCS XYZ triple in drawing millimeters.");
                return new Point3(ExternalReferenceValidation385.Number(point[0],at+"[0]"),ExternalReferenceValidation385.Number(point[1],at+"[1]"),ExternalReferenceValidation385.Number(point[2],at+"[2]"));
            }).ToArray();
        }
        return new(Validation.NormalizeHandle(Text(value,"handle",path,16)),token.ToUpperInvariant(),mode,points);
    }
}
