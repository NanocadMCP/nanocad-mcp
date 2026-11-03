using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class ModelSketchLifecycleSchema362
{
    static JsonObject Id()=>new(){["type"]="string",["pattern"]="^\\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\\}$"};
    static JsonObject Handle()=>new(){["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"};
    static JsonObject Token()=>new(){["type"]="string",["pattern"]="^[0-9A-Fa-f]{64}$"};
    static JsonObject Array(JsonObject items,int minimum=0)=>new(){["type"]="array",["minItems"]=minimum,["items"]=items};
    static JsonObject Ids()=>new(){["type"]="array",["minItems"]=1,["uniqueItems"]=true,["items"]=Id()};
    static JsonObject Object(JsonObject properties,params string[] required)=>new(){["type"]="object",["additionalProperties"]=false,["properties"]=properties,["required"]=new JsonArray(required.Select(value=>(JsonNode?)JsonValue.Create(value)).ToArray())};
    static JsonObject Nullable(JsonObject value)=>new(){["anyOf"]=new JsonArray(value,new JsonObject{["type"]="null"})};
    static JsonObject Vector()=>new(){["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=new JsonObject{["type"]="number"}};
    static JsonObject Any()=>new(){["type"]="object",["additionalProperties"]=true};
    static JsonObject Base(string operation)=>new(){["operation"]=new JsonObject{["const"]=operation},["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=Handle()},["expected_last_item_id"]=Id(),["name"]=new JsonObject{["type"]="string",["minLength"]=1,["maxLength"]=64}};
    static JsonObject NativeFrame()=>Object(new JsonObject{
        ["origin_wcs"]=Vector(),["x_axis_wcs"]=Vector(),["y_axis_wcs"]=Vector(),["normal_wcs"]=Vector()
    },"origin_wcs","x_axis_wcs","y_axis_wcs","normal_wcs");
    static JsonObject SupportInput()
    {
        var face=Object(new JsonObject{
            ["kind"]=new JsonObject{["const"]="face"},["face_id"]=Id(),["face_token"]=Token()
        },"kind","face_id","face_token");
        face["properties"]!["face_id"]!["description"]="Current native model_face_id of a planar face on the selected part from inspect_solid_topology(include_native_model_faces=true); not a B-rep face ID or drawing handle.";
        face["properties"]!["face_token"]!["description"]="Current face_token from that same native_model_faces read, with the same body/document/revision.";
        var workplane=Object(new JsonObject{
            ["kind"]=new JsonObject{["const"]="workplane"},["plane_id"]=Id(),["expected_datum_token"]=Token()
        },"kind","plane_id","expected_datum_token");
        workplane["properties"]!["plane_id"]!["description"]="Actual healthy active native WorkPlane plane_id from inspect_model_history datum_plane_ids, in the selected part's actual owning document. Fixed/retained construction may remain document-owned; existing part affinity and all native dependencies must be compatible with this selected part. No foreign-part datum transfer or owner reassignment is performed.";
        workplane["properties"]!["expected_datum_token"]!["description"]="Current datum_planes[].datum_token for that exact plane, read with the same body/document/revision.";
        return new JsonObject{["type"]="object",["oneOf"]=new JsonArray(face,workplane),["description"]="Bind the native sketch to a same-part planar face or retained WorkPlane in the selected part's actual owner. Native host/dependency scope and the physical support are checked; a reusable document datum remains construction, not implied part membership. Supply support instead of plane. Input WCS geometry must lie in the physical plane; the observed native sketch frame defines axis-based relations and is returned in native_support.native_frame."};
    }
    static JsonObject NativeSupportOutput()
    {
        var value=Object(new JsonObject{
            ["kind"]=new JsonObject{["enum"]=new JsonArray("face","workplane")},["support_id"]=Id(),
            ["source_token"]=Token(),["source_body_id"]=Id(),["mc_document_id"]=Id(),["native_frame"]=NativeFrame(),
            ["support_host_solid_id"]=Id(),["sketch_host_solid_id"]=Id()
        },"kind","support_id","source_token","source_body_id","mc_document_id","native_frame");
        value["description"]="Observed native support in the selected part's owner. source_body_id identifies the selected scoped part; it does not assert ownership of a reusable construction plane. Optional support_host_solid_id/sketch_host_solid_id report actual nonempty native hosts when observed; absent host does not imply part membership. source_token is the current face/datum token. native_frame is the actual sketch frame after binding, not a promised copy of face/WorkPlane axes. Omitted for raw standalone frames or unavailable supported read scope.";
        return value;
    }
    public static JsonArray Operations()
    {
        var create=Base("create_model_sketch");
        create["source_handles"]=new JsonObject{["type"]="array",["minItems"]=1,["uniqueItems"]=true,["items"]=Handle()};
        create["reference_ids"]=Ids();create["reference_ids"]!["description"]="Exact current native same-part geometry IDs from inspect_solid_topology with native model edges. These are linked native references, not B-rep IDs, source drawing handles or invented geometry IDs. The write verifies actual selected-body membership, supported geometry and upstream dependency scope before creating projections.";
        create["plane"]=WorkplaneSchema.Input();
        create["plane"]!["description"]="Legacy explicit WCS frame for standalone construction in the selected part's actual owner document. This does not assign part membership or a native face dependency. Supply plane instead of support.";
        create["support"]=SupportInput();
        create["native_geometry"]=NativeSketchSchema385.Geometry();create["initial_constraints"]=NativeSketchSchema385.InitialConstraints(allowReferenceIds:true);
        var c=Object(create,"operation","handles","expected_last_item_id");
        c["allOf"]=new JsonArray(new JsonObject{["oneOf"]=new JsonArray(
            new JsonObject{["required"]=new JsonArray("plane"),["not"]=new JsonObject{["required"]=new JsonArray("support")}},
            new JsonObject{["required"]=new JsonArray("support"),["not"]=new JsonObject{["required"]=new JsonArray("plane")}})},
            new JsonObject{["anyOf"]=new JsonArray(new JsonObject{["required"]=new JsonArray("source_handles")},new JsonObject{["required"]=new JsonArray("reference_ids")},new JsonObject{["required"]=new JsonArray("native_geometry")})},
            new JsonObject{["if"]=new JsonObject{["required"]=new JsonArray("initial_constraints")},["then"]=new JsonObject{["required"]=new JsonArray("native_geometry")}});
        c["description"]="Create a retained native PlanarSketch for one existing Millimeters Model part from committed coplanar drawing source_handles, same-part native reference_ids, independently owned native_geometry, or a combination. Choose exactly one plane or support: a raw WCS plane retains standalone document construction; support selects a same-part planar native face or retained healthy WorkPlane in the actual owner document, with current face/datum tokens. Document construction is not implied part membership. Projections retain their actual source/datum references as reference-only geometry. Native Line/Circle/three-point Arc, complete clamped NURBS Spline and Ellipse inputs use WCS points in the observed support plane; optional initial_constraints use own input geometry keys or exact braced source GUIDs supplied in reference_ids, with at least one owned operand. Each source GUID binds its real newly created same-sketch projection; no drawing handle, guessed projection ID or invented alias is accepted. Linear controls use millimeters and angles radians. Native initialization retains service geometry/relations separately and applies the requested native relations together; it does not promise zero remaining degrees of freedom. Owned-to-projection relations can be initialized together using those exact reference_ids operands. For later relation additions, read current native_inputs member IDs/binding_token and use add_sketch_constraints. No profile or forming feature is created. Read current last_item_id before creation, then sketch_ids for actual geometry, source/peer-profile IDs, support/frame and fresh token. When changing a driver, assess actual sketch/dependent geometry before claiming its observed effect; create-only identities do not show that later propagation was observed. Sources remain; sole operation, uncertain Undo/rollback, and started failures can leave partial construction.";
        var select=Base("create_model_profile_on_sketch");select["sketch_id"]=Id();select["expected_sketch_token"]=Token();select["region_point"]=new JsonObject{["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=new JsonObject{["type"]="number",["minimum"]=-1e6,["maximum"]=1e6}};
        var p=Object(select,"operation","handles","expected_last_item_id","sketch_id","expected_sketch_token","region_point");p["description"]="Create another real unused SketchProfile on the exact retained same-owner sketch. Read its current sketch_ids token and choose one unambiguous region interior in WCS. Selects exactly one actual native bounded area with completely observed native Line/Arc/Circle/Spline/Ellipse outer and hole contours. Choose a point in material, outside holes; disconnected/multiple areas and an adjacent-cell union are not inferred. Native SDK region membership, including holes, determines the selected area; no faceted substitute is used. Native selection retains the exact ParentSketchID and all existing peer profiles/sources. Reuse an already-used sketch through a new profile ID; an existing consumed profile is never transferred. Returns fresh profile_id/profile_token. Selected profiles use add_model_extrusion_from_profile distance join/cut, typed sweep, retained-profile revolution or an ordered section in add_model_loft_from_profiles; these retain the complete native selected area and its parent sketch. Sole operation; partial new profile can remain on started failure.";
        var sweep=Base("add_model_sweep_from_profile");sweep["profile_id"]=Id();sweep["expected_sketch_id"]=Id();sweep["expected_profile_token"]=Token();sweep["path_handles"]=new JsonObject{["type"]="array",["minItems"]=1,["uniqueItems"]=true,["items"]=Handle()};sweep["orientation"]=new JsonObject{["enum"]=new JsonArray("normal_to_path","parallel_to_profile")};sweep["taper_angle"]=new JsonObject{["type"]="number",["exclusiveMinimum"]=-Math.PI/2+1e-6,["exclusiveMaximum"]=Math.PI/2-1e-6};sweep["part_operation"]=new JsonObject{["enum"]=new JsonArray("join","cut")};
        var s=Object(sweep,"operation","handles","expected_last_item_id","profile_id","expected_sketch_id","expected_profile_token","path_handles","orientation","taper_angle","part_operation");s["description"]="Append a typed native ExtrudeAlongPathFeature join/cut to the existing part using one exact same-owner profile containing one native Line/Arc/Circle/Spline/Ellipse selected area with its retained holes, and ordered retained native drawing path curves. profile_ids supplies actual parent/plane/host and fresh token; the profile can be document construction before native consumption. Existing selected-part forward consumers are retained; no profile recreation or foreign host transfer. Native sweep must prove actual selected-body profile/sketch/projection composition, forming owner, path/driver bindings and requested orientation/taper (radians; owning variable degrees). Retains peer profiles, original sources, existing parameters and supported drawing followers. Inspect the useful final group. Sole operation; native Undo/rollback not guaranteed.";
        var revolve=Base("add_model_revolution_from_profile");
        revolve["profile_id"]=Id();revolve["expected_sketch_id"]=Id();revolve["expected_profile_token"]=Token();
        revolve["part_operation"]=new JsonObject{["enum"]=new JsonArray("join","cut")};revolve["axis_handle"]=Handle();
        revolve["angle_degrees"]=new JsonObject{["type"]="number",["exclusiveMinimum"]=0,["maximum"]=360};
        var r=Object(revolve,"operation","handles","expected_last_item_id","profile_id","expected_sketch_id","expected_profile_token","part_operation","axis_handle","angle_degrees");
        r["description"]="Append a native RevolveFeature join/cut from the exact retained profile, preserving its parent sketch, selected area, sources and existing selected-part consumers. Read current profile_id/parent/profile_token and body last_item_id. axis_handle is a committed nondegenerate coplanar native Line, retained as an associative axis source; angle_degrees is positive up to360 about its stored direction. No replacement sketch/profile or manual tessellation. Document construction or same-part profile reuse is admitted through actual forward consumers and native ownership; another part's construction is not transferred. Read the returned body_ref, feature/angle_variable_id and refreshed profile consumers, then review intended geometry. Sole operation; native Undo/rollback not guaranteed; unknown is not success.";
        return new(c,p,s,r);
    }
    public static JsonObject HistoryInputProperties()=>new(){
        ["sketch_ids"]=SketchIds(),["include_document_sketches"]=new JsonObject{["type"]="boolean",["default"]=false,["description"]="Read optional native sketch identity pages in the selected parts' one shared actual owner document, including standalone construction. Omitted by default. Pages use128KiB actual serialized item bytes; no sketch/body count ceiling."},
        ["document_sketch_cursor"]=SketchCursor()
    };
    static JsonObject SketchIds(){var value=Ids();value["description"]="Exact native sketch IDs from creation or document sketch identity pages. Returns actual geometry/frame/source/peer-profile identities, observed constraints and fresh sketch_token. The native owner can differ from the DWG lookup container; nonnull host affinity must be the selected actual part. No edit mode, solver or mutation.";return value;}
    static JsonObject SketchCursor(){var value=Id();value["description"]="The previous document_sketch_inventory.next_cursor under this same document revision. Requires include_document_sketches=true.";return value;}
    public static JsonObject ReadOutput()=>Object(new JsonObject{
        ["sketch_id"]=Id(),["mc_document_id"]=Id(),["host_solid_id"]=Nullable(Id()),["support_id"]=Nullable(Id()),["sketch_token"]=Token(),
        ["plane"]=NativeFrame(),["native_support"]=NativeSupportOutput(),
        ["geometry"]=Any(),["profile_ids"]=Array(Id()),["projection_ids"]=Array(Id()),["source_object_ids"]=Array(Id()),["constraint_observation"]=ModelSketchConstraintObservationSchema353.Read(),
        ["scope"]=new JsonObject{["enum"]=new JsonArray("document_construction","native_host_affinity")},["geometry_bindings"]=ModelSketchGeometryBindingsSchema365.Read(),["native_inputs"]=NativeSketchSchema385.Read()
    },"sketch_id","mc_document_id","host_solid_id","support_id","sketch_token","plane","geometry","profile_ids","projection_ids","source_object_ids","constraint_observation","scope","geometry_bindings","native_inputs");
    public static JsonObject InventoryOutput()=>Object(new JsonObject{
        ["mc_document_id"]=Id(),["scope"]=new JsonObject{["const"]="same_document_native_sketch_identities_not_body_composition"},
        ["items"]=Array(Object(new JsonObject{["sketch_id"]=Id(),["host_solid_id"]=Nullable(Id()),["name"]=new JsonObject{["type"]="string"}},"sketch_id","host_solid_id","name")),
        ["complete"]=new JsonObject{["type"]="boolean"},["next_cursor"]=Nullable(Id())
    },"mc_document_id","scope","items","complete","next_cursor");
    public static JsonObject ChangeOutput(JsonObject reference)=>Object(new JsonObject{
        ["operation_index"]=new JsonObject{["type"]="integer",["minimum"]=0},["operation"]=new JsonObject{["enum"]=new JsonArray("create_model_sketch","create_model_profile_on_sketch","add_model_sweep_from_profile","add_model_revolution_from_profile","add_model_loft_from_profiles")},
        ["source"]=reference.DeepClone(),["body_ref"]=reference.DeepClone(),["body_mc_id"]=Id(),["mc_document_id"]=Id(),["sketch_id"]=Id(),["sketch_token"]=Token(),["last_item_id"]=Id(),
        ["source_refs"]=Array(reference),["source_representation_replaced"]=new JsonObject{["type"]="boolean"},["profile_id"]=Id(),["profile_token"]=Token(),["feature_id"]=Id(),["profile_ownership"]=ModelProfileLifecycleSchema340.OwnershipOutput(),["native_objects"]=NativeSketchSchema385.Created(),["constraint_changes"]=NativeSketchSchema385.Changes(),["native_support"]=NativeSupportOutput(),
        ["angle_variable_id"]=Id(),["angle_degrees"]=new JsonObject{["type"]="number"},["axis_ref"]=reference.DeepClone(),["consumed_profiles"]=Array(ModelProfileLoftSchema446.ConsumedOutput())
    },"operation_index","operation","source","body_ref","body_mc_id","mc_document_id","sketch_id","sketch_token","last_item_id","source_refs","source_representation_replaced");
}
