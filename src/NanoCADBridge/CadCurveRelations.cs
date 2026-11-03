using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CadCurveRelations
{
    private static double[] P(Point3d p)=>new[]{p.X,p.Y,p.Z};
    private static void RequireXy(Curve curve,SplineGeometry? spline)
    {
        bool Normal(Vector3d n)=>n.X==0&&n.Y==0&&Math.Abs(n.Z)==1;
        bool planar=curve switch
        {
            Line line=>line.StartPoint.Z==0&&line.EndPoint.Z==0,
            Circle circle=>circle.Center.Z==0&&Normal(circle.Normal),
            Ellipse ellipse=>ellipse.Center.Z==0&&Normal(ellipse.Normal),
            Spline=>spline!=null&&SplineVerification.LiesInWcsXy(spline),
            Arc arc=>arc.Center.Z==0&&Normal(arc.Normal),
            Polyline polyline=>polyline.Elevation==0&&Normal(polyline.Normal),
            _=>false
        };
        if(!planar)throw new DomainException("PLANAR_GEOMETRY_REQUIRED","Relations requires both curves to lie exactly in WCS XY/Z=0; no projection is performed.");
    }
    private static void DisposeIntervals(IEnumerable<Interval?> intervals)
    {
        System.Exception? failure=null;
        foreach(var interval in intervals.Distinct<Interval?>(ReferenceEqualityComparer.Instance))
            try{interval?.Dispose();}catch(System.Exception error){failure??=error;}
        if(failure!=null)throw failure;
    }
    internal static (double[][] Points,object[] Overlaps) Read(Curve first,Curve second,Action check,SplineGeometry? firstSpline=null,SplineGeometry? secondSpline=null,SketchPlaneSpec? workplane=null)
    {
        var normal=Vector3d.ZAxis;
        if(workplane==null){RequireXy(first,firstSpline);RequireXy(second,secondSpline);}
        else {normal=CadCurveWorkplane.Require(first,workplane,check);CadCurveWorkplane.Require(second,workplane,check);}
        check();
        using var firstGeometry=first.GetGeCurve();check();using var secondGeometry=second.GetGeCurve();check();
        using var firstRange=firstGeometry.GetInterval();using var secondRange=secondGeometry.GetInterval();
        if(!firstRange.IsBounded||!secondRange.IsBounded)throw new DomainException("NATIVE_RELATION_MISMATCH","Geometric source intervals must be finite.");
        CurveRelationsVerification.Range(firstRange.LowerBound,firstRange.UpperBound,firstRange.LowerBound,firstRange.UpperBound);
        CurveRelationsVerification.Range(secondRange.LowerBound,secondRange.UpperBound,secondRange.LowerBound,secondRange.UpperBound);
        using var relation=new CurveCurveIntersector3d(firstGeometry,secondGeometry,firstRange,secondRange,normal);
        check();int pointCount=relation.NumberOfIntersectionPoints;int overlapCount=relation.OverlapCount();check();
        if(pointCount<0||overlapCount<0)throw new DomainException("NATIVE_RELATION_MISMATCH","Negative native relation inventory count.");
        var capacity=new SerializedCapacity357(Wire.PayloadBudget-8192,check);
        void Membership(Point3d point)
        {
            check();var a=first.GetClosestPointTo(point,false);check();var b=second.GetClosestPointTo(point,false);check();
            if(CurveDistanceVerification.Distance(P(point),P(a))>CurveRelationsVerification.Tolerance||CurveDistanceVerification.Distance(P(point),P(b))>CurveRelationsVerification.Tolerance)
                throw new DomainException("NATIVE_RELATION_MISMATCH","Native relation witness is outside a finite source curve.");
        }
        var points=new List<double[]>();
        for(int i=0;i<pointCount;i++){check();var point=relation.GetIntersectionPoint(i);Membership(point);var value=P(point);capacity.Add(value);points.Add(value);}
        var overlaps=new List<object>();
        for(int i=0;i<overlapCount;i++)
        {
            check();Interval[]? ranges=null;
            try
            {
                ranges=relation.GetOverlapRanges(i);check();
                if(ranges==null||ranges.Length!=2||ranges.Any(r=>r==null||!r.IsBounded))throw new DomainException("NATIVE_RELATION_MISMATCH","Native overlap requires two bounded intervals.");
                var a=ranges[0];var b=ranges[1];
                CurveRelationsVerification.Range(a.LowerBound,a.UpperBound,firstRange.LowerBound,firstRange.UpperBound);
                CurveRelationsVerification.Range(b.LowerBound,b.UpperBound,secondRange.LowerBound,secondRange.UpperBound);
                double firstLength=firstGeometry.GetLength(a.LowerBound,a.UpperBound,CurveRelationsVerification.Tolerance);check();
                double secondLength=secondGeometry.GetLength(b.LowerBound,b.UpperBound,CurveRelationsVerification.Tolerance);check();
                double length=CurveRelationsVerification.Lengths(firstLength,secondLength);
                var a0=firstGeometry.EvaluatePoint(a.LowerBound);var a1=firstGeometry.EvaluatePoint(a.UpperBound);
                var b0=secondGeometry.EvaluatePoint(b.LowerBound);var b1=secondGeometry.EvaluatePoint(b.UpperBound);
                Membership(a0);Membership(a1);Membership(b0);Membership(b1);
                // Positive native lengths distinguish a full circle from a single
                // repeated endpoint; midpoint witnesses also guard the interior.
                Membership(firstGeometry.EvaluatePoint((a.LowerBound+a.UpperBound)/2));
                Membership(secondGeometry.EvaluatePoint((b.LowerBound+b.UpperBound)/2));
                CurveRelationsVerification.Endpoints(P(a0),P(a1),P(b0),P(b1));
                var value=new{first_start=P(a0),first_end=P(a1),second_start=P(b0),second_end=P(b1),length};capacity.Add(value);overlaps.Add(value);
            }
            finally{if(ranges!=null)DisposeIntervals(ranges);}
        }
        check();return (points.ToArray(),overlaps.ToArray());
    }
}
