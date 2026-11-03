namespace NanoCAD.Contracts;

// Validate explicit three-point Arc intent without constructing or solving CAD.
// The returned curve is counterclockwise only for existing boundary comparison;
// native point references retain the SDK constructor's actual start/end order.
public static class NativeSketchArcGeometry390
{
    public static SketchConstraintCurve Curve(Point3[] points,SketchPlaneSpec plane,string path)
    {
        void Need(bool ok,string message){if(!ok)throw new DomainException("INVALID_ARGUMENT",message,false,new{path});}
        Need(points.Length==3,"Arc uses ordered start, through and end WCS points.");
        var a=DrawingWorkplane.Local(plane,points[0]);var b=DrawingWorkplane.Local(plane,points[1]);var c=DrawingWorkplane.Local(plane,points[2]);
        double ux=b.X-a.X,uy=b.Y-a.Y,vx=c.X-a.X,vy=c.Y-a.Y;
        double u2=ux*ux+uy*uy,v2=vx*vx+vy*vy,cross=ux*vy-uy*vx;
        Need(double.IsFinite(cross)&&cross!=0&&DrawingProfileGeometry.Distance(a,b)>1e-7&&DrawingProfileGeometry.Distance(b,c)>1e-7&&DrawingProfileGeometry.Distance(a,c)>1e-7,"Arc needs three distinct noncollinear points.");
        double x=(u2*vy-v2*uy)/(2*cross),y=(ux*v2-vx*u2)/(2*cross);
        var center=new Point2(a.X+x,a.Y+y);double radius=Math.Sqrt(x*x+y*y);
        Need(new[]{center.X,center.Y,radius}.All(double.IsFinite)&&radius>=.001&&radius<=1e6,"Arc needs a finite radius in the existing sketch range0.001..1000000 millimeters.");
        var start=cross>0?a:c;var end=cross>0?c:a;
        double sweep=Math.Atan2(end.Y-center.Y,end.X-center.X)-Math.Atan2(start.Y-center.Y,start.X-center.X);if(sweep<=0)sweep+=2*Math.PI;
        var curve=new SketchConstraintCurve("three_point_arc","arc",start,end,center,radius,sweep);
        Need(sweep>=1e-6&&sweep<=2*Math.PI-1e-6,"Arc needs a finite nonzero sweep; use Circle for a full turn.");
        DrawingArcContours.ValidateArc(curve);
        Need(DrawingArcContours.OnArc(curve,b,interior:true),"Arc through point must lie inside its requested circular sweep.");
        return curve;
    }
}
