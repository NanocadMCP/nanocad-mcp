using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class NativeSketchCurveSchema445
{
    static JsonObject Numbers()=>new(){["type"]="array",["items"]=new JsonObject{["type"]="number"}};
    static JsonObject Points()=>new(){["type"]="array",["items"]=new JsonObject{["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=new JsonObject{["type"]="number",["minimum"]=-1e6,["maximum"]=1e6}}};
    static JsonObject Base(string kind)=>new(){["key"]=new JsonObject{["type"]="string",["minLength"]=1,["maxLength"]=64},["kind"]=new JsonObject{["const"]=kind},["points"]=Points()};
    static JsonObject Object(JsonObject fields,params string[] required)=>new(){["type"]="object",["additionalProperties"]=false,["properties"]=fields,["required"]=new JsonArray(required.Select(v=>(JsonNode?)JsonValue.Create(v)).ToArray())};
    internal static JsonObject Spline()
    {
        var fields=Base("spline");fields["points"]!["minItems"]=2;fields["points"]!["description"]="Complete ordered WCS NURBS control points in the sketch plane; no line-segment approximation. Closed clamped curves repeat their first endpoint control.";
        fields["degree"]=new JsonObject{["type"]="integer",["minimum"]=1};fields["knots"]=Numbers();fields["knots"]!["description"]="Ordered clamped knot vector; count=control count+degree+1, degree+1 knots at each endpoint.";
        fields["weights"]=Numbers();fields["weights"]!["items"]!["exclusiveMinimum"]=0;fields["weights"]!["description"]="Omit or use[] for nonrational NURBS; otherwise supply one positive weight per control.";
        fields["closed"]=new JsonObject{["type"]="boolean",["default"]=false};return Object(fields,"key","kind","points","degree","knots");
    }
    internal static JsonObject Ellipse()
    {
        var fields=Base("ellipse");fields["points"]!["minItems"]=3;fields["points"]!["maxItems"]=3;fields["points"]!["description"]="WCS center, major-axis endpoint and minor-axis endpoint in the sketch plane. Axis vectors must be perpendicular; major radius is at least minor radius.";
        fields["start_angle"]=new JsonObject{["type"]="number",["default"]=0};fields["end_angle"]=new JsonObject{["type"]="number",["default"]=2*Math.PI,["description"]="Radians, greater than start_angle with span≤2pi. Omit both angles for a whole native Ellipse."};return Object(fields,"key","kind","points");
    }
}
