using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class MLeaderGeometrySchema388
{
    private static JsonObject Index()=>new(){["type"]="integer",["minimum"]=0,["maximum"]=int.MaxValue};
    private static JsonObject Point(bool input=false)=>new(){["type"]="array",["minItems"]=3,["maxItems"]=3,
        ["prefixItems"]=new JsonArray(new JsonObject{["type"]="number",["minimum"]=input?-1e6:double.MinValue,["maximum"]=input?1e6:double.MaxValue},
            new JsonObject{["type"]="number",["minimum"]=input?-1e6:double.MinValue,["maximum"]=input?1e6:double.MaxValue},input?new JsonObject{["const"]=0}:new JsonObject{["type"]="number"}),["items"]=false};
    internal static JsonObject Operation()=>new(){["type"]="object",["additionalProperties"]=false,
        ["properties"]=new JsonObject{
            ["operation"]=new JsonObject{["const"]="set_mleader_vertices"},
            ["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"}},
            ["expected_geometry_token"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{64}$",["description"]="Current complete geometry.leader_editing.geometry_token from inspect_objects, in this document revision."},
            ["leader_line_index"]=Index(),
            ["vertices"]=new JsonObject{["type"]="array",["minItems"]=1,["items"]=new JsonObject{["type"]="object",["additionalProperties"]=false,
                ["properties"]=new JsonObject{["index"]=Index(),["point_wcs"]=Point(true)},["required"]=new JsonArray("index","point_wcs")}}},
        ["required"]=new JsonArray("operation","handles","expected_geometry_token","leader_line_index","vertices"),
        ["description"]="Retarget arrow, elbow or native landing endpoint vertices on one existing ordinary straight MText-content MLeader in Model or active ordinary Paper. Select actual leader_line_index and distinct zero-based vertex indices from geometry.leader_editing.lines; all branches are inspected without a branch-count ceiling. SetFirstVertex/SetVertex/SetLastVertex retain the native root, raw text, style and owned data; no text-lines replacement, vertex insertion/deletion or new association. Arrow/interior-only edits retain actual TextLocation. Selecting the last vertex permits native text/dogleg/landing reflow; native unselected-point/layout adjustment is observed and disclosed, not assumed absent. Every requested point must read back and native line/vertex topology stays retained. Fields, annotation contexts, external dependencies or foreign content owners require their owning route. WCS XY/Z0; a complete token, owner/layer preflight and detached preview precede resident mutation. Sole operation. Modified ref is the retained source; no copies or erasures. Read the actual layout and inspect the engineering result. Native Undo/rollback is not guaranteed."};
    internal static JsonObject Read()=>new(){["type"]="object",["additionalProperties"]=false,
        ["properties"]=new JsonObject{
            ["geometry_token"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-F]{64}$"},["complete"]=new JsonObject{["const"]=true},
            ["lines"]=new JsonObject{["type"]="array",["items"]=new JsonObject{["type"]="object",["additionalProperties"]=false,
                ["properties"]=new JsonObject{["leader_index"]=Index(),["leader_line_index"]=Index(),["vertices_wcs"]=new JsonObject{["type"]="array",["minItems"]=2,["items"]=Point()}},
                ["required"]=new JsonArray("leader_index","leader_line_index","vertices_wcs")}},
            ["line_styles"]=new JsonObject{["type"]="array",["items"]=new JsonObject{["type"]="object"},["description"]="Actual per-line native type/linetype/color/weight and arrow size/symbol; overrides remain retained."},
            ["layout"]=new JsonObject{["type"]="object",["description"]="Complete observed native branches/doglegs and MText layout, including text_location and embedded placement. Observations do not guarantee edit admission or rendering."},
            ["scope"]=new JsonObject{["type"]="string"}},["required"]=new JsonArray("geometry_token","complete","lines","line_styles","layout","scope")};
}
