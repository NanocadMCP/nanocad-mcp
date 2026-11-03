using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class ModelRebuildSchema419
{
    internal static JsonObject Operation()=>new()
    {
        ["type"]="object",["additionalProperties"]=false,
        ["required"]=new JsonArray("operation","handles","expected_last_item_id"),
        ["properties"]=new JsonObject
        {
            ["operation"]=new JsonObject{["const"]="rebuild_model"},
            ["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,
                ["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"}},
            ["expected_last_item_id"]=new JsonObject{["type"]="string",["pattern"]="^\\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\\}$"}
        },
        ["description"]="Rebuild one existing native parametric part by its current representation handle, including a part with NeedUpdate or failed history nodes. Read expected_last_item_id from current inspect_model_history. Executes one native RebuildPart followed by the native update queue, preserving all stored suppression choices, history identities, source dependencies and End-Of-Part. Does not unsuppress, repair contradictory constraints, recreate geometry, delete features or select a UI object. Sole operation in a named Model Space drawing. model_healthy and unresolved_nodes report the observed result independently of the write outcome: a completed rebuild can still require engineering correction. A failed started attempt includes observed history diagnostics when readable; do not replay the previous unknown parameter write. Use the returned body_ref, inspect affected dimensions/relations and review the meaningful visual result. Native UpdateAll also flushes the host queue; this is not an isolated transaction. Undo/rollback is not guaranteed."
    };
}
