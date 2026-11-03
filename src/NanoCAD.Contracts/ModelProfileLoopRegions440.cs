namespace NanoCAD.Contracts;

// Topology preflight for a new selection on an acquired native sketch. Every
// returned boundary retains the actual observed elements; no curve is written,
// copied, unioned or used to replace the native selected-area authority.
internal static class ModelProfileLoopRegions440
{
    const double Epsilon=1e-7;
    static void Need(bool ok,string message)
    {if(!ok)throw new DomainException("PROFILE_REGION_AMBIGUOUS",message);}
    static bool Same(Point2 first,Point2 second)=>DrawingProfileGeometry.Distance(first,second)<=Epsilon;
    static bool Inside(SketchConstraintCurve[] loop,Point2 point,Action cancel)
    {
        cancel();if(loop.Length!=1||loop[0].Kind!="circle")return ModelSketchSelection362.Inside(loop,point,cancel);
        var circle=loop[0];double distance=DrawingProfileGeometry.Distance(circle.Center!,point);
        Need(Math.Abs(distance-circle.Radius)>Epsilon,"Region point lies on a circular boundary.");return distance<circle.Radius;
    }
    static SketchConstraintCurve[][] Loops(SketchConstraintCurve[] curves,Action cancel)
    {
        Need(curves.Length>0&&curves.All(curve=>!string.IsNullOrWhiteSpace(curve.Id))&&curves.Select(curve=>curve.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count()==curves.Length,
            "Native boundary element identities must be nonempty and unique.");
        var budget=new ProfileGeometryBudget(cancel);var circles=new List<SketchConstraintCurve[]>();var segments=new List<SketchConstraintCurve>();
        foreach(var curve in curves)
        {
            cancel();budget.Add(curve);Need(curve.Kind is "circle" or "line" or "arc","Replacement regions require complete Circle/Line/Arc boundaries.");
            if(curve.Kind=="circle")
            {
                Need(curve.Center!=null&&new[]{curve.Center.X,curve.Center.Y,curve.Radius}.All(double.IsFinite)&&curve.Radius>=.001&&curve.Radius<=1e6,
                    "A native Circle boundary must be finite and noncollapsed.");circles.Add([curve]);continue;
            }
            Need(curve.Start!=null&&curve.End!=null&&new[]{curve.Start.X,curve.Start.Y,curve.End.X,curve.End.Y}.All(double.IsFinite)&&!Same(curve.Start,curve.End),
                "A native Line/Arc boundary must have finite distinct endpoints.");
            if(curve.Kind=="arc")DrawingArcContours.ValidateArc(curve);segments.Add(curve);
        }
        // Prove a disjoint union of unbranched endpoint cycles. An open
        // reference or intersection is not silently promoted to a region.
        foreach(var curve in segments)foreach(var point in new[]{curve.Start!,curve.End!})
        {
            int degree=0;foreach(var other in segments){cancel();degree+=(Same(point,other.Start!)?1:0)+(Same(point,other.End!)?1:0);}
            Need(degree==2,"Replacement Line/Arc boundaries have a gap, branch or ambiguous shared vertex.");
        }
        var remaining=segments.OrderBy(curve=>curve.Id,StringComparer.OrdinalIgnoreCase).ToList();var loops=new List<SketchConstraintCurve[]>(circles);
        while(remaining.Count>0)
        {
            cancel();var group=new List<SketchConstraintCurve>{remaining[0]};remaining.RemoveAt(0);
            for(int i=0;i<group.Count;i++)
            {
                var current=group[i];var neighbors=remaining.Where(other=>
                {cancel();return new[]{current.Start!,current.End!}.Any(point=>Same(point,other.Start!)||Same(point,other.End!));}).ToArray();
                foreach(var neighbor in neighbors){group.Add(neighbor);remaining.Remove(neighbor);}
            }
            var boundary=group.ToArray();DrawingArcContours.Measure(boundary,1,cancel);loops.Add(boundary);
        }
        return loops.OrderBy(loop=>loop.Min(curve=>curve.Id),StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public static ModelSketchRegion377[] Select(SketchConstraintCurve[] curves,Point2[] points,Action cancel)
    {
        Need(points.Length>0&&points.All(point=>double.IsFinite(point.X)&&double.IsFinite(point.Y)),"Select finite interior region points.");
        var loops=Loops(curves,cancel);
        // Preserve the existing one-simple-Line/Arc route unchanged.
        if(loops.Length==1&&loops[0][0].Kind!="circle"&&points.Length==1)return [ModelSketchRegions377.Select(curves,points[0],cancel)];
        for(int i=0;i<loops.Length;i++)for(int j=i+1;j<loops.Length;j++)foreach(var first in loops[i])foreach(var second in loops[j])
        {
            cancel();Need(!DrawingArcContours.TouchesOrCrosses(first,second,cancel),
                "Intersecting, overlapping or touching native loops require a different region-topology adapter.");
        }
        // With complete simple loops and no cross-loop contacts, a point on
        // one boundary proves its whole loop is inside/outside another loop.
        var contains=new bool[loops.Length,loops.Length];
        for(int i=0;i<loops.Length;i++)for(int j=0;j<loops.Length;j++)if(i!=j)
        {
            cancel();var sample=loops[j][0];var point=sample.Kind=="circle"?new Point2(sample.Center!.X+sample.Radius,sample.Center.Y):sample.Start!;
            contains[i,j]=Inside(loops[i],point,cancel);
        }
        int Innermost(int[] candidates)
        {
            var deepest=candidates.Where(candidate=>!candidates.Any(other=>{cancel();return other!=candidate&&contains[candidate,other];})).ToArray();
            Need(deepest.Length==1,"Region containment is not a unique nested or separated loop cell.");return deepest[0];
        }
        var parents=new int?[loops.Length];
        for(int i=0;i<loops.Length;i++)
        {
            cancel();var containers=Enumerable.Range(0,loops.Length).Where(other=>contains[other,i]).ToArray();if(containers.Length>0)parents[i]=Innermost(containers);
        }
        var selected=new HashSet<int>();
        foreach(var point in points)
        {
            cancel();var containers=Enumerable.Range(0,loops.Length).Where(index=>Inside(loops[index],point,cancel)).ToArray();
            Need(containers.Length>0,"Each replacement point must lie inside a bounded native region.");
            Need(selected.Add(Innermost(containers)),"Two points select the same region; no native toggle is repeated.");
        }
        foreach(int index in selected)
        {cancel();Need(!parents[index].HasValue||!selected.Contains(parents[index]!.Value),"Adjacent selected cells may merge; their boundary-union adapter is outside this operation.");}
        return selected.OrderBy(index=>index).Select(index=>new ModelSketchRegion377(loops[index],
            Enumerable.Range(0,loops.Length).Where(child=>parents[child]==index).Select(child=>loops[child]).ToArray())).ToArray();
    }
}
