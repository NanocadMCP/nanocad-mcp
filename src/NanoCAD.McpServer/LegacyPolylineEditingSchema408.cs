using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class LegacyPolylineEditingSchema408
{
    private static JsonObject Handle()=>new(){["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"};
    private static JsonObject Number(bool width=false)=>new(){["type"]="number",["minimum"]=width?0:-1e9,["maximum"]=1e9};
    private static JsonObject Position()=>new(){["type"]="array",["minItems"]=2,["maxItems"]=2,["items"]=Number()};
    private static JsonObject Object(JsonObject properties,params string[] required)=>new(){["type"]="object",["additionalProperties"]=false,
        ["properties"]=properties,["required"]=new JsonArray(required.Select(key=>(JsonNode?)JsonValue.Create(key)).ToArray())};
    internal static JsonObject Operation()=>Object(new JsonObject{
        ["operation"]=new JsonObject{["const"]="set_legacy_polyline"},
        ["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=Handle()},
        ["polyline_token"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{64}$"},
        ["closed"]=new JsonObject{["type"]="boolean"},
        ["vertices"]=new JsonObject{["type"]="array",["items"]=Object(new JsonObject{
            ["vertex_handle"]=Handle(),["position"]=Position(),["bulge"]=Number(),["start_width"]=Number(true),["end_width"]=Number(true)},"vertex_handle")},
        ["inserts"]=new JsonObject{["type"]="array",["items"]=Object(new JsonObject{
            ["after_vertex_handle"]=Handle(),["position"]=Position(),["bulge"]=Number(),["start_width"]=Number(true),["end_width"]=Number(true)},
            "after_vertex_handle","position","bulge","start_width","end_width")},
        ["remove_vertices"]=new JsonObject{["type"]="array",["uniqueItems"]=true,["items"]=Handle()}
    },"operation","handles","polyline_token").WithDescription408();
    private static JsonObject WithDescription408(this JsonObject schema)
    {
        schema["description"]="Retain one ordinary exact native SimplePoly Polyline2d in place, its root handle, owner, surviving Vertex2d handles and unaffected data. Read editing detail once for the current polyline_token and actual vertex handles. Positions are absolute WCS XY pairs at Z=0, mapped to the source native OCS; no projection or legacy-to-lightweight conversion occurs. Omitted vertex fields remain stored; bulge is tan(signed outgoing sweep/4), widths belong to the outgoing segment. Terminal metadata of an open curve is stored without an outgoing segment. Inserts are placed after actual surviving anchors; multiple inserts after one anchor retain request order. All insertion values are explicit: splitting/merging does not infer arc shape or widths. Remove only selected ordinary vertices; preserve at least two and avoid collapsed segments. Closure is native Closed, without duplicating the first point. One sparse edit per root per request may be combined with ordinary ordered placement/copy/properties/erase. Token refers to state at request entry; positions apply at this edit's point in the ordered batch. Classified native Hatch/whole-endpoint dimension relationships are retained and refreshed; incompatible topology or unknown links use the owning route. Fitted legacy forms require their own explicit edit semantics and are never flattened. Refresh detail after a meaningful group for resulting vertex identities and token.";
        return schema;
    }
}
