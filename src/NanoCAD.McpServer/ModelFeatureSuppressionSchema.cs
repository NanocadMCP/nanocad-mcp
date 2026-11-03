using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class ModelFeatureSuppressionSchema
{
    internal static JsonObject Operation()=>new()
    {
        ["type"]="object",["additionalProperties"]=false,
        ["required"]=new JsonArray("operation","handles","expected_last_item_id","feature_id","expected_suppressed","suppressed"),
        ["properties"]=new JsonObject
        {
            ["operation"]=new JsonObject{["const"]="set_model_feature_suppressed"},
            ["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,
                ["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"}},
            ["expected_last_item_id"]=new JsonObject{["type"]="string",["pattern"]="^\\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\\}$"},
            ["feature_id"]=new JsonObject{["type"]="string",["pattern"]="^\\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\\}$"},
            ["expected_suppressed"]=new JsonObject{["type"]="boolean"},
            ["suppressed"]=new JsonObject{["type"]="boolean"}
        },
        ["description"]="Suppress or restore one selected native model feature, including a nonterminal feature, without deleting its history. No dependent suppression cascade is requested: unselected suppression choices, End-Of-Part, source/profile links and driving parameters remain retained while affected derived geometry rebuilds. Native safe-restoration eligibility is checked before unsuppressing. NeedUpdate is accepted only in the selected feature's actual same-part dependent closure for this recovery; Error/Unknown/Erased, foreign ownership and unrelated pending nodes remain protected. Supported feature types: join/cut extrusion, revolution, sweep, loft, hole, thread, rectangular/circular pattern, mirror, fillet, chamfer or shell. Suppression requires a different healthy active additive extrusion/revolution/sweep/loft to retain the solid base; a cut does not establish a base. Exact expected suppression and history IDs must match. Existing supported associative drawing views may follow the model feature and are checked after rebuild. Sole operation; inspect the resulting shape and views through MCP capture_view. ROLLBACK_NOT_GUARANTEED; unknown requires inspection before dependent writes."
    };
}
