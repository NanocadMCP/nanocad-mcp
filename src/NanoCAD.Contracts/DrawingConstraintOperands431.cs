namespace NanoCAD.Contracts;

// A segment is a role on its retained Polyline, not an exploded drawing entity.
// Canonical CCW arc normalization is local measurement only; persistent points
// and native references keep the original Polyline traversal order.
public static class DrawingConstraintOperands431
{
    public static int? VertexIndex(SketchConstraintCurve source,int? segment,string? role)
    {
        if(source.Kind!="polyline"||!segment.HasValue||role is not("start" or "end"))return null;
        _=Resolve(source,segment);var shape=source.Polyline!;
        return role=="start"?segment.Value:(segment.Value+1)%shape.Vertices.Length;
    }
    // Native endpoint picks belong to Polyline vertices, not exclusively to the
    // selected edge: a shared vertex may be returned as end(edge0) when the
    // original request used start(edge1). Classify an aligned control between
    // two adjacent vertices as that actual straight segment's length. This
    // does not equate different vertices or tessellate an arc.
    public static SketchConstraintSpec Canonical(SketchConstraintSpec spec,IReadOnlyDictionary<string,SketchConstraintCurve> sources)
    {
        if(spec.Kind!="distance"||spec.Second!=spec.First||!sources.TryGetValue(spec.First,out var source)||source.Kind!="polyline")return spec;
        var a=VertexIndex(source,spec.FirstSegmentIndex,spec.FirstPoint);var b=VertexIndex(source,spec.SecondSegmentIndex,spec.SecondPoint);
        if(a==null||b==null||a==b)return spec;
        var shape=source.Polyline!;int count=shape.Closed?shape.Vertices.Length:shape.Vertices.Length-1;int? matched=null;
        foreach(int i in new[]{a.Value,b.Value}.Distinct())
        {
            if(i<0||i>=count)continue;
            if(shape.Vertices[i].Bulge!=0)continue;
            int end=(i+1)%shape.Vertices.Length;
            if(!(i==a&&end==b||i==b&&end==a))continue;
            if(matched.HasValue)return spec; // Two possible edges is ambiguous.
            matched=i;
        }
        return matched.HasValue?spec with{Kind="length",Second=null,FirstPoint=null,SecondPoint=null,FirstSegmentIndex=matched,SecondSegmentIndex=null}:spec;
    }
    public static bool SameReference(string? first,int? firstSegment,string? firstPoint,string? second,int? secondSegment,string? secondPoint,
        IReadOnlyDictionary<string,SketchConstraintCurve> sources)
    {
        if(first!=second)return false;
        if(firstSegment==secondSegment&&firstPoint==secondPoint)return true;
        if(first==null||!sources.TryGetValue(first,out var source))return false;
        var a=VertexIndex(source,firstSegment,firstPoint);var b=VertexIndex(source,secondSegment,secondPoint);
        return a.HasValue&&a==b;
    }
    public static SketchConstraintCurve Resolve(SketchConstraintCurve source,int? index)
    {
        if(source.Kind!="polyline")
        {
            if(index.HasValue)throw new DomainException("INVALID_ARGUMENT","A segment index applies only to an actual native Polyline.");
            return source;
        }
        if(source.Polyline==null||!index.HasValue)throw new DomainException("INVALID_ARGUMENT","Select an explicit zero-based segment of the native Polyline.");
        var shape=source.Polyline;int count=shape.Closed?shape.Vertices.Length:shape.Vertices.Length-1;
        if(index.Value<0||index.Value>=count)throw new DomainException("OBJECT_NOT_FOUND","Polyline segment index is outside its current actual geometry.");
        var vertex=shape.Vertices[index.Value];var a=vertex.Position;var b=shape.Vertices[(index.Value+1)%shape.Vertices.Length].Position;
        double chord=DrawingProfileGeometry.Distance(a,b),bulge=vertex.Bulge;string key=source.Id+"#"+index.Value;
        if(!double.IsFinite(chord)||chord<.001||!double.IsFinite(bulge))throw new DomainException("INVALID_GEOMETRY","Selected native Polyline segment is degenerate.");
        if(bulge==0)return new(key,"line",a,b);
        double offset=(1-bulge*bulge)/(4*bulge),radius=chord*(1+bulge*bulge)/(4*Math.Abs(bulge));
        var center=new Point2((a.X+b.X)/2-(b.Y-a.Y)*offset,(a.Y+b.Y)/2+(b.X-a.X)*offset);
        var arc=new SketchConstraintCurve(key,"arc",bulge>0?a:b,bulge>0?b:a,center,radius,4*Math.Atan(Math.Abs(bulge)));
        DrawingArcContours.ValidateArc(arc);return arc;
    }
    public static string? Role(SketchConstraintCurve source,int? index,string? role)
    {
        _=Resolve(source,index);
        bool reversed=source.Kind=="polyline"&&source.Polyline!.Vertices[index!.Value].Bulge<0;
        return reversed?role switch{"start"=>"end","end"=>"start",_=>role}:role;
    }
    public static Point2 Point(SketchConstraintCurve source,int? index,string? role)=>DrawingProfileGeometry.Point(Resolve(source,index),Role(source,index,role));
    public static void Validate(SketchConstraintSpec[] specs,SketchConstraintCurve[] curves)
    {
        var sources=curves.ToDictionary(c=>c.Id,StringComparer.OrdinalIgnoreCase);
        SketchConstraintCurve Source(string id)=>sources.TryGetValue(id,out var value)?value:throw new DomainException("DRAWING_RELATION_MISMATCH","Selected source unavailable.");
        foreach(var spec in specs)
        {
            var first=Source(spec.First);
            if(spec.Kind=="fix"&&first.Kind=="polyline")
            {
                if(spec.FirstSegmentIndex.HasValue)throw new DomainException("INVALID_ARGUMENT","Whole-object fix cannot select one Polyline segment.");
                if(first.Polyline==null)throw new DomainException("DRAWING_RELATION_MISMATCH","Complete Polyline geometry is unavailable.");
                PolylineTopologyGeometry.Validate(first.Polyline);continue;
            }
            var a=Resolve(first,spec.FirstSegmentIndex);var active=new Dictionary<string,SketchConstraintCurve>(StringComparer.OrdinalIgnoreCase){{a.Id,a}};
            SketchConstraintCurve? b=null,c=null;
            if(spec.Second!=null){b=Resolve(Source(spec.Second),spec.SecondSegmentIndex);active.TryAdd(b.Id,b);}
            if(spec.Third!=null){c=Resolve(Source(spec.Third),spec.ThirdSegmentIndex);active.TryAdd(c.Id,c);}
            var normalized=spec with{First=a.Id,Second=b?.Id,Third=c?.Id,
                FirstPoint=Role(first,spec.FirstSegmentIndex,spec.FirstPoint),
                SecondPoint=spec.Second==null?null:Role(Source(spec.Second),spec.SecondSegmentIndex,spec.SecondPoint),
                FirstSegmentIndex=null,SecondSegmentIndex=null,ThirdSegmentIndex=null};
            DrawingProfileGeometry.Validate(new[]{normalized},active.Values.ToArray());
        }
    }
}
