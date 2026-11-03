using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class CurveCreationSchema441
{
    static JsonObject Number(double minimum=-1e9)=>new(){["type"]="number",["minimum"]=minimum,["maximum"]=1e9};
    static JsonObject Point(int dimensions)=>new(){["type"]="array",["minItems"]=dimensions,["maxItems"]=dimensions,["items"]=Number()};
    static JsonObject Points(int dimensions,int minimum)=>new(){["type"]="array",["minItems"]=minimum,["items"]=Point(dimensions)};
    static JsonObject Base(string type)=>new(){["type"]=new JsonObject{["const"]=type},["layer"]=new JsonObject{["type"]="string",["minLength"]=1,["maxLength"]=255,["default"]="0"}};
    static JsonObject Object(JsonObject properties,params string[] required)=>new(){["type"]="object",["additionalProperties"]=false,["properties"]=properties,["required"]=new JsonArray(required.Select(value=>(JsonNode?)JsonValue.Create(value)).ToArray())};
    internal static IEnumerable<JsonObject> Creates()
    {
        var spatial=Base("Spline");
        spatial["construction"]=new JsonObject{["const"]="fit_points_3d"};
        spatial["fit_points"]=Points(3,2);
        spatial["fit_points"]!["description"]="Ordered WCS XYZ points on the requested open cubic curve, in millimeters. No projection or workplane transform. No fixed point-count cap; request bytes govern capacity.";
        spatial["fit_tolerance"]=Number(0);
        spatial["start_tangent"]=Point(3);spatial["end_tangent"]=Point(3);
        foreach(var name in new[]{"start_tangent","end_tangent"})spatial[name]!["description"]="Optional nonzero WCS XYZ forward tangent vector. Supply both endpoint tangents or omit both for native automatic tangents.";
        var fitted=Object(spatial,"type","construction","fit_points","fit_tolerance");
        fitted["dependentRequired"]=new JsonObject{["start_tangent"]=new JsonArray("end_tangent"),["end_tangent"]=new JsonArray("start_tangent")};
        fitted["description"]="Create an open native cubic Spline through spatial fit points using the SDK's fitting operation. Points may be noncoplanar. Model Space, WCS XYZ, no workplane. Use its returned handle as an existing Sweep path or Loft guide when geometrically applicable. This is a smooth native curve, not straight-segment approximation.";
        yield return fitted;

        var periodic=Base("Spline");
        periodic["construction"]=new JsonObject{["const"]="closed_fit_points"};
        periodic["fit_points"]=Points(2,3);
        periodic["fit_points"]!["description"]="At least three distinct ordered local XY fit points in millimeters. Omit a repeated first point at the seam; native smooth closure closes the fitted curve. This is not a NURBS control-point list.";
        periodic["fit_tolerance"]=Number(0);periodic["workplane"]=WorkplaneSchema.Input();
        var closed=Object(periodic,"type","construction","fit_points","fit_tolerance");
        closed["description"]="Create a native closed smooth fitted Spline using vendor fitting and smooth-closure operations. The initial fit is cubic; native closure may change the resulting degree. Local XY uses the optional workplane, otherwise WCS XY/Z0. Endpoint tangents are not separate inputs on the seam. This does not promise globally periodic storage: inspect actual native closure at native_inspection.entity_definition.closed, while degree/periodic/closed/planar fields retain their reported values. Creation verifies the actual closed entity, fit deviation, control-plane geometry and forward seam tangent. Use for a genuinely free-form closed profile; retain analytic circles/arcs where those express the requested shape. The result can supply an existing native Sweep/Loft section. A solid section must be geometrically valid, planar and non-self-intersecting.";
        yield return closed;

        var polyline=Base("Polyline3d");polyline["vertices"]=Points(3,2);
        polyline["closed"]=new JsonObject{["type"]="boolean",["default"]=false};
        var lines=Object(polyline,"type","vertices");
        lines["description"]="Create a native SimplePoly Polyline3d from ordered WCS XYZ vertices in Model Space. Consecutive vertices differ; closed requires at least three vertices and omits the repeated first vertex. Segments are straight by intent; use fit_points_3d Spline for a smooth spatial path. No workplane or fixed vertex-count cap.";
        yield return lines;
    }
}
