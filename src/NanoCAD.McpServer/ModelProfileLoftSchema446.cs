using System.Text.Json.Nodes;
namespace NanoCAD.McpServer;
internal static class ModelProfileLoftSchema446
{
    static JsonObject Id()=>new(){["type"]="string",["pattern"]="^\\{[0-9A-Fa-f]{8}(-[0-9A-Fa-f]{4}){3}-[0-9A-Fa-f]{12}\\}$"};
    static JsonObject Token()=>new(){["type"]="string",["pattern"]="^[0-9A-Fa-f]{64}$"};
    static JsonObject Handle()=>new(){["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"};
    static JsonObject Object(JsonObject fields,params string[] required)=>new(){["type"]="object",["additionalProperties"]=false,["properties"]=fields,["required"]=new JsonArray(required.Select(v=>(JsonNode?)JsonValue.Create(v)).ToArray())};
    internal static JsonObject Operation()
    {
        var profile=Object(new(){["profile_id"]=Id(),["expected_sketch_id"]=Id(),["expected_profile_token"]=Token()},"profile_id","expected_sketch_id","expected_profile_token");
        var fields=new JsonObject{["operation"]=new JsonObject{["const"]="add_model_loft_from_profiles"},
            ["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=Handle()},["expected_last_item_id"]=Id(),
            ["profiles"]=new JsonObject{["type"]="array",["minItems"]=2,["items"]=profile,["description"]="Ordered exact current native selected profiles. Each includes its real parent sketch and fresh token from inspect_model_history profile_ids."},
            ["part_operation"]=new JsonObject{["enum"]=new JsonArray("join","cut")},["name"]=new JsonObject{["type"]="string",["minLength"]=1,["maxLength"]=64},
            ["path_handle"]=Handle(),["guide_handles"]=new JsonObject{["type"]="array",["items"]=new JsonObject{["type"]="array",["minItems"]=1,["uniqueItems"]=true,["items"]=Handle()}}};
        var value=Object(fields,"operation","handles","expected_last_item_id","profiles","part_operation");
        value["description"]="Append one native LoftFeature join/cut using two or more ordered existing same-owner SketchProfile IDs. Retain each selected area's exact Line/Arc/Circle/Spline/Ellipse boundary, parent sketch, internal controls, support and existing same-part consumers. Supply a current profile/sketch/token tuple for every section and current body last_item_id. No section is recreated, projected again, tessellated or auto-reselected. Native host affinity must be absent or the selected part; another part's construction is not transferred. Defaults to a regular open loft with free end conditions; optionally supply one retained native center path_handle or ordered guide_handles chains. These modes are mutually exclusive. Profiles and referenced curves must define a valid connected material join/cut. Returns current section/profile/sketch tokens and actual native feature identity; review actual shape and driver propagation at a meaningful checkpoint. Sole operation; no artificial section-count ceiling; native Undo/rollback is not guaranteed and an unknown outcome requires inspecting current state.";
        return value;
    }
    internal static JsonObject ConsumedOutput()=>Object(new(){["profile_id"]=Id(),["sketch_id"]=Id(),["profile_token"]=Token(),["sketch_token"]=Token(),["ownership"]=ModelProfileLifecycleSchema340.OwnershipOutput()},"profile_id","sketch_id","profile_token","sketch_token","ownership");
}
