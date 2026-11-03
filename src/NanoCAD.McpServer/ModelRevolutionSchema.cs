using System.Text.Json.Nodes;
namespace NanoCAD.McpServer;

internal static class ModelRevolutionSchema
{
    internal static JsonObject Operation()
    {
        JsonObject Angle()=>new(){["type"]="number",["exclusiveMinimum"]=0,["maximum"]=360};
        var fields=new JsonObject{["operation"]=new JsonObject{["const"]="set_model_revolution"},["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"}},["expected_last_item_id"]=new JsonObject{["type"]="string",["pattern"]="^\\{[0-9A-Fa-f]{8}(-[0-9A-Fa-f]{4}){3}-[0-9A-Fa-f]{12}\\}$"},["expected_angle_degrees"]=Angle(),["angle_degrees"]=Angle()};
        return new(){["type"]="object",["additionalProperties"]=false,["required"]=new JsonArray(fields.Select(p=>(JsonNode?)JsonValue.Create(p.Key)).ToArray()),["properties"]=fields,
            ["description"]="Change the positive sweep of the sole retained native revolution in this body, in DEGREES through360; a positive value must remain representable in native radians. It may be the base or follow an extrusion chain. First read history; convert its angle from radians to expected_angle_degrees and provide current last_item_id. Requires existing Angle extent and a numeric, unshared native angular variable; legacy All extent is not implicitly converted. revolve_drawing_profile with explicit angle_degrees and add_model_revolution create this extent, including360. Original profile/axis and ordered history must remain linked after native rebuild. Source profiles remain in their original WCS frame. Multiple revolution features require an explicitly addressed owning editor and are outside this selector. Shared/formula-driven angle variables are protected; use set_model_parameter with expression/propagate for supported angular formulas and name_model_parameter for meaningful names; this simple setter does not overwrite formulas. Sole operation, named Millimeters Model Space document, actual-byte history/source budgets. The native result must retain the requested angle, healthy history and a finite nonempty body. No separate reconstructed model is required. ROLLBACK_NOT_GUARANTEED; a started failure remains unknown; inspect the affected model before dependent edits. Read returned feature/variable/body refs and inspect geometry plus a visual checkpoint after a meaningful group."};
    }
}
