using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record TrimCurveSpec(string[] Targets,string[] BoundaryHandles,Point2 PickPoint) : EditSpec("trim_curve",Targets);
public sealed record ExtendCurveSpec(string[] Targets,string[] BoundaryHandles,string End) : EditSpec("extend_curve",Targets);
public sealed record OffsetCurveSpec(string[] Targets,double Distance,string Side) : EditSpec("offset_curve",Targets);

public static class EditReferences
{
    public static string[] Boundaries(EditSpec operation)=>operation switch
    {TrimCurveSpec trim=>trim.BoundaryHandles,ExtendCurveSpec extend=>extend.BoundaryHandles,NativeContourFinishingSpec342 native=>native.BoundaryHandles,_=>Array.Empty<string>()};
    public static IEnumerable<string> All(EditSpec operation)=>operation.Handles.Concat(Boundaries(operation))
        .Concat(operation is SolidBooleanEditSpec boolean?boolean.ToolHandles:Array.Empty<string>())
        .Concat(operation is CreateAssociativePathArray441 path441?new[]{path441.PathHandle}:Array.Empty<string>())
        .Concat(operation is SetAssociativePathArray441{PathHandle:{} targetPath441}?new[]{targetPath441}:Array.Empty<string>())
        .Concat(operation is SolidReplacement409 replacement?new[]{replacement.SourceHandle}:Array.Empty<string>())
        .Concat(operation is SolidImprintEditSpec393 imprint?imprint.SourceCurves.Select(value=>value.Handle):Array.Empty<string>())
        .Concat(operation is SolidFaceEditSpec{PathHandle:not null} face?new[]{face.PathHandle!}:Array.Empty<string>())
        .Concat(operation is RegionBooleanEditSpec region?region.ToolHandles:Array.Empty<string>())
        .Concat(operation is ModelFeatureRevolutionSpec revolution?revolution.SourceHandles.Append(revolution.AxisHandle):Array.Empty<string>())
        .Concat(operation is ModelProfileCreate340 profile?profile.SourceHandles:Array.Empty<string>())
        .Concat(operation is ModelSketchCreate362 sketch?sketch.SourceHandles:Array.Empty<string>())
        .Concat(operation is ModelProfileSweep362 sweep?sweep.PathHandles:Array.Empty<string>())
        .Concat(operation is ModelProfileLoft446 loft?loft.GuideHandles.SelectMany(chain=>chain).Concat(loft.PathHandle==null?Array.Empty<string>():new[]{loft.PathHandle}):Array.Empty<string>())
        .Concat(operation is ModelProfileRevolution411 revolutionProfile?new[]{revolutionProfile.AxisHandle}:Array.Empty<string>())
        .Concat(operation is ModelProfileRegionsSpec350 regions?regions.AddSourceHandles:Array.Empty<string>())
        .Concat(operation is ModelProfileElementsSpec358 elements?elements.AddSourceHandles:Array.Empty<string>())
        .Concat(operation is SetHatchAssociation358 hatch?HatchAssociationValidation358.Sources(hatch):Array.Empty<string>());
}

public static partial class ModifyValidation
{
    private static EditSpec ParseContourEdit(JsonElement item,string path,string[] handles)
    {
        if(handles.Length!=1)throw Invalid(path+".handles","Contour operations require exactly one source curve.");
        string operation=Text(item,"operation",path,32);
        if(operation=="offset_curve")
        {
            Shape(item,path,"operation","handles","distance","side","mode");
            string side=Text(item,"side",path,16);
            if(side is not "left" and not "right" and not "inward" and not "outward")throw Invalid(path+".side","Use left/right for a directed Line, inward/outward for circles, arcs and closed convex straight polylines.");
            return new OffsetCurveSpec(handles,Number(Required(item,"distance",path),path+".distance",.001,1e6),side);
        }
        if(operation=="trim_curve")Shape(item,path,"operation","handles","boundary_handles","pick_point","mode");
        else Shape(item,path,"operation","handles","boundary_handles","end","mode");
        var input=Required(item,"boundary_handles",path);
        if(input.ValueKind!=JsonValueKind.Array||input.GetArrayLength()<1)throw Invalid(path+".boundary_handles","Use distinct existing boundary handles.");
        var boundaries=new List<string>();var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var value in input.EnumerateArray())
        {
            if(value.ValueKind!=JsonValueKind.String)throw Invalid(path+".boundary_handles","Expected hex handles.");
            string handle=Validation.NormalizeHandle(value.GetString()!);
            if(handles.Contains(handle)||!seen.Add(handle))throw Invalid(path+".boundary_handles","Source and boundary handles must be distinct.");
            boundaries.Add(handle);
        }
        if(operation=="trim_curve")return new TrimCurveSpec(handles,boundaries.ToArray(),Point(item,"pick_point",path));
        string end=Text(item,"end",path,16);
        if(end is not "start" and not "end")throw Invalid(path+".end","Use start or end in the source's stored direction.");
        return new ExtendCurveSpec(handles,boundaries.ToArray(),end);
    }
}

