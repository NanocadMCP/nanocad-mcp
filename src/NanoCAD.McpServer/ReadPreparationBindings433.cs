using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;

namespace NanoCAD.McpServer;

// A projection of this response only. These observations are not native write
// preflight, a persistent editing context, or a choice of the requested result.
public static class ReadPreparationBindings433
{
    public static JsonArray Candidates(string reader, string targetOperation, string? variant,
        JsonElement data, ReadContext context, JsonElement selectedBranch)
    {
        var result = new JsonArray();
        if (data.ValueKind != JsonValueKind.Object) return result;
        reader = Local(reader); targetOperation = Local(targetOperation);
        var adapter = new Adapter(result, targetOperation, variant, selectedBranch, context);
        switch (reader)
        {
            case "inspect_objects": adapter.Objects(data); break;
            case "get_model_variables": adapter.Variables(data); break;
            case "inspect_model_history": adapter.History(data); break;
            case "inspect_solid_topology": adapter.Topology(data); break;
            case "inspect_sketch_constraints":
            case "inspect_constraints": adapter.Constraints(data, reader); break;
            // get_document contributes only common read context in the caller.
        }
        return result;
    }

    private static string Local(string name) => name.StartsWith("nanocad.", StringComparison.Ordinal) ? name[8..] : name;
    private static bool Get(JsonElement value, string name, out JsonElement found)
    {
        found = default;
        return value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out found);
    }
    private static string? Text(JsonElement value, string name) => Get(value, name, out var found) && found.ValueKind == JsonValueKind.String ? found.GetString() : null;
    private static bool Yes(JsonElement value, string name) => Get(value, name, out var found) && found.ValueKind == JsonValueKind.True;
    private static JsonNode? Node(JsonElement value) => JsonNode.Parse(value.GetRawText());
    private static IEnumerable<(JsonElement Value, string Pointer)> Array(JsonElement value, string name, string pointer)
    {
        if (!Get(value, name, out var array) || array.ValueKind != JsonValueKind.Array) yield break;
        int index = 0;
        foreach (var item in array.EnumerateArray()) yield return (item, pointer + "/" + name + "/" + index++);
    }

    private sealed class Adapter(JsonArray output, string operation, string? variant, JsonElement branch, ReadContext context)
    {
        private readonly string prefix = operation == "modify_entities" ? "/operations/0" : "";
        private bool Field(string name) => Get(branch, "properties", out var properties) && Get(properties, name, out _);
        private bool Variant(params string[] values) => operation == "modify_entities" && variant != null && values.Contains(variant, StringComparer.Ordinal);
        private static bool Present(JsonElement value) => value.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null);
        private static JsonObject Candidate(string pointer, JsonObject target) => new()
        {
            ["source_pointer"] = pointer, ["target"] = target,
            ["bindings"] = new JsonArray(), ["current_values"] = new JsonArray()
        };
        private void Bind(JsonObject candidate, string field, JsonElement source, string name, string pointer)
        {
            if (Get(source, name, out var value)) BindValue(candidate, field, value, pointer + "/" + name);
        }
        private void BindValue(JsonObject candidate, string field, JsonElement value, string pointer)
        {
            if (!Field(field) || !Present(value)) return;
            candidate["bindings"]!.AsArray().Add(new JsonObject
            {
                ["argument"] = prefix + "/" + field, ["value"] = Node(value), ["source_pointer"] = pointer
            });
        }
        private static void Current(JsonObject candidate, string name, JsonElement source, string field, string pointer, string? unit = null)
        {
            if (!Get(source, field, out var value)) return;
            var observation = new JsonObject { ["name"] = name, ["value"] = Node(value), ["source_pointer"] = pointer + "/" + field };
            if (unit != null) observation["unit"] = unit;
            candidate["current_values"]!.AsArray().Add(observation);
        }
        private static void Note(JsonObject candidate, string name, string text, string pointer)
        {
            // This is an explanation of the adapter's limits, not native data.
            candidate[name] = new JsonObject { ["reason"] = text, ["source_pointer"] = pointer };
        }
        private bool Reference(JsonElement value) =>
            Text(value, "bridge_instance_id") == context.bridge_instance_id &&
            Text(value, "document_id") == context.document_id && Text(value, "handle") is { Length: > 0 };
        private void Handle(JsonObject candidate, JsonElement reference, string pointer)
        {
            if (!Reference(reference) || !Get(reference, "handle", out var handle)) return;
            BindValue(candidate, "handle", handle, pointer + "/handle");
            if (Field("handles")) candidate["bindings"]!.AsArray().Add(new JsonObject
            {
                ["argument"] = prefix + "/handles/0", ["value"] = Node(handle), ["source_pointer"] = pointer + "/handle"
            });
        }
        private static JsonObject Target(JsonElement reference) => new() { ["ref"] = Node(reference), ["scope"] = "observed_target_not_write_preflight" };
        private void GeometryValues(JsonObject candidate, JsonElement entity, string pointer)
        {
            Current(candidate, "coordinate_system", entity, "coordinate_system", pointer);
            Current(candidate, "space", entity, "space", pointer);
            if (Variant("set_properties"))
            {
                Current(candidate, "properties", entity, "properties", pointer);
                return;
            }
            if (!Get(entity, "geometry", out var geometry)) return;
            string gp = pointer + "/geometry";
            string[] fields = variant switch
            {
                "set_point_position" => ["coordinate_frame", "position", "normal", "thickness", "ecs_rotation"],
                "set_curve_geometry" => ["start", "end", "center", "radius", "normal", "thickness", "start_point", "end_point", "start_angle_ocs", "end_angle_ocs", "sweep_angle", "major_axis", "radius_ratio", "start_parameter", "end_parameter", "sweep_parameter", "degree", "control_points", "knots", "weights", "closed", "periodic", "rational"],
                "set_polyline_geometry" or "set_polyline_vertices" or "set_polyline_corners" or "reverse_polyline" => ["closed", "elevation", "normal", "thickness", "constant_width", "vertex_count", "vertices"],
                "replace_text" => ["text", "position", "alignment_point", "rotation"],
                "replace_mtext" => ["contents", "plain_text", "location", "direction", "normal"],
                "replace_dimension_text" => ["dimension_kind", "dimension_text", "text_position"],
                "set_dimension_definition" => ["dimension_kind", "definition", "normal", "elevation"],
                "set_dimension_display" => ["dimension_kind", "dimension_text", "display", "display_unavailable_reason"],
                "set_dimension_line" => ["dimension_kind", "definition", "text_position"],
                "set_dimension_text_position" => ["dimension_kind", "text_position", "display"],
                "replace_mleader_text" => ["plain_text", "contents", "content_location", "content_type"],
                "set_mleader_vertices" => ["lines", "content_location", "normal", "leader_editing_unavailable_reason"],
                "set_hatch_properties" => ["pattern_name", "pattern_type", "pattern_angle", "pattern_scale", "pattern_space", "pattern_double", "origin_ocs", "hatch_style", "normal", "elevation", "associative", "editing_unavailable_reason"],
                "set_hatch_boundary" => ["normal", "elevation", "boundary_complete", "loops", "editing_unavailable_reason"],
                "set_associative_rectangular_array" => ["associative_array"],
                "replace_table_text" or "resize_table" or "set_table_cell_text" or "set_table_cell_format" or "set_table_cell_formula" or "fill_table_formula_range" or
                    "insert_table_rows" or "delete_table_rows" or "insert_table_columns" or "delete_table_columns" or "merge_table_cells" or "unmerge_table_cells" or
                    "set_table_grid_layout" or "set_table_cell_layout" => ["rows", "columns", "row_heights", "column_widths", "editing_unavailable_reason"],
                // Placement and erasure need a short target observation, not a
                // duplicate of complete hatch loops/table cells/model history.
                _ => ["coordinate_frame", "position", "location", "center", "start", "end", "rotation", "normal", "scale_factors"]
            };
            foreach (string field in fields) Current(candidate, field, geometry, field, gp);
        }

        internal void Objects(JsonElement data)
        {
            if (operation != "modify_entities" || variant == null) return;
            foreach (var (item, ip) in Array(data, "items", "/data"))
            {
                if (!Yes(item, "success") || !Get(item, "entity", out var entity) ||
                    !Get(entity, "ref", out var reference) || !Reference(reference)) continue;
                string? type = Text(entity, "type");
                Get(entity, "geometry", out var geometry);
                if (!ObjectVariant(type, geometry)) continue;
                var ep = ip + "/entity"; var gp = ep + "/geometry";
                var candidate = Candidate(ep, Target(reference));
                candidate["target"]!["type"] = type;
                Handle(candidate, reference, ep + "/ref");
                GeometryValues(candidate, entity, ep);
                switch (variant)
                {
                    case "set_point_position":
                        // Default Point edit frame is WCS. No workplane is invented.
                        if (Text(geometry, "coordinate_frame") == "WCS") Bind(candidate, "expected_position", geometry, "position", gp);
                        break;
                    case "replace_text": Bind(candidate, "expected_text", geometry, "text", gp); break;
                    case "replace_dimension_text": Bind(candidate, "expected_text", geometry, "dimension_text", gp); break;
                    case "replace_mleader_text": Bind(candidate, "expected_text", geometry, "plain_text", gp); break;
                    case "set_dimension_definition":
                        Bind(candidate, "dimension_kind", geometry, "dimension_kind", gp);
                        Bind(candidate, "expected_definition", geometry, "definition", gp);
                        break;
                    case "set_hatch_properties":
                    case "set_hatch_boundary":
                        if (Get(geometry, "editing", out var hatch)) Bind(candidate, "hatch_token", hatch, "hatch_token", gp + "/editing");
                        break;
                    case "set_associative_rectangular_array":
                        if (Get(geometry, "associative_array", out var array)) Bind(candidate, "array_token", array, "array_token", gp + "/associative_array");
                        break;
                    case "set_mleader_vertices":
                        if (Get(geometry, "leader_editing", out var leader)) Bind(candidate, "expected_geometry_token", leader, "geometry_token", gp + "/leader_editing");
                        break;
                }
                // Shared table lifecycle/read tokens have the same actual source.
                if (type == "Table" && Get(geometry, "editing", out var table)) Bind(candidate, "table_token", table, "table_token", gp + "/editing");
                // expected_geometry for curves/Polyline is intentionally not a
                // blind read-object copy: XYZ/XY, OCS and framed input differ.
                if (Variant("set_curve_geometry", "set_polyline_geometry", "set_polyline_vertices", "set_polyline_corners", "reverse_polyline"))
                    Note(candidate, "mapping_note", "Observed geometry is retained as current_values when requested by this read; complete expected geometry must use the selected write schema and its explicit/default coordinate frame.", ep);
                output.Add(candidate);
            }
        }

        private bool ObjectVariant(string? type, JsonElement geometry) => variant switch
        {
            "set_point_position" => type == "Point",
            "set_curve_geometry" => type is "Line" or "Circle" or "Arc" or "Ellipse" or "Spline",
            "set_polyline_geometry" or "set_polyline_vertices" or "set_polyline_corners" or "reverse_polyline" => type == "Polyline",
            "replace_text" => type == "Text",
            "replace_mtext" => type == "MText",
            "replace_dimension_text" or "set_dimension_definition" or "set_dimension_display" or "set_dimension_line" or "set_dimension_text_position" => type == "Dimension",
            "replace_mleader_text" or "set_mleader_vertices" => type == "MLeader",
            "set_hatch_properties" or "set_hatch_boundary" => type == "Hatch",
            "set_associative_rectangular_array" => type == "BlockReference" && Get(geometry, "associative_array", out var array) && Text(array, "kind") == "rectangular",
            "replace_table_text" or "resize_table" or "set_table_cell_text" or "set_table_cell_format" or "set_table_cell_formula" or "fill_table_formula_range" or
                "insert_table_rows" or "delete_table_rows" or "insert_table_columns" or "delete_table_columns" or "merge_table_cells" or "unmerge_table_cells" or
                "set_table_grid_layout" or "set_table_cell_layout" => type == "Table",
            // Generic identity hints do not introduce a second eligibility
            // allowlist: the selected native writer owns its actual admission.
            "move" or "rotate" or "scale" or "mirror" or "copy" or "erase" or "set_properties" => type != null,
            _ => false
        };

        internal void Variables(JsonElement data)
        {
            if (!(operation == "set_model_parameter" || Variant("set_model_parameter_name")) ||
                !Get(data, "source_ref", out var source) || !Reference(source)) return;
            foreach (var (variable, pointer) in Array(data, "variables", "/data"))
            {
                if (Text(variable, "mc_id") == null) continue;
                var target = Target(source); target["variable_id"] = Node(variable.GetProperty("mc_id"));
                var candidate = Candidate(pointer, target);
                Handle(candidate, source, "/data/source_ref");
                Bind(candidate, "variable_id", variable, "mc_id", pointer);
                if (operation == "set_model_parameter") Bind(candidate, "expected_expression", variable, "expression", pointer);
                else Bind(candidate, "expected_name", variable, "name", pointer);
                foreach (string name in new[] { "name", "type", "expression", "value_available", "value_unit", "host_object_id", "owner_kind", "sketch_id", "constraint_id", "control_kind" })
                    Current(candidate, name, variable, name, pointer);
                Current(candidate, "value", variable, "value", pointer, Text(variable, "value_unit"));
                Note(candidate, "mapping_note", "A readable variable/ID and value do not establish supported native control binding or write eligibility. Native units are retained verbatim; no value/expression, scope_handles or propagate choice is supplied.", pointer);
                output.Add(candidate);
            }
        }

        internal void History(JsonElement data)
        {
            if (operation != "modify_entities" || variant == null) return;
            foreach (var (body, bp) in Array(data, "items", "/data"))
            {
                if (!Get(body, "ref", out var reference) || !Reference(reference)) continue;
                if (variant.StartsWith("add_model_", StringComparison.Ordinal) || Variant("create_model_datum_plane", "create_model_sketch", "create_model_profile", "erase"))
                {
                    var candidate = HistoryCandidate(body, bp, reference, bp);
                    Current(candidate, "history_complete", body, "complete", bp);
                    output.Add(candidate);
                }
                foreach (var (feature, fp) in Array(body, "nodes", bp))
                {
                    if (!Get(feature, "parameters", out var parameters) || !FeatureVariant(Text(parameters, "kind"))) continue;
                    var candidate = HistoryCandidate(body, bp, reference, fp);
                    candidate["target"]!["feature_id"] = Get(feature, "mc_id", out var id) ? Node(id) : null;
                    Bind(candidate, "feature_id", feature, "mc_id", fp);
                    string[] observedFields = variant switch
                    {
                        "set_model_rectangular_pattern" or "set_model_pattern_element" => ["kind", "count_x", "count_y", "step_x", "step_y", "count_x_variable_id", "count_y_variable_id", "step_x_variable_id", "step_y_variable_id", "source_history_feature_id", "source_items_count", "source_items_complete", "has_sub_feature_items", "suppressed_elements_complete"],
                        "set_model_feature_suppressed" => ["kind"],
                        "set_model_shell_thickness" => ["kind", "direction", "thickness", "thickness_unit", "host_feature_id"],
                        "set_model_revolution" => ["kind", "profile_id", "axis_object_id", "extent", "extent2", "angle", "angle2", "angle_unit", "angle_variable_id", "angle2_variable_id"],
                        _ => ["kind", "profile_id", "profile_rebind", "part_operation", "extent", "extent2", "direction", "distance", "distance2", "distance_unit", "taper_angle", "taper_angle2", "angle_unit", "distance_variable_id", "distance2_variable_id"]
                    };
                    foreach (string field in observedFields) Current(candidate, field, parameters, field, fp + "/parameters");
                    foreach (string field in new[] { "status", "suppressed", "detail_unavailable_reason" }) Current(candidate, field, feature, field, fp);
                    if (Variant("set_model_rectangular_pattern", "set_model_pattern_element"))
                    {
                        foreach (string field in new[] { "count_x", "count_y", "step_x", "step_y" }) Bind(candidate, "expected_" + field, parameters, field, fp + "/parameters");
                        if (Variant("set_model_pattern_element")) Bind(candidate, "expected_source_feature_id", parameters, "source_history_feature_id", fp + "/parameters");
                        Note(candidate, "mapping_note", "native_source_item_ids identify generated copies. The read has no original source_feature_ids group; expected_source_feature_ids and a selected element's expected_suppressed are not inferred.", fp + "/parameters");
                    }
                    if (Variant("set_model_extrusion_profile"))
                    {
                        Bind(candidate, "expected_current_profile_id", parameters, "profile_id", fp + "/parameters");
                        Note(candidate, "mapping_note", "expected_sketch_id/profile_token belong to the selected replacement profile, or to the current profile only for replacement_regions. That route and replacement geometry are not chosen by preparation.", fp + "/parameters");
                    }
                    if (Variant("set_model_feature_suppressed")) Bind(candidate, "expected_suppressed", feature, "suppressed", fp);
                    if (Variant("set_model_shell_thickness")) Bind(candidate, "expected_thickness", parameters, "thickness", fp + "/parameters");
                    output.Add(candidate);
                }
            }
            ProfileObservations(data);
            ExtentObservations(data);
            ControlObservations(data);
        }

        private JsonObject HistoryCandidate(JsonElement body, string bp, JsonElement reference, string pointer)
        {
            var candidate = Candidate(pointer, Target(reference));
            Handle(candidate, reference, bp + "/ref");
            Bind(candidate, "expected_last_item_id", body, "last_item_id", bp);
            Current(candidate, "body_mc_id", body, "mc_id", bp);
            Current(candidate, "last_item_id", body, "last_item_id", bp);
            Current(candidate, "history_complete", body, "complete", bp);
            return candidate;
        }
        private bool FeatureVariant(string? kind) => variant switch
        {
            "set_model_rectangular_pattern" or "set_model_pattern_element" => kind == "rectangular_pattern",
            "set_model_extrusion_profile" or "set_model_extrusion_extent" => kind == "extrusion",
            "set_model_revolution" => kind == "revolution",
            "set_model_shell_thickness" => kind == "shell",
            "set_model_feature_suppressed" => kind is "extrusion" or "revolution" or "sweep" or "loft" or "hole" or "thread" or
                "rectangular_pattern" or "circular_pattern" or "mirror" or "fillet" or "chamfer" or "shell",
            _ => false
        };
        private void ProfileObservations(JsonElement data)
        {
            if (!Variant("set_model_extrusion_profile", "set_model_profile_regions", "edit_model_profile_elements")) return;
            foreach (var (profile, pointer) in Array(data, "document_profiles", "/data"))
            {
                if (!Get(profile, "profile_id", out var id)) continue;
                var candidate = Candidate(pointer, new JsonObject { ["profile_id"] = Node(id), ["scope"] = "observed_profile_not_selected_replacement" });
                foreach (string field in new[] { "profile_id", "parent_sketch_id", "profile_editing", "plane", "ownership" }) Current(candidate, field, profile, field, pointer);
                Note(candidate, "mapping_note", "Selected contour and forward consumers remain in the original response at this profile's source_pointer. They are not duplicated or selected as replacement geometry.", pointer);
                // A fresh alternative profile is a choice by the engineering
                // agent, not automatically the intended replacement input.
                output.Add(candidate);
            }
            string section = Variant("set_model_profile_regions") ? "profile_regions" : "profile_elements";
            if (!Variant("set_model_profile_regions", "edit_model_profile_elements")) return;
            foreach (var (profile, pointer) in Array(data, section, "/data"))
            {
                var candidate = Candidate(pointer, new JsonObject { ["scope"] = "observed_profile_edit_metadata_not_write_preflight" });
                Bind(candidate, "profile_id", profile, "profile_id", pointer);
                Bind(candidate, "feature_id", profile, "feature_id", pointer);
                Bind(candidate, "expected_sketch_id", profile, "parent_sketch_id", pointer);
                Bind(candidate, Variant("set_model_profile_regions") ? "expected_region_token" : "expected_element_token", profile, Variant("set_model_profile_regions") ? "region_token" : "element_token", pointer);
                foreach (string field in new[] { "profile_id", "parent_sketch_id", "mc_document_id", "plane", "scope" }) Current(candidate, field, profile, field, pointer);
                // Join only by the exact current consumer's native feature ID.
                // No assignment to the first body or inferred ownership occurs.
                BindBodyByFeature(candidate, data, Text(profile, "feature_id"));
                output.Add(candidate);
            }
        }
        private void BindBodyByFeature(JsonObject candidate, JsonElement data, string? featureId)
        {
            if (featureId == null) return;
            var matches = Array(data, "items", "/data").Where(body =>
                Array(body.Value, "nodes", body.Pointer).Any(node => Text(node.Value, "mc_id") == featureId)).ToArray();
            if (matches.Length != 1 || !Get(matches[0].Value, "ref", out var reference) || !Reference(reference)) return;
            candidate["target"]!["ref"] = Node(reference);
            Handle(candidate, reference, matches[0].Pointer + "/ref");
            Bind(candidate, "expected_last_item_id", matches[0].Value, "last_item_id", matches[0].Pointer);
        }
        private void ExtentObservations(JsonElement data)
        {
            if (!Variant("set_model_extrusion_extent")) return;
            foreach (var (extent, pointer) in Array(data, "extrusion_extents", "/data"))
            {
                var candidate = Candidate(pointer, new JsonObject { ["scope"] = "explicit_extrusion_extent_metadata_not_write_preflight" });
                Bind(candidate, "feature_id", extent, "feature_id", pointer);
                Bind(candidate, "expected_profile_id", extent, "profile_id", pointer);
                Bind(candidate, "expected_sketch_id", extent, "parent_sketch_id", pointer);
                Bind(candidate, "expected_extent_token", extent, "extent_token", pointer);
                BindBodyByFeature(candidate, data, Text(extent, "feature_id"));
                foreach (string field in new[] { "direction", "extent", "extent2", "native_distance", "native_distance2", "taper_angle", "taper_angle2", "length_semantics", "secondary_driver_scope" }) Current(candidate, field, extent, field, pointer);
                output.Add(candidate);
            }
        }
        private void ControlObservations(JsonElement data)
        {
            if (!Variant("set_sketch_constraint_parameter")) return;
            foreach (var (control, pointer) in Array(data, "sketch_constraint_parameter_edits", "/data"))
            {
                var candidate = Candidate(pointer, new JsonObject { ["scope"] = "explicit_control_read_metadata_not_write_preflight" });
                Bind(candidate, "constraint_id", control, "constraint_id", pointer);
                Bind(candidate, "expected_parameter_token", control, "parameter_token", pointer);
                Bind(candidate, "expected_variable_id", control, "variable_id", pointer);
                foreach (string field in new[] { "eligible", "unavailable_reason", "kind", "control_value_unit", "variable_expression", "binding_scope" }) Current(candidate, field, control, field, pointer);
                Current(candidate, "control_value", control, "control_value", pointer, Text(control, "control_value_unit"));
                output.Add(candidate);
            }
        }

        internal void Topology(JsonElement data)
        {
            if (operation != "modify_entities" || !Get(data, "source_ref", out var reference) || !Reference(reference)) return;
            if (!Field("topology_token")) return;
            var candidate = Candidate("/data", Target(reference));
            Handle(candidate, reference, "/data/source_ref");
            Bind(candidate, "topology_token", data, "snapshot_token", "/data");
            foreach (string field in new[] { "coordinate_system", "reference_scope", "complete", "record_history", "show_history" }) Current(candidate, field, data, field, "/data");
            Note(candidate, "mapping_note", "Face selection is not supplied. B-rep faces.native_id and native_model_faces.faces.model_face_id belong to different kernels and are never interchanged.", "/data");
            output.Add(candidate);
        }

        internal void Constraints(JsonElement data, string reader)
        {
            if (reader == "inspect_constraints")
            {
                DrawingConstraints(data);
                return;
            }
            if (!Variant("add_sketch_constraints", "delete_sketch_constraints", "set_sketch_constraint_parameter")) return;
            // These reads observe native references, not owned edit binding
            // tokens. Preserve their declared native units and solver scope.
            var candidate = Candidate("/data", new JsonObject { ["scope"] = "constraint_observation_only_not_control_edit_eligibility" });
            if (reader == "inspect_sketch_constraints" && Get(data, "source_ref", out var reference) && Reference(reference))
            {
                candidate["target"]!["ref"] = Node(reference);
                Handle(candidate, reference, "/data/source_ref");
                Bind(candidate, "sketch_id", data, "sketch_id", "/data");
            }
            foreach (string field in new[] { "sketch_id", "network", "manager_document_id", "scope_id", "identity_scope", "reference_coordinate_system", "solver_status", "complete", "completeness_scope" })
                Current(candidate, field, data, field, "/data");
            Note(candidate, "mapping_note", "Native constraint IDs/values are in the original constraints array. This reader does not provide the owned native_inputs.binding_token or parameter_token needed by internal relation edits.", "/data");
            output.Add(candidate);
        }
        private void DrawingConstraints(JsonElement data)
        {
            if (Variant("set_drawing_dimension", "remove_drawing_constraints"))
                foreach (var (constraint, pointer) in Array(data, "constraints", "/data"))
                {
                    if (Variant("set_drawing_dimension") && !Yes(constraint, "is_dimensional")) continue;
                    var candidate = Candidate(pointer, new JsonObject { ["scope"] = "ACAD_constraint_observation_not_complete_write_component" });
                    if (Variant("set_drawing_dimension"))
                    {
                        Bind(candidate, "constraint_id", constraint, "mc_id", pointer);
                        Bind(candidate, "expected_expression", constraint, "variable_expression", pointer);
                    }
                    foreach (string field in new[] { "mc_id", "type", "variable_id", "variable_expression", "parameter_value_unit", "parameter_value_source" }) Current(candidate, field, constraint, field, pointer);
                    Current(candidate, "parameter_value", constraint, "parameter_value", pointer, Text(constraint, "parameter_value_unit"));
                    Note(candidate, "mapping_note", "A complete connected source component and consuming profile/axis handles must be selected explicitly. Native reference coordinates are not WCS anchors; expected_value is not inferred across unverified native units. No constraint deletion list is selected.", pointer);
                    output.Add(candidate);
                }
            if (!Variant("name_drawing_parameter", "set_drawing_expression", "remove_drawing_variable") || !Get(data, "variable_inventory", out var inventory)) return;
            foreach (var (variable, pointer) in Array(inventory, "variables", "/data/variable_inventory"))
            {
                var candidate = Candidate(pointer, new JsonObject { ["scope"] = "ACAD_variable_observation_not_complete_write_component" });
                Bind(candidate, "variable_id", variable, "mc_id", pointer);
                Bind(candidate, "expected_name", variable, "name", pointer);
                Bind(candidate, "expected_expression", variable, "expression", pointer);
                Bind(candidate, "expected_value", variable, "value", pointer);
                foreach (string field in new[] { "name", "type", "expression", "value_available", "value_unit", "host_object_id" }) Current(candidate, field, variable, field, pointer);
                Current(candidate, "value", variable, "value", pointer, Text(variable, "value_unit"));
                output.Add(candidate);
            }
        }
    }
}
