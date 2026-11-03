using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record CurveEditGeometry(string Type, Point2? Start, Point2? End, Point2? Center, double Radius,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] double StartAngle=0,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] double SweepAngle=0,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] EllipseSpec? Ellipse=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] SplineSpec? Spline=null);
public sealed record CurveGeometrySpec(string[] Targets, CurveEditGeometry ExpectedGeometry, CurveEditGeometry Geometry, [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] SketchPlaneSpec? Workplane=null) : EditSpec("set_curve_geometry", Targets);

public static partial class ModifyValidation
{
    private static CurveGeometrySpec ParseCurveGeometry(JsonElement item, string path, string[] handles)
    {
        Shape(item, path, "operation", "handles", "expected_geometry", "geometry", "workplane");
        if (handles.Length != 1) throw Invalid(path + ".handles", "Geometry replacement requires one native Line, Circle, Arc, Ellipse or Spline.");
        CurveEditGeometry Geometry(string field)
        {
            var value = Required(item, field, path); string at = path + "." + field;
            if (value.ValueKind != JsonValueKind.Object) throw Invalid(at, "Expected geometry object.");
            string type = Text(value, "type", at, 16);
            if (type == "Line")
            {
                Shape(value, at, "type", "start", "end");
                var start = Point(value, "start", at); var end = Point(value, "end", at);
                if (Math.Sqrt(Math.Pow(start.X - end.X, 2) + Math.Pow(start.Y - end.Y, 2)) < 1e-9)
                    throw Invalid(at, "Line endpoints must differ.", "INVALID_GEOMETRY");
                return new(type, start, end, null, 0);
            }
            if (type == "Circle")
            {
                Shape(value, at, "type", "center", "radius");
                return new(type, null, null, Point(value, "center", at), Number(Required(value, "radius", at), at + ".radius", 1e-9, 1e9));
            }
            if (type == "Arc")
            {
                Shape(value,at,"type","center","radius","start_angle","sweep_angle");
                double start=Number(Required(value,"start_angle",at),at+".start_angle",-2*Math.PI,2*Math.PI);
                double sweep=Number(Required(value,"sweep_angle",at),at+".sweep_angle",1e-9,2*Math.PI-1e-9);
                return new(type,null,null,Point(value,"center",at),Number(Required(value,"radius",at),at+".radius",1e-9,1e9),ContourGeometry.Angle(start),sweep);
            }
            if (type is "Ellipse" or "Spline")
            {
                if (value.TryGetProperty("layer", out _)) throw Invalid(at + ".layer", "Geometry edits preserve display properties; layer is not a geometry field.");
                if (type == "Ellipse") return new(type, null, null, null, 0, Ellipse: CreateValidation.ParseEllipse(value, at, ""));
                // Closure is explicit during edits: absence must not accidentally
                // reopen an existing clamped closed spline.
                Required(value, "closed", at);
                return new(type, null, null, null, 0, Spline: CreateValidation.ParseSpline(value, at, ""));
            }
            throw Invalid(at + ".type", "Use Line, Circle, Arc, Ellipse or Spline; no entity conversion.");
        }
        var expected = Geometry("expected_geometry"); var geometry = Geometry("geometry");
        if (expected.Type != geometry.Type) throw Invalid(path, "Expected and replacement geometry must have the same native type.");
        var frame=item.TryGetProperty("workplane",out var workplane)?DrawingWorkplane.Parse(workplane):null;
        return new(handles, expected, geometry, frame);
    }
}
