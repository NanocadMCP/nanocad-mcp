using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;
internal static class SolidSliceSchema
{
    internal static JsonObject Separate()=>new(){
        ["type"]="object",["additionalProperties"]=false,["required"]=new JsonArray("operation","handles"),
        ["properties"]=new JsonObject{
            ["operation"]=new JsonObject{["type"]="string",["const"]="separate_solid"},
            ["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["uniqueItems"]=true,["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"}}},
        ["description"]="Separate disconnected volumes inside direct/imported Solid3d entities into individually selectable solids. No cutting, deletion or movement: ALL pieces remain in their original positions with inherited display properties. solid_separation_results maps each source to all resulting parts, including the retained source handle. A connected body returns just itself; this is a valid no-op, not proof that a feeder is detached. Sole operation in Model Space. Actual feature histories need their own route. Existing documentation follows the retained source, so rebuild its source set as needed. ROLLBACK_NOT_GUARANTEED; native mass data can require independent verification after decomposition. Distinguish this from slice_solid (an infinite plane cuts all intersected material). Before discarding any piece, evaluate its role and ALL affected geometry against the intended part; a detached set can contain wanted part fragments as well as unwanted stock."};
    internal static JsonObject Operation()
    {
        JsonObject Triple()=>new(){["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=new JsonObject{["type"]="number",["minimum"]=-1e9,["maximum"]=1e9}};
        return new(){["type"]="object",["additionalProperties"]=false,
            ["required"]=new JsonArray("operation","handles","plane_origin","plane_normal"),
            ["properties"]=new JsonObject{
                ["operation"]=new JsonObject{["type"]="string",["const"]="slice_solid"},
                ["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["uniqueItems"]=true,["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"}},
                ["plane_origin"]=Triple(),["plane_normal"]=Triple()},
            ["description"]="Split direct Solid3d bodies by an infinite WCS plane, keeping BOTH sides as closed solids. Normal is nonzero and normalized; each selected body must cross the plane. The original handle retains the positive side; solid_slice_results maps it to the new negative-side handle. Display properties are inherited. Choose the cutting plane from the manufacturing/repair intent; inspect both results before deleting unwanted material. No kerf allowance or automatic recognition of feeders/risers. Sole operation, any number of source bodies; Model Space millimeters. Real feature histories require their own model-edit route; imported nonparametric bodies are supported. Existing drawing-view links follow the retained source only, not the added half; regenerate documentation for the desired final source set. ROLLBACK_NOT_GUARANTEED. This physically splits geometry; create_section_view makes drawing documentation instead."};
    }
}
