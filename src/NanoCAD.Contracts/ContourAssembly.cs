using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record ContourSource(string Type, PolylineShape Shape);
public sealed record ContourAssemblySpec(string Kind,string[] Targets,ContourSource[] Sources,bool EraseSources,
    bool Closed,bool[] Reverse,double[] Distances,SketchPlaneSpec? Workplane=null):EditSpec(Kind,Targets);
public sealed record ContourAssemblyMapping(string operation,EntityRef[] sources,EntityRef[] generated,bool sources_erased);

public static partial class ModifyValidation
{
    private static ContourAssemblySpec ParseContourAssembly(JsonElement item,string path,string[] handles,string kind)
    {
        SketchPlaneSpec? frame=item.TryGetProperty("workplane",out var value)?DrawingWorkplane.Parse(value):null;
        return ParseContourAssemblyCore(DrawingWorkplane.WithoutFrame(item),path,handles,kind) with{Workplane=frame};
    }
    private static ContourAssemblySpec ParseContourAssemblyCore(JsonElement item,string path,string[] handles,string kind)
    {
        Shape(item,path,kind=="join_curves"?["operation","handles","expected_sources","erase_sources","closed","reverse"]:
            kind=="split_curve"?["operation","handles","expected_sources","erase_sources","distances"]:
            ["operation","handles","expected_sources","erase_sources"]);
        if(kind=="join_curves"?handles.Length<2:handles.Length!=1)
            throw Invalid(path+".handles","Join uses at least two ordered curves; split/explode uses one curve.");
        bool Flag(JsonElement obj,string name,string at)
        {var v=Required(obj,name,at);if(v.ValueKind is not JsonValueKind.True and not JsonValueKind.False)throw Invalid(at+"."+name,"Expected Boolean.");return v.GetBoolean();}
        var values=Required(item,"expected_sources",path);
        if(values.ValueKind!=JsonValueKind.Array||values.GetArrayLength()!=handles.Length)throw Invalid(path+".expected_sources","Supply one complete source geometry for each ordered handle.");
        var sources=new List<ContourSource>();
        foreach(var v in values.EnumerateArray())
        {
            string at=path+".expected_sources["+sources.Count+"]",type=Text(v,"type",at,16);
            PolylineShape shape;
            if(type=="Line")
            {Shape(v,at,"type","start","end");shape=new(false,[new(Point(v,"start",at),0,0,0),new(Point(v,"end",at),0,0,0)]);}
            else if(type=="Arc")
            {
                Shape(v,at,"type","center","radius","start_angle","sweep_angle");var c=Point(v,"center",at);
                double r=Number(Required(v,"radius",at),at+".radius",.001,1e6),a=Number(Required(v,"start_angle",at),at+".start_angle",-2*Math.PI,2*Math.PI),
                    sweep=Number(Required(v,"sweep_angle",at),at+".sweep_angle",1e-7,2*Math.PI-1e-5);
                Point2 P(double t)=>new(c.X+r*Math.Cos(t),c.Y+r*Math.Sin(t));
                shape=new(false,[new(P(a),Math.Tan(sweep/4),0,0),new(P(a+sweep),0,0,0)]);
            }
            else if(type=="Polyline")
            {
                Shape(v,at,"type","closed","vertices");var vertices=Required(v,"vertices",at);
                if(vertices.ValueKind!=JsonValueKind.Array||vertices.GetArrayLength()<2)throw Invalid(at,"Use at least two explicit vertices.");
                var points=new List<PolylineShapeVertex>();
                foreach(var vertex in vertices.EnumerateArray())
                {
                    string vp=at+".vertices["+points.Count+"]";Shape(vertex,vp,"position","bulge","start_width","end_width");
                    points.Add(new(Point(vertex,"position",vp),Number(Required(vertex,"bulge",vp),vp+".bulge",-1e6,1e6),Number(Required(vertex,"start_width",vp),vp+".start_width",0,1e6),Number(Required(vertex,"end_width",vp),vp+".end_width",0,1e6)));
                }
                shape=new(Flag(v,"closed",at),points.ToArray());
            }
            else throw Invalid(at+".type","Use exact native Line, Arc or Polyline.");
            PolylineTopologyGeometry.Validate(shape);sources.Add(new(type,shape));
        }
        bool closed=false;bool[] reverse=[];double[] distances=[];
        if(kind=="join_curves")
        {
            closed=Flag(item,"closed",path);var a=Required(item,"reverse",path);
            if(a.ValueKind!=JsonValueKind.Array||a.GetArrayLength()!=handles.Length||a.EnumerateArray().Any(v=>v.ValueKind is not JsonValueKind.True and not JsonValueKind.False))throw Invalid(path+".reverse","One direction flag per ordered handle.");
            reverse=a.EnumerateArray().Select(v=>v.GetBoolean()).ToArray();
        }
        if(kind=="split_curve")
        {
            var a=Required(item,"distances",path);
            if(a.ValueKind!=JsonValueKind.Array||a.GetArrayLength()<1)throw Invalid(path+".distances","Use increasing distances along the stored direction.");
            distances=a.EnumerateArray().Select((v,i)=>Number(v,path+".distances["+i+"]",0,1e9)).ToArray();
        }
        var spec=new ContourAssemblySpec(kind,handles,sources.ToArray(),Flag(item,"erase_sources",path),closed,reverse,distances);
        ContourAssemblyGeometry.Plan(spec);return spec;
    }
}

