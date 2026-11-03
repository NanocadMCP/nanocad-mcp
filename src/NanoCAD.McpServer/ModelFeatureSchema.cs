using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class ModelFeatureSchema
{
    private static JsonObject Number(double min,double max)=>new(){["type"]="number",["minimum"]=min,["maximum"]=max};
    private static JsonObject Point(int count,double bound=1e6)=>new(){["type"]="array",["minItems"]=count,["maxItems"]=count,["items"]=Number(-bound,bound)};
    private static JsonObject Object(JsonObject properties,params string[] required)=>new(){["type"]="object",["additionalProperties"]=false,["properties"]=properties,["required"]=new JsonArray(required.Select(p=>(JsonNode?)JsonValue.Create(p)).ToArray())};
    internal static JsonObject Operation()
    {
        var definition=Object(new JsonObject{
            ["plane"]=Object(new JsonObject{["origin"]=Point(3),["x_axis"]=Point(3,1),["y_axis"]=Point(3,1)},"origin","x_axis","y_axis"),
            ["profile"]=new JsonObject{["oneOf"]=new JsonArray(
                Object(new JsonObject{["kind"]=new JsonObject{["const"]="circle"},["center"]=Point(2),["radius"]=Number(.001,1e6)},"kind","center","radius"),
                Object(new JsonObject{["kind"]=new JsonObject{["const"]="polygon"},["vertices"]=new JsonObject{["type"]="array",["minItems"]=3,["items"]=Point(2)}},"kind","vertices"),ModelPolycurveSchema.Variant())},
            ["distance"]=Number(.001,1e6),["direction"]=new JsonObject{["enum"]=new JsonArray("positive","negative")},
            ["name"]=new JsonObject{["type"]="string",["minLength"]=1,["maxLength"]=64},["editable_profile"]=new JsonObject{["type"]="boolean",["description"]="Retain hidden source curves for later set_model_sketch_geometry edits of this feature; default false."}},"plane","profile","distance","direction");
        var operation=Object(new JsonObject{
            ["operation"]=new JsonObject{["const"]="add_model_extrusion"},
            ["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"}},
            ["source_handles"]=new JsonObject{["type"]="array",["minItems"]=1,["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"},
                ["description"]="Optional exact Model Space Circle or closed Line/Arc contour. Sources must geometrically match definition.profile and remain real drawing objects; editable_profile must be false."},
            ["expected_last_item_id"]=new JsonObject{["type"]="string",["pattern"]="^\\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\\}$"},
            ["part_operation"]=new JsonObject{["enum"]=new JsonArray("join","cut")},["definition"]=definition},"operation","handles","expected_last_item_id","part_operation","definition");
        operation["description"]="An inline contour is materialized as independently owned native sketch curves through the sketch editor, then its declared closed contour is selected; it does not create hidden external drawing sources or invent constraints. Retained drawing-source and reusable-profile workflows remain separate. Append a native PlanarSketch/profile/ExtrudeFeature to one existing Mc3dSolid history: join adds material; cut makes a pocket/hole. Sole operation in the request, named Model Space millimeter document, MultiCAD already loaded. Read current last_item_id from inspect_model_history and pass it exactly. Definition uses orthonormal WCS plane axes, local profile millimeters, circle, simple polygon or exact closed Line/Arc polycurve, and signed-normal direction; no holes/taper in the profile. Explicit plane, no face attachment or sketch constraints. Inherit body layer. The actual native result must retain feature ownership, parameters, prior history and a finite nonempty body with material added or removed as requested. No separate boolean comparison model is required. A healthy terminal group of associative drawing views can follow the body: the new feature is inserted before that group using the native end-of-part position. View identities and source links are retained; sections, broken views and multiple unrelated roots are outside this insertion path. Result model_history_changes identifies sketch/profile/feature/distance variable and current body_ref; a replaced native representation is explicit. Native rebuild may update dependents, so refresh history/topology/views rather than assuming unchanged handles. ROLLBACK_NOT_GUARANTEED: failed started append may leave partial history; unknown outcome requires inspection of the affected model history. No automatic Undo.";
        return operation;
    }
}

