using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record Point3(double X, double Y, double Z);
public sealed record SolidSpec(string Kind, Point3 Center, Point3? Size, double Radius, double Height, string LayerName) : EntitySpec("Solid3d", LayerName)
{
    // New primitive inputs are absent from earlier primitive fingerprints.
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public double? TopRadius { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public double? MinorRadius { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? Sides { get; init; }
}
public sealed record ExtrudedSolidSpec(Point2[] Profile, double BaseZ, double Height, string LayerName) : EntitySpec("Solid3d", LayerName);

public static partial class CreateValidation
{
    private static EntitySpec ParseSolid(JsonElement item, string path, string layer)
    {
        string kind=Text(item,"kind",path,16);
        if(kind=="model_extrusion")return ParseModelExtrusion(item,path,layer,allowNativeGeometry:true);
        if(kind=="boolean")return ParseSolidBoolean(item,path,layer);
        if((kind is "extrusion" or "revolve" or "sweep")&&item.TryGetProperty("profile_face",out _))return ParseFaceProfileSolid390(item,path,layer,kind);
        if((kind is "extrusion" or "revolve")&&item.TryGetProperty("profile_handle",out _))return ParseProfileSolid(item,path,layer,kind);
        if(kind=="revolve")return ParseRevolve(item,path,layer);
        if(kind=="model_loft")return ParseModelLoft(item,path,layer);
        if(kind=="model_sweep")return ParseModelSweep(item,path,layer);
        if(kind=="sweep"&&item.TryGetProperty("profile_handle",out _))return ParseCurveSolid(item,path,layer,kind);
        if(kind=="loft"&&item.TryGetProperty("section_handles",out _))return ParseCurveSolid(item,path,layer,kind);
        if(kind=="sweep")return ParseSweep(item,path,layer);
        if(kind=="loft")return ParseLoft(item,path,layer);
        if(kind=="transform_copy")return ParseSolidTransformCopy(item,path,layer);
        if(kind=="drilled_copy")return ParseDrilledSolidCopy(item,path,layer);
        if(kind is "fillet_copy" or "chamfer_copy" or "shell_copy")return ParseSolidFinishingCopy(item,path,layer,kind);
        if(kind=="extrusion")
        {
            Shape(item,path,"type","kind","layer","profile","base_z","height");
            var profile=Points(item,"profile",path,3);
            ValidateSimplePolygon(profile,path);
            return new ExtrudedSolidSpec(profile,Number(Required(item,"base_z",path),path+".base_z"),Number(Required(item,"height",path),path+".height",1e-3,1e6),layer);
        }
        var fields=new List<string>{"type","kind","layer","center"};
        fields.AddRange(kind switch {
            "box" or "wedge" => new[]{"size"}, "sphere" => new[]{"radius"}, "cylinder" => new[]{"radius","height"},
            "frustum" => new[]{"base_radius","top_radius","height"}, "torus" => new[]{"major_radius","minor_radius"},
            "pyramid" => new[]{"base_radius","top_radius","height","sides"},
            _ => throw Invalid(path+".kind","Expected box, sphere, cylinder, frustum, torus, wedge or pyramid.") });
        Shape(item,path,fields.ToArray());
        Point3 Triple(string field,bool size)
        {
            var value=Required(item,field,path);
            if(value.ValueKind!=JsonValueKind.Array || value.GetArrayLength()!=3) throw Invalid(path+"."+field,"Expected three WCS XYZ values.","INVALID_GEOMETRY");
            double N(int i)=>Number(value[i],path+"."+field+"["+i+"]",size?1e-3:-Maximum,size?1e6:Maximum);
            return new(N(0),N(1),N(2));
        }
        var center=Triple("center",false);
        double SizeValue(string name)=>Number(Required(item,name,path),path+"."+name,1e-3,1e6);
        int? sides=null;
        if(kind=="pyramid")
        {
            var value=Required(item,"sides",path);
            if(value.ValueKind!=JsonValueKind.Number || !value.TryGetInt32(out var count) || count<3)
                throw Invalid(path+".sides","Expected at least three polygon sides within the native Int32 representation.","INVALID_GEOMETRY");
            sides=count;
        }
        double radius=kind switch { "box" or "wedge"=>0, "frustum" or "pyramid"=>SizeValue("base_radius"),
            "torus"=>SizeValue("major_radius"), _=>SizeValue("radius") };
        double? minor=kind=="torus"?SizeValue("minor_radius"):null;
        if(minor is {} minorRadius && minorRadius>=radius)
            throw Invalid(path+".minor_radius","A regular ring torus requires minor_radius smaller than major_radius.","INVALID_GEOMETRY");
        double? top=kind=="frustum"?SizeValue("top_radius"):kind=="pyramid"
            ?Number(Required(item,"top_radius",path),path+".top_radius",0,1e6):null;
        if(kind=="pyramid" && top is >0 and <1e-3)
            throw Invalid(path+".top_radius","Use zero for the apex or a positive top radius of at least 0.001 millimeters.","INVALID_GEOMETRY");
        return new SolidSpec(kind,center,kind is "box" or "wedge"?Triple("size",true):null,radius,
            kind is "cylinder" or "frustum" or "pyramid"?SizeValue("height"):0,layer)
        {TopRadius=top,MinorRadius=minor,Sides=sides};
    }
}
