namespace NanoCAD.Contracts;

public static class CurveDistanceVerification
{
    public const double AbsoluteTolerance=1e-7;
    private static void Point(double[] value)
    {
        if(value.Length!=3||value.Any(x=>!double.IsFinite(x)))throw Invalid();
    }
    private static DomainException Invalid()=>new("NATIVE_DISTANCE_MISMATCH","Native closest points are non-finite, inconsistent with the reported distance, or outside the finite source curves.");
    public static double Distance(double[] first,double[] second)
    {
        Point(first);Point(second);
        double value=Math.Sqrt(first.Zip(second,(a,b)=>(a-b)*(a-b)).Sum());
        if(!double.IsFinite(value))throw Invalid();return value;
    }
    public static double Verify(double[] first,double[] second,double reportedDistance,double[] firstOnFiniteCurve,double[] secondOnFiniteCurve)
    {
        double distance=Distance(first,second);
        if(!double.IsFinite(reportedDistance)||reportedDistance<0||Math.Abs(distance-reportedDistance)>AbsoluteTolerance||
            Distance(first,firstOnFiniteCurve)>AbsoluteTolerance||Distance(second,secondOnFiniteCurve)>AbsoluteTolerance)throw Invalid();
        return distance;
    }
}
