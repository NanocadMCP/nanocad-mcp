namespace NanoCAD.Contracts;

// CAD-independent biarc fitting and exact finite-line/finite-arc observations.
// Tolerance is against the supplied samples, not an invented underlying scan
// surface or the straight chords between those samples.
public static class SmoothPolylineGeometry409
{
    public const double JoinAngularResolution=1e-7;
    private const double MachineEpsilon=2.2204460492503131e-16;
    private readonly record struct V(double X,double Y)
    {
        public static V operator +(V a,V b)=>new(a.X+b.X,a.Y+b.Y);
        public static V operator -(V a,V b)=>new(a.X-b.X,a.Y-b.Y);
        public static V operator -(V a)=>new(-a.X,-a.Y);
        public static V operator *(V a,double s)=>new(a.X*s,a.Y*s);
        public static V operator /(V a,double s)=>new(a.X/s,a.Y/s);
        public double Length=>Math.Sqrt(X*X+Y*Y);
        public V Left=>new(-Y,X);
        public double Dot(V other)=>X*other.X+Y*other.Y;
        public double Cross(V other)=>X*other.Y-Y*other.X;
        public V Unit=>Length>0?this/Length:throw Failure("A source cusp has no forward tangent. Mark its intended source corner explicitly.");
        public Point2 Point=>new(X,Y);
    }
    private static V Vec(Point2 p)=>new(p.X,p.Y);
    private static DomainException Failure(string message)=>new("SMOOTH_POLYLINE_FIT_MISMATCH",message);
    private static void Need(bool ok,string message){if(!ok)throw Failure(message);}
    private static bool Finite(V v)=>double.IsFinite(v.X)&&double.IsFinite(v.Y);
    public sealed record Segment(Point2 Start,Point2 End,double Bulge,int SourceFirst,int SourceLast,bool StartsAtCorner=false);
    public sealed record Plan(Segment[] Segments,bool Closed,int[] CornerVertexIndices);
    public sealed record Observation(double Resolution,double MaximumDistance,double RmsDistance,double LargestChord,
        double MaximumJoinAngle,int LineCount,int ArcCount,SmoothPolylineSample409[]? Samples);
    private sealed record Arc(V Start,V End,V Center,V CenterOffset,double Radius,double Sweep,V StartTangent,V EndTangent,double Length);
    private readonly record struct Position(int Segment,double T);
    public static double Resolution(IEnumerable<Point2> points)
    {
        double scale=1;foreach(var point in points)scale=Math.Max(scale,Math.Max(Math.Abs(point.X),Math.Abs(point.Y)));
        return Math.Max(1e-9,64*MachineEpsilon*scale);
    }
    private static Arc Geometry(Segment segment)
    {
        var a=Vec(segment.Start);var b=Vec(segment.End);var chord=b-a;double length=chord.Length;
        Need(Finite(a)&&Finite(b)&&double.IsFinite(segment.Bulge)&&length>0,"Fit output contains an invalid or collapsed segment.");
        if(segment.Bulge==0)return new(a,b,default,default,double.PositiveInfinity,0,chord.Unit,chord.Unit,length);
        double bulge=segment.Bulge,sweep=4*Math.Atan(bulge);
        var offset=chord*.5+chord.Left*((1-bulge*bulge)/(4*bulge));var center=a+offset;
        double radius=length*(1+bulge*bulge)/(4*Math.Abs(bulge));
        var start=(-offset).Left*(Math.Sign(sweep)/radius);var end=(chord-offset).Left*(Math.Sign(sweep)/radius);
        Need(Finite(center)&&double.IsFinite(radius)&&radius>0&&Finite(start)&&Finite(end),"Fit arc geometry exceeds finite arithmetic.");
        return new(a,b,center,offset,radius,sweep,start.Unit,end.Unit,radius*Math.Abs(sweep));
    }
    private static double PositiveAngle(double angle)
    {angle%=Math.PI*2;if(angle<0)angle+=Math.PI*2;return angle;}
    private static V At(Arc arc,double t)
    {
        if(t<=0)return arc.Start;if(t>=1)return arc.End;
        if(arc.Sweep==0)return arc.Start+(arc.End-arc.Start)*t;
        var v=-arc.CenterOffset;double a=arc.Sweep*t;
        // Relative rotation and cos(a)-1 avoid subtracting a huge near-flat
        // circle center from the result's small local coordinates.
        return arc.Start+v*(-2*Math.Pow(Math.Sin(a/2),2))+v.Left*Math.Sin(a);
    }
    private static (double Distance,double T) Closest(Arc arc,V p,double minimum=0,double maximum=1)
    {
        minimum=Math.Clamp(minimum,0,1);maximum=Math.Clamp(maximum,minimum,1);var begin=At(arc,minimum);var end=At(arc,maximum);
        double t;
        if(arc.Sweep==0)
            t=Math.Clamp((p-arc.Start).Dot(arc.End-arc.Start)/Math.Pow((arc.End-arc.Start).Length,2),minimum,maximum);
        else
        {
            var q=p-arc.Start;var k=arc.CenterOffset;
            double theta=PositiveAngle(Math.Sign(arc.Sweep)*Math.Atan2((-k).Cross(q),k.Dot(k)-k.Dot(q)));
            double projected=theta/Math.Abs(arc.Sweep);
            if(projected>=minimum&&projected<=maximum)t=projected;
            else t=(p-begin).Length<=(p-end).Length?minimum:maximum;
        }
        return ((p-At(arc,t)).Length,t);
    }
    private static (double Distance,int Segment,double T) Locate(Arc[] arcs,V point,Position minimum,Position maximum,Action cancel)
    {
        double best=double.PositiveInfinity,t=0;int chosen=minimum.Segment;
        for(int i=minimum.Segment;i<=maximum.Segment;i++)
        {
            cancel();double lower=i==minimum.Segment?minimum.T:0,upper=i==maximum.Segment?maximum.T:1;
            if(lower>upper)continue;var nearest=Closest(arcs[i],point,lower,upper);
            if(nearest.Distance<best){best=nearest.Distance;t=nearest.T;chosen=i;}
        }
        return (best,chosen,t);
    }
    private static double? EnterDisk(Arc arc,V point,double minimum,double tolerance)
    {
        if((At(arc,minimum)-point).Length<=tolerance)return minimum;
        if(arc.Sweep==0)
        {
            var begin=At(arc,minimum);var chord=arc.End-begin;double length=chord.Length;if(length==0)return null;
            var direction=chord/length;var q=point-begin;double lateral=Math.Abs(q.Cross(direction));
            if(lateral>tolerance)return null;
            double entry=q.Dot(direction)-Math.Sqrt(Math.Max(0,tolerance*tolerance-lateral*lateral));
            return entry>=0&&entry<=length?minimum+(1-minimum)*entry/length:null;
        }
        var offset=arc.CenterOffset;var relative=point-arc.Start;double r=arc.Radius;
        double rho=Math.Sqrt(Math.Max(0,offset.Dot(offset)-2*offset.Dot(relative)+relative.Dot(relative)));
        if(rho==0)return null;
        // Stable radial difference, including near-flat arcs with large radii.
        double delta=(2*offset.Dot(relative)-relative.Dot(relative))/(r+rho);
        if(Math.Abs(delta)>tolerance)return null;
        double alpha=2*Math.Asin(Math.Sqrt(Math.Clamp((tolerance*tolerance-delta*delta)/(4*r*rho),0,1)));
        double beta=Math.Atan2((-offset).Cross(relative),offset.Dot(offset)-offset.Dot(relative));
        double? best=null;
        foreach(double angle in new[]{beta-alpha,beta+alpha})
        {
            double t=PositiveAngle(Math.Sign(arc.Sweep)*angle)/Math.Abs(arc.Sweep);
            if(t>=minimum&&t<=1&&(best==null||t<best))best=t;
        }
        return best;
    }
    private static Position? Earliest(Arc[] arcs,V point,Position minimum,double tolerance,Action cancel)
    {
        for(int i=minimum.Segment;i<arcs.Length;i++)
        {cancel();var t=EnterDisk(arcs[i],point,i==minimum.Segment?minimum.T:0,tolerance);if(t!=null)return new(i,t.Value);}
        return null;
    }
    private static Arc Reverse(Arc arc)=>new(arc.End,arc.Start,arc.Center,arc.Sweep==0?default:arc.CenterOffset-(arc.End-arc.Start),
        arc.Radius,-arc.Sweep,-arc.EndTangent,-arc.StartTangent,arc.Length);
    // The backward earliest feasible pass supplies latest feasible forward
    // bounds. A subsequent nearest-within-bounds pass cannot jump past a
    // future sample. Distances are actual ordered correspondence witnesses,
    // not independently/global-nearest or globally optimal RMS assertions.
    private static bool Correspond(Arc[] arcs,Point2[] points,int first,int last,double tolerance,bool forceLast,Action cancel,
        Action<int,double,int,double>? collect=null)
    {
        var reversed=Enumerable.Reverse(arcs).Select(Reverse).ToArray();var upper=new Position[last-first+1];var previous=new Position(0,0);
        for(int i=last;i>=first;i--)
        {
            cancel();Position? match=i==last&&forceLast?(At(reversed[0],0)-Vec(points[i])).Length<=tolerance?new Position(0,0):null:
                Earliest(reversed,Vec(points[i]),previous,tolerance,cancel);
            if(match==null)return false;previous=match.Value;upper[i-first]=new(arcs.Length-1-previous.Segment,1-previous.T);
        }
        previous=new(0,0);
        for(int i=first;i<=last;i++)
        {
            cancel();var nearest=i==first?((At(arcs[0],0)-Vec(points[i])).Length,0,0d):i==last&&forceLast?
                ((At(arcs[^1],1)-Vec(points[i])).Length,arcs.Length-1,1d):
                Locate(arcs,Vec(points[i]),previous,upper[i-first],cancel);
            if(nearest.Item1>tolerance)return false;
            previous=new(nearest.Item2,nearest.Item3);collect?.Invoke(i,nearest.Item1,nearest.Item2,nearest.Item3);
        }
        return true;
    }
    public static Observation Observe(Plan plan,Point2[] samples,double tolerance,bool includeDetails=false,Action? check=null)
    {
        var cancel=check??(()=>{});cancel();Need(plan.Segments.Length>0,"Fit must contain actual line or arc segments.");
        double resolution=Resolution(samples.Concat(plan.Segments.SelectMany(s=>new[]{s.Start,s.End})));
        Need(tolerance>=resolution,"Requested sample tolerance is below the numerical resolution of these coordinates.");
        var arcs=new Arc[plan.Segments.Length];int lines=0,curved=0;double join=0;
        var corners=plan.CornerVertexIndices.ToHashSet();
        for(int i=0;i<arcs.Length;i++)
        {
            cancel();arcs[i]=Geometry(plan.Segments[i]);Need(arcs[i].Length>resolution,"Fit contains a segment below coordinate resolution.");
            if(arcs[i].Sweep==0)lines++;else curved++;
            if(i>0)Need((arcs[i].Start-arcs[i-1].End).Length<=resolution,"Fit segments do not share their retained join point.");
            if(i>0&&!corners.Contains(i))
            {
                double angle=Math.Abs(Math.Atan2(arcs[i-1].EndTangent.Cross(arcs[i].StartTangent),arcs[i-1].EndTangent.Dot(arcs[i].StartTangent)));
                join=Math.Max(join,angle);Need(angle<=JoinAngularResolution,"Actual arc/line join is not forward G1 tangent-continuous.");
            }
        }
        Need((arcs[0].Start-Vec(samples[0])).Length<=resolution,"Fit changed the first requested endpoint.");
        if(plan.Closed)
        {
            Need((arcs[^1].End-arcs[0].Start).Length<=resolution,"Closed fit has an open seam.");
            if(!corners.Contains(0))
            {
                double angle=Math.Abs(Math.Atan2(arcs[^1].EndTangent.Cross(arcs[0].StartTangent),arcs[^1].EndTangent.Dot(arcs[0].StartTangent)));
                join=Math.Max(join,angle);Need(angle<=JoinAngularResolution,"Closed smooth fit has a non-tangent seam.");
            }
        }
        else Need((arcs[^1].End-Vec(samples[^1])).Length<=resolution,"Fit changed the last requested endpoint.");
        var details=includeDetails?new List<SmoothPolylineSample409>():null;double maximum=0,squares=0,gap=0;
        Need(Correspond(arcs,samples,0,samples.Length-1,tolerance+resolution,!plan.Closed,cancel,(index,distance,segment,parameter)=>
        {maximum=Math.Max(maximum,distance);squares+=distance*distance;details?.Add(new(index,segment,parameter,distance));}),
            "Supplied points have no feasible ordered correspondence to the actual arc/line chain within fit_tolerance.");
        for(int i=0;i<samples.Length;i++)
        {
            cancel();
            if(i>0)gap=Math.Max(gap,(Vec(samples[i])-Vec(samples[i-1])).Length);
        }
        if(plan.Closed)gap=Math.Max(gap,(Vec(samples[0])-Vec(samples[^1])).Length);
        return new(resolution,maximum,Math.Sqrt(squares/samples.Length),gap,join,lines,curved,details?.ToArray());
    }
    private static V CircleTangent(V first,V middle,V last,V at,V forward)
    {
        var a=middle-first;var b=last-first;double cross=a.Cross(b);
        if(Math.Abs(cross)<=1e-12*a.Length*b.Length)return forward.Unit;
        var offset=new V((a.Dot(a)*b.Y-b.Dot(b)*a.Y)/(2*cross),(b.Dot(b)*a.X-a.Dot(a)*b.X)/(2*cross));
        var tangent=(at-(first+offset)).Left*Math.Sign(cross);
        return Finite(tangent)&&tangent.Length>0&&tangent.Unit.Dot(forward.Unit)>0?tangent.Unit:forward.Unit;
    }
    private static V[] Tangents(Point2[] points,bool closed,int[] corners,Point2? requestedStart,Point2? requestedEnd,Action cancel)
    {
        int n=points.Length;var result=new V[n];var cornerSet=corners.ToHashSet();
        for(int i=0;i<n;i++)
        {
            cancel();
            var p=Vec(points[i]);var before=Vec(points[(i+n-1)%n]);var after=Vec(points[(i+1)%n]);
            if(!closed&&i==0)
                result[i]=requestedStart!=null?Vec(requestedStart).Unit:n>2?CircleTangent(p,after,Vec(points[2]),p,after-p):(after-p).Unit;
            else if(!closed&&i==n-1)
                result[i]=requestedEnd!=null?Vec(requestedEnd).Unit:n>2?CircleTangent(Vec(points[n-3]),before,p,p,p-before):(p-before).Unit;
            else if(cornerSet.Contains(i))result[i]=default; // Each adjacent piece supplies its own one-sided direction.
            else
            {
                var incoming=(p-before).Unit;var outgoing=(after-p).Unit;
                Need((incoming+outgoing).Length>1e-10,"A reversing source cusp must be an explicit corner_index.");
                bool leftLine=(closed||i>=2)&&!cornerSet.Contains((i+n-1)%n)&&SameDirection(incoming,(before-Vec(points[(i+n-2)%n])).Unit);
                bool rightLine=(closed||i+2<n)&&!cornerSet.Contains((i+1)%n)&&SameDirection(outgoing,(Vec(points[(i+2)%n])-after).Unit);
                result[i]=leftLine&&!rightLine?incoming:rightLine&&!leftLine?outgoing:
                    CircleTangent(before,p,after,p,incoming+outgoing);
            }
        }
        return result;
    }
    private static Segment? FromTangent(V a,V b,V tangent,int first,int last)
    {
        var chord=b-a;double length=chord.Length;if(length<=0)return null;
        double side=chord.Dot(tangent.Left),ahead=chord.Dot(tangent);
        if(Math.Abs(side)<=64*MachineEpsilon*length)
            return ahead>0?new(a.Point,b.Point,0,first,last):null;
        double sweep=2*Math.Atan2(side,ahead);
        if(Math.Abs(sweep)>Math.PI+1e-10)return null;
        double bulge=Math.Tan(sweep/4);return double.IsFinite(bulge)?new(a.Point,b.Point,bulge,first,last):null;
    }
    private static Segment[]? Biarc(V a,V b,V ta,V tb,int first,int last)
    {
        var chord=b-a;double vv=chord.Dot(chord),dot=ta.Dot(tb);
        double qa=Math.Max(0,2*(1-dot)),qb=chord.Dot(ta+tb),root=Math.Sqrt(qb*qb+qa*vv),denominator=qb+root;
        if(!double.IsFinite(denominator)||denominator<=64*MachineEpsilon*Math.Sqrt(vv))return null;
        double d=vv/denominator;var middle=(a+b+(ta-tb)*d)*.5;
        var firstArc=FromTangent(a,middle,ta,first,last);var backwards=FromTangent(b,middle,-tb,first,last);
        if(firstArc==null||backwards==null)return null;
        var secondArc=new Segment(backwards.End,backwards.Start,-backwards.Bulge,first,last);
        return new[]{firstArc,secondArc};
    }
    private static bool Fits(Segment[] segments,Point2[] values,int first,int last,double tolerance,Action cancel)
    {
        return Correspond(segments.Select(Geometry).ToArray(),values,first,last,tolerance,true,cancel);
    }
    private static bool SameDirection(V a,V b)=>Math.Abs(Math.Atan2(a.Cross(b),a.Dot(b)))<=1e-10;
    private static Segment[]? Single(V a,V b,V ta,V tb,int first,int last)
    {
        var segment=FromTangent(a,b,ta,first,last);
        return segment!=null&&SameDirection(Geometry(segment).EndTangent,tb)?new[]{segment}:null;
    }
    public static Plan Fit(Point2[] points,double tolerance,bool closed=false,int[]? cornerIndices=null,
        Point2? startTangent=null,Point2? endTangent=null,Action? check=null,Action<Segment>? account=null)
    {
        var cancel=check??(()=>{});cancel();Need(points.Length>=(closed?3:2),"Fit needs ordered distinct samples.");
        double resolution=Resolution(points);Need(double.IsFinite(tolerance)&&tolerance>=resolution,"Positive fit_tolerance must reach the numerical resolution of the supplied coordinates.");
        for(int i=0;i<points.Length;i++)
        {
            cancel();Need(Finite(Vec(points[i])),"Fit samples must be finite.");
            if(i>0)Need((Vec(points[i])-Vec(points[i-1])).Length>resolution,"Consecutive sample spacing is below coordinate resolution.");
        }
        Need((Vec(points[0])-Vec(points[^1])).Length>resolution,"Do not repeat the first sample; request closure explicitly.");
        var corners=cornerIndices??Array.Empty<int>();Need(corners.Distinct().Count()==corners.Length&&corners.All(i=>i>=0&&i<points.Length&&(closed||i>0&&i<points.Length-1)),"Corner indices must name distinct internal samples.");
        Need((startTangent==null)==(endTangent==null)&&(!closed||startTangent==null),"Endpoint directions belong to an open fit and must be supplied together.");
        var tangent=Tangents(points,closed,corners,startTangent,endTangent,cancel);var cornerSet=corners.ToHashSet();
        var values=closed?points.Concat(new[]{points[0]}).ToArray():points;
        var extended=closed?tangent.Concat(new[]{tangent[0]}).ToArray():tangent;
        var breaks=new[]{0}.Concat(corners.Where(i=>i!=0)).Concat(new[]{values.Length-1}).Distinct().OrderBy(i=>i).ToArray();
        var segments=new List<Segment>();
        for(int piece=0;piece<breaks.Length-1;piece++)
        {
            cancel();int begin=breaks[piece],end=breaks[piece+1];
            var ta=extended[begin];var tb=extended[end];
            if(cornerSet.Contains(begin%points.Length)||!closed&&begin==0&&startTangent==null)
                ta=end-begin>=2?CircleTangent(Vec(values[begin]),Vec(values[begin+1]),Vec(values[begin+2]),Vec(values[begin]),Vec(values[begin+1])-Vec(values[begin])):(Vec(values[end])-Vec(values[begin])).Unit;
            if(cornerSet.Contains(end%points.Length)||!closed&&end==values.Length-1&&endTangent==null)
                tb=end-begin>=2?CircleTangent(Vec(values[end-2]),Vec(values[end-1]),Vec(values[end]),Vec(values[end]),Vec(values[end])-Vec(values[end-1])):(Vec(values[end])-Vec(values[begin])).Unit;
            var stack=new Stack<(int First,int Last,V Start,V End)>();stack.Push((begin,end,ta,tb));bool firstPieceSegment=true;
            while(stack.Count>0)
            {
                cancel();var span=stack.Pop();var a=Vec(values[span.First]);var b=Vec(values[span.Last]);
                Segment[]? candidate=Single(a,b,span.Start,span.End,span.First,span.Last);
                if(candidate==null||!Fits(candidate,values,span.First,span.Last,tolerance,cancel))candidate=Biarc(a,b,span.Start,span.End,span.First,span.Last);
                if(candidate!=null&&Fits(candidate,values,span.First,span.Last,tolerance,cancel))
                {
                    foreach(var s in candidate)
                    {
                        var item=s with{StartsAtCorner=firstPieceSegment&&cornerSet.Contains(begin%points.Length)};
                        account?.Invoke(item);segments.Add(item);firstPieceSegment=false;
                    }
                }
                else
                {
                    Need(span.Last-span.First>1,"Adjacent samples and tangent conditions cannot define finite forward minor biarcs. Mark an intended cusp/corner or revise its endpoint directions.");
                    int split=span.First+(span.Last-span.First)/2;var mid=extended[split];
                    stack.Push((split,span.Last,mid,span.End));stack.Push((span.First,split,span.Start,mid));
                }
            }
        }
        // Coalesce geometrically identical adjacent primitives. Do not merge
        // across an intentional corner or exceed a semicircle per arc.
        var compact=new List<Segment>();
        foreach(var current in segments)
        {
            cancel();if(compact.Count==0||current.StartsAtCorner){compact.Add(current);continue;}
            var prior=compact[^1];var x=Geometry(prior);var y=Geometry(current);
            bool line=x.Sweep==0&&y.Sweep==0&&SameDirection(x.EndTangent,y.StartTangent);
            bool arc=x.Sweep!=0&&y.Sweep!=0&&Math.Sign(x.Sweep)==Math.Sign(y.Sweep)&&
                Math.Abs(x.Sweep+y.Sweep)<=Math.PI+1e-12&&(x.Center-y.Center).Length<=resolution&&Math.Abs(x.Radius-y.Radius)<=resolution;
            if(line||arc)compact[^1]=prior with{End=current.End,Bulge=line?0:Math.Tan((x.Sweep+y.Sweep)/4),SourceLast=current.SourceLast};else compact.Add(current);
        }
        var plan=new Plan(compact.ToArray(),closed,compact.Select((segment,index)=>(segment,index)).Where(p=>p.segment.StartsAtCorner).Select(p=>p.index).ToArray());
        _=Observe(plan,points,tolerance,false,cancel);return plan;
    }
    public static void RequireEndpointDirections(Plan plan,Point2? start,Point2? end)
    {
        if(start==null&&end==null)return;Need(start!=null&&end!=null&&!plan.Closed,"Endpoint directions require an open fit.");
        Need(SameDirection(Geometry(plan.Segments[0]).StartTangent,Vec(start!).Unit)&&SameDirection(Geometry(plan.Segments[^1]).EndTangent,Vec(end!).Unit),
            "Actual Polyline endpoints do not follow the requested forward tangent directions.");
    }
}
