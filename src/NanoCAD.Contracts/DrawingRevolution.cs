namespace NanoCAD.Contracts;

public sealed record RevolutionOracle(double Volume,Point3 Centroid,Point3 Min,Point3 Max);
public static class DrawingRevolutionGeometry
{
    // Complete turns only. The contour stays on one side of its coplanar axis.
    // Signed polygon moments give the volume-weighted axial centroid, which is
    // generally different from the planar centroid for tapered sections.
    public static RevolutionOracle Measure(SketchConstraintCurve[] curves,SketchConstraintCurve axis)
    {
        void Need(bool ok,string message){if(!ok)throw new DomainException("INVALID_REVOLUTION_PROFILE",message);}
        Need(axis.Kind=="line"&&axis.Start!=null&&axis.End!=null,"Use a separate WCSXY Line as the revolution axis.");
        var start=axis.Start!;var end=axis.End!;double length=DrawingProfileGeometry.Distance(start,end);
        Need(double.IsFinite(length)&&length>=.001,"Axis is degenerate.");double ux=(end.X-start.X)/length,uy=(end.Y-start.Y)/length;
        if(curves.Any(c=>c.Kind=="arc"))return DrawingArcRevolution.Measure(curves,start,ux,uy);
        Point2 Local(Point2 p)=>new((p.X-start.X)*ux+(p.Y-start.Y)*uy,-(p.X-start.X)*uy+(p.Y-start.Y)*ux);
        var profile=DrawingProfileGeometry.Profile(curves);double volume,axial;Point3 min,max;
        if(profile.Kind=="circle")
        {
            var c=Local(profile.Center!);double radial=Math.Abs(c.Y),r=profile.Radius;Need(radial>r+1e-7,"Circle must not touch or cross the axis.");
            volume=2*Math.PI*Math.PI*radial*r*r;axial=c.X;
            double x=start.X+axial*ux,y=start.Y+axial*uy,dx=radial*Math.Abs(uy)+r,dy=radial*Math.Abs(ux)+r;
            min=new(x-dx,y-dy,-radial-r);max=new(x+dx,y+dy,radial+r);
        }
        else
        {
            var points=profile.Vertices!.Select(Local).ToArray();bool positive=points.All(p=>p.Y>=-1e-9),negative=points.All(p=>p.Y<=1e-9);
            Need(positive||negative,"A closed profile may touch but cannot cross its revolution axis.");
            points=points.Select(p=>new Point2(p.X,Math.Max(0,negative?-p.Y:p.Y))).ToArray();double radialMoment=0,mixedMoment=0;
            for(int i=0;i<points.Length;i++)
            {var a=points[i];var b=points[(i+1)%points.Length];double cross=a.X*b.Y-b.X*a.Y;radialMoment+=(a.Y+b.Y)*cross/6;mixedMoment+=(2*a.X*a.Y+a.X*b.Y+b.X*a.Y+2*b.X*b.Y)*cross/24;}
            Need(Math.Abs(radialMoment)>1e-12,"Profile has no positive revolution volume.");volume=2*Math.PI*Math.Abs(radialMoment);axial=mixedMoment/radialMoment;
            var boxes=points.Select(p=>new{X=start.X+p.X*ux,Y=start.Y+p.X*uy,R=p.Y}).ToArray();
            min=new(boxes.Min(p=>p.X-p.R*Math.Abs(uy)),boxes.Min(p=>p.Y-p.R*Math.Abs(ux)),-boxes.Max(p=>p.R));
            max=new(boxes.Max(p=>p.X+p.R*Math.Abs(uy)),boxes.Max(p=>p.Y+p.R*Math.Abs(ux)),boxes.Max(p=>p.R));
        }
        Need(new[]{volume,axial,min.X,min.Y,min.Z,max.X,max.Y,max.Z}.All(double.IsFinite)&&volume>1e-12,"Revolution metrics must be finite and nonempty.");
        return new(volume,new(start.X+axial*ux,start.Y+axial*uy,0),min,max);
    }
}
