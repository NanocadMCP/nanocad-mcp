namespace NanoCAD.Contracts;

// Actual circular/straight boundary sets, permitting segment splits, merges,
// traversal reversal and another start vertex; no area/volume or tessellation.
public static class ModelProfileBoundary340
{
    const double Tau=2*Math.PI;
    public static bool Same(SketchConstraintCurve[] first,SketchConstraintCurve[] second,Action? cancel=null)
    {
        var bytes=new ProfileGeometryBudget(cancel);foreach(var c in first.Concat(second))bytes.Add(c);
        return first.Length>0&&second.Length>0&&Covered(first,second,cancel)&&Covered(second,first,cancel);
    }
    static bool Covered(SketchConstraintCurve[] subjects,SketchConstraintCurve[] candidates,Action? cancel)
    {
        foreach(var subject in subjects)
        {
            cancel?.Invoke();var spans=new List<(double Start,double End)>();double tolerance;
            (double Start,double End)[] required;
            if(subject.Kind=="line")
            {
                var a=subject.Start!;var b=subject.End!;double dx=b.X-a.X,dy=b.Y-a.Y,length=Math.Sqrt(dx*dx+dy*dy);
                if(!double.IsFinite(length)||length<=0)return false;tolerance=1e-7/length;required=[(0,1)];
                foreach(var candidate in candidates)
                {
                    cancel?.Invoke();if(candidate.Kind!="line")continue;
                    double Position(Point2 point)=>(dx*(point.X-a.X)+dy*(point.Y-a.Y))/(length*length);
                    bool On(Point2 point)=>Math.Abs(dx*(point.Y-a.Y)-dy*(point.X-a.X))/length<=1e-7;
                    if(!On(candidate.Start!)||!On(candidate.End!))continue;
                    double u=Position(candidate.Start!),v=Position(candidate.End!);spans.Add((Math.Min(u,v),Math.Max(u,v)));
                }
            }
            else if(subject.Kind is "arc" or "circle")
            {
                tolerance=1e-8;required=Intervals(subject);
                foreach(var candidate in candidates)
                {
                    cancel?.Invoke();if(candidate.Kind is not ("arc" or "circle")||!DrawingProfileGeometry.Same(subject.Center!,candidate.Center!)||
                        !DrawingProfileGeometry.Near(subject.Radius,candidate.Radius))continue;
                    spans.AddRange(Intervals(candidate));
                }
            }
            else if(subject.Kind is "spline" or "ellipse")
            {
                if(!candidates.Any(candidate=>CurvedProfileBoundary445.Same(subject,candidate,cancel)))return false;
                continue;
            }
            else return false;
            spans.Sort((a,b)=>{cancel?.Invoke();return a.Start.CompareTo(b.Start);});
            foreach(var range in required)
            {
                double end=range.Start;
                foreach(var span in spans)
                {cancel?.Invoke();if(span.End<end-tolerance)continue;if(span.Start>end+tolerance)break;end=Math.Max(end,span.End);if(end>=range.End-tolerance)break;}
                if(end<range.End-tolerance)return false;
            }
        }
        return true;
    }
    static (double Start,double End)[] Intervals(SketchConstraintCurve curve)
    {
        if(curve.Kind=="circle")return [(0,Tau)];
        double start=Math.Atan2(curve.Start!.Y-curve.Center!.Y,curve.Start.X-curve.Center.X);if(start<0)start+=Tau;
        double end=start+curve.Sweep;
        return end<=Tau?[(start,end)]:[(start,Tau),(0,end-Tau)];
    }
}
