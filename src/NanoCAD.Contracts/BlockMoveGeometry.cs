using System.Text.Json;

namespace NanoCAD.Contracts;

// Independent WCS geometry for moving an occurrence without redefining its block.
public static class BlockMoveGeometry
{
    public static void Point(Point2 p)
    {
        if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) || Math.Abs(p.X)>1e6 || Math.Abs(p.Y)>1e6)
                throw new DomainException("INVALID_GEOMETRY","Block transform coordinates must remain within +/-1e6mm.");
    }
    public static void Budget(int references,int attributes,int vertices)
    {
        if(references<0 || attributes<0 || vertices<0)
            throw new DomainException("INVALID_GEOMETRY","Native object/attribute/vertex counts must be nonnegative.");
    }
    public static Point2 Transform(Point2 p,EditSpec operation)
    {
        Point(p);Point2 result;
        if(operation is MoveSpec move)
        {Point(move.Displacement);result=new(p.X+move.Displacement.X,p.Y+move.Displacement.Y);}
        else if(operation is RotateSpec rotate)
        {
            Point(rotate.Center);BlockWriteValidation.NormalizeRotation(rotate.Angle);
            double c=Math.Cos(rotate.Angle),s=Math.Sin(rotate.Angle),x=p.X-rotate.Center.X,y=p.Y-rotate.Center.Y;
            result=new(rotate.Center.X+c*x-s*y,rotate.Center.Y+s*x+c*y);
        }
        else if(operation is ScaleSpec scale)
        {Point(scale.Center);Scale(1,scale);result=new(scale.Center.X+scale.Factor*(p.X-scale.Center.X),scale.Center.Y+scale.Factor*(p.Y-scale.Center.Y));}
        else throw new DomainException("UNSUPPORTED_BLOCK_CONTENT","Existing block references support move, rotate and positive uniform scale only.");
        Point(result);return result;
    }
    public static double Rotation(double previous,EditSpec operation)=>BlockWriteValidation.NormalizeRotation(previous+(operation is RotateSpec r?r.Angle:0));
    public static BlockPlacement Transform(BlockPlacement p,EditSpec operation)
    {
        Point(p.BasePoint);_ = p.Matrix();
        return p with {Position=Transform(p.Position,operation),Rotation=Rotation(p.Rotation,operation),Scale=Scale(p.Scale,operation)};
    }
    public static double Scale(double previous,EditSpec operation)
    {
        double factor=operation is ScaleSpec scale?scale.Factor:1,result=previous*factor;
        if(!double.IsFinite(previous)||previous is <1e-6 or >1e6||!double.IsFinite(factor)||factor is <1e-6 or >1e6||!double.IsFinite(result)||result is <1e-6 or >1e6)
            throw new DomainException("INVALID_GEOMETRY","Existing/resulting uniform block scale and factor must stay within1e-6..1e6.");
        return result;
    }
    public static bool UniformScale(double x,double y,double z)=>
        double.IsFinite(x)&&double.IsFinite(y)&&double.IsFinite(z)&&x is >=1e-6 and <=1e6&&y is >=1e-6 and <=1e6&&z is >=1e-6 and <=1e6&&
        Math.Abs(x-y)<=1e-12*Math.Max(x,y)&&Math.Abs(x-z)<=1e-12*Math.Max(x,z);
    public static JsonElement PropertiesAfter(JsonElement before,EditSpec operation,bool attribute)
    {
        if(operation is not ScaleSpec scale)return before;
        var result=before.EnumerateObject().ToDictionary(p=>p.Name,p=>(object)p.Value.Clone(),StringComparer.Ordinal);
        if(attribute)
        {
            double height=before.GetProperty("Height").GetDouble()*scale.Factor;
            if(!double.IsFinite(height)||height is <1e-9 or >1e9)throw new DomainException("INVALID_GEOMETRY","Scaled attribute height exceeds supported text geometry bounds.");
            result["Height"]=height;
        }
        else result["scale"]=before.GetProperty("scale").EnumerateArray().Select(v=>Scale(v.GetDouble(),operation)).ToArray();
        return Wire.Element(result);
    }
    public static bool Close(double actual,double expected)=>double.IsFinite(actual)&&double.IsFinite(expected)&&Math.Abs(actual-expected)<=1e-7+Math.Abs(expected)*1e-12;
    public static bool SameProperties(JsonElement a,JsonElement b)
    {
        if(a.ValueKind!=b.ValueKind)return false;
        if(a.ValueKind==JsonValueKind.Object)
            return a.EnumerateObject().Count()==b.EnumerateObject().Count()&&a.EnumerateObject().All(p=>b.TryGetProperty(p.Name,out var v)&&SameProperties(p.Value,v));
        if(a.ValueKind==JsonValueKind.Array)
            return a.GetArrayLength()==b.GetArrayLength()&&a.EnumerateArray().Zip(b.EnumerateArray()).All(p=>SameProperties(p.First,p.Second));
        if(a.ValueKind==JsonValueKind.Number)
        {double x=a.GetDouble(),y=b.GetDouble();return double.IsFinite(x)&&double.IsFinite(y)&&Math.Abs(x-y)<=1e-12+Math.Abs(y)*1e-12;}
        return a.GetRawText()==b.GetRawText();
    }
}
