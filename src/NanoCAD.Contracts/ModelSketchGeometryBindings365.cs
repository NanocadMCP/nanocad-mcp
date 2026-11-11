using System.Text.Json.Serialization;

namespace NanoCAD.Contracts;

// Read-only identities. Native sketch membership/markers do not establish
// independently writable storage, an edit-context alias, or a driving variable.
public sealed record ModelSketchGeometryMember365(string element_id, string native_geometry_type,
    bool contained_nonprojection, bool contained_projection, string? native_gs_marker,
    string? marker_element_id, string marker_status, bool? marker_geometry_equivalent)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? fully_constrained { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? fully_constrained_unavailable_reason { get; init; }
}
public sealed record ModelSketchSolverMember365(string native_object_id, string[] constraint_ids,
    string? current_geometry_id, bool current_marker_roundtrip_proved);
public sealed record ModelSketchSolverScope365(string document_id, string? sketch_id, string? scope_id, string network);
public sealed record ModelSketchGeometryBindings365(string scope, string sketch_id, string native_document_id,
    bool geometry_members_complete, ModelSketchGeometryMember365[]? geometry_members,
    ModelSketchSolverScope365? solver_scope, bool solver_mapping_complete, ModelSketchSolverMember365[]? solver_members,
    string[]? unmapped_constraint_ids, string? unavailable_reason, string independent_storage_ownership,
    string edit_context_alias_mapping, string driving_edit_scope);
