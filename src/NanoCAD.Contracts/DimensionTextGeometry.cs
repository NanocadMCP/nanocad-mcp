using System.Text.Json;

namespace NanoCAD.Contracts;

public static class DimensionTextGeometry
{
    // DimLinePoint may move along the same dimension line during native text layout.
    // Measured endpoints and perpendicular line offset must remain unchanged.
    public static bool SameDefinition(JsonElement before,JsonElement after)
    {
        if(before.ValueKind!=JsonValueKind.Object || after.ValueKind!=JsonValueKind.Object || before.EnumerateObject().Count()!=after.EnumerateObject().Count())return false;
        bool Same(JsonElement a,JsonElement b)
        {
            if(a.ValueKind!=b.ValueKind)return false;
            if(a.ValueKind==JsonValueKind.Array)return a.GetArrayLength()==b.GetArrayLength() && a.EnumerateArray().Zip(b.EnumerateArray()).All(p=>Same(p.First,p.Second));
            if(a.ValueKind==JsonValueKind.Number)return a.TryGetDouble(out var x)&&b.TryGetDouble(out var y)&&double.IsFinite(x)&&double.IsFinite(y)&&Math.Abs(x-y)<=1e-8;
            return a.ToString()==b.ToString();
        }
        foreach(var p in before.EnumerateObject())
            if(!after.TryGetProperty(p.Name,out var value) || p.Name!="dimline_point"&&!Same(p.Value,value))return false;
        if(!before.TryGetProperty("dimline_point",out var oldPoint))return true;
        var newPoint=after.GetProperty("dimline_point");
        if(oldPoint.GetArrayLength()!=3 || newPoint.GetArrayLength()!=3 || !Same(oldPoint[2],newPoint[2]))return false;
        double ux,uy;
        if(before.TryGetProperty("rotation",out var angle)){ux=Math.Cos(angle.GetDouble());uy=Math.Sin(angle.GetDouble());}
        else
        {
            var first=before.GetProperty("xline1_point");var second=before.GetProperty("xline2_point");
            ux=second[0].GetDouble()-first[0].GetDouble();uy=second[1].GetDouble()-first[1].GetDouble();
            var length=Math.Sqrt(ux*ux+uy*uy);if(!double.IsFinite(length)||length<1e-9)return false;ux/=length;uy/=length;
        }
        var cross=(newPoint[0].GetDouble()-oldPoint[0].GetDouble())*uy-(newPoint[1].GetDouble()-oldPoint[1].GetDouble())*ux;
        return double.IsFinite(cross)&&Math.Abs(cross)<=1e-7;
    }
}
