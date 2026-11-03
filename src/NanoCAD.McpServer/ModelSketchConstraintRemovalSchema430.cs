using System.Text.Json.Nodes;
namespace NanoCAD.McpServer;
public static class ModelSketchConstraintRemovalSchema430
{
    static JsonObject Id()=>new(){["type"]="string",["pattern"]="^\\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\\}$"};
    static JsonObject Ids()=>new(){["type"]="array",["uniqueItems"]=true,["items"]=Id()};
    static JsonObject Object(JsonObject properties)=>new(){["type"]="object",["additionalProperties"]=false,["properties"]=properties,["required"]=new JsonArray(properties.Select(p=>(JsonNode?)JsonValue.Create(p.Key)).ToArray())};
    public static JsonObject Operation()
    {
        var ids=Ids();ids["minItems"]=1;
        var op=Object(new(){["operation"]=new JsonObject{["const"]="delete_sketch_constraints"},["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"}},["sketch_id"]=Id(),["expected_binding_token"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{64}$"},["constraint_ids"]=ids});
        op["description"]="Remove selected existing internal native sketch relations through their actual sketch constraint manager. Read current relation IDs and native_inputs.binding_token; never infer constraint IDs from labels or drawing dimensions. Removes the selected geometric/dimensional controls, not their sketch geometry, profile or forming operations. Existing independent source drawing constraints use their own adapter. Native-owned control variables can retire only within the observed exclusive scope; user variables and other formulas remain protected. All selected relations on the sketch share one request. Rebuild is attempted and actual model health/unresolved IDs are returned; deleting a conflict does not promise that every remaining construction is solved. Undo/rollback is not guaranteed and is only a recovery warning.";
        return op;
    }
    public static JsonObject Change()=>Object(new(){["sketch_id"]=Id(),["deleted_constraint_ids"]=Ids(),["retired_control_variable_ids"]=Ids(),["retained_control_variable_ids"]=Ids(),["native_rebuild_accepted"]=new JsonObject{["type"]="boolean"},["model_healthy"]=new JsonObject{["type"]="boolean"},["unresolved_history_ids"]=Ids()});
}
