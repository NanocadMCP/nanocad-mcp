using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class ModelProfileElementsSchema358
{
    static JsonObject Text(string description)=>new(){["type"]="string",["description"]=description};
    static JsonObject Id(string description){var value=Text(description);value["pattern"]="^\\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\\}$";return value;}
    static JsonObject Nullable(JsonObject value){value["type"]=new JsonArray(value["type"]!.GetValue<string>(),"null");return value;}
    static JsonObject Array(JsonNode items,int min=0)=>new(){["type"]="array",["minItems"]=min,["items"]=items};
    static JsonObject Count()=>new(){["type"]="integer",["minimum"]=0};
    static JsonObject Flag()=>new(){["type"]="boolean"};
    static JsonObject Object(params (string Name,JsonNode Schema)[] values)=>new(){["type"]="object",["additionalProperties"]=false,
        ["required"]=new JsonArray(values.Select(value=>(JsonNode?)JsonValue.Create(value.Name)).ToArray()),
        ["properties"]=new JsonObject(values.Select(value=>new KeyValuePair<string,JsonNode?>(value.Name,value.Schema)))};
    static JsonObject Vector()=>new(){["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=new JsonObject{["type"]="number"}};
    static JsonObject Raw(string description)=>new(){["type"]="object",["description"]=description};
    static JsonObject Token()=>new(){["type"]="string",["pattern"]="^[0-9A-Fa-f]{64}$"};
    internal static JsonObject HistoryInput()=>new(){["type"]="array",["minItems"]=1,["uniqueItems"]=true,["items"]=Id("Exact actual-owner native profile ID."),
        ["description"]="Optional targeted element metadata for unused same-owner construction or a consumed profile of one selected existing part. Reports actual native element IDs, geometry, source links, full selected boundaries, constraint observation and fresh element_token. Omitted by default; no new document/geometry scan in ordinary history. Actual owner is distinct from the active drawing lookup container."};
    internal static JsonObject Operation()
    {
        var result=Object(("operation",new JsonObject{["const"]="edit_model_profile_elements"}),
            ("handles",new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=Text("Current existing native part handle.")}),
            ("profile_id",Id("Current actual-owner native profile.")),("expected_sketch_id",Id("Exact retained native parent.")),
            ("expected_element_token",Token()),("expected_last_item_id",Id("Current selected body last_item_id.")),
            ("remove_element_ids",new JsonObject{["type"]="array",["minItems"]=1,["uniqueItems"]=true,["items"]=Id("Actual Line/Arc/Circle sketch element to remove from native membership; never a source DWG handle.")}),
            ("add_source_handles",new JsonObject{["type"]="array",["uniqueItems"]=true,["items"]=Text("Optional distinct committed coplanar Line/Arc/Circle drawing sources, retained without coordinate reconstruction.")}),
            ("region_points",Array(new JsonObject{["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=new JsonObject{["type"]="number",["minimum"]=-1_000_000,["maximum"]=1_000_000}},1)));
        result["required"]=new JsonArray("operation","handles","profile_id","expected_sketch_id","expected_element_token","expected_last_item_id");
        result["anyOf"]=new JsonArray(new JsonObject{["required"]=new JsonArray("remove_element_ids")},new JsonObject{["required"]=new JsonArray("add_source_handles"),
            ["properties"]=new JsonObject{["add_source_handles"]=new JsonObject{["minItems"]=1}}});
        result["description"]="Edit native Line/Arc/Circle element membership in the SAME retained sketch/profile: exact removals and/or new real source projections. Removal targets actual projections or abstract sketch geometry IDs; directly included resident nonprojection objects need their owning retirement adapter. Preserve original DWG curves, profile/parent/consumer IDs, frame/support and existing MCS drivers. A single surviving complete simple Line/Arc contour or Circle is selected natively when region_points is omitted; separated/nested whole Circle cells require one distinct interior WCS point per desired region. Consume unused construction later through the existing once-only extrusion route; consumed profiles currently require one actual extrusion consumer with its supported downstream history. Native object retirement is observed, not forced or advertised as source erase/release/reuse. Shared profiles/sketches, listed constraints on removed elements, general polyline/spline/point geometry and sweep/loft ownership adapters are separate. Unobservable private constraints are disclosed. Recovery/Undo are not guaranteed.";
        return result;
    }
    internal static JsonObject ReadOutput(JsonNode reference)=>Object(
        ("profile_id",Id("Actual profile.")),("parent_sketch_id",Id("Real parent.")),("mc_document_id",Id("Actual native owner; may differ from history's active drawing container.")),
        ("feature_id",Nullable(Id("Actual forming backlink, null for unused construction."))),("element_token",Token()),
        ("plane",Object(("origin_wcs",Vector()),("x_axis_wcs",Vector()),("y_axis_wcs",Vector()),("normal_wcs",Vector()))),
        ("elements",Array(Object(("element_id",Id("Actual native sketch element identity, including abstract geometry IDs.")),("geometry_type",Text("line or circular_arc.")),
            ("source_object_id",Nullable(Id("Actual projection source, null for internal geometry."))),("source_ref",new JsonObject{["anyOf"]=new JsonArray(reference.DeepClone(),new JsonObject{["type"]="null"})}),
            ("geometry",Raw("Actual WCS Line/Arc/Circle geometry from the native sketch reader."))))),
        ("selected_profile_geometry",Array(Raw("Native selected closed outer/inner boundaries; array order is observation, not durable boundary IDs."),1)),
        ("constraint_observation",ModelSketchConstraintObservationSchema353.Read()),("scope",Text("Exact ownership/geometry and explicit observation scope.")));
    internal static JsonObject ChangeOutput(JsonNode reference)=>Object(
        ("operation_index",Count()),("source",reference.DeepClone()),("body_ref",reference.DeepClone()),("body_mc_id",Id("Same native body.")),
        ("profile_id",Id("Same native profile.")),("parent_sketch_id",Id("Same native parent.")),("mc_document_id",Id("Same actual owner.")),
        ("feature_id",Nullable(Id("Retained extrusion or null unused forming backlink."))),("element_token",Token()),("last_item_id",Id("Retained body terminal.")),
        ("source_representation_replaced",Flag()),("removed_elements",Array(Object(("element_id",Id("Exact requested element.")),("source_object_id",Nullable(Id("Retained actual source if projected."))),
            ("retained_source_ref",new JsonObject{["anyOf"]=new JsonArray(reference.DeepClone(),new JsonObject{["type"]="null"})}),
            ("geometry_present",Flag()),("containing_membership",Flag()),("body_membership",Flag()),("document_projection_membership",Flag()),
            ("observed_host_sketch_id",Nullable(Id("Actual resident projection host, not a requested/reset host."))),
            ("observed_host_solid_id",Nullable(Id("Actual resident projection body host."))),("disposition",Text("Actual inventory/membership observation; no physical erase/release/reusability claim."))))),
        ("added_source_refs",Array(reference.DeepClone())),("added_projection_ids",Array(Id("Real fresh native projection IDs."))),
        ("constraint_observation",Object(("document_id",Id("Actual owner.")),("sketch_id",Id("Actual sketch.")),("scope_id",Id("Actual manager scope.")),("network",Text("Reported manager network.")),
            ("solver_status",Text("not_evaluated")),("constraints_complete",Flag()),("constraint_count",Nullable(Count())),("constraints_sha256",Nullable(Token())),("inventory_scope",Text("Summary only; full inventory in optional read; unavailable is not empty.")))),
        ("selected_region_count",Count()),("selected_hole_count",Count()),("selected_geometry_sha256",Token()),("scope",Text("Retained design/source and honest private-constraint/native-retirement boundaries.")));
}
