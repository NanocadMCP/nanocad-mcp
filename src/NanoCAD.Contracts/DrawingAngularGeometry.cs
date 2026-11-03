namespace NanoCAD.Contracts;

// The selected sector follows each Line's start-to-end direction, not an
// unordered acute angle between infinite lines. Native annotation is placed
// inside that sector so an obtuse request cannot become its supplement.
public static class DrawingAngularGeometry
{
    public const double MinimumDegrees=.1;
    public const double MaximumDegrees=179.9;
    public sealed record Sector(double Radians,Point2 Intersection,Point2 Annotation);
    public static Sector Measure(SketchConstraintCurve first,SketchConstraintCurve second)
    {
        if(first.Kind!="line"||second.Kind!="line")throw new DomainException("DRAWING_RELATION_MISMATCH","Angular dimensions require two distinct Lines.");
        var a=first.Start!;var b=second.Start!;
        double ux=first.End!.X-a.X,uy=first.End.Y-a.Y,vx=second.End!.X-b.X,vy=second.End.Y-b.Y;
        double la=Math.Sqrt(ux*ux+uy*uy),lb=Math.Sqrt(vx*vx+vy*vy);
        if(!double.IsFinite(la)||!double.IsFinite(lb)||la<.001||lb<.001)throw new DomainException("INVALID_GEOMETRY","Angular source is degenerate.");
        ux/=la;uy/=la;vx/=lb;vy/=lb;
        double dot=Math.Clamp(ux*vx+uy*vy,-1,1),cross=ux*vy-uy*vx,angle=Math.Atan2(Math.Abs(cross),dot);
        if(angle<MinimumDegrees*Math.PI/180||angle>MaximumDegrees*Math.PI/180)throw new DomainException("OUT_OF_SCOPE","Use an angle from0.1 to179.9 degrees; parallel or opposite rays have no supported sector.");
        double along=((b.X-a.X)*vy-(b.Y-a.Y)*vx)/cross;
        var center=new Point2(a.X+along*ux,a.Y+along*uy);double sx=ux+vx,sy=uy+vy,length=Math.Sqrt(sx*sx+sy*sy),radius=Math.Max(.01,Math.Min(la,lb)*.4);
        var annotation=new Point2(center.X+radius*sx/length,center.Y+radius*sy/length);
        if(new[]{center.X,center.Y,annotation.X,annotation.Y}.Any(v=>!double.IsFinite(v)||Math.Abs(v)>1e6))throw new DomainException("OUT_OF_SCOPE","Angular intersection and annotation must be within the supported drawing bounds.");
        return new(angle,center,annotation);
    }
    public static void Validate(SketchConstraintSpec spec,SketchConstraintCurve first,SketchConstraintCurve second)
    {
        if(spec.Value is not {} value||value<MinimumDegrees||value>MaximumDegrees||!DrawingProfileGeometry.Near(Measure(first,second).Radians,value*Math.PI/180))
            throw new DomainException("DRAWING_RELATION_MISMATCH","Requested degrees differ from the sector between the Lines' start-to-end directions.");
    }
}
