using System.Text.Json;
using Multicad.DatabaseServices;
using Multicad.DatabaseServices.StandardObjects;
using NanoCAD.Contracts;
using G=Multicad.Geometry;

namespace NanoCADBridge;

// Uses the SDK's actual geometry objects and native evaluation. There is no
// resampling/faceting, substitute renderer, or hidden sketch-editor lifecycle.
internal static class NativeSketchCurveGeometry445
{
    static double Number(double value)=>double.IsFinite(value)?value:throw new DomainException("SKETCH_GEOMETRY_UNAVAILABLE","Native curve reports nonfinite geometry.");
    static double[] P(G.Point3d value)=>[Number(value.X),Number(value.Y),Number(value.Z)];
    static double[] V(G.Vector3d value)=>[Number(value.X),Number(value.Y),Number(value.Z)];
    static G.Point3d Point(Point3 value)=>new(value.X,value.Y,value.Z);
    static Point3 Q(JsonElement value)=>new(value[0].GetDouble(),value[1].GetDouble(),value[2].GetDouble());
    static Point3 Subtract(Point3 a,Point3 b)=>new(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
    static double Norm(Point3 value)=>Math.Sqrt(DrawingWorkplane.Dot(value,value));
    static double[] Tangent(G.Curve3d curve,double parameter,Action cancel)
    {
        cancel();curve.EvalPoint(parameter,1,out var derivatives);
        NativeSketchMembers385.Need(derivatives!=null&&derivatives.Count>=1,"SKETCH_GEOMETRY_UNAVAILABLE","Native curve endpoint derivative is unavailable.");return V(derivatives[0]);
    }
    internal static G.NurbCurve3d CreateSpline(NativeSketchPrimitive385 input)
    {
        NativeSketchMembers385.Need(input.Kind=="spline","INVALID_ARGUMENT","Native NURBS creation requires a Spline input.");
        var spec=input.Spline??throw new DomainException("INVALID_ARGUMENT","Missing complete native NURBS definition.");
        using var knots=new G.GeomKnotVector(spec.Knots);var controls=input.Points.Select(Point).ToList();
        // The caller owns the SDK curve until AddGeometry has copied its full
        // definition into the sketch. No resident DbSpline is registered.
        // Closed clamped curves retain their endpoint control; `periodic`
        // is not silently substituted for that representation.
        return spec.Weights.Length==0?new G.NurbCurve3d(spec.Degree,knots,controls,false):new G.NurbCurve3d(spec.Degree,knots,controls,spec.Weights,false);
    }
    internal static DbGeometry Create(NativeSketchPrimitive385 input)
    {
        var ellipse=input.Ellipse??throw new DomainException("INVALID_ARGUMENT","Missing native Ellipse angles.");
        var a=Subtract(input.Points[1],input.Points[0]);var b=Subtract(input.Points[2],input.Points[0]);double ra=Norm(a),rb=Norm(b);
        using var geometry=new G.EllipArc3d(Point(input.Points[0]),new(a.X/ra,a.Y/ra,a.Z/ra),new(b.X/rb,b.Y/rb,b.Z/rb),ra,rb,ellipse.StartAngle,ellipse.EndAngle);
        var result=new DbGeometry();result.Geometry=geometry;return result;
    }
    internal static JsonElement Spline(G.NurbCurve3d spline,Action cancel)
    {
        cancel();int count=spline.NumControlPoints,degree=spline.Degree;var controls=new List<double[]>();var knots=new List<double>();var weights=new List<double>();var budget=new ModelInventoryBudget(cancel);
        for(int i=0;i<count;i++){cancel();var point=P(spline.ControlPointAt(i));budget.Add(point);controls.Add(point);}
        for(int i=0;i<spline.NumKnots;i++){cancel();double value=Number(spline.KnotAt(i));budget.Add(value);knots.Add(value);}
        for(int i=0;i<spline.NumWeights;i++){cancel();double value=Number(spline.WeightAt(i));budget.Add(value);weights.Add(value);}
        using var nativeKnots=spline.Knots;double knotTolerance=Number(nativeKnots.Tolerance);
        bool planar=spline.IsPlanar(out var nativePlane);nativePlane?.Dispose();
        CurveSplineFit445? fit=null;string? reason=null;
        if(spline.HasFitData)
        {
            if(spline.GetFitData(out var points,out var tolerance,out bool tangents,out var start,out var end)&&points!=null)
            {
                var actualPoints=new List<double[]>();foreach(var point in points){cancel();var actual=P(point);budget.Add(actual);actualPoints.Add(actual);}
                fit=new(degree,actualPoints.ToArray(),Number(tolerance.EqualPoint),tangents,tangents?V(start):null,tangents?V(end):null,null);
            }
            else reason="native_MAPI_GetFitData_returned_unavailable";
        }
        double first=Number(spline.StartParam),last=Number(spline.EndParam);
        double period=0;bool periodic=spline.IsPeriodic(ref period);
        var definition=new CurveSplineGeometry445(degree,spline.Rational,periodic,spline.IsClosed(),planar,spline.HasFitData,
            controls.ToArray(),knots.ToArray(),weights.ToArray(),null,knotTolerance,first,last,P(spline.StartPoint),P(spline.EndPoint),fit,reason,
            "native_MAPI_NurbCurve3d_WCS_definition; control_point_tolerance_and_fit_knot_parameterization_are_not_exposed_and_remain_null");
        var result=Wire.Element(new{kind="spline",definition,start_tangent=Tangent(spline,first,cancel),end_tangent=Tangent(spline,last,cancel)});budget.Add(result);return result;
    }
    internal static JsonElement Ellipse(G.EllipArc3d ellipse,Action cancel)
    {
        cancel();var result=Wire.Element(new{kind="elliptical_arc",center=P(ellipse.Center),major_axis=V(ellipse.MajorAxis),minor_axis=V(ellipse.MinorAxis),normal=V(ellipse.Normal),
            major_radius=Number(ellipse.MajorRadius),minor_radius=Number(ellipse.MinorRadius),start_angle=Number(ellipse.StartAng),end_angle=Number(ellipse.EndAng),
            start=P(ellipse.StartPoint),end=P(ellipse.EndPoint),start_tangent=Tangent(ellipse,ellipse.StartAng,cancel),end_tangent=Tangent(ellipse,ellipse.EndAng,cancel),closed=ellipse.IsClosed(),angle_unit="radians"});new ModelInventoryBudget(cancel).Add(result);return result;
    }
    internal static SketchConstraintCurve Curve(string id,JsonElement geometry,SketchPlaneSpec plane,Action cancel)
    {
        cancel();Point2 Local(JsonElement point)=>DrawingWorkplane.Local(plane,Q(point));
        Point2 Direction(JsonElement vector){var v=Q(vector);return new(DrawingWorkplane.Dot(v,plane.XAxis),DrawingWorkplane.Dot(v,plane.YAxis));}
        var startTangent=Direction(geometry.GetProperty("start_tangent"));var endTangent=Direction(geometry.GetProperty("end_tangent"));
        if(geometry.GetProperty("kind").GetString()=="spline")
        {
            var data=geometry.GetProperty("definition").Deserialize<CurveSplineGeometry445>(Wire.Json)??throw new DomainException("SKETCH_GEOMETRY_UNAVAILABLE","Complete native NURBS definition is unavailable.");
            foreach(var point in data.control_points){cancel();DrawingWorkplane.Local(plane,new(point[0],point[1],point[2]));}
            return new(id,"spline",DrawingWorkplane.Local(plane,new(data.start_point[0],data.start_point[1],data.start_point[2])),DrawingWorkplane.Local(plane,new(data.end_point[0],data.end_point[1],data.end_point[2])),Spline:data,StartTangent:startTangent,EndTangent:endTangent);
        }
        var ellipse=new NativeSketchEllipseGeometry445(Q(geometry.GetProperty("center")),Q(geometry.GetProperty("major_axis")),Q(geometry.GetProperty("minor_axis")),geometry.GetProperty("major_radius").GetDouble(),geometry.GetProperty("minor_radius").GetDouble(),geometry.GetProperty("start_angle").GetDouble(),geometry.GetProperty("end_angle").GetDouble());
        DrawingWorkplane.Local(plane,ellipse.Center);
        var normal=DrawingWorkplane.Normal(plane);NativeSketchMembers385.Need(Math.Abs(DrawingWorkplane.Dot(normal,ellipse.MajorAxis))<=1e-9&&Math.Abs(DrawingWorkplane.Dot(normal,ellipse.MinorAxis))<=1e-9,"SKETCH_GEOMETRY_UNAVAILABLE","Native Ellipse axes do not lie in the observed sketch plane.");
        return new(id,"ellipse",Local(geometry.GetProperty("start")),Local(geometry.GetProperty("end")),Local(geometry.GetProperty("center")),StartTangent:startTangent,EndTangent:endTangent,Ellipse:ellipse);
    }
    internal static bool Requested(NativeSketchPrimitive385 input,JsonElement geometry,SketchPlaneSpec plane,Action cancel)
    {
        cancel();bool Near(double a,double b)=>Math.Abs(a-b)<=Math.Max(1e-7,1e-9*Math.Max(Math.Abs(a),Math.Abs(b)));
        bool Same(Point3 a,Point3 b)=>Near(a.X,b.X)&&Near(a.Y,b.Y)&&Near(a.Z,b.Z);
        if(input.Kind=="spline")
        {
            if(geometry.GetProperty("kind").GetString()!="spline")return false;
            var actual=Curve("requested",geometry,plane,cancel).Spline!;var expected=input.Spline!;double span=actual.end_parameter-actual.start_parameter,requestedSpan=expected.Knots[input.Points.Length]-expected.Knots[expected.Degree];
            bool sameKnots=span>0&&actual.knots.Length==expected.Knots.Length&&actual.knots.Zip(expected.Knots,(a,b)=>Near((a-actual.start_parameter)/span,(b-expected.Knots[expected.Degree])/requestedSpan)).All(v=>v);
            bool uniform=expected.Weights.Length==0||expected.Weights.All(w=>Near(w/expected.Weights[0],1));
            bool sameWeights=expected.Weights.Length==0?actual.weights.Length==0||actual.weights.All(v=>Near(v/actual.weights[0],1)):
                !actual.rational&&uniform||actual.weights.Length==expected.Weights.Length&&actual.weights.Zip(expected.Weights,(a,b)=>Near(a/actual.weights[0],b/expected.Weights[0])).All(v=>v);
            bool rational=actual.rational==(expected.Weights.Length>0)||uniform;
            return actual.degree==expected.Degree&&actual.closed==expected.Closed&&rational&&actual.control_points.Length==input.Points.Length&&actual.control_points.Zip(input.Points,(a,b)=>Same(new(a[0],a[1],a[2]),b)).All(v=>v)&&sameKnots&&sameWeights;
        }
        if(geometry.GetProperty("kind").GetString()!="elliptical_arc")return false;var ellipse=Curve("requested",geometry,plane,cancel).Ellipse!;
        var major=Subtract(input.Points[1],input.Points[0]);var minor=Subtract(input.Points[2],input.Points[0]);double ra=Norm(major),rb=Norm(minor);
        double offset=(ellipse.StartAngle-input.Ellipse!.StartAngle)/(2*Math.PI);
        return Same(ellipse.Center,input.Points[0])&&Same(ellipse.MajorAxis,new(major.X/ra,major.Y/ra,major.Z/ra))&&Same(ellipse.MinorAxis,new(minor.X/rb,minor.Y/rb,minor.Z/rb))&&Near(ellipse.MajorRadius,ra)&&Near(ellipse.MinorRadius,rb)&&Near(offset,Math.Round(offset))&&Near(ellipse.EndAngle-ellipse.StartAngle,input.Ellipse.EndAngle-input.Ellipse.StartAngle);
    }
}
