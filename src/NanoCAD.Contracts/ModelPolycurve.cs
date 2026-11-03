namespace NanoCAD.Contracts;

// Closed planar polycurve: each vertex's bulge describes its outgoing edge.
// This is a reusable model profile, independent of drawing source handles.
public static class ModelPolycurve
{
    public static SketchConstraintCurve[] Curves(ModelProfileSpec profile,Action? cancel=null)
    {
        void Need(bool ok,string message){if(!ok)throw new DomainException("INVALID_GEOMETRY",message);}
        var vertices=profile.Vertices;var bulges=profile.Bulges;
        Need(profile.Kind=="polycurve"&&vertices is {Length:>=2}&&bulges!=null&&bulges.Length==vertices.Length,"Polycurve needs at least two vertices and one bulge per edge.");
        ProfileGeometryBudget.Profile(profile,cancel);var budget=new ProfileGeometryBudget(cancel);
        var result=new List<SketchConstraintCurve>();
        for(int i=0;i<vertices!.Length;i++)
        {
            cancel?.Invoke();
            var a=vertices[i];var b=vertices[(i+1)%vertices.Length];double bulge=bulges![i],length=DrawingProfileGeometry.Distance(a,b);
            Need(new[]{a.X,a.Y,b.X,b.Y,bulge,length}.All(double.IsFinite)&&Math.Abs(a.X)<=1e6&&Math.Abs(a.Y)<=1e6&&length>=.001,"Polycurve contains a nonfinite, unbounded or degenerate edge.");
            if(bulge==0){var line=new SketchConstraintCurve(i.ToString(),"line",a,b);budget.Add(line);result.Add(line);continue;}
            Need(Math.Abs(bulge)>=1e-5&&Math.Abs(bulge)<=1000,"Use zero for straight edges; nonzero bulge magnitude must be1e-5..1000.");
            double factor=(1-bulge*bulge)/(4*bulge);var center=new Point2((a.X+b.X)/2-(b.Y-a.Y)*factor,(a.Y+b.Y)/2+(b.X-a.X)*factor);
            double radius=length*(1+bulge*bulge)/(4*Math.Abs(bulge)),sweep=4*Math.Atan(Math.Abs(bulge));
            var arc=new SketchConstraintCurve(i.ToString(),"arc",bulge>0?a:b,bulge>0?b:a,center,radius,sweep);DrawingArcContours.ValidateArc(arc);budget.Add(arc);result.Add(arc);
        }
        return result.ToArray();
    }
    public static ModelProfileSpec FromCurves(SketchConstraintCurve[] curves,Action? cancel=null)
    {
        var edges=DrawingArcContours.Order(curves,cancel);return new("polycurve",null,0,edges.Select(e=>e.Start).ToArray(),edges.Select(e=>e.Bulge).ToArray());
    }
    public static Point2 Midpoint(SketchConstraintCurve arc)
    {
        double angle=Math.Atan2(arc.Start!.Y-arc.Center!.Y,arc.Start.X-arc.Center.X)+arc.Sweep/2;
        return new(arc.Center.X+arc.Radius*Math.Cos(angle),arc.Center.Y+arc.Radius*Math.Sin(angle));
    }
    public static SketchConstraintCurve FromSketchArc(string id,Point3 center,double radius,Point3 normal,Point3 reference,double start,double end,SketchPlaneSpec plane)
    {
        void Need(bool ok,string message){if(!ok)throw new DomainException("SKETCH_GEOMETRY_UNAVAILABLE",message);}
        double Dot(Point3 a,Point3 b)=>a.X*b.X+a.Y*b.Y+a.Z*b.Z;
        Point3 Cross(Point3 a,Point3 b)=>new(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);
        var planeNormal=Cross(plane.XAxis,plane.YAxis);double orientation=Dot(normal,planeNormal);
        Need(new[]{radius,start,end,orientation}.All(double.IsFinite)&&radius>=.001&&radius<=1e6&&Math.Abs(Dot(normal,normal)-1)<1e-9&&Math.Abs(Dot(reference,reference)-1)<1e-9&&Math.Abs(Dot(reference,normal))<1e-9&&Math.Abs(Math.Abs(orientation)-1)<1e-9,"Native circular arc frame is not an orthonormal frame in its sketch plane.");
        Point2 Local(Point3 p)
        {
            var delta=new Point3(p.X-plane.Origin.X,p.Y-plane.Origin.Y,p.Z-plane.Origin.Z);
            Need(Math.Abs(Dot(delta,planeNormal))<1e-7,"Native arc lies outside its sketch plane.");return new(Dot(delta,plane.XAxis),Dot(delta,plane.YAxis));
        }
        var localCenter=Local(center);double sweep=end-start;
        if(DrawingProfileGeometry.Near(Math.Abs(sweep),2*Math.PI))return new(id,"circle",Center:localCenter,Radius:radius);
        if(sweep<=0)sweep+=2*Math.PI;
        Need(sweep>=1e-6&&sweep<=2*Math.PI-1e-6,"Native arc sweep is unavailable or out of scope.");
        var tangent=Cross(normal,reference);
        Point2 At(double angle)=>Local(new(center.X+radius*(reference.X*Math.Cos(angle)+tangent.X*Math.Sin(angle)),center.Y+radius*(reference.Y*Math.Cos(angle)+tangent.Y*Math.Sin(angle)),center.Z+radius*(reference.Z*Math.Cos(angle)+tangent.Z*Math.Sin(angle))));
        var a=At(start);var b=At(start+sweep);var result=new SketchConstraintCurve(id,"arc",orientation>0?a:b,orientation>0?b:a,localCenter,radius,sweep);DrawingArcContours.ValidateArc(result);return result;
    }
    public static (Point3 Min,Point3 Max) Bounds(SketchConstraintCurve[] curves,SketchPlaneSpec plane,Action? cancel=null)
    {
        var points=curves.SelectMany(c=>{cancel?.Invoke();return new[]{c.Start!,c.End!};}).ToList();
        foreach(var arc in curves.Where(c=>c.Kind=="arc"))
        {
            cancel?.Invoke();
            foreach(var pair in new[]{(plane.XAxis.X,plane.YAxis.X),(plane.XAxis.Y,plane.YAxis.Y),(plane.XAxis.Z,plane.YAxis.Z)})
            {
                if(Math.Abs(pair.Item1)+Math.Abs(pair.Item2)<1e-15)continue;
                double angle=Math.Atan2(pair.Item2,pair.Item1);
                foreach(double t in new[]{angle,angle+Math.PI})
                {var p=new Point2(arc.Center!.X+arc.Radius*Math.Cos(t),arc.Center.Y+arc.Radius*Math.Sin(t));if(DrawingArcContours.OnArc(arc,p))points.Add(p);}
            }
        }
        var world=points.Select(p=>{cancel?.Invoke();return ModelExtrusionGeometry.World(plane,p);}).ToArray();
        return(new(world.Min(p=>p.X),world.Min(p=>p.Y),world.Min(p=>p.Z)),new(world.Max(p=>p.X),world.Max(p=>p.Y),world.Max(p=>p.Z)));
    }
}
