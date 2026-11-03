namespace NanoCAD.Contracts;

public sealed record EllipseGeometry(double[] center,double[] major_axis,double[] minor_axis,double major_radius,double minor_radius,
    double radius_ratio,double[] normal,double[] start_point,double[] end_point,double start_parameter,double end_parameter,bool closed);

public static class EllipseVerification
{
    public const double Tolerance=1e-7;
    private static DomainException Invalid()=>new("NATIVE_ELLIPSE_MISMATCH","Native ellipse axes, parameters, endpoints or closed state are inconsistent or outside finite bounds.");
    private static double Length(double[] v)=>Math.Sqrt(v.Sum(x=>x*x));
    private static double[] Unit(double[] v)=>v.Select(x=>x/Length(v)).ToArray();
    private static bool Near(double a,double b)=>double.IsFinite(a)&&double.IsFinite(b)&&Math.Abs(a-b)<=Tolerance;
    public static void Verify(EllipseGeometry e)
    {
        foreach(var p in new[]{e.center,e.major_axis,e.minor_axis,e.normal,e.start_point,e.end_point})
            if(p.Length!=3||p.Any(x=>!double.IsFinite(x)||Math.Abs(x)>1e9))throw Invalid();
        if(!double.IsFinite(e.major_radius)||!double.IsFinite(e.minor_radius)||e.major_radius is <=0 or >1e9||e.minor_radius is <=0 or >1e9||
            !double.IsFinite(e.radius_ratio)||e.radius_ratio is <=0 or >1||!Near(e.major_radius,Length(e.major_axis))||!Near(e.minor_radius,Length(e.minor_axis))||
            !Near(e.minor_radius/e.major_radius,e.radius_ratio)||!Near(Length(e.normal),1))throw Invalid();
        var a=Unit(e.major_axis);var b=Unit(e.minor_axis);
        if(!Near(a.Zip(b,(x,y)=>x*y).Sum(),0))throw Invalid();
        var cross=new[]{a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]};
        if(!cross.Zip(e.normal,Near).All(x=>x))throw Invalid();
        double sweep=e.end_parameter-e.start_parameter;
        if(!double.IsFinite(e.start_parameter)||!double.IsFinite(e.end_parameter)||Math.Abs(e.start_parameter)>4*Math.PI||Math.Abs(e.end_parameter)>4*Math.PI||
            !double.IsFinite(sweep)||sweep<=0||sweep>2*Math.PI+1e-9||e.closed!= (Math.Abs(sweep-2*Math.PI)<=1e-9))throw Invalid();
        double[] At(double t)=>Enumerable.Range(0,3).Select(i=>e.center[i]+e.major_axis[i]*Math.Cos(t)+e.minor_axis[i]*Math.Sin(t)).ToArray();
        if(!At(e.start_parameter).Zip(e.start_point,Near).All(x=>x)||!At(e.end_parameter).Zip(e.end_point,Near).All(x=>x))throw Invalid();
    }
    public static double LengthFromDistances(double start,double end)
    {
        double length=end-start;
        if(!double.IsFinite(start)||!double.IsFinite(end)||!double.IsFinite(length)||start<0||length<=0)throw Invalid();
        return length;
    }
}
