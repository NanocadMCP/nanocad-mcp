using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class ModelShellSchema
{
    internal static JsonObject Operation()=>new()
    {
        ["type"]="object",["additionalProperties"]=false,
        ["required"]=new JsonArray("operation","handles","expected_last_item_id","face_token","thickness"),
        ["properties"]=new JsonObject
        {
            ["operation"]=new JsonObject{["const"]="add_model_shell"},
            ["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,
                ["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"}},
            ["expected_last_item_id"]=new JsonObject{["type"]="string",["pattern"]="^\\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\\}$"},
            ["face_token"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{64}$"},
            ["open_face_id"]=new JsonObject{["type"]="string",["minLength"]=1,["maxLength"]=128},
            ["open_face_ids"]=new JsonObject{["type"]="array",["minItems"]=1,["uniqueItems"]=true,
                ["items"]=new JsonObject{["type"]="string",["minLength"]=1,["maxLength"]=128}},
            ["thickness"]=new JsonObject{["type"]="number",["minimum"]=.001,["maximum"]=1e6}
        },
        ["oneOf"]=new JsonArray(
            new JsonObject{["required"]=new JsonArray("open_face_id")},
            new JsonObject{["required"]=new JsonArray("open_face_ids")}),
        ["description"]="Append a native inward ThicknessFeature to one Mc3dSolid. Select one opening with open_face_id or a nonempty distinct set with open_face_ids, using actual model_face_id values and face_token from inspect_solid_topology(include_native_model_faces=true), plus the current last_item_id from inspect_model_history. Choose wall thickness from functional and manufacturing requirements; millimeters. Each selected opening must be a current planar face owned by this body. Choose openings from the intended housing or passage and wall thickness from function/manufacture. The native feature receives all selected openings; no opening-count ceiling is added. Sole operation. Native history, all requested opening identities and retained wall thickness are checked. The result lists all open_face_ids; open_face_id is its legacy first item, not the complete set. Inspect the engineering result using MCP capture_view and exact geometry as needed. ROLLBACK_NOT_GUARANTEED; unknown requires inspection, not a new-ID retry."
    };

    internal static JsonObject ThicknessOperation()=>new()
    {
        ["type"]="object",["additionalProperties"]=false,
        ["required"]=new JsonArray("operation","handles","expected_last_item_id","feature_id","expected_thickness","thickness"),
        ["properties"]=new JsonObject
        {
            ["operation"]=new JsonObject{["const"]="set_model_shell_thickness"},
            ["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,
                ["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"}},
            ["expected_last_item_id"]=new JsonObject{["type"]="string",["pattern"]="^\\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\\}$"},
            ["feature_id"]=new JsonObject{["type"]="string",["pattern"]="^\\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\\}$"},
            ["expected_thickness"]=new JsonObject{["type"]="number",["minimum"]=.001,["maximum"]=1e6},
            ["replace_driver_formulas"]=new JsonObject{["type"]="boolean",["default"]=false,
                ["description"]="Explicitly replace a changed formula-driven thickness with the supplied numeric literal. Set this only for an intentional replacement. The tested native rebuild lost named thickness formulas."},
            ["thickness"]=new JsonObject{["type"]="number",["minimum"]=.001,["maximum"]=1e6}
        },
        ["description"]="Change native wall thickness of a healthy inward ThicknessFeature, including one followed by other model features, preserving its feature, driver identity and complete native set of opening faces. Read open_face_ids and thickness from current history, and variables before editing a formula-driven thickness. The tested native rebuild lost the named thickness formula, so named propagation through this control is unavailable. Replacing a changed driver formula with a numeric literal requires replace_driver_formulas=true; unrelated expressions remain retained. An existing associative drawing view chain may follow it and is verified after rebuild. Supply current last_item_id, exact feature_id and expected_thickness. Sole operation. Inspect wall, cavity and linked views through MCP capture_view. ROLLBACK_NOT_GUARANTEED; unknown requires inspection before any new dependent write."
    };
}
