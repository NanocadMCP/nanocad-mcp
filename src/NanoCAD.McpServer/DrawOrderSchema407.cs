using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class DrawOrderSchema407
{
    internal static JsonObject Operation()
    {
        JsonObject Handle()=>new(){["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"};
        return new JsonObject{
            ["type"]="object",["additionalProperties"]=false,
            ["properties"]=new JsonObject{
                ["operation"]=new JsonObject{["const"]="set_draw_order"},
                ["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["uniqueItems"]=true,["items"]=Handle()},
                ["position"]=new JsonObject{["type"]="string",["enum"]=new JsonArray("top","bottom","above","below")},
                ["relative_to"]=Handle()},
            ["required"]=new JsonArray("operation","handles","position"),
            ["allOf"]=new JsonArray(new JsonObject{
                ["if"]=new JsonObject{["properties"]=new JsonObject{["position"]=new JsonObject{["enum"]=new JsonArray("above","below")}},["required"]=new JsonArray("position")},
                ["then"]=new JsonObject{["required"]=new JsonArray("relative_to")},
                ["else"]=new JsonObject{["not"]=new JsonObject{["required"]=new JsonArray("relative_to")}}}),
            ["description"]="Set native drawing order of actual selected top-level entity handles in requested Model/active ordinary Paper space. top draws them after other entities, bottom before; above/below compares against relative_to in the same space. This changes the owning native DrawOrderTable without rewriting geometry, Hatch boundaries, block definitions or annotation content. Use a dedicated batch of ordered set_draw_order operations after construction/editing to keep fills behind needed notes and outlines. Output lists actual modified references; native Undo/rollback is not guaranteed."};
    }
}
