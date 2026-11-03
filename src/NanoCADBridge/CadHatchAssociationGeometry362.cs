using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using static NanoCADBridge.CadHatchBoundaryState349;

namespace NanoCADBridge;

// Managed exact primitive inventories, copied before native wrappers are freed.
// Readback admits native reversal/start offsets, never tessellated substitutes.
internal static class CadHatchAssociationGeometry362
{
    internal sealed record Vertex(Point2d Point,double Bulge);
    internal sealed record Edge(string Kind,Point2d Start,Point2d End,Point2d Center,double Radius,double MinorRadius,
        Vector2d MajorAxis,Vector2d MinorAxis,double Sweep,bool Clockwise);
    internal sealed record Boundary(string Handle,string Kind,Vertex[] Vertices,Edge[] Edges,string[]? Handles=null,CadHatchSpline443.Definition? Spline443=null)
    {
        internal string[] SourceHandles=>Handles??[Handle];
    }
    static double Number(double value)
    {Need(double.IsFinite(value),"INVALID_GEOMETRY","Native boundary primitive contains a nonfinite value.");return value;}
    static bool Near(double a,double b,double tolerance)=>double.IsFinite(a)&&double.IsFinite(b)&&Math.Abs(a-b)<=tolerance;
    static bool Same(Point2d a,Point2d b)=>a.GetDistanceTo(b)<=Tolerance.Global.EqualPoint;
    static bool SameAxis(Vector2d a,Vector2d b)=>a.Length>0&&b.Length>0&&
        Math.Abs(a.GetNormal().X*b.GetNormal().Y-a.GetNormal().Y*b.GetNormal().X)<=Tolerance.Global.EqualVector;
    internal static Edge Capture(Curve2d curve)
    {
        using var interval=curve.GetInterval();Need(interval.IsBounded,"UNSUPPORTED_HATCH_BOUNDARY","Stored boundary curve needs a finite native interval.");
        var start=curve.StartPoint;var end=curve.EndPoint;Number(start.X);Number(start.Y);Number(end.X);Number(end.Y);
        if(curve.GetType()==typeof(LineSegment2d))return new("line",start,end,default,0,0,default,default,0,false);
        double sweep=Number(interval.UpperBound-interval.LowerBound);
        Need(sweep>0&&sweep<=2*Math.PI+Tolerance.Global.EqualVector,"INVALID_GEOMETRY","Boundary arc has no finite single-turn native span.");
        if(curve.GetType()==typeof(CircularArc2d))
        {var arc=(CircularArc2d)curve;return new("circle",start,end,arc.Center,Number(arc.Radius),0,default,default,sweep,arc.IsClockWise);}
        if(curve.GetType()==typeof(EllipticalArc2d))
        {var arc=(EllipticalArc2d)curve;return new("ellipse",start,end,arc.Center,Number(arc.MajorRadius),Number(arc.MinorRadius),arc.MajorAxis,arc.MinorAxis,sweep,arc.IsClockWise);}
        throw new DomainException("UNSUPPORTED_HATCH_BOUNDARY","Native association loop readback requires its exact curve adapter; no sampling or ownership waiver was used.",false,new{runtime_type=curve.GetType().FullName});
    }
    static bool Polyline(Vertex[] expected,Vertex[] actual,Action cancel)
    {
        if(actual.Length==expected.Length+1&&Same(actual[0].Point,actual[^1].Point)&&actual[^1].Bulge==0)actual=actual[..^1];
        int count=expected.Length;if(count!=actual.Length)return false;
        for(int start=0;start<count;start++)foreach(int direction in new[]{1,-1})
        {
            bool match=true;
            for(int index=0;index<count;index++)
            {
                cancel();int slot=(start+direction*index+count)%count;
                // A reversed segment starts at the old end and uses the old
                // preceding segment's negated bulge, including major arcs.
                double bulge=direction==1?actual[slot].Bulge:-actual[(slot-1+count)%count].Bulge;
                if(!Same(expected[index].Point,actual[slot].Point)||!Near(expected[index].Bulge,bulge,Tolerance.Global.EqualVector)){match=false;break;}
            }
            if(match)return true;
        }
        return false;
    }
    static bool SameEdge(Edge expected,Edge actual,bool reversed)
    {
        if(expected.Kind!=actual.Kind||!Same(expected.Start,reversed?actual.End:actual.Start)||!Same(expected.End,reversed?actual.Start:actual.End))return false;
        if(expected.Kind=="line")return true;
        return Same(expected.Center,actual.Center)&&Near(expected.Radius,actual.Radius,Tolerance.Global.EqualPoint)&&
            Near(expected.Sweep,actual.Sweep,Tolerance.Global.EqualVector)&&expected.Clockwise==(reversed?!actual.Clockwise:actual.Clockwise);
    }
    static bool Segments(Edge[] expected,Edge[] actual,Action cancel)
    {
        int count=expected.Length;if(count!=actual.Length)return false;
        for(int start=0;start<count;start++)foreach(int direction in new[]{1,-1})
        {
            bool match=true;for(int index=0;index<count;index++)
            {cancel();if(!SameEdge(expected[index],actual[(start+direction*index+count)%count],direction<0)){match=false;break;}}
            if(match)return true;
        }
        return false;
    }
    static bool FullConic(Edge expected,Edge[] actual,Action cancel)
    {
        if(actual.Length==0)return false;double span=0;bool? clockwise=null;
        for(int index=0;index<actual.Length;index++)
        {
            cancel();var edge=actual[index];if(edge.Kind!=expected.Kind||!Same(expected.Center,edge.Center)||!Near(expected.Radius,edge.Radius,Tolerance.Global.EqualPoint))return false;
            if(expected.Kind=="ellipse"&&(!Near(expected.MinorRadius,edge.MinorRadius,Tolerance.Global.EqualPoint)||!SameAxis(expected.MajorAxis,edge.MajorAxis)||!SameAxis(expected.MinorAxis,edge.MinorAxis)))return false;
            if(clockwise.HasValue&&clockwise.Value!=edge.Clockwise||!Same(edge.End,actual[(index+1)%actual.Length].Start))return false;
            clockwise??=edge.Clockwise;span+=edge.Sweep;
        }
        return Near(span,2*Math.PI,Tolerance.Global.EqualVector);
    }
    static bool CirclePolyline(Edge expected,Vertex[] vertices)
    {
        if(vertices.Length==3&&Same(vertices[0].Point,vertices[2].Point)&&vertices[2].Bulge==0)vertices=vertices[..2];
        if(vertices.Length!=2||!Near(Math.Abs(vertices[0].Bulge),1,Tolerance.Global.EqualVector)||!Near(vertices[0].Bulge,vertices[1].Bulge,Tolerance.Global.EqualVector))return false;
        var a=vertices[0].Point;var b=vertices[1].Point;
        return Same(expected.Center,new((a.X+b.X)/2,(a.Y+b.Y)/2))&&Near(expected.Radius,a.GetDistanceTo(b)/2,Tolerance.Global.EqualPoint);
    }
    internal static Edge Reverse(Edge edge)=>edge with{Start=edge.End,End=edge.Start,Clockwise=edge.Kind!="line"&&!edge.Clockwise};
    static bool Merge(Edge a,Edge b,out Edge merged)
    {
        merged=a;if(a.Kind!=b.Kind||!Same(a.End,b.Start))return false;
        if(a.Kind=="line")
        {
            var first=a.End-a.Start;var second=b.End-b.Start;
            if(first.Length<=Tolerance.Global.EqualPoint||second.Length<=Tolerance.Global.EqualPoint)return false;
            var x=first.GetNormal();var y=second.GetNormal();
            if(Math.Abs(x.X*y.Y-x.Y*y.X)>Tolerance.Global.EqualVector||x.X*y.X+x.Y*y.Y<=0)return false;
            merged=a with{End=b.End};return true;
        }
        if(a.Kind!="circle"||!Same(a.Center,b.Center)||!Near(a.Radius,b.Radius,Tolerance.Global.EqualPoint)||a.Clockwise!=b.Clockwise||
            a.Sweep+b.Sweep>2*Math.PI+Tolerance.Global.EqualVector)return false;
        merged=a with{End=b.End,Sweep=a.Sweep+b.Sweep};return true;
    }
    static Edge[] Coalesced(Edge[] edges,Action cancel)
    {
        // Native storage may split/coalesce collinear lines or contiguous arcs.
        // Normalize these exact primitive spans solely for geometry readback.
        var result=new List<Edge>();
        foreach(var edge in edges)
        {cancel();if(result.Count>0&&Merge(result[^1],edge,out var merged))result[^1]=merged;else result.Add(edge);}
        while(result.Count>1&&Merge(result[^1],result[0],out var merged))
        {cancel();result[0]=merged;result.RemoveAt(result.Count-1);}
        return result.ToArray();
    }
    static bool Chain(Edge[] expected,Edge[] actual,Action cancel)
    {
        expected=Coalesced(expected,cancel);actual=Coalesced(actual,cancel);
        if(expected.Length==1&&expected[0].Kind=="circle"&&Near(expected[0].Sweep,2*Math.PI,Tolerance.Global.EqualVector))
            return FullConic(expected[0],actual,cancel);
        return Segments(expected,actual,cancel);
    }
    static Edge[] PolylineEdges(Vertex[] vertices,Action cancel)
    {
        if(vertices.Length>2&&Same(vertices[0].Point,vertices[^1].Point)&&vertices[^1].Bulge==0)vertices=vertices[..^1];
        var result=new List<Edge>();
        for(int index=0;index<vertices.Length;index++)
        {
            cancel();var a=vertices[index];var b=vertices[(index+1)%vertices.Length];
            Need(double.IsFinite(a.Bulge)&&a.Point.GetDistanceTo(b.Point)>Tolerance.Global.EqualPoint,
                "NATIVE_HATCH_ASSOCIATION_MISMATCH","Native grouped Hatch polyline has an invalid/empty segment.");
            if(a.Bulge==0)result.Add(new("line",a.Point,b.Point,default,0,0,default,default,0,false));
            else
            {
                // Exact inverse of the native bulge definition: tan(sweep/4).
                // This reads native stored circular segments; it does not
                // replace sources or approximate them by sampled chords.
                double chord=a.Point.GetDistanceTo(b.Point),factor=(1/a.Bulge-a.Bulge)/4;
                var center=new Point2d((a.Point.X+b.Point.X)/2-(b.Point.Y-a.Point.Y)*factor,
                    (a.Point.Y+b.Point.Y)/2+(b.Point.X-a.Point.X)*factor);
                double radius=chord*(Math.Abs(a.Bulge)+1/Math.Abs(a.Bulge))/4,sweep=4*Math.Atan(Math.Abs(a.Bulge));
                Need(double.IsFinite(center.X)&&double.IsFinite(center.Y)&&double.IsFinite(radius)&&double.IsFinite(sweep),
                    "NATIVE_HATCH_ASSOCIATION_MISMATCH","Native grouped Hatch bulge geometry is nonfinite.");
                result.Add(new("circle",a.Point,b.Point,center,radius,0,default,default,sweep,a.Bulge<0));
            }
        }
        return result.ToArray();
    }
    static string Key(IEnumerable<string> handles)=>string.Join(",",handles.OrderBy(value=>value,StringComparer.Ordinal));
    internal static void Verify(Hatch hatch,Boundary[] expected,Action cancel)
    {
        Need(hatch.NumberOfLoops==expected.Length,"NATIVE_HATCH_ASSOCIATION_MISMATCH","Native loop count differs from the explicit outer and holes.");
        var wanted=expected.ToDictionary(value=>Key(value.SourceHandles),StringComparer.Ordinal);var seen=new HashSet<string>(StringComparer.Ordinal);
        for(int index=0;index<hatch.NumberOfLoops;index++)
        {
            cancel();using var ids=hatch.GetAssociatedObjectIdsAt(index);
            var handles=new List<string>();foreach(Teigha.DatabaseServices.ObjectId id in ids){cancel();handles.Add(Validation.NormalizeHandle(id.Handle.ToString()));}
            string key=Key(handles);
            bool known=wanted.TryGetValue(key,out var source);
            Need(handles.Count>0&&handles.Distinct(StringComparer.Ordinal).Count()==handles.Count&&known&&seen.Add(key),
                "NATIVE_HATCH_ASSOCIATION_MISMATCH","Each rebound loop must retain exactly its complete requested source group, without duplicate or mixed IDs.");
            var loop=hatch.GetLoopAt(index);
            Need((loop.LoopType&(HatchLoopTypes.NotClosed|HatchLoopTypes.SelfIntersecting|HatchLoopTypes.Duplicate))==0&&
                ((loop.LoopType&HatchLoopTypes.External)!=0)==(key==Key(expected[0].SourceHandles)),"NATIVE_HATCH_ASSOCIATION_MISMATCH","Native loop validity/outer-hole role differs from the request.");
            bool match;
            if(source!.Spline443 is {} spline)
            {
                var curves=loop.IsPolyline?CadHatchSpline443.StoredPolyline(loop,cancel):loop.Curves.Cast<Curve2d>().ToArray();
                try{match=CadHatchSpline443.Same(spline,curves,cancel);}
                finally{var released=new HashSet<Curve2d>(ReferenceEqualityComparer.Instance);foreach(Curve2d curve in curves)if(released.Add(curve))curve.Dispose();}
            }
            else if(loop.IsPolyline)
            {
                var vertices=new List<Vertex>();foreach(BulgeVertex value in loop.Polyline){cancel();vertices.Add(new(value.Vertex,value.Bulge));}
                match=source!.Kind=="Line_Arc_chain"?Chain(source.Edges,PolylineEdges(vertices.ToArray(),cancel),cancel):
                    source.Vertices.Length>0?Polyline(source.Vertices,vertices.ToArray(),cancel):source.Kind=="Circle"&&CirclePolyline(source.Edges[0],vertices.ToArray());
            }
            else
            {
                var curves=loop.Curves;var edges=new List<Edge>();
                try{foreach(Curve2d curve in curves){cancel();edges.Add(Capture(curve));}}
                finally{var released=new HashSet<Curve2d>(ReferenceEqualityComparer.Instance);foreach(Curve2d curve in curves)if(released.Add(curve))curve.Dispose();}
                match=source!.Kind=="Line_Arc_chain"?Chain(source.Edges,edges.ToArray(),cancel):
                    source.Vertices.Length>0?Segments(source.Edges,edges.ToArray(),cancel):FullConic(source.Edges[0],edges.ToArray(),cancel);
            }
            Need(match,"NATIVE_HATCH_ASSOCIATION_MISMATCH","Stored native loop geometry differs from its unchanged source primitive inventory.");
        }
    }
}
