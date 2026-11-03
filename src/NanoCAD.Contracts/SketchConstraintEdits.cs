using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record SketchConstraintSpec(string Kind,string First,string? Second,string? FirstPoint,string? SecondPoint,double? Value,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? Third=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] int? FirstSegmentIndex=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] int? SecondSegmentIndex=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] int? ThirdSegmentIndex=null);
public sealed record SketchConstraintsEdit(string[] Targets,string SketchId,SketchConstraintSpec[] Constraints,string? ExpectedBindingToken=null):EditSpec("add_sketch_constraints",Targets);
public sealed record SketchConstraintChange(string sketch_id,string constraint_id,string kind,string first_element_id,string? second_element_id,string? variable_id,double? value,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? third_element_id=null);
public sealed record SketchConstraintCurve(string Id,string Kind,Point2? Start=null,Point2? End=null,Point2? Center=null,double Radius=0,double Sweep=0,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] PolylineShape? Polyline=null,
    // Complete native Spline definition remains in WCS. Endpoints and derivatives
    // above/below use the observed constraint workplane's local XY coordinates.
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] CurveSplineGeometry445? Spline=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] Point2? StartTangent=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] Point2? EndTangent=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] NativeSketchEllipseGeometry445? Ellipse=null);

public static partial class ModifyValidation
{
    private static SketchConstraintsEdit ParseSketchConstraints(JsonElement item,string path,string[] handles)
    {
        Shape(item,path,"operation","handles","sketch_id","constraints","expected_binding_token");
        if(handles.Length!=1)throw Invalid(path,"Choose one native model body.");
        string Identity(JsonElement value,string field,string location)
        {
            if(!Guid.TryParseExact(Text(value,field,location,38),"B",out var id)||id==Guid.Empty)throw Invalid(location+"."+field,"Use an opaque nonempty MCS ID from the current sketch geometry.");
            return id.ToString("B").ToUpperInvariant();
        }
        string sketch=Identity(item,"sketch_id",path);var array=Required(item,"constraints",path);
        if(array.ValueKind!=JsonValueKind.Array||array.GetArrayLength()<1)throw Invalid(path,"Use a nonempty array of sketch constraints.");
        if(!item.TryGetProperty("expected_binding_token",out var token)||token.ValueKind!=JsonValueKind.String||token.GetString() is not {Length:64} text||text.Any(c=>!Uri.IsHexDigit(c)))throw Invalid(path,"Read native_inputs.binding_token from inspect_model_history(sketch_ids).");
        var specs=SketchConstraintValidation385.Parse(array,path+".constraints");
        return new(handles,sketch,specs,text.ToUpperInvariant());
    }
}

// Attach constraints to already matching geometry. This preserves the engineer's
// current shape instead of letting an underconstrained solver guess a new one.
// It does not diagnose all redundant/incompatible systems; native solve/readback
// remains authoritative and no full-constraint status is inferred here.
public static class SketchConstraintGeometry
{
    public static void Validate(SketchConstraintSpec[] constraints,SketchConstraintCurve[] geometry)
    {
        void Need(bool valid,string message){if(!valid)throw new DomainException("SKETCH_RELATION_MISMATCH",message);}
        Need(geometry.Length>0,"Expected a nonempty current sketch element set.");
        SerializedCapacity357.Check(geometry);
        var byId=geometry.ToDictionary(c=>c.Id,StringComparer.OrdinalIgnoreCase);
        bool Near(double a,double b)=>double.IsFinite(a)&&double.IsFinite(b)&&Math.Abs(a-b)<=Math.Max(1e-7,1e-9*Math.Max(Math.Abs(a),Math.Abs(b)));
        double Length(SketchConstraintCurve c)
        {Need(c.Kind=="line"&&c.Start!=null&&c.End!=null,"A complete line is required.");double length=Math.Sqrt(Math.Pow(c.End!.X-c.Start!.X,2)+Math.Pow(c.End.Y-c.Start.Y,2));Need(double.IsFinite(length)&&length>=.001,"Degenerate line.");return length;}
        Point2 Point(SketchConstraintCurve c,string? which)=>which switch
        {"start" when c.Kind=="line"&&c.Start!=null=>c.Start,"end" when c.Kind=="line"&&c.End!=null=>c.End,"center" when c.Kind=="circle"&&c.Center!=null=>c.Center,_=>throw new DomainException("SKETCH_RELATION_MISMATCH","Point reference does not belong to this curve type.")};
        foreach(var constraint in constraints)
        {
            Need(byId.TryGetValue(constraint.First,out var first),"First element is outside the current sketch.");
            SketchConstraintCurve? second=null;
            if(constraint.Second!=null)Need(byId.TryGetValue(constraint.Second,out second),"Second element is outside the current sketch.");
            var a=first!;var b=second!;
            switch(constraint.Kind)
            {
                case "horizontal":Length(a);Need(Near(a.Start!.Y,a.End!.Y),"Line is not horizontal in sketch coordinates.");break;
                case "vertical":Length(a);Need(Near(a.Start!.X,a.End!.X),"Line is not vertical in sketch coordinates.");break;
                case "parallel":case "perpendicular":
                    double l1=Length(a),l2=Length(b);Need(l1>=.001&&l2>=.001,"Degenerate line.");
                    double ax=(a.End!.X-a.Start!.X)/l1,ay=(a.End.Y-a.Start.Y)/l1,bx=(b.End!.X-b.Start!.X)/l2,by=(b.End.Y-b.Start.Y)/l2;
                    Need(Near(constraint.Kind=="parallel"?ax*by-ay*bx:ax*bx+ay*by,0),"Lines do not already satisfy the requested angular relation.");break;
                case "equal":Need(a.Kind==b.Kind&&(a.Kind=="line"||a.Kind=="circle"),"Equality requires two lines or two circles.");
                    Need(Near(a.Kind=="line"?Length(a):a.Radius,b.Kind=="line"?Length(b):b.Radius),"Elements do not have equal lengths/radii.");break;
                case "concentric":Need(a.Kind=="circle"&&b.Kind=="circle"&&a.Center!=null&&b.Center!=null,"Concentricity requires complete circles.");
                    Need(Near(a.Center!.X,b.Center!.X)&&Near(a.Center.Y,b.Center.Y),"Circle centers differ.");break;
                case "coincident":var p=Point(a,constraint.FirstPoint);var q=Point(b,constraint.SecondPoint);
                    Need(Near(p.X,q.X)&&Near(p.Y,q.Y),"Selected sketch points do not coincide.");break;
                case "length":Need(Near(Length(a),constraint.Value!.Value),"Requested length differs from the existing line.");break;
                case "radius":case "diameter":Need(a.Kind=="circle","Radial dimensions require a complete circle.");
                    Need(Near(a.Radius*(constraint.Kind=="diameter"?2:1),constraint.Value!.Value),"Requested radial dimension differs from the existing circle.");break;
                default:throw new DomainException("INVALID_ARGUMENT","Unsupported sketch relation.");
            }
        }
    }
}
