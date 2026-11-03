using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record DrawingProfileEdit(string Action,string[] Targets,SketchConstraintSpec[]? Constraints=null,
    string? ConstraintId=null,string? ExpectedExpression=null,double? ExpectedValue=null,double? Value=null,
    double? Distance=null,string? Direction=null,string? Name=null,string? VariableId=null,string? ExpectedName=null,string? Expression=null,string? AxisHandle=null,string[]? ConstraintIds=null,double? AngleDegrees=null,SketchPlaneSpec? Workplane=null):EditSpec(Action,Targets);
public sealed record DrawingVariableChange(string variable_id,string name,string expression,double value,string value_unit);
public sealed record DrawingRelationChange(string constraint_id,string kind,string first_handle,string? second_handle,string? variable_id,double? value,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? third_handle=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] int? first_segment_index=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] int? second_segment_index=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] int? third_segment_index=null);
public sealed record DrawingProfileChange(string operation,EntityRef[] source_refs,DrawingRelationChange[] constraints,
    EntityRef? body_ref,string? sketch_id,string[] projection_ids,string? feature_id,EntityRef[] rebuilt_bodies,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] DrawingVariableChange[]? variable_changes=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string[]? removed_constraint_ids=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string[]? removed_variable_ids=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] DrawingRetainedSweepPart366[]? retained_sweep_parts=null);

