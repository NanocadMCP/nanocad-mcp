namespace NanoCAD.Contracts;

public static class CurveRelationsVerification
{
    public const double Tolerance=1e-7;
    private static DomainException Invalid()=>new("NATIVE_RELATION_MISMATCH","Native overlap intervals, lengths or witnesses are inconsistent with finite source curves.");
    public static void Range(double lower,double upper,double sourceLower,double sourceUpper)
    {
        if(new[]{lower,upper,sourceLower,sourceUpper}.Any(v=>!double.IsFinite(v))||upper<=lower||sourceUpper<=sourceLower||
            lower<sourceLower-1e-9||upper>sourceUpper+1e-9)throw Invalid();
    }
    public static double Lengths(double first,double second)
    {
        if(!double.IsFinite(first)||!double.IsFinite(second)||first<=Tolerance||second<=Tolerance||Math.Abs(first-second)>Tolerance)throw Invalid();
        return first;
    }
    public static void Endpoints(double[] firstStart,double[] firstEnd,double[] secondStart,double[] secondEnd)
    {
        bool Near(double[] a,double[] b)=>CurveDistanceVerification.Distance(a,b)<=Tolerance;
        if(!(Near(firstStart,secondStart)&&Near(firstEnd,secondEnd)||Near(firstStart,secondEnd)&&Near(firstEnd,secondStart)))throw Invalid();
    }
}
