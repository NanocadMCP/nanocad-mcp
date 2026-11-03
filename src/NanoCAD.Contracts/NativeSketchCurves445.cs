using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record NativeSketchSpline445(int Degree,double[] Knots,double[] Weights,bool Closed);
public sealed record NativeSketchEllipse445(double StartAngle,double EndAngle);
// Full native WCS definition. Start/End/Center in SketchConstraintCurve retain
// their separate local sketch-coordinate meaning.
public sealed record NativeSketchEllipseGeometry445(Point3 Center,Point3 MajorAxis,Point3 MinorAxis,
    double MajorRadius,double MinorRadius,double StartAngle,double EndAngle);

public static class NativeSketchCurveValidation445
{
    static DomainException Invalid(string path,string message)=>new("INVALID_ARGUMENT",message,false,new{path});
    static double Number(JsonElement value,string path)
    {if(value.ValueKind!=JsonValueKind.Number||!value.TryGetDouble(out double n)||!double.IsFinite(n))throw Invalid(path,"Use finite numeric values.");return n;}
    static Point3 Point(JsonElement value,string path)
    {
        if(value.ValueKind!=JsonValueKind.Array||value.GetArrayLength()!=3)throw Invalid(path,"Use a WCS XYZ triple.");
        var p=new Point3(Number(value[0],path),Number(value[1],path),Number(value[2],path));
        if(Math.Max(Math.Abs(p.X),Math.Max(Math.Abs(p.Y),Math.Abs(p.Z)))>1e6)throw Invalid(path,"Use WCS coordinates within drawing range.");return p;
    }
    static Point3 Delta(Point3 a,Point3 b)=>new(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
    static double Length(Point3 a)=>Math.Sqrt(DrawingWorkplane.Dot(a,a));
    public static NativeSketchPrimitive385 Parse(JsonElement row,string key,string kind,SketchPlaneSpec? plane,string path)
    {
        foreach(var property in row.EnumerateObject())
            if(property.Name is not ("key" or "kind" or "points")&&
                !(kind=="spline"&&property.Name is "degree" or "knots" or "weights" or "closed")&&
                !(kind=="ellipse"&&property.Name is "start_angle" or "end_angle"))throw Invalid(path,"Unknown geometry field.");
        if(!row.TryGetProperty("points",out var raw)||raw.ValueKind!=JsonValueKind.Array)throw Invalid(path+".points","Supply WCS defining points.");
        var points=raw.EnumerateArray().Select((p,i)=>Point(p,path+".points["+i+"]")).ToArray();
        NativeSketchPrimitive385 result;
        if(kind=="spline")
        {
            if(!row.TryGetProperty("degree",out var d)||d.ValueKind!=JsonValueKind.Number||!d.TryGetInt32(out int degree)||degree<1||points.Length<=degree)
                throw Invalid(path,"A native NURBS requires a positive integer degree and at least degree+1 control points.");
            if(!row.TryGetProperty("knots",out var k)||k.ValueKind!=JsonValueKind.Array||k.GetArrayLength()!=(long)points.Length+degree+1)
                throw Invalid(path+".knots","Knot count must equal control count+degree+1.");
            var knots=k.EnumerateArray().Select((v,i)=>Number(v,path+".knots["+i+"]")).ToArray();
            if(knots.Zip(knots.Skip(1),(a,b)=>a<=b).Any(ordered=>!ordered)||knots[degree]>=knots[points.Length]||!double.IsFinite(knots[points.Length]-knots[degree])||
                !knots.Take(degree+1).All(v=>v==knots[degree])||!knots.Skip(points.Length).All(v=>v==knots[points.Length]))
                throw Invalid(path+".knots","Use an ordered nondegenerate clamped knot vector with degree+1 endpoint knots.");
            int multiplicity=0;double previous=double.NaN;
            for(int i=degree+1;i<points.Length;i++)
            {
                double knot=knots[i];multiplicity=knot==previous?multiplicity+1:1;previous=knot;
                if(knot<=knots[degree]||knot>=knots[points.Length]||multiplicity>degree)
                    throw Invalid(path+".knots","Internal knot multiplicity cannot exceed degree and knots must lie inside the domain.");
            }
            double[] weights=[];
            if(row.TryGetProperty("weights",out var w))
            {
                if(w.ValueKind!=JsonValueKind.Array||w.GetArrayLength()!=0&&w.GetArrayLength()!=points.Length)throw Invalid(path+".weights","Use no weights or one positive weight per control point.");
                weights=w.EnumerateArray().Select((v,i)=>Number(v,path+".weights["+i+"]")).ToArray();
                if(weights.Any(v=>v<=0))throw Invalid(path+".weights","NURBS weights must be positive.");
            }
            bool closed=false;if(row.TryGetProperty("closed",out var c))
            {if(c.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw Invalid(path+".closed","Use a boolean.");closed=c.GetBoolean();}
            bool same=Length(Delta(points[0],points[^1]))<=1e-7;
            if(closed?points[0]!=points[^1]:same||points.All(p=>Length(Delta(p,points[0]))<=1e-7))throw Invalid(path+".points","Closed clamped NURBS repeat the first endpoint control exactly; open NURBS have separated endpoints and geometry must be nondegenerate.");
            if(points.All(p=>Length(Delta(p,points[0]))<=1e-7))throw Invalid(path+".points","Native NURBS geometry must be nondegenerate.");
            result=new(key,kind,points,null,new(degree,knots,weights,closed));
        }
        else
        {
            if(points.Length!=3)throw Invalid(path+".points","Ellipse uses center, major-axis endpoint, minor-axis endpoint in WCS.");
            var major=Delta(points[1],points[0]);var minor=Delta(points[2],points[0]);double a=Length(major),b=Length(minor);
            if(a<=1e-7||b<=1e-7||a>1e6||b>a||Math.Abs(DrawingWorkplane.Dot(major,minor)/(a*b))>1e-9)
                throw Invalid(path+".points","Ellipse axes must be nonzero perpendicular vectors with major radius at least minor radius.");
            double start=row.TryGetProperty("start_angle",out var s)?Number(s,path+".start_angle"):0;
            double end=row.TryGetProperty("end_angle",out var e)?Number(e,path+".end_angle"):2*Math.PI;
            if(end<=start||end-start>2*Math.PI+1e-10)throw Invalid(path,"Ellipse end_angle must follow start_angle with a span no greater than2pi radians.");
            result=new(key,kind,points,null,Ellipse:new(start,end));
        }
        if(plane!=null)RequirePlane(result,plane,path);SerializedCapacity357.Check(result);return result;
    }
    public static void RequirePlane(NativeSketchPrimitive385 value,SketchPlaneSpec plane,string path)
    {foreach(var p in value.Points)DrawingWorkplane.Local(plane,p);}
}
