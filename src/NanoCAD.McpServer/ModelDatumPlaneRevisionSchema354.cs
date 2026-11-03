using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class ModelDatumPlaneRevisionSchema354
{
    static JsonObject Text()=>new(){["type"]="string"};
    static JsonObject Boolean()=>new(){["type"]="boolean"};
    static JsonObject Id()=>ModelDatumPlaneSchema353.Id();
    static JsonObject Token()=>ModelDatumPlaneSchema353.Token();
    static JsonObject List(JsonObject item)=>new(){["type"]="array",["items"]=item};
    static JsonObject Nullable(JsonObject item)=>new(){["anyOf"]=new JsonArray(item,new JsonObject{["type"]="null"})};
    static JsonObject Object(JsonObject fields,params string[] required)=>new(){["type"]="object",["additionalProperties"]=false,["properties"]=fields,["required"]=new JsonArray(required.Select(key=>(JsonNode?)JsonValue.Create(key)).ToArray())};
    internal static JsonObject Operation()
    {
        var point=new JsonObject{["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=new JsonObject{["type"]="number",["minimum"]=-1e6,["maximum"]=1e6}};
        var frame=Object(new(){["origin"]=point,["x_axis"]=point.DeepClone(),["y_axis"]=point.DeepClone()},"origin","x_axis","y_axis");
        var definition=Object(new(){["kind"]=new JsonObject{["const"]="fixed"},["workplane"]=frame},"kind","workplane");
        var result=Object(new(){["operation"]=new JsonObject{["const"]="set_model_datum_plane"},["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=BasicModelingSchema.Handle()},
            ["plane_id"]=Id(),["expected_datum_token"]=Token(),["expected_revision_token"]=Token(),["expected_last_item_id"]=Id(),
            ["expected_mirror_ids"]=new JsonObject{["type"]="array",["minItems"]=1,["uniqueItems"]=true,["items"]=Id()},
            ["expected_direct_consumer_ids"]=new JsonObject{["type"]="array",["minItems"]=1,["uniqueItems"]=true,["items"]=Id()},["definition"]=definition},
            "operation","handles","plane_id","expected_datum_token","expected_revision_token","expected_last_item_id","definition");
        result["oneOf"]=new JsonArray(
            new JsonObject{["required"]=new JsonArray("expected_direct_consumer_ids"),["not"]=new JsonObject{["required"]=new JsonArray("expected_mirror_ids")}},
            new JsonObject{["required"]=new JsonArray("expected_mirror_ids"),["not"]=new JsonObject{["required"]=new JsonArray("expected_direct_consumer_ids")}});
        result["description"]="Revise one retained fixed non-GCS WorkPlane in the selected part's native owner, preserving its ID and exact direct same-part sketch/Mirror consumers. Read datum_plane_revision_ids, supply both fresh tokens, last_item_id and the complete direct_consumer_ids set as expected_direct_consumer_ids; legacy expected_mirror_ids is for mirror-only scope. Native SetFixedDefinition updates support placement and dependent geometry; no plane/feature recreation or host reassignment occurs. Existing sketch/profile/source/driver links and consumer identities are retained while their intended geometry can move with the plane. Foreign/shared-part consumers, attached3D constraints and unsupported dependent datum modes require a separate route. In-plane U/V values are native representation, not independent axis controls. Refresh current body_ref and returned datum/revision tokens, then inspect the changed part and affected drawing. Sole operation; Model Space/Millimeters; native Undo/rollback not guaranteed, unknown is not success.";
        return result;
    }
    internal static JsonObject HistoryInput()=>new(){["type"]="array",["minItems"]=1,["uniqueItems"]=true,["items"]=Id(),["description"]="Exact current retained fixed datum IDs supporting sketches or Mirrors of the selected part. Returns actual owner/plane/token, direct_sketches with retained profile IDs, direct_mirrors, complete direct_consumer_ids, downstream IDs and fresh revision_token. Omit for ordinary history without this support/dependency read. No setter, solver or view refresh occurs."};
    internal static JsonObject Read(JsonObject reference)=>Object(new(){["body_ref"]=reference,["body_mc_id"]=Id(),["last_item_id"]=Id(),["revision_token"]=Token(),["datum"]=ModelDatumPlaneSchema353.Read(),
        ["direct_mirrors"]=List(Object(new(){["feature_id"]=Id(),["source_item_ids"]=List(Id()),["has_sub_feature_items"]=Boolean(),["native_order_before_id"]=Nullable(Id()),["dependency_ids"]=List(Id())},"feature_id","source_item_ids","has_sub_feature_items","native_order_before_id","dependency_ids")),
        ["direct_sketches"]=List(Object(new(){["sketch_id"]=Id(),["profile_ids"]=List(Id()),["support_id"]=Id()},"sketch_id","profile_ids","support_id")),["direct_consumer_ids"]=List(Id()),
        ["downstream_ids"]=List(Id()),["body_geometry"]=new JsonObject{["type"]="object"},["geometry_scope"]=Text()},"body_ref","body_mc_id","last_item_id","revision_token","datum","direct_mirrors","downstream_ids","body_geometry","geometry_scope");
    internal static JsonObject Change(JsonObject reference)=>Object(new(){["operation_index"]=new JsonObject{["type"]="integer",["minimum"]=0},["source_ref"]=reference.DeepClone(),["body_ref"]=reference.DeepClone(),["representation_replaced"]=Boolean(),["datum_representation_replaced"]=Boolean(),["native_representations"]=List(reference.DeepClone().AsObject()),["revision"]=Read(reference.DeepClone().AsObject())},"operation_index","source_ref","body_ref","representation_replaced","datum_representation_replaced","native_representations","revision");
}
