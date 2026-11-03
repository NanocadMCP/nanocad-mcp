using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class NativeCurveInspectionSchema342
{
    private static JsonObject Number() => new() { ["type"] = "number" };
    private static JsonObject Integer() => new() { ["type"] = "integer" };
    private static JsonObject Bool() => new() { ["type"] = "boolean" };
    private static JsonObject Text() => new() { ["type"] = "string" };
    private static JsonObject Array(JsonObject item) => new() { ["type"] = "array", ["items"] = item };
    private static JsonObject Point() { var value = Array(Number()); value["minItems"] = 3; value["maxItems"] = 3; return value; }
    private static JsonObject Nullable(JsonObject schema) => new() { ["anyOf"] = new JsonArray(schema, new JsonObject { ["type"] = "null" }) };
    private static JsonObject Object(params (string Name, JsonObject Schema)[] members)
    {
        var props = new JsonObject(); var required = new JsonArray();
        foreach (var (name, schema) in members) { props[name] = schema; required.Add(name); }
        return new() { ["type"] = "object", ["properties"] = props, ["required"] = required, ["additionalProperties"] = false };
    }
    internal static JsonObject Plane() => Object(("point", Point()), ("normal", Point()));
    internal static JsonObject ReadProperties()
    {
        var token = Text(); token["pattern"] = "^[0-9A-F]{64}$";
        var parameter = Number(); parameter["description"] = "Current native curve parameter; not necessarily a length, radians or a normalized0–1 value.";
        return Object(("curve_token", token), ("runtime_type", Text()), ("start_parameter", parameter), ("end_parameter", (JsonObject)parameter.DeepClone()),
            ("closed", Bool()), ("planar", Bool()), ("start_point", Point()), ("end_point", Point()), ("length", Nullable(Number())),
            ("length_unavailable_reason", Nullable(Text())), ("plane", Nullable(Plane())), ("plane_unavailable_reason", Nullable(Text())), ("inspection_scope", Text()));
    }
    private static JsonObject Fit() => Object(("degree", Integer()), ("points", Array(Point())), ("fit_tolerance", Number()),
        ("tangents_exist", Bool()), ("start_tangent", Nullable(Point())), ("end_tangent", Nullable(Point())), ("knot_parameterization", Text()));
    internal static JsonObject Spline()
    {
        var value = Object(("degree", Integer()), ("rational", Bool()), ("periodic", Bool()), ("closed", Bool()), ("planar", Bool()),
            ("has_fit_data", Bool()), ("control_points", Array(Point())), ("knots", Array(Number())), ("weights", Array(Number())),
            ("control_point_tolerance", Number()), ("knot_tolerance", Number()), ("start_parameter", Number()), ("end_parameter", Number()),
            ("start_point", Point()), ("end_point", Point()));
        value["properties"]!["native_inspection"] = Object(("scope", Text()), ("plane", Nullable(Plane())), ("plane_unavailable_reason", Nullable(Text())),
            ("fit_data", Nullable(Fit())), ("fit_data_unavailable_reason", Nullable(Text())), ("fit_constraints_scope", Text()), ("edit_support_scope", Text()),
            ("entity_definition", Object(("degree", Integer()), ("control_point_count", Integer()), ("rational", Bool()), ("closed", Bool()))));
        value["description"] = "Complete finite native NURBS data, including periodic/fit/high-degree/larger-control representations when its full serialized result fits the actual response budget. No degree/control count/clamped-knot admission is inferred from this read. native_inspection is additive for older bridges and reports plane/fit observations and narrower editing scope; no controls/knots/weights are silently clipped.";
        return value;
    }
}
