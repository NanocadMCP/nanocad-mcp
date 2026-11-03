using System.Text.Json.Nodes;
namespace NanoCAD.McpServer;
internal static class SketchConstraintSchema
{
    internal static JsonObject Operation()=>new()
    {
        ["type"]="object",["additionalProperties"]=false,["required"]=new JsonArray("operation","handles","sketch_id","expected_binding_token","constraints"),
        ["properties"]=new JsonObject{
            ["operation"]=new JsonObject{["const"]="add_sketch_constraints"},
            ["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"}},
            ["sketch_id"]=new JsonObject{["type"]="string",["pattern"]="^\\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\\}$"},
            ["expected_binding_token"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{64}$"},
            ["constraints"]=new JsonObject{["type"]="array",["minItems"]=1,["items"]=NativeSketchSchema385.Constraint()}},
        ["description"]="Create native geometric relations or dimensional controls on the selected internal retained sketch, then solve and rebuild its dependent part. Read inspect_model_history(sketch_ids).native_inputs for exact current member IDs, independently_owned/reference_only roles and binding_token; polyline segments use returned SDK native member IDs. At least one paired operand must be owned. Own Line/Circle/Arc inputs can move. fix fixes one complete owned Line/Circle/Arc, including size, without a point selector; fix_point fixes only the chosen endpoint/center. symmetric relates two Lines or two Circles about a separate actual Line axis supplied as third; its receipt returns third_element_id. For fix_point(center) on an existing owned Circle with an actual retained radial driver, the native editor must preserve that driver and return the requested persistent relation. Native editing may renew member/relation IDs; use returned current references. Known initial constraints should be declared during sketch creation. Other relation and dimensional editing routes retain their separate scopes. Proved same-sketch projections are immutable reference-only operands for relations to owned geometry; their geometry/source/datum data are retained, and a dimensional control of projected geometry uses its owning source route. Horizontal/vertical/x/y follow the actual sketch frame, distances millimeters and angles radians. Native service relations, fixed anchors, existing controls/formulas and typed profile/feature consumers remain retained. Sole operation; returns actual relation/variable IDs. A stored value or VarId alone does not establish a driving effect: assess the meaningful intended change against actual sketch/dependent geometry. No global fully-constrained or Undo/rollback guarantee."
    };
}
