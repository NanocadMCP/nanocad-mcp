using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record RevolvedSolidSpec(Point2[] Profile,Point3 AxisOrigin,double Angle,double StartAngle,string LayerName):EntitySpec("Solid3d",LayerName);
public sealed record SolidSweepPath(string Kind,Point3? Start,Point3? End,Point3? Center,double Radius,double StartAngle,double SweepAngle);
public sealed record SweptSolidSpec(double Radius,SolidSweepPath Path,string LayerName):EntitySpec("Solid3d",LayerName);

public static partial class CreateValidation
{
    private static Point3 TripleValue(JsonElement owner,string name,string path)
    {
        var value=Required(owner,name,path);
        if(value.ValueKind!=JsonValueKind.Array || value.GetArrayLength()!=3)throw Invalid(path+"."+name,"Expected WCS XYZ triple.","INVALID_GEOMETRY");
        return new(Number(value[0],path+"."+name+"[0]"),Number(value[1],path+"."+name+"[1]"),Number(value[2],path+"."+name+"[2]"));
    }
    private static EntitySpec ParseRevolve(JsonElement item,string path,string layer)
    {
        Shape(item,path,"type","kind","layer","profile","axis_origin","angle","start_angle");
        var profile=Points(item,"profile",path,3);ValidateSimplePolygon(profile,path);
        if(profile.Any(p=>p.X<0 || p.X>1e6 || Math.Abs(p.Y)>1e6) || profile.Max(p=>p.X)<1e-3)
            throw Invalid(path+".profile","Profile pairs are nonnegative radius / axial height, bounded to1000000 mm.","INVALID_GEOMETRY");
        double start=item.TryGetProperty("start_angle",out var s)?Number(s,path+".start_angle",0,Math.PI*2):0;
        if(start>=Math.PI*2)throw Invalid(path+".start_angle","Start angle must be below2π.","INVALID_GEOMETRY");
        return new RevolvedSolidSpec(profile,TripleValue(item,"axis_origin",path),Number(Required(item,"angle",path),path+".angle",1e-3,Math.PI*2),start,layer);
    }
    private static EntitySpec ParseSweep(JsonElement item,string path,string layer)
    {
        Shape(item,path,"type","kind","layer","radius","path");
        double radius=Number(Required(item,"radius",path),path+".radius",1e-3,1e6);
        var p=Required(item,"path",path);string label=path+".path";
        if(p.ValueKind!=JsonValueKind.Object)throw Invalid(label,"Expected path object.");
        string kind=Text(p,"kind",label,16);SolidSweepPath spec;
        if(kind=="line")
        {
            Shape(p,label,"kind","start","end");var start=TripleValue(p,"start",label);var end=TripleValue(p,"end",label);
            double length=Math.Sqrt(Math.Pow(end.X-start.X,2)+Math.Pow(end.Y-start.Y,2)+Math.Pow(end.Z-start.Z,2));
            if(length<1e-3 || length>1e6)throw Invalid(label,"Path length must be0.001–1000000 mm.","INVALID_GEOMETRY");
            spec=new(kind,start,end,null,0,0,0);
        }
        else if(kind=="arc")
        {
            Shape(p,label,"kind","center","radius","start_angle","sweep_angle");
            var center=TripleValue(p,"center",label);double pathRadius=Number(Required(p,"radius",label),label+".radius",1e-3,1e6);
            if(pathRadius-radius<1e-3)throw Invalid(label+".radius","Arc radius must exceed profile radius by at least0.001 mm.","INVALID_GEOMETRY");
            double start=Number(Required(p,"start_angle",label),label+".start_angle",0,Math.PI*2);
            if(start>=Math.PI*2)throw Invalid(label+".start_angle","Start angle must be below2π.","INVALID_GEOMETRY");
            spec=new(kind,null,null,center,pathRadius,start,Number(Required(p,"sweep_angle",label),label+".sweep_angle",1e-3,Math.PI));
        }
        else throw Invalid(label+".kind","Expected line or arc path.");
        return new SweptSolidSpec(radius,spec,layer);
    }
}
