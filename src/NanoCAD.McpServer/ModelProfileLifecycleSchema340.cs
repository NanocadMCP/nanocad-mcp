using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

public static class ModelProfileLifecycleSchema340
{
    static JsonObject Text()=>new(){["type"]="string",["minLength"]=1,["maxLength"]=64};
    static JsonObject Id()=>new(){["type"]="string",["pattern"]="^\\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\\}$"};
    static JsonObject Token()=>new(){["type"]="string",["pattern"]="^[0-9A-Fa-f]{64}$"};
    static JsonObject Handle()=>new(){["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"};
    static JsonObject Object(JsonObject properties,params string[] required)=>new(){["type"]="object",["additionalProperties"]=false,["properties"]=properties,["required"]=new JsonArray(required.Select(s=>(JsonNode?)JsonValue.Create(s)).ToArray())};
    static JsonObject Base(string operation)=>new(){["operation"]=new JsonObject{["const"]=operation},["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=Handle()},["expected_last_item_id"]=Id(),["name"]=Text()};
    public static JsonArray Operations()
    {
        var create=Base("create_model_profile");create["source_handles"]=new JsonObject{["type"]="array",["minItems"]=1,["uniqueItems"]=true,["items"]=Handle()};create["plane"]=WorkplaneSchema.Input();
        var c=Object(create,"operation","handles","expected_last_item_id","source_handles","plane");
        c["description"]="Create a reusable native PlanarSketch/SketchProfile in the SAME DOCUMENT as one explicit EXISTING healthy named Millimeters Model Space part. Select committed coplanar native Circle or a complete closed Line/Arc contour and its explicit plane. Preserve drawing sources as real projections; do not recreate or consume them. Unused construction can be document-owned: result profile_ownership reports actual native hosts, and body_ref identifies the selected context without asserting construction membership. This creates no forming feature, new empty body, internal driving constraints, hole loop or source deletion. Read current last_item_id first. Result gives actual sketch/profile IDs and fresh profile token. Read exact profile_ids via inspect_model_history; optional include_document_profiles discovers identity pages. Native rollback/Undo is not guaranteed; started failures may leave partial document construction.";
        var extrude=Base("add_model_extrusion_from_profile");extrude["profile_id"]=Id();extrude["expected_sketch_id"]=Id();extrude["expected_profile_token"]=Token();extrude["part_operation"]=new JsonObject{["enum"]=new JsonArray("join","cut")};extrude["distance"]=new JsonObject{["type"]="number",["exclusiveMinimum"]=0,["maximum"]=1e6};extrude["direction"]=new JsonObject{["enum"]=new JsonArray("positive","negative")};
        var e=Object(extrude,"operation","handles","expected_last_item_id","profile_id","expected_sketch_id","expected_profile_token","part_operation","distance","direction");
        e["description"]="Append a native distance ExtrudeFeature join/cut to one explicit EXISTING body using an exact SAME-DOCUMENT SketchProfile and its retained parent/source dependencies. Read document_profiles through inspect_model_history profile_ids: reusable=true, profile_editing.profile_token, parent_sketch_id, oriented plane and actual ownership. Use current body last_item_id; expected_sketch_id must match the profile parent. Native host affinity must be absent or match the selected body; another body's construction is refused. The exact profile is passed to AddExtrudeFeature; existing same-part forward consumers remain retained and foreign-body construction is not transferred. Result verifies actual selected-body composition, native hosts and forming owner. No sketch, profile or projection is recreated. One actual native bounded selected area with Line/Arc/Circle outer and inner loops, including retained holes; not disconnected or multiple selected areas. Select the hole profile through create_model_profile_on_sketch; the complete native outer/inner selection is consumed, never just its outer circle. No taper or other end condition. Distance is drawing millimeters; direction is relative to the reported plane normal. Existing formulas, source identities and supported terminal drawing-view followers remain. Inspect the useful native result group. Sole operation, named Model Space part, native rollback/Undo not guaranteed.";
        return new(c,e);
    }
    static JsonObject Vector()=>new(){["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=new JsonObject{["type"]="number"}};
    static JsonObject NullableId()=>new(){["anyOf"]=new JsonArray(Id(),new JsonObject{["type"]="null"})};
    static JsonObject OwnerDocumentId()
    {
        var value=Id();value["description"]="The selected body's actual native owner document, admitted through its current drawing representation. It can differ from the drawing-container mc_document_id in ordinary body history.";return value;
    }
    public static JsonObject OwnershipOutput()=>Object(new JsonObject{
        ["mc_document_id"]=OwnerDocumentId(),["profile_host_solid_id"]=NullableId(),["sketch_host_solid_id"]=NullableId(),
        ["scope"]=new JsonObject{["enum"]=new JsonArray("document_construction","native_host_affinity")}
    },"mc_document_id","profile_host_solid_id","sketch_host_solid_id","scope");
    public static JsonObject HistoryInputProperties()=>new(){
        ["include_document_profiles"]=new JsonObject{["type"]="boolean",["default"]=false,["description"]="Opt in to read-only native profile identity discovery in the selected bodies' one unambiguous actual native owner document, including unused construction. Its ID can differ from ordinary history's drawing container. Multiple bodies sharing that native scope are allowed; differing owner scopes require choosing the intended scope. Identity pages use actual128KiB serialized item bytes; targeted full geometry is requested separately with profile_ids. Omit for ordinary body history."},
        ["document_profile_cursor"]=new JsonObject{["type"]="string",["pattern"]=Id()["pattern"]!.DeepClone(),["description"]="The preceding document_profile_inventory.next_cursor at this same document revision. Requires include_document_profiles=true."},
        ["profile_ids"]=new JsonObject{["type"]="array",["minItems"]=1,["uniqueItems"]=true,["items"]=Id(),["description"]="Exact current same-document SketchProfile IDs from a creation result or document profile identity page. Returns full document_profiles metadata, actual parent/host/source/plane/selected contour and fresh profile token and actual forward-consumer inventory. Native IDs are not DWG handles. Ordinary history omits this read."}
    };
    public static JsonObject InventoryOutput()=>Object(new JsonObject{
        ["mc_document_id"]=OwnerDocumentId(),["scope"]=new JsonObject{["const"]="same_document_native_profile_identities_not_body_composition"},
        ["items"]=new JsonObject{["type"]="array",["items"]=Object(new JsonObject{["profile_id"]=Id(),["parent_sketch_id"]=NullableId(),["profile_host_solid_id"]=NullableId(),["dependent_feature_id"]=NullableId()},"profile_id","parent_sketch_id","profile_host_solid_id","dependent_feature_id")},
        ["complete"]=new JsonObject{["type"]="boolean"},["next_cursor"]=NullableId()
    },"mc_document_id","scope","items","complete","next_cursor");
    public static JsonObject ReadProperties()=>Object(new JsonObject{
        ["kind"]=new JsonObject{["const"]="profile"},["parent_sketch_id"]=NullableId(),["profile_id"]=Id(),["ownership"]=OwnershipOutput(),["support_id"]=Id(),
        ["profile_editing"]=Object(new JsonObject{["reusable"]=new JsonObject{["type"]="boolean"},["profile_token"]=Token(),["unavailable_reason"]=new JsonObject{["type"]="string"}},"reusable"),
        ["plane"]=Object(new JsonObject{["origin_wcs"]=Vector(),["x_axis_wcs"]=Vector(),["y_axis_wcs"]=Vector(),["normal_wcs"]=Vector()},"origin_wcs","x_axis_wcs","y_axis_wcs","normal_wcs"),
        ["source_object_ids"]=new JsonObject{["type"]="array",["items"]=Id()},["projection_ids"]=new JsonObject{["type"]="array",["items"]=Id()},
        ["forward_consumers"]=new JsonObject{["type"]="array",["items"]=Object(new JsonObject{
            ["feature_id"]=Id(),["kind"]=new JsonObject{["type"]="string"},["body_mc_id"]=Id(),["profile_ids"]=new JsonObject{["type"]="array",["items"]=Id()}
        },"feature_id","kind","body_mc_id","profile_ids"),["description"]="Observed actual forward native feature consumers in this owner, including shared use of the selected profile. dependent_feature_id is a raw singular SDK backlink, not a complete consumer inventory."},
        ["dependent_feature_id"]=Id(),["contour_geometry"]=new JsonObject{["type"]="object",["description"]="Actual selected SketchProfile.GetContours(false) WCS boundary. Simple contour returns elements; a selected area with holes returns areas containing actual outer/holes. The specialized separated/nested whole-Circle parent adapter also reports current sketch outer_element_id/hole_element_ids. A mixed parent sketch, including unrelated open references, uses selected native contours and boundary order only; those member-ID fields are not supplied. Ordered segment element_id labels are geometry indices, never native member aliases. Read inspect_model_history with sketch_ids for document_sketches[].native_inputs or current constraint references to obtain relation member IDs. This exact single selected area can feed retained-profile extrusion, sweep or revolution, or one ordered section of a retained-profile loft, preserving its complete actual outer/inner native contours and scoped consumers."}
    },"kind","parent_sketch_id","profile_editing");
    public static JsonObject ChangeOutput(JsonObject entityRef)
    {
        var p=new JsonObject{["operation_index"]=new JsonObject{["type"]="integer",["minimum"]=0},["operation"]=new JsonObject{["enum"]=new JsonArray("create_model_profile","add_model_extrusion_from_profile")},["source"]=entityRef.DeepClone(),["body_ref"]=entityRef.DeepClone(),["body_mc_id"]=Id(),["sketch_id"]=Id(),["profile_id"]=Id(),["profile_token"]=Token(),["last_item_id"]=Id(),["source_refs"]=new JsonObject{["type"]="array",["items"]=entityRef.DeepClone()},["source_representation_replaced"]=new JsonObject{["type"]="boolean"},["feature_id"]=Id(),["distance_variable_id"]=Id(),["profile_ownership"]=OwnershipOutput()};
        return Object(p,"operation_index","operation","source","body_ref","body_mc_id","sketch_id","profile_id","profile_token","last_item_id","source_refs","source_representation_replaced");
    }
}
