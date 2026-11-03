using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class DrawingViewEditSchema
{
    private static JsonObject Handle()=>new(){["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"};
    private static JsonObject TargetProperties()=>new(){["layout_handle"]=Handle(),["handle"]=Handle(),
        ["expected_name"]=new JsonObject{["type"]="string",["minLength"]=1,["maxLength"]=4096}};
    public static JsonElement Properties()
    {
        var properties=TargetProperties();properties["operation"]=new JsonObject{["type"]="string",["const"]="set_drawing_view_properties"};
        properties["origin"]=new JsonObject{["type"]="array",["minItems"]=2,["maxItems"]=2,["items"]=new JsonObject{["type"]="number"},
            ["description"]="WCS XY millimeters in the owning Model/Paper space; preserve native plane axes and Z. Native parent alignment remains enabled as stored."};
        properties["model_scale"]=new JsonObject{["type"]="number",["exclusiveMinimum"]=0,
            ["description"]="Native DScale denominator (2 draws at one-half size). Preserve design_scale. Include affected aligned descendants with the requested placement/scale in this same group."};
        foreach(string key in new[]{"show_hidden_lines","show_hatches","show_tangent_edges"})properties[key]=new JsonObject{["type"]="boolean"};
        return JsonSerializer.SerializeToElement(new JsonObject{["type"]="object",["additionalProperties"]=false,
            ["required"]=new JsonArray("operation","layout_handle","handle","expected_name"),["properties"]=properties,
            ["anyOf"]=new JsonArray(new JsonObject{["required"]=new JsonArray("origin")},new JsonObject{["required"]=new JsonArray("model_scale")},
                new JsonObject{["required"]=new JsonArray("show_hidden_lines")},new JsonObject{["required"]=new JsonArray("show_hatches")},new JsonObject{["required"]=new JsonArray("show_tangent_edges")}),
            ["description"]="Edit an existing exact native DrawingView in an ordinary Model/Paper layout in place. Use get_drawing_views for layout/view identity, names, parent/child and alignment. Group only these property operations in one request; each view appears once. Preserve 3D source, parent/root, section and attached-cut/break identities; no source reattachment, projection-plane change, view reconstruction or manual geometry. Placement/scale with breaks/local cuts requires its owning adapter. Native update/Undo is not guaranteed atomic; failures after starting can be unknown."});
    }
    public static JsonElement Delete()=>JsonSerializer.SerializeToElement(new JsonObject{["type"]="object",["additionalProperties"]=false,
        ["required"]=new JsonArray("operation","targets"),["properties"]=new JsonObject{
            ["operation"]=new JsonObject{["type"]="string",["const"]="delete_drawing_views"},
            ["targets"]=new JsonObject{["type"]="array",["minItems"]=1,["items"]=new JsonObject{["type"]="object",["additionalProperties"]=false,
                ["required"]=new JsonArray("layout_handle","handle","expected_name"),["properties"]=TargetProperties()}}},
        ["description"]="Sole operation. Native erase of an explicit ordinary projection-view leaf or subtree, possibly across Model/Paper layouts. Select every child/descendant to be removed by exact layout/view handle and current name; no implicit cascade into unselected views. Keep all 3D source bodies and retained view identities. Section/slice views, attached breaks/local cuts and external native dependents require their owning deletion adapter. Returned drawing_view changes refer to the erased identities. Native Undo/rollback is not guaranteed; unknown requires inspection, never a new duplicate request."});
}