public static partial class ModifyValidation
{
    private static DrawingProfileEdit ParseDrawingProfile(JsonElement item,string path,string[] handles,string action)
    {
        SketchPlaneSpec? frame=null;
        if(item.TryGetProperty("workplane",out var plane)){frame=DrawingWorkplane.Parse(plane);item=DrawingWorkplane.WithoutFrame(item);}
        return ParseDrawingProfileCore(item,path,handles,action) with{Workplane=frame};
    }
    private static DrawingProfileEdit ParseDrawingProfileCore(JsonElement item,string path,string[] handles,string action)
    {
        double Size(string field)=>Number(Required(item,field,path),path+"."+field,.001,1e6);
        if(action is "remove_drawing_constraints" or "remove_drawing_variable")
        {
            string Id(JsonElement v){if(v.ValueKind!=JsonValueKind.String||!Guid.TryParseExact(v.GetString(),"B",out var id)||id==Guid.Empty)throw Invalid(path,"Use a current braced native ID from inspect_constraints.");return id.ToString("B").ToUpperInvariant();}
            if(action=="remove_drawing_variable")
            {Shape(item,path,"operation","handles","variable_id","expected_name","expected_expression","expected_value");return new(action,handles,VariableId:Id(Required(item,"variable_id",path)),ExpectedName:Text(item,"expected_name",path,128),ExpectedExpression:Text(item,"expected_expression",path,8192),ExpectedValue:Number(Required(item,"expected_value",path),path+".expected_value",-1e12,1e12));}
            Shape(item,path,"operation","handles","constraint_ids");var ids=Required(item,"constraint_ids",path);
            if(ids.ValueKind!=JsonValueKind.Array||ids.GetArrayLength()<1)throw Invalid(path,"Use at least one selected constraint ID.");var parsed=ids.EnumerateArray().Select(Id).ToArray();
            if(parsed.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=parsed.Length)throw Invalid(path,"Duplicate constraint ID.");return new(action,handles,ConstraintIds:parsed);
        }
        if(DrawingVariablePlanning.IsAction(action))return ParseDrawingVariable(item,path,handles,action);
        if(action=="revolve_drawing_profile")
        {
            Shape(item,path,"operation","handles","axis_handle","name","angle_degrees");var axis=Validation.NormalizeHandle(Text(item,"axis_handle",path,16));
            if(handles.Contains(axis,StringComparer.OrdinalIgnoreCase))throw Invalid(path,"The axis Line handle must be separate from the selected profile sources.");
            return new(action,handles,Name:Text(item,"name",path,128),AxisHandle:axis,AngleDegrees:item.TryGetProperty("angle_degrees",out var angle)?Number(angle,path+".angle_degrees",.1,360):null);
        }
        if(action=="extrude_drawing_profile")
        {
            Shape(item,path,"operation","handles","distance","direction","name");var direction=Text(item,"direction",path,8);
            if(direction is not "positive" and not "negative")throw Invalid(path,"Direction is positive or negative relative to the selected workplane normal.");
            return new(action,handles,Distance:Size("distance"),Direction:direction,Name:Text(item,"name",path,128));
        }
        if(action=="set_drawing_dimension")
        {
            Shape(item,path,"operation","handles","constraint_id","expected_expression","expected_value","value");
            if(!Guid.TryParseExact(Text(item,"constraint_id",path,38),"B",out var id)||id==Guid.Empty)throw Invalid(path,"Use a current opaque constraint ID.");
            return new(action,handles,ConstraintId:id.ToString("B").ToUpperInvariant(),ExpectedExpression:Text(item,"expected_expression",path,8192),ExpectedValue:Size("expected_value"),Value:Size("value"));
        }
        Shape(item,path,"operation","handles","constraints");var array=Required(item,"constraints",path);
        if(array.ValueKind!=JsonValueKind.Array||array.GetArrayLength()<1)throw Invalid(path,"Use at least one drawing constraint.");
        var selected=handles.ToHashSet(StringComparer.OrdinalIgnoreCase);var specs=new List<SketchConstraintSpec>();var unique=new HashSet<string>();
        foreach(var entry in array.EnumerateArray())
        {
            string p=path+".constraints["+specs.Count+"]";Shape(entry,p,"kind","first","second","third","first_point","second_point","value","first_segment_index","second_segment_index","third_segment_index");
            string Ref(string field){var h=Validation.NormalizeHandle(Text(entry,field,p,16));if(!selected.Contains(h))throw Invalid(p,"Every constraint source must be listed in handles.");return h;}
            string Point(string field){var v=Text(entry,field,p,8);if(v is not "start" and not "end" and not "center")throw Invalid(p,"Use start/end of Line/Arc/open Spline or center of Circle/Arc.");return v;}
            string kind=Text(entry,"kind",p,24),first=Ref("first");string? second=null,third=null,fp=null,sp=null;double? value=null;
            int? Segment(string field)=>entry.TryGetProperty(field,out var index)?index.ValueKind==JsonValueKind.Number&&index.TryGetInt32(out var n)&&n>=0?n:throw Invalid(p+"."+field,"Use the actual zero-based Polyline segment index."):null;
            int? firstSegment=Segment("first_segment_index"),secondSegment=Segment("second_segment_index"),thirdSegment=Segment("third_segment_index");
            bool axis=kind is "distance_x" or "distance_y";
            bool pair=axis||kind is "parallel" or "perpendicular" or "equal" or "concentric" or "coincident" or "collinear" or "tangent" or "smooth" or "distance" or "angle" or "symmetric";
            bool dimensional=axis||kind is "radius" or "diameter" or "length" or "distance" or "angle";
            if(!pair&&!dimensional&&kind is not "horizontal" and not "vertical" and not "fix_point" and not "fix")throw Invalid(p,"Unsupported drawing constraint kind.");
            if(pair){second=Ref("second");if(first==second&&firstSegment==secondSegment&&!axis)throw Invalid(p,"Pair relations need distinct source curves or Polyline segments.");}
            else if(entry.TryGetProperty("second",out _))throw Invalid(p,"Unexpected second source.");
            if(kind=="symmetric"){third=Ref("third");if(third==first&&thirdSegment==firstSegment||third==second&&thirdSegment==secondSegment)throw Invalid(p,"Symmetry needs a separate Line curve or segment axis.");}
            else if(entry.TryGetProperty("third",out _))throw Invalid(p,"Unexpected third source.");
            if(secondSegment.HasValue&&second==null||thirdSegment.HasValue&&third==null)throw Invalid(p,"A segment selector requires its corresponding source.");
            if(kind=="fix"&&firstSegment.HasValue)throw Invalid(p,"fix anchors the complete source; use fix_point for a selected Polyline segment endpoint.");
            if(axis||kind is "coincident" or "smooth" or "distance" or "fix_point")fp=Point("first_point");
            else if(entry.TryGetProperty("first_point",out _))throw Invalid(p,"Unexpected first point.");
            if(axis||kind is "coincident" or "smooth" or "distance")sp=Point("second_point");
            else if(entry.TryGetProperty("second_point",out _))throw Invalid(p,"Unexpected second point.");
            if(kind=="smooth"&&(fp is not ("start" or "end")||sp is not ("start" or "end")))throw Invalid(p,"Smooth currently selects actual open-curve endpoints, using start/end on both sources.");
            if(axis&&first==second&&firstSegment==secondSegment&&fp==sp)throw Invalid(p,"Coordinate dimensions need two distinct points.");
            if(dimensional)value=Number(Required(entry,"value",p),p+".value",kind=="angle"?DrawingAngularGeometry.MinimumDegrees:.001,kind=="angle"?DrawingAngularGeometry.MaximumDegrees:1e6);
            else if(entry.TryGetProperty("value",out _))throw Invalid(p,"Geometric relation has no numeric value.");
            string one=first+":"+firstSegment+":"+fp,two=second+":"+secondSegment+":"+sp;if(pair&&StringComparer.Ordinal.Compare(one,two)>0)(one,two)=(two,one);
            if(!unique.Add(kind+"|"+one+"|"+two+"|"+third+":"+thirdSegment))throw Invalid(p,"Duplicate relation.");
            specs.Add(new(kind,first,second,fp,sp,value,third,firstSegment,secondSegment,thirdSegment));
        }
        return new(action,handles,specs.ToArray());
    }
}