// Detached analytic geometry used before write-open and for native readback.
// Angles are counterclockwise from +X, in radians; all points are WCS XY.
public sealed record ContourCurve(string Kind,Point2? Start=null,Point2? End=null,Point2? Center=null,
    double Radius=0,double StartAngle=0,double Sweep=0,Point2[]? Vertices=null,bool Closed=false);

public static class ContourGeometry
{
    private const double Epsilon=1e-7;
    private const double Tau=2*Math.PI;
    private static void Need(bool valid,string code,string message){if(!valid)throw new DomainException(code,message);}
    private static Point2 Add(Point2 a,Point2 b)=>new(a.X+b.X,a.Y+b.Y);
    private static Point2 Sub(Point2 a,Point2 b)=>new(a.X-b.X,a.Y-b.Y);
    private static Point2 Mul(Point2 a,double t)=>new(a.X*t,a.Y*t);
    private static double Dot(Point2 a,Point2 b)=>a.X*b.X+a.Y*b.Y;
    private static double Cross(Point2 a,Point2 b)=>a.X*b.Y-a.Y*b.X;
    private static double Length(Point2 a)=>Math.Sqrt(Dot(a,a));
    public static double Angle(double a){a%=Tau;return a<0?a+Tau:a;}
    public static double Span(ContourCurve curve)=>curve.Kind=="Line"?1:curve.Kind=="Circle"?Tau:curve.Sweep;
    private static double ParameterTolerance(ContourCurve curve)=>Epsilon/(curve.Kind=="Line"?Length(Sub(curve.End!,curve.Start!)):curve.Radius);
    public static Point2 At(ContourCurve curve,double parameter)=>curve.Kind=="Line"?Add(curve.Start!,Mul(Sub(curve.End!,curve.Start!),parameter)):
        Add(curve.Center!,new(curve.Radius*Math.Cos(curve.StartAngle+parameter),curve.Radius*Math.Sin(curve.StartAngle+parameter)));
    public static void Require(ContourCurve curve,Action? cancel=null)
    {
        cancel?.Invoke();
        Need(curve.Kind is "Line" or "Circle" or "Arc" or "Polyline","UNSUPPORTED_ENTITY_TYPE","Use a native planar Line, Circle, Arc or supported straight Polyline.");
        var points=curve.Kind=="Line"?new[]{curve.Start!,curve.End!}:curve.Kind=="Polyline"?curve.Vertices!:new[]{curve.Center!};
        if(points is null)throw new DomainException("INVALID_GEOMETRY","Contour coordinates must be finite and within +/-1000000 mm.");
        var budget=new ContourGeometryBudget344(cancel);
        foreach(var p in points)
        {
            cancel?.Invoke();Need(p!=null&&double.IsFinite(p.X)&&double.IsFinite(p.Y)&&Math.Abs(p.X)<=1e6&&Math.Abs(p.Y)<=1e6,"INVALID_GEOMETRY","Contour coordinates must be finite and within +/-1000000 mm.");budget.Add(p);
        }
        if(curve.Kind=="Line")Need(Length(Sub(curve.End!,curve.Start!))>=.001,"INVALID_GEOMETRY","Contour line must be at least0.001 mm long.");
        else if(curve.Kind=="Polyline")
        {
            Need(points.Length>=2,"INVALID_GEOMETRY","Contour polylines require at least two vertices.");
            for(int i=0;i<points.Length-(curve.Closed?0:1);i++){cancel?.Invoke();Need(Length(Sub(points[(i+1)%points.Length],points[i]))>=.001,"INVALID_GEOMETRY","Polyline edges must not collapse.");}
        }
        else
        {
            Need(double.IsFinite(curve.Radius)&&curve.Radius>=.001&&curve.Radius<=1e6&&double.IsFinite(curve.StartAngle),"INVALID_GEOMETRY","Contour radius must be0.001–1000000 mm.");
            if(curve.Kind=="Arc")Need(double.IsFinite(curve.Sweep)&&curve.Sweep>1e-8&&curve.Sweep<Tau-1e-8&&curve.Radius*curve.Sweep>=.001,"INVALID_GEOMETRY","Use a nonzero arc shorter than one circle.");
        }
        ContourGeometryBudget344.Check(curve,cancel);
    }
    public static double Parameter(ContourCurve curve,Point2 point,bool finite=true,Action? cancel=null)
    {
        Require(curve,cancel);Need(curve.Kind!="Polyline","UNSUPPORTED_ENTITY_TYPE","Polyline is a boundary or closed offset source, not a trim/extend target.");
        double parameter,distance;
        if(curve.Kind=="Line")
        {
            var vector=Sub(curve.End!,curve.Start!);parameter=Dot(Sub(point,curve.Start!),vector)/Dot(vector,vector);
            distance=Length(Sub(point,At(curve,parameter)));
        }
        else
        {
            var radial=Sub(point,curve.Center!);distance=Math.Abs(Length(radial)-curve.Radius);
            parameter=Angle(Math.Atan2(radial.Y,radial.X)-curve.StartAngle);
            if(Tau-parameter<ParameterTolerance(curve))parameter=0;
        }
        Need(distance<=1e-5,"PICK_POINT_OFF_CURVE","Point must lie on the source curve within0.00001 mm.");
        Need(!finite||parameter>=-ParameterTolerance(curve)&&parameter<=Span(curve)+ParameterTolerance(curve),"PICK_POINT_OFF_CURVE","Point lies outside the finite source curve.");
        return parameter;
    }
    private static bool On(ContourCurve curve,Point2 point,Action? cancel)
    {
        try{Parameter(curve,point,cancel:cancel);return true;}catch(DomainException e)when(e.Error.code=="PICK_POINT_OFF_CURVE"){return false;}
    }
    private static IEnumerable<ContourCurve> Boundaries(ContourCurve curve,Action? cancel)
    {
        Require(curve,cancel);
        if(curve.Kind!="Polyline"){yield return curve;yield break;}
        var vertices=curve.Vertices!;
        for(int i=0;i<vertices.Length-(curve.Closed?0:1);i++){cancel?.Invoke();yield return new("Line",vertices[i],vertices[(i+1)%vertices.Length]);}
    }
    private static IEnumerable<Point2> Intersections(ContourCurve a,ContourCurve b)
    {
        if(a.Kind=="Line"&&b.Kind=="Line")
        {
            var u=Sub(a.End!,a.Start!);var v=Sub(b.End!,b.Start!);double cross=Cross(u,v);
            if(Math.Abs(cross)<=1e-12*Length(u)*Length(v))
            {
                Need(Math.Abs(Cross(Sub(b.Start!,a.Start!),u))/Length(u)>Epsilon,"AMBIGUOUS_INTERSECTION","Collinear source/boundary lines do not define a unique cut or extension.");yield break;
            }
            yield return At(a,Cross(Sub(b.Start!,a.Start!),v)/cross);yield break;
        }
        if(a.Kind=="Line"||b.Kind=="Line")
        {
            var line=a.Kind=="Line"?a:b;var circle=a.Kind=="Line"?b:a;
            var vector=Sub(line.End!,line.Start!);double length=Length(vector);var unit=Mul(vector,1/length);
            var delta=Sub(circle.Center!,line.Start!);double projection=Dot(delta,unit),perpendicular=Cross(delta,unit);
            double square=circle.Radius*circle.Radius-perpendicular*perpendicular;
            if(square< -Epsilon*Math.Max(1,circle.Radius))yield break;
            double half=Math.Sqrt(Math.Max(0,square));yield return At(line,(projection-half)/length);
            if(half>Epsilon)yield return At(line,(projection+half)/length);yield break;
        }
        var between=Sub(b.Center!,a.Center!);double d=Length(between);
        if(d<=Epsilon){Need(Math.Abs(a.Radius-b.Radius)>Epsilon,"AMBIGUOUS_INTERSECTION","Coincident circular supports do not define a unique cut.");yield break;}
        if(d>a.Radius+b.Radius+Epsilon||d<Math.Abs(a.Radius-b.Radius)-Epsilon)yield break;
        double along=((a.Radius-b.Radius)*(a.Radius+b.Radius)+d*d)/(2*d);
        double heightSquared=a.Radius*a.Radius-along*along;
        if(heightSquared< -Epsilon*Math.Max(1,a.Radius))yield break;
        var unitBetween=Mul(between,1/d);var basePoint=Add(a.Center!,Mul(unitBetween,along));
        var offset=Mul(new(-unitBetween.Y,unitBetween.X),Math.Sqrt(Math.Max(0,heightSquared)));
        yield return Add(basePoint,offset);if(Length(offset)>Epsilon)yield return Sub(basePoint,offset);
    }
    private static double[] Cuts(ContourCurve source,ContourCurve[] boundaries,bool extend,Action? cancel)
    {
        Require(source,cancel);Need(source.Kind!="Polyline","UNSUPPORTED_ENTITY_TYPE","Trim/extend targets are Line, Arc or Circle.");
        Need(boundaries.Length>=1,"INVALID_ARGUMENT","Use at least one boundary curve.");
        var inputBudget=new ContourGeometryBudget344(cancel);foreach(var boundary in boundaries){cancel?.Invoke();Require(boundary,cancel);inputBudget.Add(boundary);}
        var values=new List<double>();var valueBudget=new ContourGeometryBudget344(cancel);
        foreach(var original in boundaries)foreach(var boundary in Boundaries(original,cancel))
        {
            cancel?.Invoke();
            foreach(var point in Intersections(source,boundary))
            {
                cancel?.Invoke();if(!On(boundary,point,cancel)||!extend&&!On(source,point,cancel))continue;
                double parameter=Parameter(source,point,!extend,cancel);bool duplicate=false;
                foreach(double v in values){cancel?.Invoke();if(Math.Abs(v-parameter)<=ParameterTolerance(source)){duplicate=true;break;}}
                if(!duplicate){valueBudget.Add(parameter);values.Add(parameter);}
            }
        }
        System.Exception? callbackFailure=null;
        try
        {
            values.Sort((a,b)=>
            {
                try{cancel?.Invoke();}catch(System.Exception e){callbackFailure=e;throw;}
                return a.CompareTo(b);
            });
        }
        catch(InvalidOperationException)when(callbackFailure!=null)
        {
            // Array.Sort wraps comparer exceptions. Preserve cancellation's
            // actual type/outcome rather than publishing an unknown comparer error.
            if(callbackFailure is {} failure)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            throw;
        }
        cancel?.Invoke();return values.ToArray();
    }
    private static double LastBefore(double[] values,double point,double fallback,Action? cancel)
    {double result=fallback;foreach(double value in values){cancel?.Invoke();if(value<point)result=value;}return result;}
    private static double FirstAfter(double[] values,double point,double fallback,Action? cancel)
    {foreach(double value in values){cancel?.Invoke();if(value>point)return value;}return fallback;}
    private static ContourCurve Segment(ContourCurve source,double start,double end,Action? cancel)
    {
        var segment=source.Kind=="Line"?source with{Start=At(source,start),End=At(source,end)}:
            source with{Kind="Arc",StartAngle=Angle(source.StartAngle+start),Sweep=end-start};
        Require(segment,cancel);return segment;
    }
    public static ContourCurve[] Trim(ContourCurve source,ContourCurve[] boundaries,Point2 pick,Action? cancel=null)
    {
        double picked=Parameter(source,pick,cancel:cancel),tolerance=ParameterTolerance(source),span=Span(source);
        var cuts=Cuts(source,boundaries,false,cancel);
        Need(cuts.All(c=>{cancel?.Invoke();return Math.Abs(c-picked)>tolerance;}),"AMBIGUOUS_PICK","Choose the interval to remove, away from intersections.");
        if(source.Kind=="Circle")
        {
            Need(cuts.Length>=2,"NO_INTERSECTION","A circle requires at least two distinct boundary intersections.");
            double left=LastBefore(cuts,picked,cuts[^1]-Tau,cancel),right=FirstAfter(cuts,picked,cuts[0]+Tau,cancel);
            return new[]{Segment(source,right,left+Tau,cancel)};
        }
        var interior=cuts.Where(c=>{cancel?.Invoke();return c>tolerance&&c<span-tolerance;}).ToArray();
        Need(interior.Length>0,"NO_INTERSECTION","No boundary intersects the interior of the source curve.");
        double a=LastBefore(interior,picked,0,cancel),b=FirstAfter(interior,picked,span,cancel);
        var kept=new List<ContourCurve>();if(a>tolerance)kept.Add(Segment(source,0,a,cancel));if(b<span-tolerance)kept.Add(Segment(source,b,span,cancel));
        return kept.ToArray();
    }
    public static ContourCurve Extend(ContourCurve source,ContourCurve[] boundaries,string end,Action? cancel=null)
    {
        Need(source.Kind is "Line" or "Arc","UNSUPPORTED_ENTITY_TYPE","Only finite Line/Arc have extendable endpoints.");
        Need(end is "start" or "end","INVALID_ARGUMENT","Use start or end.");
        double tolerance=ParameterTolerance(source),span=Span(source);var cuts=Cuts(source,boundaries,true,cancel);
        if(end=="start")
        {
            var candidates=cuts.Select(c=>{cancel?.Invoke();return source.Kind=="Arc"?c-Tau:c;}).Where(c=>c< -tolerance&&(source.Kind=="Line"||span-c<Tau-1e-8)).ToArray();
            Need(candidates.Length>0,"NO_EXTENSION","No finite boundary intersects beyond this endpoint.");return Segment(source,LastBefore(candidates,double.PositiveInfinity,candidates[0],cancel),span,cancel);
        }
        var ends=cuts.Where(c=>{cancel?.Invoke();return c>span+tolerance&&(source.Kind=="Line"||c<Tau-1e-8);}).ToArray();
        Need(ends.Length>0,"NO_EXTENSION","No finite boundary intersects beyond this endpoint.");return Segment(source,0,FirstAfter(ends,double.NegativeInfinity,ends[0],cancel),cancel);
    }
    public static ContourCurve Offset(ContourCurve source,double distance,string side,Action? cancel=null)
    {
        Require(source,cancel);Need(double.IsFinite(distance)&&distance>=.001&&distance<=1e6,"INVALID_GEOMETRY","Offset distance must be0.001–1000000 mm.");
        if(source.Kind=="Line")
        {
            Need(side is "left" or "right","INVALID_ARGUMENT","Line offsets use left/right relative to start→end.");
            var direction=Sub(source.End!,source.Start!);var displacement=Mul(new(-direction.Y,direction.X),distance*(side=="left"?1:-1)/Length(direction));
            var line=source with{Start=Add(source.Start!,displacement),End=Add(source.End!,displacement)};Require(line,cancel);return line;
        }
        Need(side is "inward" or "outward","INVALID_ARGUMENT","Circular and closed polygon offsets use inward/outward.");
        if(source.Kind!="Polyline")
        {var circular=source with{Radius=source.Radius+(side=="outward"?distance:-distance)};Require(circular,cancel);return circular;}
        Need(source.Closed&&source.Vertices!.Length>=3,"OUT_OF_SCOPE","Polyline offset requires a closed strictly convex straight contour with zero widths.");
        var p=source.Vertices!;double area=0;var anchor=p[0];for(int i=0;i<p.Length;i++){cancel?.Invoke();area+=Cross(Sub(p[i],anchor),Sub(p[(i+1)%p.Length],anchor));}
        double orientation=Math.Sign(area);Need(orientation!=0,"INVALID_GEOMETRY","Polygon area is zero.");
        var directions=new Point2[p.Length];var shifted=new Point2[p.Length];
        for(int i=0;i<p.Length;i++)
        {
            cancel?.Invoke();
            var edge=Sub(p[(i+1)%p.Length],p[i]);var unit=Mul(edge,1/Length(edge));directions[i]=unit;
            for(int j=0;j<p.Length;j++){cancel?.Invoke();if(j!=i&&j!=(i+1)%p.Length)
                Need(orientation*Cross(unit,Sub(p[j],p[i]))>Epsilon,"OUT_OF_SCOPE","Offset currently requires strict convexity; concave or collinear contours are not silently simplified.");}
            double amount=distance*orientation*(side=="inward"?1:-1);
            shifted[i]=Add(p[i],Mul(new(-unit.Y,unit.X),amount));
        }
        var vertices=new Point2[p.Length];
        for(int i=0;i<p.Length;i++)
        {
            cancel?.Invoke();
            int previous=(i+p.Length-1)%p.Length;double cross=Cross(directions[previous],directions[i]);
            Need(Math.Abs(cross)>1e-10,"INVALID_GEOMETRY","Offset miter is unstable near a straight or collapsed corner.");
            vertices[i]=Add(shifted[previous],Mul(directions[previous],Cross(Sub(shifted[i],shifted[previous]),directions[i])/cross));
        }
        for(int i=0;i<p.Length;i++)foreach(var vertex in vertices)
        {cancel?.Invoke();Need(orientation*Cross(directions[i],Sub(vertex,shifted[i]))>=-Epsilon,"INVALID_GEOMETRY","Requested inward offset collapses the convex contour.");}
        var polygon=source with{Vertices=vertices};Require(polygon,cancel);return polygon;
    }
}
