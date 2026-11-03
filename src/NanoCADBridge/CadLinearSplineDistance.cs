using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class CadLinearSplineDistance
{
    internal static (double[] First,double[] Second,double Distance) Read(Curve first,Curve second,IReadOnlyDictionary<Curve,SplineGeometry> splines,Action check)
    {
        double[][] Vertices(Curve curve)
        {
            check();
            if(curve is Line line)return new[]{new[]{line.StartPoint.X,line.StartPoint.Y,line.StartPoint.Z},new[]{line.EndPoint.X,line.EndPoint.Y,line.EndPoint.Z}};
            if(curve is Spline&&splines.TryGetValue(curve,out var data)&&LinearSplineSegments.TryVertices(data) is { } vertices)return vertices;
            if(curve is Polyline polyline)
            {
                var points=new List<double[]>();
                for(int i=0;i<polyline.NumberOfVertices;i++)
                {
                    check();
                    if((i<polyline.NumberOfVertices-1||polyline.Closed)&&polyline.GetBulgeAt(i)!=0)
                        throw new DomainException("UNSUPPORTED_SPLINE","Closed degree1 spline distance requires straight polyline segments; bulges are not approximated.");
                    var p=polyline.GetPoint3dAt(i);points.Add(new[]{p.X,p.Y,p.Z});
                }
                if(polyline.Closed)points.Add(points[0].ToArray());
                return points.ToArray();
            }
            throw new DomainException("UNSUPPORTED_SPLINE","Closed degree1 spline distance supports Line, straight lightweight Polyline, or clamped degree1 Spline; curved operands remain excluded.");
        }
        if(first is Arc||second is Arc)
        {
            bool reverse=first is Arc;var arc=(Arc)(reverse?first:second);
            check();var normal=arc.Normal;var center=arc.Center;var start=arc.StartPoint;
            if(normal.X!=0||normal.Y!=0||Math.Abs(normal.Z)!=1||start.Z!=0)
                throw new DomainException("UNSUPPORTED_SPLINE","Arc operand must lie in WCS XY/Z0.");
            var result=FiniteArcDistance.Minimum(Vertices(reverse?second:first),new[]{center.X,center.Y,center.Z},arc.Radius,
                Math.Atan2(start.Y-center.Y,start.X-center.X),normal.Z*arc.TotalAngle,check);
            return reverse?(result.Second,result.First,result.Distance):result;
        }
        if(first is Circle||second is Circle)
        {
            bool reverse=first is Circle;var circle=(Circle)(reverse?first:second);
            check();
            if(circle.Normal.X!=0||circle.Normal.Y!=0||Math.Abs(circle.Normal.Z)!=1)
                throw new DomainException("UNSUPPORTED_SPLINE","Circle operand must lie in WCS XY/Z0.");
            var center=circle.Center;var result=FiniteSegmentDistance.ToCircle(Vertices(reverse?second:first),new[]{center.X,center.Y,center.Z},circle.Radius,check);
            return reverse?(result.Second,result.First,result.Distance):result;
        }
        return FiniteSegmentDistance.Minimum(Vertices(first),Vertices(second),check);
    }
}
