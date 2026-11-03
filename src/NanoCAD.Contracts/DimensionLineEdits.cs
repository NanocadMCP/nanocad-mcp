using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.Contracts;

public sealed record SetDimensionLineSpec(string[] Targets,Point2 ExpectedPoint,Point2 Point):EditSpec("set_dimension_line",Targets);
public static partial class ModifyValidation
{
    private static SetDimensionLineSpec ParseDimensionLine(JsonElement item,string path,string[] handles)
    {
        Shape(item,path,"operation","handles","expected_dimline_point","dimline_point");
        if(handles.Length!=1)throw Invalid(path,"One aligned/rotated dimension handle required.");
        Point2 Read(string field){var p=Point(item,field,path);if(Math.Abs(p.X)>1e6||Math.Abs(p.Y)>1e6)throw Invalid(path+"."+field,"Coordinates exceed +/-1000000mm.","INVALID_GEOMETRY");return p;}
        return new(handles,Read("expected_dimline_point"),Read("dimline_point"));
    }
}
public static class DimensionLineGeometry
{
    public static JsonElement At(JsonElement definition,Point2 point)
    {
        var value=JsonNode.Parse(definition.GetRawText())!;value["dimline_point"]=new JsonArray(point.X,point.Y,0);
        return Wire.Element(value);
    }
    public static (double X,double Y,double Measurement) Axis(JsonElement definition)
    {
        var a=definition.GetProperty("xline1_point");var b=definition.GetProperty("xline2_point");
        double dx=b[0].GetDouble()-a[0].GetDouble(),dy=b[1].GetDouble()-a[1].GetDouble();
        double length=Math.Sqrt(dx*dx+dy*dy);
        if(!double.IsFinite(length)||length<0.001)throw new DomainException("INVALID_GEOMETRY","Dimension endpoints coincide or are too close.");
        double ux=dx/length,uy=dy/length;
        if(definition.TryGetProperty("rotation",out var angle)){ux=Math.Cos(angle.GetDouble());uy=Math.Sin(angle.GetDouble());length=Math.Abs(dx*ux+dy*uy);}
        if(!double.IsFinite(length)||length<0.001)throw new DomainException("INVALID_GEOMETRY","Dimension projected measurement is too small.");
        return (ux,uy,length);
    }
    public static JsonElement Expected(JsonElement before,SetDimensionLineSpec edit)
    {
        if(!DimensionTextGeometry.SameDefinition(before,At(before,edit.ExpectedPoint)))
            throw new DomainException("DIMENSION_LINE_CHANGED","Dimension line differs from expected_dimline_point after preceding edits.");
        var axis=Axis(before);var origin=before.GetProperty("xline1_point");
        double offset=(edit.Point.X-origin[0].GetDouble())*axis.Y-(edit.Point.Y-origin[1].GetDouble())*axis.X;
        if(!double.IsFinite(offset)||Math.Abs(offset)<0.001)throw new DomainException("INVALID_GEOMETRY","Dimension line must be at least0.001mm from the first definition point along its normal.");
        return At(before,edit.Point);
    }
}
