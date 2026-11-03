using System.Text.Json;
namespace NanoCAD.Contracts;

public sealed record ConstructionCurve443(string NativeType,Point3 BasePoint,Point3 UnitDirection,string LayerName):EntitySpec(NativeType,LayerName);

public static class ConstructionCurves443
{
    public static Point3 Direction(Point3 value)
    {
        double scale=Math.Max(Math.Abs(value.X),Math.Max(Math.Abs(value.Y),Math.Abs(value.Z)));
        if(!double.IsFinite(scale)||scale==0)throw new DomainException("INVALID_GEOMETRY","Ray/Xline direction must be finite and nonzero.");
        double x=value.X/scale,y=value.Y/scale,z=value.Z/scale,length=Math.Sqrt(x*x+y*y+z*z);
        return new(x/length,y/length,z/length);
    }
    public static ConstructionCurve443 Parse(JsonElement item,string path,string layer)
    {
        var fields=new HashSet<string>(StringComparer.Ordinal);
        foreach(var field in item.EnumerateObject())
            if(!fields.Add(field.Name)||field.Name is not ("type" or "layer" or "base_point" or "direction"))throw new DomainException("INVALID_ARGUMENT",path+": unknown or duplicate construction-curve field.");
        if(!item.TryGetProperty("type",out var type)||type.ValueKind!=JsonValueKind.String||type.GetString() is not ("Ray" or "Xline")||
           !item.TryGetProperty("base_point",out var point)||!item.TryGetProperty("direction",out var direction))
            throw new DomainException("INVALID_ARGUMENT",path+": Ray/Xline require base_point and direction as XYZ arrays.");
        return new(type.GetString()!,PointCoordinates408.Parse(point,path+".base_point"),Direction(PointCoordinates408.Parse(direction,path+".direction")),layer);
    }
}
