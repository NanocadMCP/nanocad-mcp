namespace NanoCAD.Contracts;

// Independent sector mass properties and exact support bounds of a simple
// Line/Arc/Circle region revolved about a coplanar WCSXY axis. Native SDK setters
// use radians; the public editing contract will expose explicit degrees.
public static class DrawingPartialRevolution
{
    public static RevolutionOracle Measure(SketchConstraintCurve[] curves,SketchConstraintCurve axis,double angle)
    {
        void Need(bool ok,string message){if(!ok)throw new DomainException("INVALID_REVOLUTION_PROFILE",message);}
        Need(double.IsFinite(angle)&&angle>=Math.PI/1800&&angle<=2*Math.PI,"Revolution angle must be0.1..360 degrees.");
        var full=DrawingRevolutionGeometry.Measure(curves,axis);
        if(angle==2*Math.PI)return full;
        var start=axis.Start!;double length=DrawingProfileGeometry.Distance(start,axis.End!);
        double ux=(axis.End!.X-start.X)/length,uy=(axis.End.Y-start.Y)/length;
        Point2 Local(Point2 p)=>new((p.X-start.X)*ux+(p.Y-start.Y)*uy,-(p.X-start.X)*uy+(p.Y-start.Y)*ux);
        var local=curves.Select(c=>c with{Start=c.Start is {} a?Local(a):null,End=c.End is {} b?Local(b):null,Center=c.Center is {} o?Local(o):null}).ToArray();
        double radialMoment,radialSecond;
        Func<double,double,(double Min,double Max)> range;
        if(local.Length==1&&local[0].Kind=="circle")
        {
            var c=local[0];double area=Math.PI*c.Radius*c.Radius;
            radialMoment=area*c.Center!.Y;radialSecond=area*(c.Center.Y*c.Center.Y+c.Radius*c.Radius/4);
            range=(a,b)=>{double center=a*c.Center.X+b*c.Center.Y,span=c.Radius*Math.Sqrt(a*a+b*b);return(center-span,center+span);};
        }
        else
        {
            var edges=DrawingArcContours.Order(local);radialMoment=0;radialSecond=0;
            // Translate x to reduce cancellation for profiles distant from the
            // finite axis origin. Integrals of y and y² are translation invariant.
            double offset=edges[0].Start.X;
            foreach(var edge in edges)
            {
                var a=new Point2(edge.Start.X-offset,edge.Start.Y);var b=new Point2(edge.End.X-offset,edge.End.Y);
                double cross=a.X*b.Y-b.X*a.Y;
                radialMoment+=(a.Y+b.Y)*cross/6;
                radialSecond+=(a.Y*a.Y+a.Y*b.Y+b.Y*b.Y)*cross/12;
                var c=edge.Curve;if(c.Kind!="arc")continue;
                double theta=edge.Sweep,half=theta/2,r=c.Radius,mid=Math.Atan2(edge.Start.Y-c.Center!.Y,edge.Start.X-c.Center.X)+half;
                double difference=Math.Abs(theta)<.01?theta*theta*theta*(1d/6-theta*theta/120+Math.Pow(theta,4)/5040):theta-Math.Sin(theta);
                double area=r*r*difference/2,first=2d/3*r*r*r*Math.Pow(Math.Sin(half),3),my=first*Math.Sin(mid);
                // In axes radial/tangential to the arc midpoint, subtract the
                // chord triangle from the sector. Stable small-angle series
                // avoid subtracting two almost equal sector moments.
                double along,across;
                if(Math.Abs(theta)<.01)
                {
                    double t2=theta*theta,t3=theta*t2;
                    along=t3*(1d/12-t2/60+Math.Pow(theta,4)/630);
                    across=t3*t2*(1d/240-t2/2016+Math.Pow(theta,4)/34560);
                }
                else
                {
                    along=(theta+Math.Sin(theta))/8-Math.Sin(theta)*Math.Pow(Math.Cos(half),2)/4;
                    across=(theta-Math.Sin(theta))/8-Math.Sin(theta)*Math.Pow(Math.Sin(half),2)/12;
                }
                double yy=Math.Pow(r,4)*(along*Math.Pow(Math.Sin(mid),2)+across*Math.Pow(Math.Cos(mid),2));
                radialMoment+=area*c.Center.Y+my;
                radialSecond+=area*c.Center.Y*c.Center.Y+2*c.Center.Y*my+yy;
            }
            range=(a,b)=>
            {
                var values=new List<double>();
                foreach(var edge in edges)
                {
                    values.Add(a*edge.Start.X+b*edge.Start.Y);values.Add(a*edge.End.X+b*edge.End.Y);
                    var c=edge.Curve;if(c.Kind!="arc")continue;
                    double direction=Math.Atan2(b,a);
                    foreach(double t in new[]{direction,direction+Math.PI})
                    {var p=new Point2(c.Center!.X+c.Radius*Math.Cos(t),c.Center.Y+c.Radius*Math.Sin(t));if(DrawingArcContours.OnArc(c,p))values.Add(a*p.X+b*p.Y);}
                }
                return(values.Min(),values.Max());
            };
        }
        Need(double.IsFinite(radialMoment)&&double.IsFinite(radialSecond)&&Math.Abs(radialMoment)>1e-12,"Sector moments are unavailable.");
        // The same side of the axis is guaranteed by the independent full-turn
        // validation; the signed ratio retains which side the original profile lies on.
        double radial=radialSecond/radialMoment,cosAverage=Math.Sin(angle)/angle,sinAverage=2*Math.Pow(Math.Sin(angle/2),2)/angle;
        var centroid=new Point3(full.Centroid.X-uy*radial*cosAverage,full.Centroid.Y+ux*radial*cosAverage,radial*sinAverage);
        double cmin=angle>=Math.PI?-1:Math.Cos(angle),cmax=1;
        double smin=angle>Math.PI?angle>=3*Math.PI/2?-1:Math.Sin(angle):0,smax=angle>=Math.PI/2?1:Math.Sin(angle);
        var xr1=range(ux,-uy*cmin);var xr2=range(ux,-uy*cmax);var yr1=range(uy,ux*cmin);var yr2=range(uy,ux*cmax);var zr1=range(0,smin);var zr2=range(0,smax);
        var result=new RevolutionOracle(full.Volume*angle/(2*Math.PI),centroid,
            new(start.X+Math.Min(xr1.Min,xr2.Min),start.Y+Math.Min(yr1.Min,yr2.Min),Math.Min(zr1.Min,zr2.Min)),
            new(start.X+Math.Max(xr1.Max,xr2.Max),start.Y+Math.Max(yr1.Max,yr2.Max),Math.Max(zr1.Max,zr2.Max)));
        Need(new[]{result.Volume,centroid.X,centroid.Y,centroid.Z,result.Min.X,result.Min.Y,result.Min.Z,result.Max.X,result.Max.Y,result.Max.Z}.All(double.IsFinite),"Sector metrics must be finite.");
        return result;
    }
}
