using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class PolylineTopologySchema
{
    private static JsonObject Object(JsonObject fields) => new() { ["type"]="object", ["additionalProperties"]=false,
        ["required"]=new JsonArray(fields.Select(p=>(JsonNode?)JsonValue.Create(p.Key)).ToArray()), ["properties"]=fields };
    private static JsonObject Number(double min, double max) => new(){["type"]="number",["minimum"]=min,["maximum"]=max};
    private static JsonObject Shape() => Object(new JsonObject {
        ["closed"]=new JsonObject{["type"]="boolean"},
        ["vertices"]=new JsonObject{["type"]="array",["minItems"]=2,["items"]=Object(new JsonObject{
            ["position"]=new JsonObject{["type"]="array",["minItems"]=2,["maxItems"]=2,["items"]=Number(-1e6,1e6)},
            ["bulge"]=Number(-1e6,1e6),["start_width"]=Number(0,1e6),["end_width"]=Number(0,1e6)})}});
    private static JsonObject Corner(string kind)
    {
        var fields=new JsonObject{["index"]=new JsonObject{["type"]="integer",["minimum"]=0,["maximum"]=int.MaxValue},["kind"]=new JsonObject{["const"]=kind}};
        if(kind=="fillet")fields["radius"]=Number(.001,1e6);
        else {fields["incoming_distance"]=Number(.001,1e6);fields["outgoing_distance"]=Number(.001,1e6);}
        return Object(fields);
    }
    internal static JsonObject Operation(string kind)
    {
        var fields=new JsonObject{["operation"]=new JsonObject{["const"]=kind},
            ["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"}},
            ["expected_geometry"]=Shape()};
        if(kind=="set_polyline_geometry")fields["geometry"]=Shape();
        if(kind=="set_polyline_corners")fields["corners"]=new JsonObject{["type"]="array",["minItems"]=1,["items"]=new JsonObject{["oneOf"]=new JsonArray(Corner("fillet"),Corner("chamfer"))}};
        var result=Object(fields);
        fields["workplane"]=WorkplaneSchema.Input();
        result["description"]="Edit one independent exact native Polyline, keeping its handle/display properties and native plane. Default coordinates use WCSXY/Z0 normal+Z: read position from point_ocs, plus bulge/start_width/end_width. Optional workplane expresses all positions and signed bulges in that explicit oriented frame; read actual point_wcs and convert to the frame, and negate stored bulges when its normal opposes the Polyline normal. Normals may be parallel or antiparallel. No projection, resident transform or UCS change occurs. A framed target may share only appearance edits or Polyline edits with the same frame. expected_geometry must match the complete ordered source including closed after earlier edits. Coordinates +/-1e6mm, at least two vertices; do not duplicate the first point for closure. Complete requests and results are bounded by actual serialized bytes, without truncating geometry to a vertex count. Fields, persistent dependencies and native model source ownership require the owning adapter. "+(kind switch {
            "set_polyline_geometry"=>"Replace explicit segment geometry to insert/remove vertices, reshape the contour, change widths or open/close it. All vertex fields are required; bulge is tan(signed sweep/4),0 straight. Widths describe the outgoing segment. An open terminal vertex stores metadata but has no outgoing segment. Reject collapsed segments; no automatic joining, intersection repair or inference of omitted widths. Self-intersecting contours are not automatically made simple.",
            "reverse_polyline"=>"Reverse traversal without changing the physical contour: reverse vertex order, negate/reindex bulges, swap outgoing segment endpoint widths. Closure and open terminal metadata are retained. Corner indices subsequently refer to the reversed order.",
            _=>"Apply fillets/chamfers simultaneously using distinct indices of expected_geometry. A fillet uses radius; a chamfer uses distances measured from the corner along incoming/outgoing segments. Each chosen corner needs two straight zero-width segments; open endpoints/straight continuations/U-turns and overlapping adjacent treatments are rejected. Unselected segments, including arcs/widths elsewhere, are retained. Supports either winding and concave corners, without tessellation. Does not promise a globally simple outline for arbitrary input: inspect intersections and the image when the task requires a simple region."})+
            " Refresh vertex indices after topology edits; inspect API geometry and a visual checkpoint after a meaningful group.";
        return result;
    }
}
