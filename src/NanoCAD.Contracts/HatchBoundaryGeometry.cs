namespace NanoCAD.Contracts;

public static class HatchBoundaryGeometry
{
    public sealed record ParameterSpan443(double Start,double End);

    // Union native overlap parameter ranges in each curve's own domain. No
    // shared parameterization, knot basis or sampling grid is assumed. The
    // native caller measures any remaining gaps in drawing units, not params.
    public static ParameterSpan443[] Uncovered443(double start,double end,IEnumerable<ParameterSpan443> overlaps,Action? cancel=null)
    {
        if(!double.IsFinite(start)||!double.IsFinite(end)||end<=start)
            throw new DomainException("INVALID_GEOMETRY","Native overlap coverage needs a finite increasing curve domain.");
        var spans=new List<ParameterSpan443>();
        foreach(var span in overlaps)
        {
            cancel?.Invoke();
            if(!double.IsFinite(span.Start)||!double.IsFinite(span.End))
                throw new DomainException("INVALID_GEOMETRY","Native overlap coverage returned a nonfinite range.");
            double a=Math.Max(start,Math.Min(span.Start,span.End)),b=Math.Min(end,Math.Max(span.Start,span.End));
            if(b>a)spans.Add(new(a,b));
        }
        spans.Sort((a,b)=>a.Start.CompareTo(b.Start));var gaps=new List<ParameterSpan443>();double cursor=start;
        foreach(var span in spans)
        {
            cancel?.Invoke();if(span.Start>cursor)gaps.Add(new(cursor,span.Start));cursor=Math.Max(cursor,span.End);
        }
        if(cursor<end)gaps.Add(new(cursor,end));return gaps.ToArray();
    }

    // Native loops can reverse orientation, change their start vertex or repeat
    // the closing point. Compare cyclic geometry without changing the source.
    public static bool SamePolygon(Point2[] expected,Point2[] actual)
    {
        bool Same(Point2 a,Point2 b)=>double.IsFinite(b.X)&&double.IsFinite(b.Y)&&Math.Abs(a.X-b.X)<=1e-8&&Math.Abs(a.Y-b.Y)<=1e-8;
        int count=actual.Length;
        if(count>1&&Same(actual[0],actual[count-1]))count--;
        if(expected.Length<3||count!=expected.Length)return false;
        for(int start=0;start<count;start++)foreach(int direction in new[]{1,-1})
            if(Enumerable.Range(0,count).All(i=>Same(expected[i],actual[(start+direction*i+count)%count])))return true;
        return false;
    }
}
