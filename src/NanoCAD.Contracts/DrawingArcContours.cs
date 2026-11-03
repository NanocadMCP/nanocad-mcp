namespace NanoCAD.Contracts;

// Exact circular segments, not tessellated approximations. Angles are radians;
// the ordinary ACAD angular *dimension variables* use degrees separately.
public static class DrawingArcContours
{
    public sealed record Edge(SketchConstraintCurve Curve,bool Reversed)
    {
        public Point2 Start=>Reversed?Curve.End!:Curve.Start!;
        public Point2 End=>Reversed?Curve.Start!:Curve.End!;
        public double Sweep=>Curve.Kind=="arc"?Curve.Sweep*(Reversed?-1:1):0;
        public double Bulge=>Math.Tan(Sweep/4);
    }
    private const double Tau=2*Math.PI,Tolerance=1e-7;
    private static void Need(bool ok,string message){if(!ok)throw new DomainException("INVALID_GEOMETRY",message);}
    private static double Norm(double a){a%=Tau;return a<0?a+Tau:a;}
    private static Point2 Sub(Point2 a,Point2 b)=>new(a.X-b.X,a.Y-b.Y);
    private static double Cross(Point2 a,Point2 b)=>a.X*b.Y-a.Y*b.X;
    private static double Dot(Point2 a,Point2 b)=>a.X*b.X+a.Y*b.Y;
    private static bool Same(Point2 a,Point2 b)=>DrawingProfileGeometry.Distance(a,b)<=Tolerance;
    private static double Angle(SketchConstraintCurve c,Point2 p)=>Math.Atan2(p.Y-c.Center!.Y,p.X-c.Center.X);
    public static void ValidateArc(SketchConstraintCurve c)
    {
        Need(c.Kind=="arc"&&c.Start!=null&&c.End!=null&&c.Center!=null,"A complete arc source is required.");
        Need(new[]{c.Start!.X,c.Start.Y,c.End!.X,c.End.Y,c.Center!.X,c.Center.Y,c.Radius,c.Sweep}.All(double.IsFinite)&&
            c.Radius>=.001&&c.Radius<=1e6&&c.Sweep>=1e-6&&c.Sweep<=Tau-1e-6,"Arc has invalid radius or sweep; use Circle for a full turn.");
        Need(DrawingProfileGeometry.Near(DrawingProfileGeometry.Distance(c.Start,c.Center),c.Radius)&&DrawingProfileGeometry.Near(DrawingProfileGeometry.Distance(c.End,c.Center),c.Radius),"Arc endpoints are not on the circle.");
        double a=Angle(c,c.Start)+c.Sweep;
        Need(DrawingProfileGeometry.Same(c.End,new(c.Center.X+c.Radius*Math.Cos(a),c.Center.Y+c.Radius*Math.Sin(a))),"Arc sweep does not match its counterclockwise endpoints.");
    }
    public static bool OnArc(SketchConstraintCurve c,Point2 p,bool interior=false)
    {
        if(Math.Abs(DrawingProfileGeometry.Distance(p,c.Center!)-c.Radius)>Tolerance)return false;
        if(Same(p,c.Start!)||Same(p,c.End!))return !interior;
        double a=Norm(Angle(c,p)-Angle(c,c.Start!));return a<c.Sweep;
    }
    public static Edge[] Order(SketchConstraintCurve[] curves,Action? cancel=null)
    {
        Need(curves.Length>=2,"Use at least two Lines/Arcs forming one simple closed contour.");
        var budget=new ProfileGeometryBudget(cancel);var ids=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var c in curves)
        {
            budget.Add(c);Need(c.Kind is "line" or "arc","Use Lines/Arcs forming one simple closed contour.");
            Need(ids.Add(c.Id),"Duplicate contour source.");
            if(c.Kind=="arc")ValidateArc(c);
            Need(c.Start!=null&&c.End!=null&&!Same(c.Start,c.End)&&new[]{c.Start.X,c.Start.Y,c.End.X,c.End.Y}.All(v=>double.IsFinite(v)&&Math.Abs(v)<=1e6),"Degenerate or unbounded contour edge.");
        }
        foreach(var c in curves)foreach(var p in new[]{c.Start!,c.End!})
        {
            int degree=0;
            foreach(var v in curves){cancel?.Invoke();degree+=(Same(p,v.Start!)?1:0)+(Same(p,v.End!)?1:0);}
            Need(degree==2,"Contour branches or has a gap.");
        }
        var remaining=curves.Skip(1).ToList();var edges=new List<Edge>{new(curves[0],false)};
        while(remaining.Count>0)
        {
            cancel?.Invoke();var p=edges[^1].End;var matches=remaining.Where(c=>{cancel?.Invoke();return Same(c.Start!,p)||Same(c.End!,p);}).ToArray();
            Need(matches.Length==1,"Contour contains disconnected loops.");var c=matches[0];edges.Add(new(c,!Same(c.Start!,p)));remaining.Remove(c);
        }
        Need(Same(edges[^1].End,edges[0].Start),"Contour is open.");
        for(int i=0;i<edges.Count;i++)for(int j=i+1;j<edges.Count;j++)
        {
            cancel?.Invoke();
            var a=edges[i].Curve;var b=edges[j].Curve;
            var common=new[]{a.Start!,a.End!}.Where(p=>Same(p,b.Start!)||Same(p,b.End!)).ToArray();
            foreach(var p in Intersections(a,b))Need(common.Any(q=>Same(p,q)),"Contour self-intersects or touches away from its shared endpoints.");
        }
        return edges.ToArray();
    }
    private static IEnumerable<Point2> Intersections(SketchConstraintCurve a,SketchConstraintCurve b)
    {
        if(a.Kind=="line"&&b.Kind=="line")
        {
            var u=Sub(a.End!,a.Start!);var v=Sub(b.End!,b.Start!);var d=Sub(b.Start!,a.Start!);double cross=Cross(u,v);
            if(Math.Abs(cross)<=1e-12*Math.Sqrt(Dot(u,u)*Dot(v,v)))
            {
                if(Math.Abs(Cross(d,u))>Tolerance*Math.Sqrt(Dot(u,u)))yield break;
                double t0=Dot(d,u)/Dot(u,u),t1=Dot(Sub(b.End!,a.Start!),u)/Dot(u,u);
                Need(Math.Min(1,Math.Max(t0,t1))-Math.Max(0,Math.Min(t0,t1))<=Tolerance/Math.Sqrt(Dot(u,u)),"Overlapping contour lines.");yield break;
            }
            double t=Cross(d,v)/cross,s=Cross(d,u)/cross;
            if(t>=-1e-10&&t<=1+1e-10&&s>=-1e-10&&s<=1+1e-10)yield return new(a.Start!.X+t*u.X,a.Start.Y+t*u.Y);
            yield break;
        }
        if(a.Kind=="line"||b.Kind=="line")
        {
            if(a.Kind!="line")(a,b)=(b,a);var u=Sub(a.End!,a.Start!);var d=Sub(a.Start!,b.Center!);
            double aa=Dot(u,u),half=Dot(d,u),cc=Dot(d,d)-b.Radius*b.Radius,discriminant=half*half-aa*cc;
            double eps=1e-12*Math.Max(1,Math.Abs(half*half)+Math.Abs(aa*cc));if(discriminant< -eps)yield break;
            double root=Math.Sqrt(Math.Max(0,discriminant));
            foreach(var t in new[]{(-half-root)/aa,(-half+root)/aa})
                if(t>=-1e-10&&t<=1+1e-10){var p=new Point2(a.Start!.X+t*u.X,a.Start.Y+t*u.Y);if(OnArc(b,p))yield return p;}
            yield break;
        }
        var delta=Sub(b.Center!,a.Center!);double distance=Math.Sqrt(Dot(delta,delta));
        if(distance<=Tolerance&&Math.Abs(a.Radius-b.Radius)<=Tolerance)
        {
            Point2 Mid(SketchConstraintCurve c){double angle=Angle(c,c.Start!)+c.Sweep/2;return new(c.Center!.X+c.Radius*Math.Cos(angle),c.Center.Y+c.Radius*Math.Sin(angle));}
            Need(!new[]{a.Start!,a.End!,Mid(a)}.Any(p=>OnArc(b,p,true))&&!new[]{b.Start!,b.End!,Mid(b)}.Any(p=>OnArc(a,p,true)),"Overlapping contour arcs.");
            foreach(var p in new[]{a.Start!,a.End!})if(OnArc(b,p))yield return p;yield break;
        }
        if(distance<=Tolerance||distance>a.Radius+b.Radius+Tolerance||distance<Math.Abs(a.Radius-b.Radius)-Tolerance)yield break;
        double x=(a.Radius*a.Radius-b.Radius*b.Radius+distance*distance)/(2*distance),h2=a.Radius*a.Radius-x*x;
        if(h2< -Tolerance*Math.Max(a.Radius,b.Radius))yield break;double h=Math.Sqrt(Math.Max(0,h2));
        foreach(double sign in new[]{-1d,1d})
        {var p=new Point2(a.Center!.X+(x*delta.X-sign*h*delta.Y)/distance,a.Center.Y+(x*delta.Y+sign*h*delta.X)/distance);if(OnArc(a,p)&&OnArc(b,p))yield return p;}
    }
    // Cross-loop topology only. Circle halves are analytic query operands,
    // never substitute native sketch elements or geometry to write into CAD.
    internal static bool TouchesOrCrosses(SketchConstraintCurve first,SketchConstraintCurve second,Action cancel)
    {
        SketchConstraintCurve[] Parts(SketchConstraintCurve curve)
        {
            if(curve.Kind!="circle")return [curve];
            var center=curve.Center!;var right=new Point2(center.X+curve.Radius,center.Y);var left=new Point2(center.X-curve.Radius,center.Y);
            return [curve with{Kind="arc",Start=right,End=left,Sweep=Math.PI},curve with{Kind="arc",Start=left,End=right,Sweep=Math.PI}];
        }
        bool On(SketchConstraintCurve curve,Point2 point)
        {
            if(curve.Kind=="arc")return OnArc(curve,point);
            var a=curve.Start!;var b=curve.End!;double dx=b.X-a.X,dy=b.Y-a.Y,length=dx*dx+dy*dy;
            double t=Math.Clamp(((point.X-a.X)*dx+(point.Y-a.Y)*dy)/length,0,1);
            return DrawingProfileGeometry.Distance(point,new(a.X+t*dx,a.Y+t*dy))<=Tolerance;
        }
        foreach(var a in Parts(first))foreach(var b in Parts(second))
        {
            cancel();if(new[]{a.Start!,a.End!}.Any(point=>On(b,point))||new[]{b.Start!,b.End!}.Any(point=>On(a,point))||Intersections(a,b).Any())return true;
        }
        return false;
    }
    public static ExtrusionOracle Measure(SketchConstraintCurve[] curves,double height,Action? cancel=null)
    {
        var edges=Order(curves,cancel);Need(double.IsFinite(height)&&Math.Abs(height)>=.001&&Math.Abs(height)<=1e6,"Invalid extrusion height.");
        var origin=edges[0].Start;double area=0,mx=0,my=0;var bounds=new List<Point2>();
        foreach(var edge in edges)
        {
            cancel?.Invoke();
            var a=Sub(edge.Start,origin);var b=Sub(edge.End,origin);double cross=Cross(a,b);area+=cross/2;mx+=(a.X+b.X)*cross/6;my+=(a.Y+b.Y)*cross/6;
            bounds.Add(edge.Start);bounds.Add(edge.End);
            if(edge.Curve.Kind!="arc")continue;
            var c=edge.Curve;var center=Sub(c.Center!,origin);double theta=edge.Sweep,half=theta/2,mid=Angle(c,edge.Start)+half;
            // Stable theta - sin(theta), including small arcs.
            double difference=Math.Abs(theta)<.01?theta*theta*theta*(1d/6-theta*theta/120+Math.Pow(theta,4)/5040):theta-Math.Sin(theta);
            double segment=c.Radius*c.Radius*difference/2,moment=2d/3*c.Radius*c.Radius*c.Radius*Math.Pow(Math.Sin(half),3);
            area+=segment;mx+=segment*center.X+moment*Math.Cos(mid);my+=segment*center.Y+moment*Math.Sin(mid);
            for(int k=0;k<4;k++){double t=k*Math.PI/2;var p=new Point2(c.Center!.X+c.Radius*Math.Cos(t),c.Center.Y+c.Radius*Math.Sin(t));if(OnArc(c,p))bounds.Add(p);}
        }
        Need(double.IsFinite(area)&&Math.Abs(area)>1e-8,"Contour encloses no usable area.");
        var centroid=new Point3(origin.X+mx/area,origin.Y+my/area,height/2);
        Need(new[]{centroid.X,centroid.Y}.All(double.IsFinite)&&bounds.All(p=>Math.Abs(p.X)<=1e6&&Math.Abs(p.Y)<=1e6),"Contour moments or bounds unavailable.");
        return new(Math.Abs(area),Math.Abs(area*height),centroid,new(bounds.Min(p=>p.X),bounds.Min(p=>p.Y),Math.Min(0,height)),new(bounds.Max(p=>p.X),bounds.Max(p=>p.Y),Math.Max(0,height)),new(0,0,1));
    }
}
