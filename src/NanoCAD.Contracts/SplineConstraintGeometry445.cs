namespace NanoCAD.Contracts;

// Native Smooth identity is checked by the owning network adapter. These checks
// establish a useful common endpoint and tangent direction, not a G2 claim or
// a replacement for the native solver. No spline is resampled/reconstructed.
public static class SplineConstraintGeometry445
{
    private static void Need(bool condition,string message)
    {if(!condition)throw new DomainException("DRAWING_RELATION_MISMATCH",message);}
    // Guide26 GCSMOOTH requires the first native operand to be a Spline.
    // Preserve the public request while moving each complete endpoint/segment
    // binding with its object; this is not a geometric or identity search.
    public static SketchConstraintSpec NativeOrder(SketchConstraintSpec spec,string firstKind,string secondKind)
    {
        if(spec.Kind!="smooth"||firstKind=="spline"||secondKind!="spline")return spec;
        return spec with{First=spec.Second!,Second=spec.First,FirstPoint=spec.SecondPoint,SecondPoint=spec.FirstPoint,
            FirstSegmentIndex=spec.SecondSegmentIndex,SecondSegmentIndex=spec.FirstSegmentIndex};
    }
    public static void Complete(CurveSplineGeometry445 value)
    {
        void Number(double n)=>Need(double.IsFinite(n),"The complete native Spline definition contains a nonfinite value.");
        void Point(double[] p){Need(p.Length==3,"The complete native Spline point or vector must contain XYZ.");foreach(double n in p)Number(n);}
        foreach(var p in value.control_points)Point(p);foreach(double n in value.knots)Number(n);foreach(double n in value.weights)Number(n);
        if(value.control_point_tolerance is double tolerance)Number(tolerance);Number(value.knot_tolerance);
        Number(value.start_parameter);Number(value.end_parameter);Point(value.start_point);Point(value.end_point);
        Need(value.degree>0&&value.control_points.Length>0&&value.knots.Length>0&&value.end_parameter>value.start_parameter,
            "A Spline constraint requires its complete finite native NURBS definition and increasing parameter domain.");
        if(value.has_fit_data)
        {
            var fit=value.fit_data;
            Need(fit!=null,
                "The complete native fitted definition must be readable before adding or modifying its constraints.");
            foreach(var p in fit!.points)Point(p);Number(fit.fit_tolerance);
            if(fit.start_tangent is {} start)Point(start);if(fit.end_tangent is {} end)Point(end);
            Need(!fit!.tangents_exist||fit.start_tangent!=null&&fit.end_tangent!=null,
                "The native fitted Spline's stored tangent definitions are unavailable.");
        }
    }
    public static bool SameDefinition(CurveSplineGeometry445 a,CurveSplineGeometry445 b)
    {
        bool Numbers(double[] x,double[] y)=>x.Length==y.Length&&x.Zip(y).All(p=>DrawingProfileGeometry.Near(p.First,p.Second));
        bool Points(double[][] x,double[][] y)=>x.Length==y.Length&&x.Zip(y).All(p=>Numbers(p.First,p.Second));
        bool Optional(double[]? x,double[]? y)=>x==null?y==null:y!=null&&Numbers(x,y);
        var af=a.fit_data;var bf=b.fit_data;
        bool fit=af==null?bf==null:bf!=null&&af.degree==bf.degree&&af.fit_tolerance==bf.fit_tolerance&&
            af.tangents_exist==bf.tangents_exist&&af.knot_parameterization==bf.knot_parameterization&&
            Points(af.points,bf.points)&&Optional(af.start_tangent,bf.start_tangent)&&Optional(af.end_tangent,bf.end_tangent);
        return a.degree==b.degree&&a.rational==b.rational&&a.periodic==b.periodic&&a.closed==b.closed&&
            a.planar==b.planar&&a.has_fit_data==b.has_fit_data&&Points(a.control_points,b.control_points)&&
            Numbers(a.knots,b.knots)&&Numbers(a.weights,b.weights)&&a.control_point_tolerance==b.control_point_tolerance&&
            a.knot_tolerance==b.knot_tolerance&&DrawingProfileGeometry.Near(a.start_parameter,b.start_parameter)&&
            DrawingProfileGeometry.Near(a.end_parameter,b.end_parameter)&&Numbers(a.start_point,b.start_point)&&Numbers(a.end_point,b.end_point)&&fit&&
            a.entity_definition==b.entity_definition;
    }
    public static Point2 Tangent(SketchConstraintCurve curve,string? endpoint)
    {
        Need(endpoint is "start" or "end","Smooth uses an explicit start/end role on each open curve.");
        var point=DrawingProfileGeometry.Point(curve,endpoint);
        Point2? tangent=curve.Kind switch
        {
            "line"=>new(curve.End!.X-curve.Start!.X,curve.End.Y-curve.Start.Y),
            "arc"=>new(-(point.Y-curve.Center!.Y),point.X-curve.Center.X),
            "spline" or "ellipse"=>endpoint=="start"?curve.StartTangent:curve.EndTangent,
            _=>null
        };
        Need(tangent!=null&&double.IsFinite(tangent.X)&&double.IsFinite(tangent.Y),"The selected endpoint has no complete finite native tangent.");
        double length=Math.Sqrt(tangent!.X*tangent.X+tangent.Y*tangent.Y);
        Need(double.IsFinite(length)&&length>1e-12,"The selected endpoint has a degenerate tangent.");
        return new(tangent.X/length,tangent.Y/length);
    }
    public static void Smooth(SketchConstraintCurve a,string? firstPoint,SketchConstraintCurve b,string? secondPoint)
    {
        Need(a.Kind=="spline"||b.Kind=="spline","Native Smooth requires at least one actual Spline.");
        foreach(var curve in new[]{a,b}.Where(c=>c.Kind=="spline"))
        {Need(curve.Spline!=null&&!curve.Spline.closed&&!curve.Spline.periodic,"Smooth endpoint roles require an open native Spline.");Complete(curve.Spline!);}
        Need(DrawingProfileGeometry.Same(DrawingProfileGeometry.Point(a,firstPoint),DrawingProfileGeometry.Point(b,secondPoint)),
            "Selected curve endpoints must already coincide; Smooth attaches a retained relationship without changing the intended shape.");
        var u=Tangent(a,firstPoint);var v=Tangent(b,secondPoint);
        Need(Math.Abs(u.X*v.Y-u.Y*v.X)<=1e-7,"Selected endpoint tangents must already be collinear before adding native Smooth.");
        double orientation=(firstPoint=="start"?1:-1)*(secondPoint=="start"?1:-1);
        Need(orientation*(u.X*v.X+u.Y*v.Y)<0,"The two curves must continue through the shared endpoint, without a tangent reversal or cusp.");
    }
}
