using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record SolidCopyTransform(string Kind,Point3? Displacement,Point3? Origin,Point3? Direction,double Angle);
public sealed record SolidTransformCopySpec(string SourceHandle,SolidCopyTransform Transform,string LayerName):EntitySpec("Solid3d",LayerName);

public static partial class CreateValidation
{
    private static EntitySpec ParseSolidTransformCopy(JsonElement item,string path,string layer)
    {
        Shape(item,path,"type","kind","layer","source_handle","transform");
        string handle=Validation.NormalizeHandle(Text(item,"source_handle",path,16));
        var transform=Required(item,"transform",path);string label=path+".transform";
        if(transform.ValueKind!=JsonValueKind.Object)throw Invalid(label,"Expected a transform object.");
        string kind=Text(transform,"kind",label,16);SolidCopyTransform spec;
        if(kind=="translation")
        {
            Shape(transform,label,"kind","displacement");var vector=TripleValue(transform,"displacement",label);
            if(Math.Sqrt(vector.X*vector.X+vector.Y*vector.Y+vector.Z*vector.Z)>1e6)
                throw Invalid(label+".displacement","Translation is limited to 1000000 mm.","INVALID_GEOMETRY");
            spec=new(kind,vector,null,null,0);
        }
        else if(kind is "rotation" or "mirror")
        {
            string originName=kind=="rotation"?"axis_origin":"plane_origin",directionName=kind=="rotation"?"axis_direction":"plane_normal";
            if(kind=="rotation")Shape(transform,label,"kind",originName,directionName,"angle");
            else Shape(transform,label,"kind",originName,directionName);
            var origin=TripleValue(transform,originName,label);var direction=TripleValue(transform,directionName,label);
            if(Math.Sqrt(direction.X*direction.X+direction.Y*direction.Y+direction.Z*direction.Z)<1e-9)
                throw Invalid(label+"."+directionName,"Direction must be a nonzero vector.","INVALID_GEOMETRY");
            spec=new(kind,null,origin,direction,kind=="rotation"?Number(Required(transform,"angle",label),label+".angle",-Math.PI*2,Math.PI*2):0);
        }
        else throw Invalid(label+".kind","Expected translation, rotation or mirror.");
        return new SolidTransformCopySpec(handle,spec,layer);
    }
}
