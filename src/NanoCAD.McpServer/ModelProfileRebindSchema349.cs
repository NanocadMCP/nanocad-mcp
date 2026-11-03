using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

public static class ModelProfileRebindSchema349
{
    static JsonObject Id()=>new(){["type"]="string",["pattern"]="^\\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\\}$"};
    static JsonObject Text()=>new(){["type"]="string"};
    static JsonObject Flag()=>new(){["type"]="boolean"};
    static JsonObject Ids()=>new(){["type"]="array",["items"]=Id()};
    static JsonObject NullableId()=>new(){["anyOf"]=new JsonArray(Id(),new JsonObject{["type"]="null"})};
    static JsonObject Object(JsonObject properties,params string[] required)=>new(){["type"]="object",["additionalProperties"]=false,
        ["properties"]=properties,["required"]=new JsonArray(required.Select(value=>(JsonNode?)JsonValue.Create(value)).ToArray())};
    public static JsonObject Operation()
    {
        var p=new JsonObject{["operation"]=new JsonObject{["const"]="set_model_extrusion_profile"},
            ["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"}},
            ["feature_id"]=Id(),["expected_current_profile_id"]=Id(),["expected_last_item_id"]=Id(),["profile_id"]=Id(),
            ["expected_sketch_id"]=Id(),["expected_profile_token"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{64}$"},
            ["replacement_regions"]=new JsonObject{["type"]="array",["minItems"]=1,["items"]=new JsonObject{["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=new JsonObject{["type"]="number",["minimum"]=-1e6,["maximum"]=1e6}}}};
        var result=Object(p,p.Select(value=>value.Key).Where(key=>key is not ("profile_id" or "replacement_regions")).ToArray());
        JsonObject Choice(string required,string excluded)=>new(){["required"]=new JsonArray(required),["not"]=new JsonObject{["required"]=new JsonArray(excluded)}};
        result["oneOf"]=new JsonArray(Choice("profile_id","replacement_regions"),Choice("replacement_regions","profile_id"));
        result["description"]="Change the closed section of an existing native extrusion while retaining its feature ID, dimensions/formulas and supported downstream native consumers. Read current feature_id/profile_id/last_item_id from body history. Choose exactly one route: (1) profile_id identifies a different existing profile; expected_sketch_id and expected_profile_token belong to that replacement profile's fresh exact profile_ids read. (2) replacement_regions supplies one interior WCS [x,y,z] point per desired region on the CURRENT profile's already acquired sketch; expected_sketch_id and expected_profile_token belong to the CURRENT profile. This second route creates one separate selected profile without editing the original profile or sketch geometry/constraints. It supports annular and separated/nested Circle regions, plus disjoint simple closed Line/Arc loops mixed with Circles, including holes and multiple selected areas. All observed boundary elements must form complete closed loops; open references, branched, intersecting, overlapping or touching loops require another topology adapter. Select each region once; adjacent nested cells that merge are excluded. Existing-ID profiles may have complete selected Circle/Line/Arc boundaries including holes/multiple areas and actual same-part shared consumers. Replacement construction must retain the same actual native owner, oriented sketch frame and support. Its parent must already belong to the selected part's composition, or the profile must have an actual same-part forming consumer. An unused profile on an outside document sketch is refused before the setter; same owner document alone does not establish acquisition. Existing material operation, extents and drivers remain. Only this extrusion changes forward-consumer sets; other consumers keep their profiles. Native cycles are rejected before mutation. Old profile/sketch/sources and available constraint/variable bindings remain protected; actual ownership is reported without a release/reuse promise. Sole operation in named Millimeters Model Space; recovery/Undo not guaranteed. Refresh returned body_ref/history and inspect the resulting part and linked drawings through MCP.";
        return result;
    }
    public static JsonObject ReadOutput()=>Object(new(){["candidate"]=Flag(),["current_profile_id"]=NullableId(),
        ["required_inputs"]=new JsonObject{["type"]="array",["items"]=Text()},["scope"]=Text()},
        "candidate","current_profile_id","required_inputs","scope");
    public static JsonObject ChangeOutput(JsonObject entityRef)
    {
        var old=Object(new(){["profile_id"]=Id(),["parent_sketch_id"]=Id(),["mc_document_id"]=Id(),
            ["profile_host_solid_id"]=NullableId(),["sketch_host_solid_id"]=NullableId(),["dependent_feature_id"]=NullableId(),
            ["in_body_history"]=Flag(),["disposition"]=Text(),["reuse_scope"]=Text()},"profile_id","parent_sketch_id","mc_document_id",
            "profile_host_solid_id","sketch_host_solid_id","dependent_feature_id","in_body_history","disposition","reuse_scope");
        var p=new JsonObject{["operation_index"]=new JsonObject{["type"]="integer",["minimum"]=0},["source"]=entityRef.DeepClone(),["body_ref"]=entityRef.DeepClone(),
            ["body_mc_id"]=Id(),["feature_id"]=Id(),["previous_profile_id"]=Id(),["profile_id"]=Id(),["parent_sketch_id"]=Id(),
            ["profile_token"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-F]{64}$"},["mc_document_id"]=Id(),["last_item_id"]=Id(),
            ["source_representation_replaced"]=Flag(),["retained_driver_ids"]=Ids(),
            ["retained_downstream_count"]=new JsonObject{["type"]="integer",["minimum"]=0},["downstream_history_count"]=new JsonObject{["type"]="integer",["minimum"]=0},
            ["downstream_history_sha256"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-F]{64}$"},["previous_profile"]=old,["profile_selection_created"]=Flag()};
        return Object(p,p.Select(value=>value.Key).ToArray());
    }
}
