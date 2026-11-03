namespace NanoCAD.Contracts;

// Physical boundary coverage, allowing native split/merged collinear segments.
// This compares curves rather than tessellation, mass or boundary-order labels.
public static class ModelProfileElementGeometry358
{
    const double E=1e-7,Tau=2*Math.PI;
    static double Dot(Point2 a,Point2 b)=>a.X*b.X+a.Y*b.Y;
    static Point2 Sub(Point2 a,Point2 b)=>new(a.X-b.X,a.Y-b.Y);
    static double Cross(Point2 a,Point2 b)=>a.X*b.Y-a.Y*b.X;
    static double Norm(double angle){angle%=Tau;return angle<0?angle+Tau:angle;}
    static IEnumerable<(double From,double To)> Angular(SketchConstraintCurve curve)
    {
        if(curve.Kind=="circle"){yield return(0,Tau);yield break;}
        double start=Norm(Math.Atan2(curve.Start!.Y-curve.Center!.Y,curve.Start.X-curve.Center.X)),end=start+curve.Sweep;
        if(end<=Tau)yield return(start,end);else{yield return(start,Tau);yield return(0,end-Tau);}
    }
    static bool Covered((double From,double To) target,IEnumerable<(double From,double To)> intervals,double epsilon,Action cancel)
    {
        double end=target.From;foreach(var span in intervals.OrderBy(value=>value.From))
        {cancel();if(span.To<end-epsilon)continue;if(span.From>end+epsilon)return false;end=Math.Max(end,span.To);if(end>=target.To-epsilon)return true;}
        return false;
    }
    static bool Covers(SketchConstraintCurve[] target,SketchConstraintCurve[] candidates,Action cancel)
    {
        foreach(var curve in target)
        {
            cancel();var intervals=new List<(double From,double To)>();
            if(curve.Kind=="line")
            {
                var axis=Sub(curve.End!,curve.Start!);double length=DrawingProfileGeometry.Distance(curve.Start!,curve.End!),square=Dot(axis,axis);
                if(length<.001)return false;
                foreach(var other in candidates)
                {
                    cancel();if(other.Kind!="line")continue;var from=Sub(other.Start!,curve.Start!);var to=Sub(other.End!,curve.Start!);
                    if(Math.Abs(Cross(axis,from))/length>E||Math.Abs(Cross(axis,to))/length>E)continue;
                    double a=Dot(axis,from)/square,b=Dot(axis,to)/square;intervals.Add((Math.Min(a,b),Math.Max(a,b)));
                }
                if(!Covered((0,1),intervals,E/length,cancel))return false;
            }
            else if(curve.Kind is "spline" or "ellipse")
            {
                if(!candidates.Any(other=>CurvedProfileBoundary445.Same(curve,other,cancel)))return false;
            }
            else
            {
                if(curve.Kind is not ("circle" or "arc"))return false;
                foreach(var other in candidates)
                {
                    cancel();if(other.Kind is not ("circle" or "arc")||DrawingProfileGeometry.Distance(curve.Center!,other.Center!)>E||Math.Abs(curve.Radius-other.Radius)>E)continue;
                    intervals.AddRange(Angular(other));
                }
                foreach(var span in Angular(curve))if(!Covered(span,intervals,E/Math.Max(.001,curve.Radius),cancel))return false;
            }
        }
        return true;
    }
    public static bool SameBoundary(SketchConstraintCurve[] before,SketchConstraintCurve[] after,Action cancel)=>
        before.Length>0&&after.Length>0&&Covers(before,after,cancel)&&Covers(after,before,cancel);
    public static ModelProfileSpec Single(SketchConstraintCurve[] curves,Action cancel)
    {
        if(curves.Length==1&&curves[0].Kind=="circle")return new("circle",curves[0].Center,curves[0].Radius,null);
        if(!curves.All(curve=>curve.Kind is "line" or "arc"))throw new DomainException("PROFILE_GEOMETRY_AMBIGUOUS","Use one complete simple Line/Arc contour, or separated/nested whole Circle regions.");
        return curves.Any(curve=>curve.Kind=="arc")?ModelPolycurve.FromCurves(curves,cancel):DrawingProfileGeometry.Profile(curves,cancel);
    }
}
