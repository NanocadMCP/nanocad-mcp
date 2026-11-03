using System.Text.Json.Nodes;
namespace NanoCAD.McpServer;
internal static class ModelPlacementSchema
{
    private static JsonObject Obj(JsonObject properties)=>new(){["type"]="object",["additionalProperties"]=false,["properties"]=properties,["required"]=new JsonArray(properties.Select(p=>(JsonNode?)JsonValue.Create(p.Key)).ToArray())};
    private static JsonObject Triple(double limit)=>new(){["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=new JsonObject{["type"]="number",["minimum"]=-limit,["maximum"]=limit}};
    private static JsonObject Placement()=>new(){["oneOf"]=new JsonArray(
        Obj(new(){["kind"]=new JsonObject{["const"]="move"},["displacement"]=Triple(1e6)}),
        Obj(new(){["kind"]=new JsonObject{["const"]="rotate"},["center"]=Triple(1e6),["angles_degrees"]=Triple(360)}))};
    internal static JsonObject Operation(bool edit)
    {
        var properties=new JsonObject{["operation"]=new JsonObject{["const"]=edit?"set_model_placement":"add_model_placement"},["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"}},
            ["expected_last_item_id"]=new JsonObject{["type"]="string",["pattern"]="^\\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\\}$"},["placement"]=Placement()};
        if(edit)properties["expected_placement"]=Placement();var result=Obj(properties);
        result["description"]="Place one native Mc3dSolid by an associative history feature, preserving the source sketch frame and later rebuild. Sole operation; named active Model Space, fresh history/last_item_id. WCS displacement or explicit center and X/Y/Z angles in DEGREES, fixed X then Y then Z order; not a camera/view change. Rotation creates a hidden fixed WorkPoint anchor (returned in created and model_placement_changes); retain it with the model. Complete history uses actual serialized-byte/cancellation budgets; sketch-based extrusion/revolution histories only. set_model_placement edits only the final placement with full expected_placement, same kind and unchanged center; formula-driven/shared parameters protected. Append for a new rotation center or another transform. No copy, scale or mirror. Direct Solid3d uses transform_solid instead. Supported source-profile/dimension and retained sketch edits reconstruct placements in real history order; previously unsupported histories remain unsupported. ROLLBACK_NOT_GUARANTEED: a started failure is unknown; inspect the affected model and never assume Undo. Refresh history/topology/drawing views and visually verify after the related modeling group.";
        return result;
    }
}
