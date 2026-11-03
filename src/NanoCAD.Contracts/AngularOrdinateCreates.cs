using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record AngularDimensionSpec(string Kind,Point2 FirstStart,Point2 FirstEnd,Point2 SecondStart,Point2 SecondEnd,Point2 ArcPoint,Point2 Center,double Angle,string DimensionStyle,string TextOverride,string LayerName):EntitySpec("Dimension",LayerName),IDimensionCreation;
public sealed record OrdinateDimensionSpec(Point2 Origin,Point2 DefiningPoint,Point2 LeaderEndPoint,bool UsingXAxis,string DimensionStyle,string TextOverride,string LayerName):EntitySpec("Dimension",LayerName),IDimensionCreation;

public static class AngularDimensionGeometry
{
    public const double MinimumAngle=1e-5;
    public static double Length(Point2 p,Point2 q)=>Math.Sqrt((p.X-q.X)*(p.X-q.X)+(p.Y-q.Y)*(p.Y-q.Y));
    public static double Turn(Point2 first,Point2 second)
    {
        var angle=Math.Atan2(first.X*second.Y-first.Y*second.X,first.X*second.X+first.Y*second.Y);
        return angle<0?angle+2*Math.PI:angle;
    }
    public static Point2 Difference(Point2 a,Point2 b)=>new(a.X-b.X,a.Y-b.Y);
    public static bool InSector(Point2 center,Point2 firstDirection,Point2 arc,double angle)
    {
        double position=Turn(firstDirection,Difference(arc,center));
        return position>1e-6 && position<angle-1e-6;
    }
}

public static partial class CreateValidation
{
    private static EntitySpec ParseAngularOrdinate(JsonElement item,string kind,string path,string style,string text,string layer)
    {
        if(!DimensionTextContent.PreservesMeasurement(text))throw Invalid(path,"New dimension types require at most256 plain characters and one <> measurement token; no fields or formatting.");
        Point2 P(string name)
        {
            var p=Point(item,name,path);
            if(Math.Abs(p.X)>1e6||Math.Abs(p.Y)>1e6)throw Invalid(path,"Dimension coordinates must be within +/-1000000 mm.","INVALID_GEOMETRY");
            return p;
        }
        if(kind=="ordinate")
        {
            Shape(item,path,"type","kind","layer","dimension_style","text_override","origin","defining_point","leader_end_point","axis");
            string axis=Text(item,"axis",path,1);if(axis is not("x" or "y"))throw Invalid(path,"Ordinate axis must be x or y.");
            var origin=P("origin");var point=P("defining_point");var end=P("leader_end_point");
            double coordinate=axis=="x"?point.X-origin.X:point.Y-origin.Y;
            double extension=axis=="x"?end.Y-point.Y:end.X-point.X;
            if(Math.Abs(coordinate)<.001||Math.Abs(extension)<.001)throw Invalid(path,"Ordinate coordinate and perpendicular leader extension must be at least0.001 mm.","INVALID_GEOMETRY");
            return new OrdinateDimensionSpec(origin,point,end,axis=="x",style,text,layer);
        }
        Point2 a,b,c,d,center;
        if(kind=="angular_3point")
        {
            Shape(item,path,"type","kind","layer","dimension_style","text_override","center","xline1","xline2","arc_point");
            center=P("center");a=c=center;b=P("xline1");d=P("xline2");
        }
        else
        {
            Shape(item,path,"type","kind","layer","dimension_style","text_override","xline1_start","xline1_end","xline2_start","xline2_end","arc_point");
            a=P("xline1_start");b=P("xline1_end");c=P("xline2_start");d=P("xline2_end");center=new(0,0);
        }
        var first=AngularDimensionGeometry.Difference(b,a);var second=AngularDimensionGeometry.Difference(d,c);
        double length1=AngularDimensionGeometry.Length(a,b),length2=AngularDimensionGeometry.Length(c,d);
        if(length1<.001||length2<.001)throw Invalid(path,"Angular defining segments must be at least0.001 mm.","INVALID_GEOMETRY");
        double angle=AngularDimensionGeometry.Turn(first,second);
        if(angle<AngularDimensionGeometry.MinimumAngle||angle>Math.PI-AngularDimensionGeometry.MinimumAngle)
            throw Invalid(path,"Order rays/line directions counterclockwise with an angle in1e-5..pi-1e-5; parallel and reflex sectors are unsupported.","INVALID_GEOMETRY");
        if(kind=="angular_2line")
        {
            var offset=AngularDimensionGeometry.Difference(c,a);
            double t=(offset.X*second.Y-offset.Y*second.X)/(first.X*second.Y-first.Y*second.X);
            center=new(a.X+t*first.X,a.Y+t*first.Y);
            if(!double.IsFinite(center.X)||!double.IsFinite(center.Y)||Math.Abs(center.X)>1e6||Math.Abs(center.Y)>1e6)throw Invalid(path,"Line intersection exceeds the bounded dimension coordinates.","INVALID_GEOMETRY");
        }
        var arc=P("arc_point");double radius=AngularDimensionGeometry.Length(center,arc);
        if(radius<.001||radius>1e6||!AngularDimensionGeometry.InSector(center,first,arc,angle))throw Invalid(path,"Arc point must be strictly inside the counterclockwise sector, at radius0.001..1000000 mm.","INVALID_GEOMETRY");
        return new AngularDimensionSpec(kind,a,b,c,d,arc,center,angle,style,text,layer);
    }
}