// Shared numeric checks are independent of the native solver and history API.
public static class DrawingProfileGeometry
{
    public static bool Near(double a,double b)=>double.IsFinite(a)&&double.IsFinite(b)&&Math.Abs(a-b)<=Math.Max(1e-7,Math.Max(Math.Abs(a),Math.Abs(b))*1e-9);
    public static bool Same(Point2 a,Point2 b)=>Near(a.X,b.X)&&Near(a.Y,b.Y);
    public static Point2 Point(SketchConstraintCurve c,string? point)=>point switch
    {"start" when c.Kind is "line" or "arc"||c.Kind=="spline"&&c.Spline is {closed:false,periodic:false}||c.Kind=="ellipse"&&OpenEllipse(c)=>c.Start!,
     "end" when c.Kind is "line" or "arc"||c.Kind=="spline"&&c.Spline is {closed:false,periodic:false}||c.Kind=="ellipse"&&OpenEllipse(c)=>c.End!,
     "center" when c.Kind is "circle" or "arc" or "ellipse"=>c.Center!,_=>throw new DomainException("DRAWING_RELATION_MISMATCH","Point does not belong to this source type or is an ambiguous closed-curve seam.")};
    private static bool OpenEllipse(SketchConstraintCurve c)=>c.Ellipse is {} ellipse&&ellipse.EndAngle>ellipse.StartAngle&&
        ellipse.EndAngle-ellipse.StartAngle<2*Math.PI-1e-7;
    public static double Distance(Point2 a,Point2 b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Y-b.Y,2));
    public static bool Same(SketchConstraintCurve a,SketchConstraintCurve b)
    {
        if(a.Kind!=b.Kind)return false;
        if(a.Kind=="polyline")return a.Polyline!=null&&b.Polyline!=null&&PolylineTopologyGeometry.Equivalent(a.Polyline,b.Polyline);
        if(a.Kind=="spline")return a.Spline!=null&&b.Spline!=null&&SplineConstraintGeometry445.SameDefinition(a.Spline,b.Spline)&&
            Same(a.Start!,b.Start!)&&Same(a.End!,b.End!)&&SameOptional(a.StartTangent,b.StartTangent)&&SameOptional(a.EndTangent,b.EndTangent);
        if(a.Kind=="ellipse")return a.Ellipse!=null&&b.Ellipse!=null&&SameEllipse(a.Ellipse,b.Ellipse)&&
            Same(a.Start!,b.Start!)&&Same(a.End!,b.End!)&&SameOptional(a.StartTangent,b.StartTangent)&&SameOptional(a.EndTangent,b.EndTangent);
        return a.Kind=="circle"?Same(a.Center!,b.Center!)&&Near(a.Radius,b.Radius):Same(a.Start!,b.Start!)&&Same(a.End!,b.End!)&&
            (a.Kind!="arc"||Same(a.Center!,b.Center!)&&Near(a.Radius,b.Radius)&&Near(a.Sweep,b.Sweep));
    }
    private static bool SameEllipse(NativeSketchEllipseGeometry445 a,NativeSketchEllipseGeometry445 b)
    {
        bool Vector(Point3 p,Point3 q)=>Near(p.X,q.X)&&Near(p.Y,q.Y)&&Near(p.Z,q.Z);
        return Vector(a.Center,b.Center)&&Vector(a.MajorAxis,b.MajorAxis)&&Vector(a.MinorAxis,b.MinorAxis)&&
            Near(a.MajorRadius,b.MajorRadius)&&Near(a.MinorRadius,b.MinorRadius)&&Near(a.StartAngle,b.StartAngle)&&Near(a.EndAngle,b.EndAngle);
    }
    private static bool SameOptional(Point2? a,Point2? b)=>a==null?b==null:b!=null&&Same(a,b);
    public static void Validate(SketchConstraintSpec[] specs,SketchConstraintCurve[] curves)
    {
        if(curves.Any(c=>c.Kind=="polyline")||specs.Any(s=>s.FirstSegmentIndex.HasValue||s.SecondSegmentIndex.HasValue||s.ThirdSegmentIndex.HasValue)){DrawingConstraintOperands431.Validate(specs,curves);return;}
        foreach(var arc in curves.Where(c=>c.Kind=="arc"))DrawingArcContours.ValidateArc(arc);
        var byId=curves.ToDictionary(c=>c.Id,StringComparer.OrdinalIgnoreCase);
        void Need(bool ok,string message){if(!ok)throw new DomainException("DRAWING_RELATION_MISMATCH",message);}
        foreach(var s in specs)
        {
            Need(byId.TryGetValue(s.First,out var first),"First source unavailable.");var a=first!;
            SketchConstraintCurve? b=null;if(s.Second!=null)Need(byId.TryGetValue(s.Second,out b),"Second source unavailable.");
            if(s.Kind=="fix"){Need(a.Kind is "line" or "circle" or "arc" or "spline" or "ellipse","Whole-object fix needs an exact native Line/Circle/Arc/Spline/Ellipse.");if(a.Kind=="spline"){Need(a.Spline!=null,"Complete native Spline state is unavailable.");SplineConstraintGeometry445.Complete(a.Spline!);}if(a.Kind=="ellipse")Need(a.Ellipse!=null,"Complete native Ellipse state is unavailable.");continue;}
            if(s.Kind=="smooth"){SplineConstraintGeometry445.Smooth(a,s.FirstPoint,b!,s.SecondPoint);continue;}
            if(s.Kind=="symmetric")
            {
                Need(s.Third!=null&&byId.TryGetValue(s.Third,out _),"Symmetry axis is unavailable.");
                SketchSymmetryGeometry414.Validate(a,b!,byId[s.Third!]);continue;
            }
            if(s.Kind=="fix_point"){Point(a,s.FirstPoint);continue;}
            if(s.Kind=="coincident"){Need(Same(Point(a,s.FirstPoint),Point(b!,s.SecondPoint)),"Selected points do not coincide.");continue;}
            bool Round(SketchConstraintCurve c)=>c.Kind is "circle" or "arc";
            if(s.Kind is "radius" or "diameter")
            {Need(Round(a)&&Near(a.Radius*(s.Kind=="diameter"?2:1),s.Value!.Value),"Radial dimension requires a matching Circle/Arc radius.");continue;}
            if(s.Kind=="equal"&&Round(a)&&Round(b!)){Need(Near(a.Radius,b!.Radius),"Circular radii differ.");continue;}
            if(s.Kind=="concentric"){Need(Round(a)&&Round(b!)&&Same(a.Center!,b!.Center!),"Circular centers differ.");continue;}
            if(s.Kind=="angle"){DrawingAngularGeometry.Validate(s,a,b!);continue;}
            if(s.Kind is "distance_x" or "distance_y")
            {
                var p=Point(a,s.FirstPoint);var q=Point(b!,s.SecondPoint);double projected=s.Kind=="distance_x"?Math.Abs(q.X-p.X):Math.Abs(q.Y-p.Y);
                Need(projected>=.001&&Near(projected,s.Value!.Value),"Selected points' absolute WCS axis separation differs from the requested dimension.");continue;
            }
            if(s.Kind=="distance"){Need(Near(Distance(Point(a,s.FirstPoint),Point(b!,s.SecondPoint)),s.Value!.Value),"Selected point distance differs from requested dimension.");continue;}
            if(s.Kind=="collinear")
            {
                SketchConstraintGeometry.Validate(new[]{s with{Kind="parallel"}},curves);
                double length=Distance(a.Start!,a.End!);Need(Math.Abs((a.End!.X-a.Start!.X)*(b!.Start!.Y-a.Start.Y)-(a.End.Y-a.Start.Y)*(b.Start.X-a.Start.X))/length<=1e-7,"Lines are not collinear.");continue;
            }
            if(s.Kind=="tangent")
            {
                if(Round(a)&&Round(b!))
                {
                    var d=Distance(a.Center!,b!.Center!);Need(d>1e-7&&(Near(d,a.Radius+b.Radius)||Near(d,Math.Abs(a.Radius-b.Radius))),"Circular curves are not tangent.");
                    bool On(SketchConstraintCurve c,Point2 p)=>c.Kind=="circle"?Near(Distance(c.Center!,p),c.Radius):DrawingArcContours.OnArc(c,p);
                    Need(new[]{-1d,1d}.Any(sign=>{var p=new Point2(a.Center!.X+sign*a.Radius*(b.Center!.X-a.Center.X)/d,a.Center.Y+sign*a.Radius*(b.Center.Y-a.Center.Y)/d);return On(a,p)&&On(b,p);}),"Tangency lies outside an arc's sweep.");
                }
                else
                {
                    var line=a.Kind=="line"?a:b!;var circle=Round(a)?a:b!;
                    Need(line.Kind=="line"&&Round(circle),"Tangency requires Line/Circle/Arc circular support.");
                    var p=line.Start!;var q=line.End!;var c=circle.Center!;double length=Distance(p,q);
                    Need(length>=.001&&Near(Math.Abs((q.X-p.X)*(c.Y-p.Y)-(q.Y-p.Y)*(c.X-p.X))/length,circle.Radius),"Line and circle are not tangent.");
                    double t=((c.X-p.X)*(q.X-p.X)+(c.Y-p.Y)*(q.Y-p.Y))/(length*length);
                    if(circle.Kind=="arc")Need(DrawingArcContours.OnArc(circle,new(p.X+t*(q.X-p.X),p.Y+t*(q.Y-p.Y))),"Tangency lies outside arc sweep.");
                }
                continue;
            }
            SketchConstraintGeometry.Validate(new[]{s},curves);
        }
    }
    public static ModelProfileSpec Profile(SketchConstraintCurve[] curves,Action? cancel=null)
    {
        var budget=new ProfileGeometryBudget(cancel);foreach(var curve in curves)budget.Add(curve);
        if(curves.Length==1&&curves[0].Kind=="circle")return new("circle",curves[0].Center,curves[0].Radius,null);
        if(curves.Length<3||curves.Any(c=>c.Kind!="line"))throw new DomainException("OUT_OF_SCOPE","Use one Circle or at least three Lines forming one simple closed contour without holes.");
        // Ordering and reversed line endpoints are resolved by connectivity.
        // A branch, gap or a second loop must not be silently ignored.
        var remaining=curves.Skip(1).ToList();var vertices=new List<Point2>{curves[0].Start!};var current=curves[0].End!;
        while(remaining.Count>0)
        {
            cancel?.Invoke();vertices.Add(current);var next=remaining.Where(c=>{cancel?.Invoke();return Same(c.Start!,current)||Same(c.End!,current);}).ToArray();
            if(next.Length!=1)throw new DomainException("INVALID_GEOMETRY","Contour branches, has a gap, or contains separate loops.");
            var edge=next[0];remaining.Remove(edge);current=Same(edge.Start!,current)?edge.End!:edge.Start!;
        }
        if(!Same(current,vertices[0]))throw new DomainException("INVALID_GEOMETRY","Contour is open.");
        // Reuse the production polygon parser to reject self-intersections and
        // degeneracy before entering any native write.
        var payload=Wire.Element(new{type="Solid3d",kind="model_extrusion",plane=new{origin=new[]{0,0,0},x_axis=new[]{1,0,0},y_axis=new[]{0,1,0}},
            profile=new{kind="polygon",vertices=vertices.Select(p=>new[]{p.X,p.Y}).ToArray()},distance=1,direction="positive",name="Profile"});
        return ((ModelExtrusionSpec)CreateValidation.ParseModelExtrusion(payload,"$.profile","0",cancel)).Profile;
    }
    public static ExtrusionOracle Measure(SketchConstraintCurve[] curves,double height,Action? cancel=null)=>curves.Any(c=>c.Kind=="arc")?DrawingArcContours.Measure(curves,height,cancel):ModelExtrusionGeometry.Measure(new(
        new(new(0,0,0),new(1,0,0),new(0,1,0)),Profile(curves,cancel),Math.Abs(height),height>0?"positive":"negative","Profile","0"),cancel);
}
