using System.Text.Json;

namespace NanoCAD.Contracts;

// Explicit orthonormal frames keep planar coordinates separate from WCS points.
public static class DrawingWorkplane
{
    public static readonly SketchPlaneSpec XY=new(new(0,0,0),new(1,0,0),new(0,1,0));
    public static double Dot(Point3 a,Point3 b)=>a.X*b.X+a.Y*b.Y+a.Z*b.Z;
    public static Point3 Normal(SketchPlaneSpec p)=>new(p.XAxis.Y*p.YAxis.Z-p.XAxis.Z*p.YAxis.Y,p.XAxis.Z*p.YAxis.X-p.XAxis.X*p.YAxis.Z,p.XAxis.X*p.YAxis.Y-p.XAxis.Y*p.YAxis.X);
    public static Point3 World(SketchPlaneSpec p,Point2 q)=>ModelExtrusionGeometry.World(p,q);
    public static JsonElement WithoutFrame(JsonElement value)
    {
        var fields=new Dictionary<string,JsonElement>(StringComparer.Ordinal);
        foreach(var p in value.EnumerateObject())if(!fields.TryAdd(p.Name,p.Value))throw new DomainException("INVALID_ARGUMENT","Duplicate argument.");
        fields.Remove("workplane");return Wire.Element(fields);
    }
    public static Point2 Local(SketchPlaneSpec p,Point3 world)
    {
        var d=new Point3(world.X-p.Origin.X,world.Y-p.Origin.Y,world.Z-p.Origin.Z);
        if(!new[]{world.X,world.Y,world.Z}.All(double.IsFinite)||Math.Abs(Dot(d,Normal(p)))>1e-7)
            throw new DomainException("OUT_OF_SCOPE","Source or reference point does not belong to the explicit workplane.");
        var result=new Point2(Dot(d,p.XAxis),Dot(d,p.YAxis));
        if(Math.Abs(result.X)>1e6||Math.Abs(result.Y)>1e6)throw new DomainException("OUT_OF_SCOPE","Workplane coordinates exceed +/-1000000mm.");return result;
    }
    public static SketchPlaneSpec Parse(JsonElement value)
    {
        void Need(bool ok,string message){if(!ok)throw new DomainException("INVALID_ARGUMENT",message);}
        Need(value.ValueKind==JsonValueKind.Object,"workplane must contain origin, x_axis and y_axis.");
        var fields=value.EnumerateObject().ToArray();Need(fields.Length==3&&fields.Select(p=>p.Name).ToHashSet().SetEquals(new[]{"origin","x_axis","y_axis"}),"workplane fields must be exactly origin, x_axis and y_axis.");
        Point3 Triple(string name)
        {
            var a=value.GetProperty(name);Need(a.ValueKind==JsonValueKind.Array&&a.GetArrayLength()==3,"workplane vectors must contain three numbers.");
            Need(a.EnumerateArray().All(v=>v.ValueKind==JsonValueKind.Number&&v.TryGetDouble(out var n)&&double.IsFinite(n)&&Math.Abs(n)<=1e6),"workplane coordinates must be finite and within +/-1000000mm.");
            return new(a[0].GetDouble(),a[1].GetDouble(),a[2].GetDouble());
        }
        var frame=new SketchPlaneSpec(Triple("origin"),Triple("x_axis"),Triple("y_axis"));
        if(Math.Abs(Dot(frame.XAxis,frame.XAxis)-1)>1e-10||Math.Abs(Dot(frame.YAxis,frame.YAxis)-1)>1e-10||Math.Abs(Dot(frame.XAxis,frame.YAxis))>1e-10)
            throw new DomainException("INVALID_GEOMETRY","workplane axes must be perpendicular unit vectors, without implicit normalization.");
        return frame;
    }
}
