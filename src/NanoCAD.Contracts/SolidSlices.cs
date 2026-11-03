using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record SliceSolidSpec(string[] Targets,Point3 PlaneOrigin,Point3 PlaneNormal):EditSpec("slice_solid",Targets);
public sealed record SeparateSolidSpec(string[] Targets):EditSpec("separate_solid",Targets);
public sealed record SolidSeparationMapping(int operation_index,EntityRef source,EntityRef[] parts);
public sealed record SolidSliceMapping(int operation_index,EntityRef source,EntityRef positive,EntityRef negative);
public static partial class ModifyValidation
{
    private static SliceSolidSpec ParseSolidSlice(JsonElement item,string path,string[] handles)
    {
        Shape(item,path,"operation","handles","plane_origin","plane_normal");
        Point3 Triple(string name)
        {
            var p=Required(item,name,path);
            if(p.ValueKind!=JsonValueKind.Array||p.GetArrayLength()!=3)throw Invalid(path+"."+name,"Expected WCS [x,y,z].");
            return new(Number(p[0],path),Number(p[1],path),Number(p[2],path));
        }
        var origin=Triple("plane_origin");var normal=Triple("plane_normal");
        double length=Math.Sqrt(normal.X*normal.X+normal.Y*normal.Y+normal.Z*normal.Z);
        if(length<1e-12)throw Invalid(path+".plane_normal","Plane normal must be nonzero.","INVALID_GEOMETRY");
        return new(handles,origin,new(normal.X/length,normal.Y/length,normal.Z/length));
    }
}
