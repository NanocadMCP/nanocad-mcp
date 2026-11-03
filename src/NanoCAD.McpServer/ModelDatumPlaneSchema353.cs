using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class ModelDatumPlaneSchema353
{
    static JsonObject Text()=>new(){["type"]="string"};
    static JsonObject Flag()=>new(){["type"]="boolean"};
    internal static JsonObject Id()=>new(){["type"]="string",["pattern"]="^\\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\\}$"};
    internal static JsonObject Token()=>new(){["type"]="string",["pattern"]="^[0-9A-Fa-f]{64}$"};
    static JsonObject Nullable(JsonObject value)=>new(){["anyOf"]=new JsonArray(value,new JsonObject{["type"]="null"})};
    static JsonObject Object(JsonObject fields,params string[] required)=>new(){["type"]="object",["additionalProperties"]=false,["properties"]=fields,["required"]=new JsonArray(required.Select(key=>(JsonNode?)JsonValue.Create(key)).ToArray())};
    static JsonObject List(JsonObject value)=>new(){["type"]="array",["items"]=value};
    internal static JsonObject Create()
    {
        var point=new JsonObject{["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=new JsonObject{["type"]="number",["minimum"]=-1e6,["maximum"]=1e6}};
        var frame=Object(new(){["origin"]=point,["x_axis"]=point.DeepClone(),["y_axis"]=point.DeepClone()},"origin","x_axis","y_axis");
        var fixedDefinition=Object(new(){["kind"]=new JsonObject{["const"]="fixed"},["workplane"]=frame},"kind","workplane");
        var definition=new JsonObject{["oneOf"]=new JsonArray(fixedDefinition,ModelFaceDatumPlaneSchema356.Definition())};
        var result=Object(new(){["operation"]=new JsonObject{["const"]="create_model_datum_plane"},["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=BasicModelingSchema.Handle()},
            ["expected_last_item_id"]=Id(),["name"]=new JsonObject{["type"]="string",["minLength"]=1,["maxLength"]=255},["definition"]=definition},"operation","handles","expected_last_item_id","name","definition");
        result["description"]="Create one reusable native WorkPlane in the selected existing part's actual native owner document. definition.kind=fixed uses an oriented WCS plane; definition.kind=face_angle uses current planar face/lying straight edge tokens and signed native radians through PlaneLineNAngle, retaining those actual native sources. Use Millimeters in Model Space; only the fixed branch takes a finite orthonormal WCS workplane. Its oriented normal and physical plane location define the datum; native in-plane origin/U/V are read back as representation, not durable frame constraints. No HostSolid/Owner setter or coordinate recreation occurs. Fixed construction adds no face/edge association; face_angle adds that native definition and reports any actual new plane-owned angular driver with its observed native units, without solving internal sketch constraints. Existing part geometry/history/drivers/sources remain. Actual native ID, owner/host/body-membership, fresh token and any real DWG representation are returned; document construction is not invented body ownership. Discover or reread via inspect_model_history datum_plane_ids, then use add_model_mirror definition.datum_plane_id/expected_datum_token for retained native plane reuse. One operation per request; rollback/Undo is not guaranteed, unknown is not success.";
        result["properties"]!["recovery_context"]=Object(new(){["sketch_id"]=Id(),["expected_support_id"]=Id()},"sketch_id","expected_support_id");
        result["allOf"]=new JsonArray(new JsonObject
        {
            ["if"]=new JsonObject{["required"]=new JsonArray("recovery_context")},
            ["then"]=new JsonObject{["properties"]=new JsonObject{["definition"]=fixedDefinition.DeepClone()}}
        });
        result["description"]=result["description"]!.GetValue<string>()+" Optional recovery_context selects the exact failed face-supported sketch/retained support IDs for creating a fixed replacement datum while that known failed graph remains unchanged. This explicit recovery preparation does not repair/reassign the sketch, waive unrelated errors or permit a face_angle target. recovery_preparation can include the already captured local geometry, controls and exact frame_references to inform the separate recovery's frame_reference_policy. Read the new datum's actual frame/token before that explicit set_model_sketch_support recovery; requested in-plane axes are not assumed durable.";
        return result;
    }
    internal static JsonObject HistoryIds()=>new(){["type"]="array",["minItems"]=1,["uniqueItems"]=true,["items"]=Id(),["description"]="Optional exact native WorkPlane IDs in the actual owner document selected by current native body handles. Returns actual geometry, native definition/source/driver/consumer IDs and current token. A same-part face_angle datum also reports its actual source_relationship geometry and physical unsigned dihedral; null does not establish an empty source network. Omit for ordinary history without a document scan."};
    internal static JsonObject MirrorOperation()
    {
        var definition=Object(new(){["source_feature_ids"]=new JsonObject{["type"]="array",["minItems"]=1,["uniqueItems"]=true,["items"]=Id()},["datum_plane_id"]=Id(),["expected_datum_token"]=Token()},"source_feature_ids","datum_plane_id","expected_datum_token");
        var result=Object(new(){["operation"]=new JsonObject{["const"]="add_model_mirror"},["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=BasicModelingSchema.Handle()},["expected_last_item_id"]=Id(),["definition"]=definition},"operation","handles","expected_last_item_id","definition");
        result["description"]="Native mirror insertion into one existing part using an exact reusable WorkPlane from the part's actual owner document. Read datum_plane_ids and refresh datum_token plus current body/history first. Select healthy source_feature_ids of this same body and supply datum_plane_id/expected_datum_token. The actual physical plane, definition/source/driver/constraints, previous consumers, retained part feature parameters/drivers/source links and documented followers remain checked before/after native insertion and after native write closure. A datum may have null HostSolid as document construction or real active affinity to this body; another body's host is not reassigned. Native acquisition into body composition is observed, never forced. Uses the existing native AddMirrorFeature/SetPlane path; no plane reconstruction, clone, source-coordinate copy or datum definition setter. Returned datum_plane reports actual host/composition/current IDs/consumers. Native plane in-plane frame representation and opaque owned internals are explicitly unobserved. Existing GCS/history plane selection remains separate. One operation; Model Space/Millimeters; rollback/Undo not guaranteed, unknown is not success.";return result;
    }
    internal static JsonObject Read()=>Object(new(){["plane_id"]=Id(),["name"]=Text(),["mc_document_id"]=Id(),["datum_token"]=Token(),["definition_type"]=Text(),["plane_wcs"]=new JsonObject{["type"]="object"},
        ["native_parameter"]=Nullable(new JsonObject{["type"]="number"}),["parameter_variable_id"]=Nullable(Id()),["driver"]=Nullable(new JsonObject{["type"]="object"}),["definition_bindings"]=List(new JsonObject{["type"]="object"}),
        ["host_solid_id"]=Nullable(Id()),["in_selected_body_contents"]=Flag(),["native_order_before_id"]=Nullable(Id()),["status"]=Text(),["suppressed"]=Flag(),["beyond_end_of_part"]=Flag(),
        ["dependency_ids"]=List(Id()),["direct_consumer_ids"]=List(Id()),["downstream_consumer_ids"]=List(Id()),["representation"]=Nullable(new JsonObject{["type"]="object"}),["geometry_scope"]=Text(),["source_relationship"]=Nullable(ModelFaceDatumPlaneSchema356.Relation())},
        "plane_id","name","mc_document_id","datum_token","definition_type","plane_wcs","native_parameter","parameter_variable_id","driver","definition_bindings","host_solid_id","in_selected_body_contents","native_order_before_id","status","suppressed","beyond_end_of_part","dependency_ids","direct_consumer_ids","downstream_consumer_ids","representation","geometry_scope");
    internal static JsonObject Inventory()=>Object(new(){["mc_document_id"]=Id(),["scope"]=Text(),["items"]=List(Object(new(){["plane_id"]=Id(),["name"]=Text(),["definition_type"]=Text(),["host_solid_id"]=Nullable(Id())},"plane_id","name","definition_type","host_solid_id")),["complete"]=Flag(),["next_cursor"]=Nullable(Id())},"mc_document_id","scope","items","complete","next_cursor");
}
