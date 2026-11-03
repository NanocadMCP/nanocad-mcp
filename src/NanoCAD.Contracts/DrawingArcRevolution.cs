namespace NanoCAD.Contracts;

// Full-turn moments of a simple Line/Arc region. Circular segments stay analytic;
// no tessellation is used for axis-crossing checks, mass properties or bounds.
internal static class DrawingArcRevolution
{
    internal static RevolutionOracle Measure(SketchConstraintCurve[] curves,Point2 axisStart,double ux,double uy)
    {
        void Need(bool ok,string message){if(!ok)throw new DomainException("INVALID_REVOLUTION_PROFILE",message);}
        Point2 Local(Point2 p)=>new((p.X-axisStart.X)*ux+(p.Y-axisStart.Y)*uy,-(p.X-axisStart.X)*uy+(p.Y-axisStart.Y)*ux);
        var local=curves.Select(c=>c with{Start=c.Start is {} a?Local(a):null,End=c.End is {} b?Local(b):null,Center=c.Center is {} o?Local(o):null}).ToArray();
        var edges=DrawingArcContours.Order(local);
        (double Min,double Max) Range(double a,double b)
        {
            var values=new List<double>();
            foreach(var edge in edges)
            {
                values.Add(a*edge.Start.X+b*edge.Start.Y);values.Add(a*edge.End.X+b*edge.End.Y);
                var c=edge.Curve;if(c.Kind!="arc")continue;
                double angle=Math.Atan2(b,a);
                foreach(var t in new[]{angle,angle+Math.PI})
                {var p=new Point2(c.Center!.X+c.Radius*Math.Cos(t),c.Center.Y+c.Radius*Math.Sin(t));if(DrawingArcContours.OnArc(c,p))values.Add(a*p.X+b*p.Y);}
            }
            return(values.Min(),values.Max());
        }
        var radialRange=Range(0,1);
        Need(radialRange.Min>=-1e-9||radialRange.Max<=1e-9,"The entire curved profile, including arc interiors, must stay on one side of its revolution axis.");
        double side=radialRange.Min>=-1e-9?1:-1;
        // Shift the axial coordinate before taking mixed moments to reduce
        // cancellation for parts far from the finite axis Line's start point.
        double offset=edges[0].Start.X,radialMoment=0,mixedMoment=0;
        foreach(var edge in edges)
        {
            var a=new Point2(edge.Start.X-offset,edge.Start.Y);var b=new Point2(edge.End.X-offset,edge.End.Y);
            double cross=a.X*b.Y-b.X*a.Y;
            radialMoment+=(a.Y+b.Y)*cross/6;
            mixedMoment+=(2*a.X*a.Y+a.X*b.Y+b.X*a.Y+2*b.X*b.Y)*cross/24;
            var c=edge.Curve;if(c.Kind!="arc")continue;
            double theta=edge.Sweep,half=theta/2,mid=Math.Atan2(edge.Start.Y-c.Center!.Y,edge.Start.X-c.Center.X)+half,r=c.Radius;
            double difference=Math.Abs(theta)<.01?theta*theta*theta*(1d/6-theta*theta/120+Math.Pow(theta,4)/5040):theta-Math.Sin(theta);
            double area=r*r*difference/2,first=2d/3*r*r*r*Math.Pow(Math.Sin(half),3);
            double mx=first*Math.Cos(mid),my=first*Math.Sin(mid),cx=c.Center.X-offset,cy=c.Center.Y;
            // Sector minus its signed chord triangle, translated to the axis.
            double xy=Math.Pow(r,4)*Math.Sin(2*mid)*Math.Sin(theta)*Math.Pow(Math.Sin(half),2)/6;
            radialMoment+=area*cy+my;
            mixedMoment+=area*cx*cy+cx*my+cy*mx+xy;
        }
        Need(double.IsFinite(radialMoment)&&Math.Abs(radialMoment)>1e-12,"Profile has no positive revolution volume.");
        double volume=2*Math.PI*Math.Abs(radialMoment),axial=offset+mixedMoment/radialMoment;
        double rmax=side>0?radialRange.Max:-radialRange.Min;
        var min=new Point3(axisStart.X+Range(ux,-Math.Abs(uy)*side).Min,axisStart.Y+Range(uy,-Math.Abs(ux)*side).Min,-rmax);
        var max=new Point3(axisStart.X+Range(ux,Math.Abs(uy)*side).Max,axisStart.Y+Range(uy,Math.Abs(ux)*side).Max,rmax);
        Need(new[]{volume,axial,min.X,min.Y,min.Z,max.X,max.Y,max.Z}.All(double.IsFinite)&&volume>1e-12,"Curved revolution metrics must be finite and nonempty.");
        return new(volume,new(axisStart.X+axial*ux,axisStart.Y+axial*uy,0),min,max);
    }
}
