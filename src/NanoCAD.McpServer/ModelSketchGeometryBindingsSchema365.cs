using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class ModelSketchGeometryBindingsSchema365
{
    static JsonObject Text()=>new(){["type"]="string"};
    static JsonObject Bool()=>new(){["type"]="boolean"};
    static JsonObject Array(JsonObject item)=>new(){["type"]="array",["items"]=item};
    static JsonObject Nullable(JsonObject item)=>new(){["anyOf"]=new JsonArray(item,new JsonObject{["type"]="null"})};
    static JsonObject Object(params (string Name,JsonObject Value)[] fields)
    {
        var properties=new JsonObject();foreach(var field in fields)properties[field.Name]=field.Value;
        return new(){["type"]="object",["additionalProperties"]=false,["properties"]=properties,
            ["required"]=new JsonArray(fields.Select(field=>(JsonNode?)JsonValue.Create(field.Name)).ToArray())};
    }
    internal static JsonObject Read()
    {
        var member=Object(("element_id",Text()),("native_geometry_type",Text()),("contained_nonprojection",Bool()),("contained_projection",Bool()),
            ("native_gs_marker",Nullable(Text())),("marker_element_id",Nullable(Text())),("marker_status",Text()),("marker_geometry_equivalent",Nullable(Bool())));
        var solver=Object(("native_object_id",Text()),("constraint_ids",Array(Text())),("current_geometry_id",Nullable(Text())),("current_marker_roundtrip_proved",Bool()));
        var scope=Object(("document_id",Text()),("sketch_id",Nullable(Text())),("scope_id",Nullable(Text())),("network",Text()));
        var result=Object(("scope",Text()),("sketch_id",Text()),("native_document_id",Text()),("geometry_members_complete",Bool()),("geometry_members",Nullable(Array(member))),
            ("solver_scope",Nullable(scope)),("solver_mapping_complete",Bool()),("solver_members",Nullable(Array(solver))),
            ("unmapped_constraint_ids",Nullable(Array(Text()))),("unavailable_reason",Nullable(Text())),("independent_storage_ownership",Text()),
            ("edit_context_alias_mapping",Text()),("driving_edit_scope",Text()));
        result["description"]="Actual current native sketch geometry/member IDs, native GS-marker roundtrip and exact scoped solver object-to-constraint identity. Abstract GUID geometry is valid and does not require a standalone DBObject, handle or DbGeometry. A marker is read from native geometry and passed to the public lookup; IDs are never cast to markers. Geometric coincidence never supplies an alias proof. solver_mapping_complete requires agreement in both directions between native object mapping and every returned constraint reference; it does not prove that every solver object maps to a current geometry item. Constraint references absent from the current geometry remain explicit native IDs with current_geometry_id=null. Missing inventory/mapping stays unavailable. This read does not enter edit mode, solve, add constraints, initialize storage or prove an independently editable geometry/variable owner. Existing source-driven native constraints remain a separate applicable route; select it when the design uses drawing sources. No driving change or fully constrained status is inferred from this observation.";
        return result;
    }
}
