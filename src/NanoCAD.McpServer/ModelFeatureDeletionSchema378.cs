using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

public static class ModelFeatureDeletionSchema378
{
    static JsonObject Id()=>new(){["type"]="string",["pattern"]="^\\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\\}$"};
    static JsonObject Nullable(JsonObject value)=>new(){["anyOf"]=new JsonArray(value,new JsonObject{["type"]="null"})};
    static JsonObject Text()=>new(){["type"]="string"};
    static JsonObject Bool()=>new(){["type"]="boolean"};
    static JsonObject Ids()=>new(){["type"]="array",["uniqueItems"]=true,["items"]=Id()};
    static JsonObject Object(JsonObject properties,params string[] required)=>new(){["type"]="object",["additionalProperties"]=false,["properties"]=properties,["required"]=new JsonArray(required.Select(key=>(JsonNode?)JsonValue.Create(key)).ToArray())};
    public static JsonObject Operation()
    {
        var result=Object(new(){["operation"]=new JsonObject{["const"]="delete_model_feature"},
            ["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"}},
            ["feature_id"]=Id(),["expected_deletion_token"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{64}$"},
            ["expected_last_item_id"]=Id(),["expected_remaining_last_item_id"]=Id(),["retirable_driver_ids"]=Ids(),["expected_retained_profile_id"]=Id()},
            "operation","handles","feature_id","expected_deletion_token","expected_last_item_id","expected_remaining_last_item_id","retirable_driver_ids");
        result["description"]="Remove one obsolete non-base terminal Fillet/Chamfer/Shell, extrusion, revolution or feature Mirror using native Erase/rebuild. Read fresh feature_deletion_ids for deletion_token, resulting_last_item_id and explicit retirable_driver_ids ([] when empty). The exact selected terminal Mirror may be NeedUpdate when all retained peers/sources are healthy or intentionally healthy-suppressed; its original same-part feature sources, profiles/sketches and controls remain. Whole-solid/block Mirror inputs are excluded. A profile consumer also requires expected_retained_profile_id from retained_profile: release only that sole consumer and retain actual profile/sketch/sources/observed controls. Dependent forming cascades, custom features and drawing-view followers are excluded. Result reports removed IDs and current retained_profile/body_ref. Native cleanup/Undo/rollback is uncertain; refresh current reads and inspect the intended shape. Sole operation.";
        return result;
    }
    public static JsonObject HistoryInput()
    {var result=Ids();result["minItems"]=1;result["description"]="Optional exact current native forming feature IDs of one selected part for terminal Fillet/Chamfer/Shell, sole-consumer extrusion/revolution or feature Mirror deletion readiness/token, retained-profile identity and complete current critical/downstream/driver/source scope. Only the selected terminal Mirror may be NeedUpdate; retained peers/sources must remain healthy or intentionally healthy-suppressed. Omitted by default; no erase, TryOnly marker write, solver or rebuild. Unavailable scope reports eligible=false without an invented token.";return result;}
    public static JsonObject ReadOutput(JsonObject entityRef)=>Object(new(){["source"]=entityRef.DeepClone(),["body_mc_id"]=Nullable(Id()),["feature_id"]=Id(),["feature_kind"]=Nullable(Text()),["eligible"]=Bool(),
        ["deletion_token"]=Nullable(new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{64}$"}),["unavailable_reason"]=Nullable(Text()),["resulting_last_item_id"]=Nullable(Id()),
        ["required_deleted_ids"]=Ids(),["retirable_driver_ids"]=Ids(),["retained_forming_ids"]=Ids(),["dependency_observations"]=Nullable(new JsonObject{["type"]="object"}),["storage_retirement_proved"]=Bool(),["storage_retirement_reason"]=Text(),["effect_scope"]=Text(),["cleanup_scope"]=Text(),["retained_profile"]=Nullable(ProfileOutput())},
        "source","body_mc_id","feature_id","feature_kind","eligible","deletion_token","unavailable_reason","resulting_last_item_id","required_deleted_ids","retirable_driver_ids","retained_forming_ids","dependency_observations","storage_retirement_proved","storage_retirement_reason","effect_scope","cleanup_scope","retained_profile");
    static JsonObject ProfileOutput()=>Object(new(){["profile_id"]=Id(),["sketch_id"]=Id(),["owner_document_id"]=Id(),
        ["profile_token"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{64}$"},["profile_host_solid_id"]=Nullable(Id()),["sketch_host_solid_id"]=Nullable(Id()),
        ["source_ids"]=Ids(),["projection_ids"]=Ids(),["disposition"]=Text()},"profile_id","sketch_id","owner_document_id","profile_token","profile_host_solid_id","sketch_host_solid_id","source_ids","projection_ids","disposition");
    public static JsonObject ChangeOutput(JsonObject entityRef)=>Object(new(){["operation_index"]=new JsonObject{["type"]="integer",["minimum"]=0},["source"]=entityRef.DeepClone(),["body_ref"]=entityRef.DeepClone(),["body_mc_id"]=Id(),["feature_id"]=Id(),["feature_kind"]=Text(),["last_item_id"]=Id(),
        ["deleted_history_ids"]=Ids(),["retired_driver_ids"]=Ids(),["retained_driver_ids"]=Ids(),["order_changes"]=new JsonObject{["type"]="array",["items"]=Object(new(){["mc_id"]=Id(),["before"]=Nullable(Id()),["after"]=Nullable(Id())},"mc_id","before","after")},
        ["source_representation_replaced"]=Bool(),["storage_retention_scope"]=Text(),["cleanup_scope"]=Text(),["retained_profile"]=Nullable(ProfileOutput())},
        "operation_index","source","body_ref","body_mc_id","feature_id","feature_kind","last_item_id","deleted_history_ids","retired_driver_ids","retained_driver_ids","order_changes","source_representation_replaced","storage_retention_scope","cleanup_scope","retained_profile");
}
