using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class NativeStretchSchema345
{
    private static JsonObject Point(bool input=false)=>input
        ?new(){["type"]="array",["minItems"]=3,["maxItems"]=3,
            ["prefixItems"]=new JsonArray(new JsonObject{["type"]="number",["minimum"]=-1e6,["maximum"]=1e6},
                new JsonObject{["type"]="number",["minimum"]=-1e6,["maximum"]=1e6},new JsonObject{["const"]=0}),["items"]=false}
        :new(){["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=new JsonObject{["type"]="number"}};
    private static JsonObject IndexedOperation()=>new(){["type"]="object",["additionalProperties"]=false,
        ["properties"]=new JsonObject{
            ["operation"]=new JsonObject{["const"]="stretch_curve"},["mode"]=new JsonObject{["const"]="native"},
            ["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"}},
            ["expected_stretch_token"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{64}$",["description"]="Current geometry.native_stretch.stretch_token from complete native point/shape inspection in the same original document revision."},
            ["indices"]=new JsonObject{["type"]="array",["minItems"]=1,["uniqueItems"]=true,["items"]=new JsonObject{["type"]="integer",["minimum"]=0,["maximum"]=int.MaxValue},["description"]="Actual ordered indices from geometry.native_stretch.points; these are not assumed Polyline vertex or generic grip indices."},
            ["displacement_wcs"]=Point(true)},
        ["required"]=new JsonArray("operation","mode","handles","expected_stretch_token","indices","displacement_wcs"),
        ["description"]="Stretch selected native points of one independent exact Line/Polyline in Model or active ordinary Paper, retaining its native root. Initial scope is WCS XY/Z=0/zero thickness with zero Z displacement. Use complete inspected native point indices and a fresh token; all unselected native points, closed state, plane, explicit widths, appearance and classified owned/application data remain retained. Native incident curve/arc shape may change; read the actual result. This does not drive associated dimensions, sketches or model consumers. Fields/reactors/unsupported owning relationships need their dedicated route. Sole operation per request; returned modified ref is the same source, with no copies or source erasure. Native recovery is not guaranteed."};
    private static JsonObject WindowPoint()=>new(){["type"]="array",["minItems"]=2,["maxItems"]=2,["items"]=new JsonObject{["type"]="number",["minimum"]=-1e6,["maximum"]=1e6}};
    internal static JsonObject Operation()
    {
        var indexed=IndexedOperation();var window=new JsonObject{["type"]="object",["additionalProperties"]=false,
            ["properties"]=new JsonObject{
                ["operation"]=new JsonObject{["const"]="stretch_curve"},["mode"]=new JsonObject{["const"]="native"},
                ["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["uniqueItems"]=true,["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"},["description"]="Explicit independent Line/Polyline candidates in one current Model/active ordinary Paper space. No scene-wide automatic entity selection."},
                ["expected_stretch_tokens"]=new JsonObject{["type"]="array",["minItems"]=1,["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{64}$"},["description"]="One current complete geometry.native_stretch.stretch_token per handle, in exactly the same order and current original document revision."},
                ["selection_window"]=new JsonObject{["type"]="object",["additionalProperties"]=false,["properties"]=new JsonObject{
                    ["min"]=WindowPoint(),["max"]=WindowPoint()},["required"]=new JsonArray("min","max"),["description"]="Inclusive WCS XY rectangle in millimeters; min.X < max.X and min.Y < max.Y. Selects actual native stretch points inside or on its edges, not whole entities by bbox. Window direction has no different crossing/polygon-selection meaning."},
                ["displacement_wcs"]=Point(true)},
            ["required"]=new JsonArray("operation","mode","handles","expected_stretch_tokens","selection_window","displacement_wcs")};
        // One discoverable operation with strict alternative input arms. The
        // original indexed contract and read schema remain unchanged.
        var properties=new JsonObject();foreach(var p in indexed["properties"]!.AsObject())properties[p.Key]=p.Value?.DeepClone();foreach(var p in window["properties"]!.AsObject())properties[p.Key]=p.Value?.DeepClone();
        return new(){["type"]="object",["properties"]=properties,["oneOf"]=new JsonArray(indexed,window),
            ["description"]=indexed["description"]!.GetValue<string>()+" Alternatively selection_window with aligned expected_stretch_tokens stretches one or more explicit independent sources as one native group. All complete snapshots and result/warning bytes are checked before mutation. Candidates with no selected points remain unchanged and have no modified ref; at least one actual point must be selected across the group. Every selected point receives the same displacement and all other points remain. Returned modified refs are the affected original roots in input order, with no source erasure or copies. The complete group retains each source's plane/closed state/explicit widths/appearance/owned application data; native incident bulge/arc changes are disclosed. This is ordinary non-driving geometric editing: associated dimensions, Fields, sketches/model consumers and foreign owners need their dedicated routes. No arbitrary source/point count ceiling; actual request/result/snapshot bytes and cancellation bound the group. Sole operation per request. Plan the connected engineering outline and assess its native result; recovery/Undo is not guaranteed."};
    }
    internal static JsonObject ReadProperties()=>new(){["type"]="object",["additionalProperties"]=false,
        ["properties"]=new JsonObject{
            ["stretch_token"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-F]{64}$"},
            ["curve_token"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-F]{64}$"},
            ["runtime_type"]=new JsonObject{["type"]="string"},["point_count"]=new JsonObject{["type"]="integer",["minimum"]=0},["complete"]=new JsonObject{["const"]=true},
            ["points"]=new JsonObject{["type"]="array",["items"]=new JsonObject{["type"]="object",["additionalProperties"]=false,["properties"]=new JsonObject{
                ["index"]=new JsonObject{["type"]="integer",["minimum"]=0},["point_wcs"]=Point()},["required"]=new JsonArray("index","point_wcs")}},
            ["scope"]=new JsonObject{["type"]="string"}},
        ["required"]=new JsonArray("stretch_token","curve_token","runtime_type","point_count","complete","points","scope")};
}
