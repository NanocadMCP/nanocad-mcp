namespace NanoCAD.Contracts;

public sealed record EllipseSpec(Point2 Center, Point2 MajorAxis, double RadiusRatio, double StartParameter, double SweepParameter, string LayerName) : EntitySpec("Ellipse", LayerName);

public static partial class CreateValidation
{
    internal static EllipseSpec ParseEllipse(System.Text.Json.JsonElement item,string path,string layer)
    {
        Shape(item,path,"type","layer","center","major_axis","radius_ratio","start_parameter","sweep_parameter");
        var center=Point(item,"center",path);var axis=Point(item,"major_axis",path);
        double ratio=Number(Required(item,"radius_ratio",path),path+".radius_ratio",.01,1);
        double start=Number(Required(item,"start_parameter",path),path+".start_parameter",0,2*Math.PI);
        double sweep=Number(Required(item,"sweep_parameter",path),path+".sweep_parameter",1e-6,2*Math.PI);
        double radius=Math.Sqrt(axis.X*axis.X+axis.Y*axis.Y);
        double extentX=Math.Sqrt(axis.X*axis.X+axis.Y*axis.Y*ratio*ratio),extentY=Math.Sqrt(axis.Y*axis.Y+axis.X*axis.X*ratio*ratio);
        if(radius is <.001 or >1e6 || radius*ratio<.001 || Math.Abs(center.X)+extentX>Maximum || Math.Abs(center.Y)+extentY>Maximum)
            throw Invalid(path,"Ellipse major radius must be0.001..1e6, minor radius>=0.001, and the full ellipse must stay inside coordinate bounds.","INVALID_GEOMETRY");
        if(start>=2*Math.PI || sweep==2*Math.PI && start!=0 || sweep!=2*Math.PI && 2*Math.PI-sweep<1e-6)
            throw Invalid(path,"Start parameter must be below2pi; full ellipse requires start0, partial sweep must stay at least1e-6 below2pi.","INVALID_GEOMETRY");
        return new(center,axis,ratio,start,sweep,layer);
    }
}

public static class EllipseCreation
{
    public static EllipseGeometry Expected(EllipseSpec spec)
    {
        var major=new[]{spec.MajorAxis.X,spec.MajorAxis.Y,0};var minor=new[]{-spec.MajorAxis.Y*spec.RadiusRatio,spec.MajorAxis.X*spec.RadiusRatio,0};
        var center=new[]{spec.Center.X,spec.Center.Y,0};double radius=Math.Sqrt(major[0]*major[0]+major[1]*major[1]);
        double[] At(double t)=>Enumerable.Range(0,3).Select(i=>center[i]+major[i]*Math.Cos(t)+minor[i]*Math.Sin(t)).ToArray();
        return new(center,major,minor,radius,radius*spec.RadiusRatio,spec.RadiusRatio,new[]{0d,0d,1d},
            At(spec.StartParameter),At(spec.StartParameter+spec.SweepParameter),spec.StartParameter,spec.StartParameter+spec.SweepParameter,spec.SweepParameter==2*Math.PI);
    }
    public static void RequireMatch(EllipseSpec spec,EllipseGeometry actual)
    {
        EllipseVerification.Verify(actual);var expected=Expected(spec);
        bool Near(double a,double b)=>Math.Abs(a-b)<=EllipseVerification.Tolerance;
        bool Point(double[] a,double[] b)=>a.Length==b.Length&&a.Zip(b,Near).All(x=>x);
        if(!Point(actual.center,expected.center)||!Point(actual.major_axis,expected.major_axis)||!Point(actual.minor_axis,expected.minor_axis)||
            !Point(actual.normal,expected.normal)||!Point(actual.start_point,expected.start_point)||!Point(actual.end_point,expected.end_point)||
            !Near(actual.major_radius,expected.major_radius)||!Near(actual.minor_radius,expected.minor_radius)||!Near(actual.radius_ratio,expected.radius_ratio)||
            !Near(actual.start_parameter,expected.start_parameter)||!Near(actual.end_parameter,expected.end_parameter)||actual.closed!=expected.closed)
            throw new DomainException("NATIVE_ELLIPSE_MISMATCH","Constructed native ellipse differs from the requested WCS axes, ratio or parameter interval.",false,new{expected,actual});
    }
}
