using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class ModelSketchConstraintParameterSchema379
{
    static JsonObject Text()=>new(){["type"]="string"};
    static JsonObject Number()=>new(){["type"]="number"};
    static JsonObject Id()=>new(){["type"]="string",["pattern"]="^\\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\\}$"};
    static JsonObject Ids()=>new(){["type"]="array",["uniqueItems"]=true,["items"]=Id()};
    static JsonObject Bool()=>new(){["type"]="boolean"};
    static JsonObject Nullable(JsonObject value)=>new(){["anyOf"]=new JsonArray(value,new JsonObject{["type"]="null"})};
    static JsonObject Object(JsonObject properties)=>new(){["type"]="object",["additionalProperties"]=false,["properties"]=properties,
        ["required"]=new JsonArray(properties.Select(pair=>(JsonNode?)JsonValue.Create(pair.Key)).ToArray())};
    static JsonObject Token()=>new(){["type"]="string",["pattern"]="^[0-9A-Fa-f]{64}$"};
    public static JsonObject Operation()
    {
        var value=Object(new(){["operation"]=new JsonObject{["const"]="set_sketch_constraint_parameter"},
            ["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"}},
            ["sketch_id"]=Id(),["constraint_id"]=Id(),["expected_parameter_token"]=Token(),["expected_variable_id"]=Id(),["value"]=new JsonObject{["type"]="number",["exclusiveMinimum"]=0}});
        value["description"]="Change an existing internal native dimensional control and explicitly solve/rebuild its retained profile consumers. Read inspect_model_history with one sketch_ids ID and sketch_constraint_parameter_ids for current eligibility, token and variable_id. Use actual independently owned Line/Circle/Arc or SDK polyline-segment member bindings with a numeric literal. Supported controls are radius/diameter, Line length, aligned/x/y point distance and directed Line angle. Value is positive millimeters, or radians strictly between zero and pi for angle. Connected internal sketch geometry can move; existing fixed anchors/other controls/formulas/sources remain retained. Does not create a constraint, change a formula, edit source-projected curves or toggle reference/driving mode. Sole operation. Changed proved native storage can carry a preservation-unverified warning and bounded diagnostics; user/source data remain protected. Refresh the current body_ref/history and inspect the intended part through MCP. Undo/rollback is not guaranteed.";
        return value;
    }
    public static JsonObject ReadInput()
    {var value=Ids();value["minItems"]=1;value["description"]="Optional exact existing internal dimensional relation IDs. Requires one handles part and one sketch_ids sketch. Returns current eligibility, parameter_token, actual owning variable/member IDs and declared millimeter/radian control units for set_sketch_constraint_parameter. An unavailable binding gives its reason and no usable token; it is not an empty or solved network. This read does not solve, enter edit mode, create a relation or change geometry.";return value;}
    public static JsonObject ReadOutput()=>Object(new(){["constraint_id"]=Id(),["eligible"]=Bool(),["parameter_token"]=Nullable(Token()),["unavailable_reason"]=Nullable(Text()),["variable_id"]=Nullable(Id()),["member_id"]=Nullable(Id()),["kind"]=Nullable(Text()),["control_value"]=Nullable(Number()),
        ["control_value_unit"]=Text(),["variable_expression"]=Nullable(Text()),["variable_host_id"]=Nullable(Id()),["circle_geometry"]=Nullable(new JsonObject{["type"]="object"}),["profile_ids"]=Ids(),["consumer_feature_ids"]=Ids(),["effect_scope"]=Text(),["binding_scope"]=Nullable(new JsonObject{["type"]="object"})});
    public static JsonObject ChangeOutput(JsonObject entityRef)=>Object(new(){["operation_index"]=new JsonObject{["type"]="integer",["minimum"]=0},["source"]=entityRef.DeepClone(),["body_ref"]=entityRef.DeepClone(),["body_mc_id"]=Id(),["sketch_id"]=Id(),["constraint_id"]=Id(),["variable_id"]=Id(),["member_id"]=Id(),["kind"]=Text(),
        ["previous_value"]=Number(),["value"]=Number(),["value_unit"]=Text(),["previous_expression"]=Text(),["expression_numeric_value"]=Number(),["previous_radius"]=Nullable(Number()),["radius"]=Nullable(Number()),["profile_ids"]=Ids(),["consumer_feature_ids"]=Ids(),["source_representation_replaced"]=Bool(),["storage_preservation"]=Text(),["storage_diagnostics"]=Nullable(new JsonObject{["type"]="object"})});
}
