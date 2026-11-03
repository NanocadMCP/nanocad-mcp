using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class ModelFeatureRevolutionSchema
{
    internal static JsonObject Operation()
    {
        JsonObject Number(double low,double high)=>new(){["type"]="number",["minimum"]=low,["maximum"]=high};
        JsonObject Triple(double bound)=>new(){["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=Number(-bound,bound)};
        JsonObject Handle()=>new(){["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"};
        var plane=new JsonObject{["type"]="object",["additionalProperties"]=false,["required"]=new JsonArray("origin","x_axis","y_axis"),
            ["properties"]=new JsonObject{["origin"]=Triple(1e6),["x_axis"]=Triple(1),["y_axis"]=Triple(1)}};
        var fields=new JsonObject{
            ["operation"]=new JsonObject{["const"]="add_model_revolution"},
            ["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=Handle()},
            ["expected_last_item_id"]=new JsonObject{["type"]="string",["pattern"]="^\\{[0-9A-Fa-f]{8}(-[0-9A-Fa-f]{4}){3}-[0-9A-Fa-f]{12}\\}$"},
            ["part_operation"]=new JsonObject{["enum"]=new JsonArray("join","cut")},
            ["source_handles"]=new JsonObject{["type"]="array",["minItems"]=1,["uniqueItems"]=true,["items"]=Handle()},
            ["axis_handle"]=Handle(),["plane"]=plane,["angle_degrees"]=new JsonObject{["type"]="number",["exclusiveMinimum"]=0,["maximum"]=360},
            ["name"]=new JsonObject{["type"]="string",["minLength"]=1,["maxLength"]=64,["default"]="Revolution"}};
        return new(){["type"]="object",["additionalProperties"]=false,
            ["required"]=new JsonArray("operation","handles","expected_last_item_id","part_operation","source_handles","axis_handle","plane","angle_degrees"),["properties"]=fields,
            ["description"]="Append a native join/cut revolution to one existing Mc3dSolid. Select a complete closed exact native Circle or Line/Arc contour and a separate coplanar native Line axis in Model Space; plane supplies an orthonormal WCS frame. Real source curves remain unchanged and are projected into a new body-owned sketch/profile. Existing source bindings may be reused. Use a positive angle at most360 DEGREES around the directed native axis, representable in native radians; an explicit positive Angle extent retains its native degree variable, including360. Named Millimeters Model Space document, MultiCAD already loaded, current expected_last_item_id, sole operation. Original history, formulas and prior source/view bindings are retained; healthy terminal drawing views can follow via native history insertion. Result model_revolution_features returns current body_ref, sketch/profile/feature/angle_variable IDs and source/axis refs. Native rebuild can replace the body's DWG representation and update dependents; refresh history, variables, topology and views. Check the engineering result with MCP after the meaningful group. No detached revolved/boolean volume oracle is required. This operation creates a new projected profile within the existing body; standalone sketch lifecycle and selecting an arbitrary existing profile ID are separate scopes. ROLLBACK_NOT_GUARANTEED: started failure remains unknown and may leave partial history. No automatic Undo."};
    }
}