// Analytic Line/Arc path algebra, also used on captured native sources before any
// write-open. No kernel split/join or tolerance-driven gap healing is invoked.
public static class ContourAssemblyGeometry
{
    public const double Tolerance=1e-7;
    private static DomainException Bad(string message,string code="INVALID_GEOMETRY")=>new(code,message);
    private static double Distance(Point2 a,Point2 b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Y-b.Y,2));
    private static object StoredSegment(Segment s)=>new{s.Start,s.End,s.Bulge,s.StartWidth,s.EndWidth};
    public sealed record Segment(Point2 Start,Point2 End,double Bulge,double StartWidth,double EndWidth)
    {
        public double Sweep=>4*Math.Atan(Bulge);
        public double Radius=>Distance(Start,End)*(1+Bulge*Bulge)/(4*Math.Abs(Bulge));
        public double Length=>Bulge==0?Distance(Start,End):Math.Abs(Sweep)*Radius;
        public Point2 Center=>new((Start.X+End.X)/2-(End.Y-Start.Y)*(1-Bulge*Bulge)/(4*Bulge),
            (Start.Y+End.Y)/2+(End.X-Start.X)*(1-Bulge*Bulge)/(4*Bulge));
        public Point2 At(double fraction)
        {
            if(fraction==0)return Start;if(fraction==1)return End;
            if(Bulge==0)return new(Start.X+(End.X-Start.X)*fraction,Start.Y+(End.Y-Start.Y)*fraction);
            var c=Center;double angle=Sweep*fraction,dx=Start.X-c.X,dy=Start.Y-c.Y;
            return new(c.X+dx*Math.Cos(angle)-dy*Math.Sin(angle),c.Y+dx*Math.Sin(angle)+dy*Math.Cos(angle));
        }
        public Segment Slice(double from,double to)=>new(At(from),At(to),Bulge==0?0:Math.Tan(Sweep*(to-from)/4),
            StartWidth+(EndWidth-StartWidth)*from,StartWidth+(EndWidth-StartWidth)*to);
    }
    public static Segment[] Segments(PolylineShape shape,Action? cancel=null)
    {
        PolylineTopologyGeometry.Validate(shape,cancel);int n=shape.Closed?shape.Vertices.Length:shape.Vertices.Length-1;
        var result=new Segment[n];var budget=new ContourGeometryBudget344(cancel);
        for(int i=0;i<n;i++)
        {
            cancel?.Invoke();var a=shape.Vertices[i];
            var segment=new Segment(a.Position,shape.Vertices[(i+1)%shape.Vertices.Length].Position,a.Bulge,a.StartWidth,a.EndWidth);
            budget.Add(StoredSegment(segment));result[i]=segment;
        }
        return result;
    }
    public static PolylineShape Shape(Segment[] segments,bool closed=false,Action? cancel=null)
    {
        var v=new List<PolylineShapeVertex>();var budget=new ContourGeometryBudget344(cancel);
        foreach(var s in segments){cancel?.Invoke();var item=new PolylineShapeVertex(s.Start,s.Bulge,s.StartWidth,s.EndWidth);budget.Add(item);v.Add(item);}
        if(!closed){var end=new PolylineShapeVertex(segments[^1].End,0,0,0);budget.Add(end);v.Add(end);}
        var shape=new PolylineShape(closed,v.ToArray());PolylineTopologyGeometry.Validate(shape,cancel);return shape;
    }
    public static ContourSource[] Plan(ContourAssemblySpec edit,Action? cancel=null)
    {
        var inputBudget=new ContourGeometryBudget344(cancel);
        foreach(var s in edit.Sources){cancel?.Invoke();PolylineTopologyGeometry.Validate(s.Shape,cancel);inputBudget.Add(s);}
        ContourGeometryBudget344.Check(edit,cancel);
        if(edit.Kind=="join_curves")
        {
            var segments=new List<Segment>();var budget=new ContourGeometryBudget344(cancel);
            for(int i=0;i<edit.Sources.Length;i++)
            {
                cancel?.Invoke();if(edit.Sources[i].Shape.Closed)throw Bad("Join requires open sources.","CLOSED_JOIN_SOURCE");
                var shape=edit.Reverse[i]?PolylineTopologyGeometry.Reverse(edit.Sources[i].Shape,cancel):edit.Sources[i].Shape;
                var part=Segments(shape,cancel);
                if(segments.Count>0&&Distance(segments[^1].End,part[0].Start)>Tolerance)throw Bad("Ordered curve endpoints do not coincide; no gaps are bridged.","CONTOUR_GAP");
                foreach(var segment in part){budget.Add(StoredSegment(segment));segments.Add(segment);}
            }
            if(edit.Closed&&Distance(segments[^1].End,segments[0].Start)>Tolerance)throw Bad("Last endpoint must coincide with the first to close the joined contour.","CONTOUR_GAP");
            var joined=new[]{new ContourSource("Polyline",Shape(segments.ToArray(),edit.Closed,cancel))};ContourGeometryBudget344.Check(joined,cancel);return joined;
        }
        var source=edit.Sources.Single();var original=Segments(source.Shape,cancel);
        if(edit.Kind=="explode_polyline")
        {
            if(source.Type!="Polyline")throw Bad("Explode requires one native Polyline.","UNSUPPORTED_ENTITY_TYPE");
            var parts=new List<ContourSource>();var budget=new ContourGeometryBudget344(cancel);
            foreach(var s in original)
            {
                cancel?.Invoke();if(s.StartWidth!=0||s.EndWidth!=0)throw Bad("Line/Arc cannot retain polyline widths; split the Polyline instead.","UNSUPPORTED_CURVE_WIDTH");
                var part=new ContourSource(s.Bulge==0?"Line":"Arc",Shape([s],cancel:cancel));budget.Add(part);parts.Add(part);
            }
            var exploded=parts.ToArray();ContourGeometryBudget344.Check(exploded,cancel);return exploded;
        }
        double length=0;foreach(var segment in original){cancel?.Invoke();length+=segment.Length;}var cuts=edit.Distances;
        for(int i=0;i<cuts.Length;i++)
        {
            cancel?.Invoke();
            if(cuts[i]<0||cuts[i]>=length-Tolerance||(!source.Shape.Closed&&cuts[i]<=Tolerance)||i>0&&cuts[i]-cuts[i-1]<=Tolerance)
                throw Bad("Distances must increase strictly within curve length; only closed sources allow distance0.","INVALID_SPLIT_DISTANCE");
        }
        var ranges=new List<(double Start,double End)>();
        if(source.Shape.Closed){for(int i=0;i<cuts.Length;i++){cancel?.Invoke();ranges.Add((cuts[i],i+1<cuts.Length?cuts[i+1]:cuts[0]+length));}}
        else {double start=0;foreach(double cut in cuts){cancel?.Invoke();ranges.Add((start,cut));start=cut;}ranges.Add((start,length));}
        var result=new List<ContourSource>();var outputBudget=new ContourGeometryBudget344(cancel);
        foreach(var range in ranges)
        {
            cancel?.Invoke();var parts=new List<Segment>();var partBudget=new ContourGeometryBudget344(cancel);
            for(int pass=0;pass<(range.End>length?2:1);pass++)
            {
                double position=pass*length;
                foreach(var segment in original)
                {
                    cancel?.Invoke();
                    double end=position+segment.Length,from=Math.Max(range.Start,position),to=Math.Min(range.End,end);
                    if(to-from>Tolerance){var part=segment.Slice(Math.Clamp((from-position)/segment.Length,0,1),Math.Clamp((to-position)/segment.Length,0,1));partBudget.Add(StoredSegment(part));parts.Add(part);}
                    position=end;
                }
            }
            if(parts.Count==0)throw Bad("Split produced a collapsed piece.");
            var piece=new ContourSource(source.Type,Shape(parts.ToArray(),cancel:cancel));outputBudget.Add(piece);result.Add(piece);
        }
        var split=result.ToArray();ContourGeometryBudget344.Check(split,cancel);return split;
    }
}
