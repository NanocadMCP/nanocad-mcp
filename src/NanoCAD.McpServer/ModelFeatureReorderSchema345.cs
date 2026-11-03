using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

public static class ModelFeatureReorderSchema345
{
    static JsonObject Id()=>new(){["type"]="string",["pattern"]="^\\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\\}$"};
    static JsonObject Object(JsonObject properties,params string[] required)=>new(){["type"]="object",["additionalProperties"]=false,["properties"]=properties,["required"]=new JsonArray(required.Select(s=>(JsonNode?)JsonValue.Create(s)).ToArray())};
    public static JsonObject Operation()
    {
        var result=Object(new(){["operation"]=new JsonObject{["const"]="reorder_model_feature"},
            ["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"}},
            ["feature_id"]=Id(),["predecessor_id"]=Id(),["expected_current_predecessor_id"]=Id(),["expected_last_item_id"]=Id(),["expected_resulting_last_item_id"]=Id()},
            "operation","handles","feature_id","predecessor_id","expected_current_predecessor_id","expected_last_item_id");
        result["description"]="Reorder one existing non-base native forming feature in a healthy named Model Space part. Read current feature IDs, native_order_before_id and last_item_id from inspect_model_history. predecessor_id goes BEFORE the selected feature. Omit expected_resulting_last_item_id to retain the existing fixed-terminal scope. To change the terminal, explicitly name the resulting last forming item from the intended complete order; only a part without drawing-view followers/nonforming terminal is supported. Native SetOrderObjBefore TryOnly and, when needed, SetAsLastPartItem TryOnly decide eligibility; no dependency bypass. Base, actual source/profile/sketch/support/driver identities, formulas and peer consumers remain retained; shared separately selected profiles use actual sketch membership and contour reads. The native rebuild may intentionally change material shape. Current single-area Circle/Line/Arc profile adapter; no deletion, profile rebind, internal solver or assemblies. Sole operation; Undo/recovery not guaranteed. Result last_item_id is the actual current terminal, with current body_ref and every assigned predecessor change. Refresh history and review the intended part through MCP; unknown/pending is not success or rollback.";
        return result;
    }
    public static JsonObject ChangeOutput(JsonObject entityRef)
    {
        JsonObject NullableId()=>new(){["anyOf"]=new JsonArray(Id(),new JsonObject{["type"]="null"})};
        return Object(new(){["operation_index"]=new JsonObject{["type"]="integer",["minimum"]=0},["source"]=entityRef.DeepClone(),["body_ref"]=entityRef.DeepClone(),
            ["body_mc_id"]=Id(),["feature_id"]=Id(),["predecessor_id"]=Id(),["old_predecessor_id"]=Id(),["last_item_id"]=Id(),
            ["source_representation_replaced"]=new JsonObject{["type"]="boolean"},["order_changes"]=new JsonObject{["type"]="array",["items"]=Object(new(){["mc_id"]=Id(),["before"]=NullableId(),["after"]=NullableId()},"mc_id","before","after")}},
            "operation_index","source","body_ref","body_mc_id","feature_id","predecessor_id","old_predecessor_id","last_item_id","source_representation_replaced","order_changes");
    }
}
