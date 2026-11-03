using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class ModelProfileRegionsSchema350
{
    static JsonObject Text(string description)=>new(){["type"]="string",["description"]=description};
    static JsonObject Id(string description){var item=Text(description);item["pattern"]="^\\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\\}$";return item;}
    static JsonObject Array(JsonNode items,int minimum=0)=>new(){["type"]="array",["items"]=items,["minItems"]=minimum};
    static JsonObject Object(params (string Name,JsonObject Schema)[] fields)=>new(){["type"]="object",["additionalProperties"]=false,
        ["required"]=new JsonArray(fields.Select(field=>(JsonNode?)JsonValue.Create(field.Name)).ToArray()),
        ["properties"]=new JsonObject(fields.Select(field=>new KeyValuePair<string,JsonNode?>(field.Name,field.Schema)))};
    static JsonObject Vector()=>new(){["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=new JsonObject{["type"]="number"}};
    static JsonObject LocalCenter(){var value=Object(("X",new JsonObject{["type"]="number"}),("Y",new JsonObject{["type"]="number"}));
        value["description"]="Native sketch XY center in the reported plane frame, in drawing millimeters. region_points are WCS; transform local coordinates through origin_wcs/x_axis_wcs/y_axis_wcs.";return value;}
    internal static JsonObject HistoryInput()=>new(){["type"]="array",["minItems"]=1,["uniqueItems"]=true,
        ["description"]="Optional exact consumed native Circle profile IDs in one selected existing part. Returns actual selected areas/hole loops, retained element/source IDs and fresh region_token. Ordinary history is unchanged when omitted. This read does not enter edit mode or evaluate the MCS solver; unavailable existing constraint scope is explicit. All selected parts for this field must be the same one target body.",
        ["items"]=Id("Current consumed extrusion profile native ID, not a DWG handle.")};
    internal static JsonObject Operation()
    {
        var point=new JsonObject{["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=new JsonObject{["type"]="number",["minimum"]=-1e6,["maximum"]=1e6}};
        return new(){["type"]="object",["additionalProperties"]=false,["required"]=new JsonArray("operation","handles","feature_id","profile_id","expected_sketch_id","expected_region_token","expected_last_item_id","region_points"),
            ["description"]="Change selected native regions of an existing extrusion's retained sketch/profile, optionally adding exact Circle source projections. Separated/nested whole Circle loops support native annular holes and multiple nonadjacent regions. Preserve the extrusion ID, extent/direction/material/drivers and downstream history. Sources/sketch elements are retained; no erase, native-owner reset or MCS solve. Read current exact profile_region_ids metadata first. Recovery and Undo are not guaranteed.",
            ["properties"]=new JsonObject{["operation"]=new JsonObject{["const"]="set_model_profile_regions"},["handles"]=new JsonObject{["type"]="array",["items"]=Text("Current body handle."),["minItems"]=1,["maxItems"]=1},
                ["feature_id"]=Id("Existing extrusion native ID."),["profile_id"]=Id("Its current exact native profile."),["expected_sketch_id"]=Id("Current parent sketch."),
                ["expected_region_token"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{64}$"},["expected_last_item_id"]=Id("Current native history terminal ID."),
                ["add_source_handles"]=Array(Text("Optional exact existing top-level Circle source; original geometry/display/constraints are retained.")),
                ["region_points"]=Array(point,1)}};
    }
    internal static JsonObject ReadOutput()=>new(){["type"]="object",["required"]=new JsonArray("profile_id","parent_sketch_id","mc_document_id","feature_id","region_token","plane","loops","regions","scope","constraint_observation"),
        ["properties"]=new JsonObject{["profile_id"]=Id("Native profile."),["parent_sketch_id"]=Id("Actual retained parent."),["mc_document_id"]=Id("Actual body native owner document; may differ from active drawing lookup container."),
            ["feature_id"]=Id("Actual existing extrusion consumer."),["region_token"]=Text("Complete current native region/source/constraint/frame token."),
            ["plane"]=Object(("origin_wcs",Vector()),("x_axis_wcs",Vector()),("y_axis_wcs",Vector()),("normal_wcs",Vector())),
            ["loops"]=Array(Object(("element_id",Id("Actual retained native sketch projection ID.")),
                ("center",LocalCenter()),
                ("radius",new JsonObject{["type"]="number",["minimum"]=.001}),("source_object_id",Id("Exact retained ordinary Circle source native ID.")))),
            ["regions"]=Array(Object(("outer_element_id",Id("Exact Circle outer boundary element.")),("hole_element_ids",Array(Id("Exact Circle inner boundary element."))))),
            ["scope"]=Text("Whole Circle loop and actual selected native area/hole identities."),["constraint_observation"]=ModelSketchConstraintObservationSchema353.Read()}};
    internal static JsonObject ChangeOutput(JsonNode reference)=>new(){["type"]="object",["required"]=new JsonArray("operation_index","source","body_ref","body_mc_id","feature_id","profile_id","parent_sketch_id","region_token","mc_document_id","last_item_id","source_representation_replaced","region_count","hole_count","added_source_refs","added_projection_ids","retained_downstream_count","downstream_history_sha256","constraint_observation"),
        ["properties"]=new JsonObject{["operation_index"]=new JsonObject{["type"]="integer"},["source"]=reference.DeepClone(),["body_ref"]=reference.DeepClone(),["body_mc_id"]=Id("Native body."),["feature_id"]=Id("Retained native extrusion."),["profile_id"]=Id("Same native profile."),["parent_sketch_id"]=Id("Same native sketch."),["region_token"]=Text("Fresh current token."),["mc_document_id"]=Id("Actual owner."),["last_item_id"]=Id("Retained terminal."),["source_representation_replaced"]=new JsonObject{["type"]="boolean"},
            ["region_count"]=new JsonObject{["type"]="integer",["minimum"]=1},["hole_count"]=new JsonObject{["type"]="integer",["minimum"]=0},["added_source_refs"]=Array(reference.DeepClone()),["added_projection_ids"]=Array(Id("Actual native projection ID, never a requested geometry GUID.")),["retained_downstream_count"]=new JsonObject{["type"]="integer",["minimum"]=0},["downstream_history_sha256"]=Text("Exact sorted native downstream inventory hash."),["constraint_observation"]=ModelSketchConstraintObservationSchema353.Read()}};
}
