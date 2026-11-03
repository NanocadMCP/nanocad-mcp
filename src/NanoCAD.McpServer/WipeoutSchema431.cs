using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class WipeoutSchema431
{
    static JsonObject Point(int count)=>new(){["type"]="array",["minItems"]=count,["maxItems"]=count,["items"]=new JsonObject{["type"]="number",["minimum"]=-1e6,["maximum"]=1e6}};
    internal static JsonObject Create()=>new(){["type"]="object",["additionalProperties"]=false,["properties"]=new JsonObject{
        ["type"]=new JsonObject{["const"]="Wipeout"},["layer"]=new JsonObject{["type"]="string",["maxLength"]=255},["boundary"]=new JsonObject{["type"]="array",["minItems"]=3,["items"]=Point(2)}},["required"]=new JsonArray("type","boundary"),
        ["description"]="Create a native opaque Wipeout mask from one simple straight polygon; local XY points in drawing millimeters, implicit closing edge, no duplicate last vertex. Optional workplane uses the common drawing frame. This masks existing drawing content without replacing it with a filled polygon or changing source geometry. Use set_draw_order with real object handles to place the mask above hidden content and below labels/details that stay visible. Existing move/rotate/uniform scale/copy/erase preserve the native mask. Frame display is a nanoCAD setting and is not changed. Inspect actual masking and ordering."};
    internal static JsonObject Edit()=>new(){["type"]="object",["additionalProperties"]=false,["properties"]=new JsonObject{
        ["operation"]=new JsonObject{["const"]="set_wipeout_boundary"},["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"}},
        ["expected_geometry_token"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{64}$"},["boundary_world"]=new JsonObject{["type"]="array",["minItems"]=3,["items"]=Point(3)}},["required"]=new JsonArray("operation","handles","expected_geometry_token","boundary_world"),
        ["description"]="Replace one existing native Wipeout polygon in place. Read actual geometry_token and boundary_world first; ordered WCS vertices must form a simple straight polygon on its existing plane, without duplicate closure. Keeps the real mask object, ordinary display/application data and draw order; does not edit covered source geometry or global frame settings. Sole operation. Native Undo/recovery is not guaranteed; inspect final masking with appropriate draw order."};
}
