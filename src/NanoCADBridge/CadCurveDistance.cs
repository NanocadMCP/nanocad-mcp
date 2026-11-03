using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CadCurveDistance
{
    internal static (double[] First,double[] Second,double Distance) Read(Curve first,Curve second,Action checkBudget)
    {
        // Database curves belong to the caller's ForRead transaction. Only the
        // geometric copies and PointOnCurve wrappers below are owned here.
        checkBudget();using var firstGeometry=first.GetGeCurve();
        checkBudget();using var secondGeometry=second.GetGeCurve();
        PointOnCurve3d[]? pair=null;
        try
        {
            checkBudget();pair=firstGeometry.GetClosestPointTo(secondGeometry);checkBudget();
            if(pair==null||pair.Length!=2||pair[0]==null||pair[1]==null)
                throw new DomainException("NATIVE_DISTANCE_MISMATCH","Native solver did not return two closest points.");
            var a=pair[0].Point;var b=pair[1].Point;
            double scalar=firstGeometry.GetDistanceTo(secondGeometry);checkBudget();
            var finiteA=first.GetClosestPointTo(a,false);checkBudget();
            var finiteB=second.GetClosestPointTo(b,false);checkBudget();
            double[] P(Point3d p)=>new[]{p.X,p.Y,p.Z};
            var pa=P(a);var pb=P(b);
            double distance=CurveDistanceVerification.Verify(pa,pb,scalar,P(finiteA),P(finiteB));
            return (pa,pb,distance);
        }
        finally
        {
            if(pair!=null)
            {
                System.Exception? cleanupFailure=null;
                foreach(var point in pair.Distinct<PointOnCurve3d>(ReferenceEqualityComparer.Instance))
                    try{point?.Dispose();}catch(System.Exception error){cleanupFailure??=error;}
                if(cleanupFailure!=null)throw cleanupFailure;
            }
        }
    }
}
