using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record DrilledSolidCopySpec(string SourceHandle,Point3 Start,Point3 Direction,double Radius,double Depth,string LayerName):EntitySpec("Solid3d",LayerName);
public sealed record SolidDrillEditSpec(string[] Targets,Point3 Start,Point3 Direction,double Radius,double Depth):EditSpec("drill_solid",Targets)
{
    public DrilledSolidCopySpec AsCopySpec(string layer)=>new(Targets.Single(),Start,Direction,Radius,Depth,layer);
}

public static partial class CreateValidation
{
    private static EntitySpec ParseDrilledSolidCopy(JsonElement item,string path,string layer)
    {
        Shape(item,path,"type","kind","layer","source_handle","start","direction","radius","depth");
        string handle=Validation.NormalizeHandle(Text(item,"source_handle",path,16));
        var start=TripleValue(item,"start",path);var direction=TripleValue(item,"direction",path);
        if(Math.Sqrt(direction.X*direction.X+direction.Y*direction.Y+direction.Z*direction.Z)<1e-9)
            throw Invalid(path+".direction","Drill direction must be nonzero.","INVALID_GEOMETRY");
        return new DrilledSolidCopySpec(handle,start,direction,
            Number(Required(item,"radius",path),path+".radius",1e-3,1e6),
            Number(Required(item,"depth",path),path+".depth",1e-3,1e6),layer);
    }
}

public static partial class ModifyValidation
{
    private static SolidDrillEditSpec ParseSolidDrill(JsonElement item,string path,string[] handles)
    {
        if(handles.Length!=1)throw Invalid(path+".handles","Drill exactly one direct solid per operation.");
        Shape(item,path,"operation","handles","start","direction","radius","depth");
        Point3 Triple(string name)
        {
            var value=Required(item,name,path);
            if(value.ValueKind!=JsonValueKind.Array||value.GetArrayLength()!=3)throw Invalid(path+"."+name,"Expected WCS XYZ triple.","INVALID_GEOMETRY");
            return new(Number(value[0],path+"."+name+"[0]",-1e6,1e6),Number(value[1],path+"."+name+"[1]",-1e6,1e6),Number(value[2],path+"."+name+"[2]",-1e6,1e6));
        }
        var start=Triple("start");var direction=Triple("direction");
        if(Math.Sqrt(direction.X*direction.X+direction.Y*direction.Y+direction.Z*direction.Z)<1e-9)
            throw Invalid(path+".direction","Drill direction must be nonzero.","INVALID_GEOMETRY");
        return new(handles,start,direction,Number(Required(item,"radius",path),path+".radius",1e-3,1e6),
            Number(Required(item,"depth",path),path+".depth",1e-3,1e6));
    }
}
