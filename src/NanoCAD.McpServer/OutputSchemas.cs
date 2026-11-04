using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class OutputSchemas
{
    private static JsonObject CreateData409()
    {
        var data=Object(("operation_id",Text()),("status",new JsonObject{["type"]="string",["const"]="committed"}),
            ("replayed",Bool()),("created",Array(Ref("ref"))),("undo_history",Text()));
        data["properties"]!["polyline_fits"]=Array(SmoothPolylineSchema409.FitResult());
        data["properties"]!["model_sketch_creation"]=Array(Object(("body_ref",Ref("ref")),("body_mc_id",Text()),
            ("sketch_id",Text()),("profile_id",Text()),("feature_id",Text()),("last_item_id",Text()),
            ("native_objects",NativeSketchSchema385.Created()),("constraint_changes",NativeSketchSchema385.Changes()),
            ("distance_variable_id",Nullable(Text()))));
        return data;
    }

    private static JsonObject Text() => new() { ["type"] = "string" };
    private static JsonObject Bool() => new() { ["type"] = "boolean" };
    private static JsonObject Number() => new() { ["type"] = "number" };
    private static JsonObject Integer() => new() { ["type"] = "integer" };
    private static JsonObject Ref(string name) => new() { ["$ref"] = "#/$defs/" + name };
    private static JsonObject Nullable(JsonObject schema) => new() { ["anyOf"] = new JsonArray(schema, new JsonObject { ["type"] = "null" }) };
    private static JsonObject Array(JsonObject schema) => new() { ["type"] = "array", ["items"] = schema };
    private static JsonObject Point(int dimensions = 3) => new() { ["type"] = "array", ["minItems"] = dimensions, ["maxItems"] = dimensions, ["items"] = Number() };
    private static JsonObject Object(params (string Name, JsonObject Schema)[] fields)
    {
        var properties = new JsonObject(); var required = new JsonArray();
        foreach (var (name, schema) in fields) { properties[name] = schema; required.Add(name); }
        return new() { ["type"] = "object", ["additionalProperties"] = false, ["properties"] = properties, ["required"] = required };
    }
    private static JsonObject AnyObject() => new() { ["type"] = "object" };
    private static JsonObject RegionWithOptionalBooleanGeometry()
    {
        var result=Object(("area",Number()),("normal",Point()));
        result["properties"]!["boolean_geometry"]=Nullable(RegionBooleanSchema.Read());
        result["properties"]!["boolean_geometry_unavailable_reason"]=Nullable(Text());
        return result;
    }
    private static JsonObject DocumentWithOptionalView()
    {
        var data=Object(("document_id",Text()),("name",Text()),("path",Nullable(Text())),("is_named",Bool()),("is_read_only",Bool()),
            ("units",Object(("name",Text()),("value",Integer()))),("active_space",Text()),("revision",Text()),("fingerprint_guid",Text()));
        data["properties"]!["view"]=Nullable(CameraViewData());
        data["properties"]!["view_unavailable_reason"]=Nullable(Text());
        return data;
    }
    private static JsonObject StructureData(JsonObject change)
    {
        var data=Object(("operation_id",Text()),("status",new JsonObject{["type"]="string",["const"]="committed"}),
            ("replayed",Bool()),("changes",Array(change)),("undo_history",Text()));
        data["properties"]!["group_changes"]=Array(Object(("operation_index",Integer()),("operation",Text()),("group_ref",Ref("ref")),("name",Text()),("member_handles",Array(Text())),("group_token",Nullable(Text())),("dissolved",Bool())));
        data["properties"]!["viewport_lifecycle_results"]=Array(ViewportLifecycleSchema.Output());
        data["properties"]!["viewport_clipping_changes"]=Array(ViewportClippingSchema358.Change(Ref("ref")));
        data["properties"]!["table_style_changes"]=Array(TableStyleSchema337.ChangeOutput());
        data["properties"]!["drawing_view_lifecycle_changes"]=Array(Object(("operation_index",Integer()),("view_ref",Ref("ref")),
            ("view_token",Text()),("source_refs",Array(Ref("ref"))),("top_source_ids",Array(Text())),
            ("parent_view_id",Nullable(Text())),("root_view_id",Nullable(Text())),("child_view_ids",Array(Text()))));
        data["properties"]!["section_cut_changes"]=Array(SectionCutSchema351.Change(Ref("ref")));
        data["properties"]!["drawing_view_creation_results"]=Array(PaperDrawingViewsSchema356.Creation(Ref("ref")));
        return data;
    }
    private static JsonObject ModelParameterData()
    {
        var data=Object(("operation_id",Text()),("status",new JsonObject{["const"]="committed"}),("replayed",Bool()),
            ("body_ref",Ref("ref")),("feature_id",Nullable(Text())),("variable_id",Text()),("previous_expression",Text()),
            ("expression",Text()),("value",Number()),("value_unit",Text()),("volume",Number()),("undo_history",Text()));
        data["properties"]!["parameter_changes"]=Array(Object(("variable_id",Text()),("feature_id",Nullable(Text())),
            ("previous_expression",Text()),("expression",Text()),("previous_value",Number()),("value",Number())));
        data["properties"]!["parameter_changes"]!["items"]!["properties"]!["value_unit"]=Text();
        data["properties"]!["parameter_changes"]!["items"]!["properties"]!["body_ref"]=Ref("ref");
        data["properties"]!["rebuilt_bodies"]=Array(Ref("ref"));
        data["properties"]!["model_healthy"] = new JsonObject
        {
            ["type"] = "boolean",
            ["description"] = "True when post-command verification observed healthy native history for the checked bodies, including intentional suppression. Omitted when this verification is unavailable."
        };
        data["properties"]!["downstream_values_changed"] = new JsonObject
        {
            ["type"] = "boolean",
            ["description"] = "Whether any verified downstream parameter value changed outside the native value comparison tolerance. Excludes the edited variable. Omitted when this verification is unavailable."
        };
        foreach(var fields in new[]{data["properties"]!.AsObject(),data["properties"]!["parameter_changes"]!["items"]!["properties"]!.AsObject()})
        {
            fields["owner_kind"]=new JsonObject{["type"]="string",["enum"]=new JsonArray("feature","sketch_dimension","user")};
            fields["sketch_id"]=Text();fields["constraint_id"]=Text();
        }
        return data;
    }
    private static JsonObject ModifiedData()
    {
        var data=Object(("operation_id",Text()),("status",new JsonObject{["type"]="string",["const"]="committed"}),("replayed",Bool()),("modified",Array(Ref("ref"))),
            ("erased",Array(Ref("ref"))),("created",Array(Ref("ref"))),
            ("copies",Array(Object(("operation_index",Integer()),("source",Ref("ref")),("created",Ref("ref"))))),("undo_history",Text()));
        data["properties"]!["solid_separation_results"]=Array(Object(("operation_index",Integer()),("source",Ref("ref")),("parts",Array(Ref("ref")))));
        data["properties"]!["repetition_results"]=Array(CopyTransforms2dSchema407.Result(Ref("ref")));
        data["properties"]!["solid_slice_results"]=Array(Object(("operation_index",Integer()),("source",Ref("ref")),("positive",Ref("ref")),("negative",Ref("ref"))));
        data["properties"]!["native_explode_changes"]=Array(NativeExplodeSchema386.Change(Ref("ref")));
        data["properties"]!["contour_results"]=Array(Object(("operation_index",Integer()),("operation",Text()),("source",Ref("ref")),
            ("generated",Array(Ref("ref"))),("source_erased",Bool())));
        data["properties"]!["contour_assembly_results"]=Array(Object(("operation",Text()),("sources",Array(Ref("ref"))),("generated",Array(Ref("ref"))),("sources_erased",Bool())));
        data["properties"]!["contour_corner_results"]=Array(IndependentContourCornersSchema343.ReadResult());
        data["properties"]!["model_revolution_changes"]=Array(Object(("body_ref",Ref("ref")),("feature_id",Text()),("variable_id",Text()),("previous_angle_degrees",Number()),("angle_degrees",Number())));
        data["properties"]!["text_format_changes"]=Array(TextFormatSchema442.Change());
        data["properties"]!["mtext_format_changes"]=Array(Object(("source_ref",Ref("ref")),("mtext_token",Text()),("text_style_ref",Ref("ref")),
            ("text_height",Number()),("width",Number()),("attachment",Text()),("line_spacing_style",Text()),("line_spacing_factor",Number()),("direction_wcs",Point())));
        data["properties"]!["mtext_format_changes"]!["items"]!["properties"]!["warnings"]=Array(Text());
        data["properties"]!["model_history_changes"]=Array(Object(("operation_index",Integer()),("source",Ref("ref")),("body_ref",Ref("ref")),
            ("body_mc_id",Text()),("sketch_id",Text()),("profile_id",Text()),("feature_id",Text()),("distance_variable_id",Text()),
            ("part_operation",Text()),("volume_before",Number()),("volume_after",Number()),("source_representation_replaced",Bool())));
        data["properties"]!["model_history_changes"]!["items"]!["properties"]!["source_refs"]=Array(Ref("ref"));
        data["properties"]!["model_profile_changes"]=Array(ModelProfileLifecycleSchema340.ChangeOutput(Ref("ref")));
        data["properties"]!["model_sketch_lifecycle"]=Array(ModelSketchLifecycleSchema362.ChangeOutput(Ref("ref")));
        data["properties"]!["model_feature_reorders"]=Array(ModelFeatureReorderSchema345.ChangeOutput(Ref("ref")));
        data["properties"]!["model_feature_deletions"]=Array(ModelFeatureDeletionSchema378.ChangeOutput(Ref("ref")));
        data["properties"]!["sketch_constraint_parameter_changes"]=Array(ModelSketchConstraintParameterSchema379.ChangeOutput(Ref("ref")));
        data["properties"]!["sketch_constraint_deletions"]=Array(ModelSketchConstraintRemovalSchema430.Change());
        data["properties"]!["model_sketch_visibility_changes"]=Array(ModelSketchVisibilitySchema436.Change(Ref("ref")));
        data["properties"]!["model_profile_rebinds"]=Array(ModelProfileRebindSchema349.ChangeOutput(Ref("ref")));
        data["properties"]!["model_profile_regions"]=Array(ModelProfileRegionsSchema350.ChangeOutput(Ref("ref")));
        data["properties"]!["model_profile_elements"]=Array(ModelProfileElementsSchema358.ChangeOutput(Ref("ref")));
        data["properties"]!["model_extrusion_extents"]=Array(ModelExtrusionExtentsSchema351.ChangeOutput(Ref("ref")));
        var datumChange=Object(("operation_index",Integer()),("body_ref",Ref("ref")),("body_mc_id",Text()),("last_item_id",Text()),("datum",ModelDatumPlaneSchema353.Read()),("native_representations",Array(Ref("ref"))),("requested_native_angle_radians",Nullable(Number())),("created_variable_ids",Nullable(Array(Text()))));
        var pointRecord=Object(("X",Number()),("Y",Number()),("Z",Number()));
        var originalFrame=Object(("Origin",(JsonObject)pointRecord.DeepClone()),("XAxis",(JsonObject)pointRecord.DeepClone()),("YAxis",(JsonObject)pointRecord.DeepClone()));
        var recoveryPreparation=Object(("sketch_id",Text()),("retained_support_id",Text()),
            ("coordinate_system",new JsonObject{["type"]="string",["const"]="native_sketch_local"}),
            ("length_unit",new JsonObject{["type"]="string",["const"]="Millimeters"}),
            ("geometry",Array(Object(("id",Text()),("geometry",AnyObject())))),("controls",Array(AnyObject())),
            ("original_frame_wcs",Nullable(originalFrame)),("placement_policy",Text()));
        var frameReference=Object(("is_empty",Bool()),("native_id",Nullable(Text())),
            ("complex_form",new JsonObject{["type"]="string",["enum"]=new JsonArray("empty","simple_id_only","canonical_simple_complex_id")}),
            ("parameter",Nullable(Number())),("parameter_bits",new JsonObject{["type"]="string",["pattern"]="^[0-9A-F]{16}$"}),
            ("gs_marker",Integer()),("data",Integer()));
        recoveryPreparation["properties"]!["frame_references"]=Object(("point_ref",(JsonObject)frameReference.DeepClone()),
            ("axis_ref",(JsonObject)frameReference.DeepClone()),("axis_ref2",(JsonObject)frameReference.DeepClone()),
            ("axis_is_x",Bool()),("invert_axis",Bool()));
        datumChange["properties"]!["recovery_preparation"]=recoveryPreparation;
        data["properties"]!["model_datum_planes"]=Array(datumChange);
        data["properties"]!["datum_plane_revisions"]=Array(ModelDatumPlaneRevisionSchema354.Change(Ref("ref")));
        data["properties"]!["hatch_fill_changes"]=Array(HatchFillSchema377.Change(Ref("ref")));
        data["properties"]!["dimension_association_changes"]=Array(DimensionAssociationSchema385.Change(Ref("ref")));
        data["properties"]!["hatch_boundary_changes"]=Array(HatchBoundarySchema349.Change(Ref("ref")));
        data["properties"]!["hatch_association_changes"]=Array(HatchAssociationSchema358.Change(Ref("ref")));
        data["properties"]!["model_revolution_features"]=Array(Object(("operation_index",Integer()),("source",Ref("ref")),("body_ref",Ref("ref")),
            ("body_mc_id",Text()),("sketch_id",Text()),("profile_id",Text()),("feature_id",Text()),("angle_variable_id",Text()),
            ("part_operation",Text()),("angle_degrees",Number()),("axis_ref",Ref("ref")),("source_refs",Array(Ref("ref"))),
            ("volume_before",Number()),("volume_after",Number()),("source_representation_replaced",Bool())));
        data["properties"]!["model_edge_features"]=Array(Object(("source",Ref("ref")),("body_ref",Ref("ref")),("body_mc_id",Text()),
            ("feature_id",Text()),("variable_id",Text()),("kind",Text()),("edge_ids",Array(Text())),("size",Number()),
            ("volume_before",Number()),("volume_after",Number()),("source_representation_replaced",Bool())));
        data["properties"]!["manufacturing_changes"]=Array(Object(("operation",Text()),("body_ref",Ref("ref")),("body_mc_id",Text()),("feature_id",Text()),("parameters",AnyObject())));
        data["properties"]!["manufacturing_changes"]!["items"]!["properties"]!["datum_plane"]=ModelDatumPlaneSchema353.Read();
        data["properties"]!["model_pattern_changes"]=Array(Object(("operation",Text()),("source",Ref("ref")),("body_ref",Ref("ref")),
            ("body_mc_id",Text()),("feature_id",Text()),("source_feature_id",Nullable(Text())),
            ("direction_x_ref",Nullable(Ref("ref"))),("direction_y_ref",Nullable(Ref("ref"))),
            ("count_x",Integer()),("count_y",Integer()),("step_x",Number()),("step_y",Number()),
            ("volume_before",Number()),("volume_after",Number()),("source_representation_replaced",Bool())));
        data["properties"]!["model_pattern_changes"]!["items"]!["properties"]!["source_feature_ids"]=Array(Text());
        data["properties"]!["model_pattern_changes"]!["items"]!["properties"]!["native_source_item_ids"]=Array(Text());
        data["properties"]!["model_pattern_changes"]!["items"]!["properties"]!["source_binding_scope"]=Text();
        data["properties"]!["model_pattern_changes"]!["items"]!["properties"]!["previous_source_feature_ids"]=Array(Text());
        data["properties"]!["model_pattern_changes"]!["items"]!["properties"]!["sources_retargeted"]=Bool();
        data["properties"]!["model_pattern_changes"]!["items"]!["properties"]!["direction_x"]=ModelPatternDirectionSchema439.Binding();
        data["properties"]!["model_pattern_changes"]!["items"]!["properties"]!["direction_y"]=ModelPatternDirectionSchema439.Binding();
        data["properties"]!["model_pattern_element_changes"]=Array(Object(("source",Ref("ref")),("body_ref",Ref("ref")),
            ("body_mc_id",Text()),("feature_id",Text()),("source_feature_id",Text()),
            ("index_x",Integer()),("index_y",Integer()),("suppressed",Bool()),
            ("volume_before",Number()),("volume_after",Number()),("source_representation_replaced",Bool())));
        data["properties"]!["model_shell_changes"]=Array(Object(("operation",Text()),("source",Ref("ref")),("body_ref",Ref("ref")),
            ("body_mc_id",Text()),("feature_id",Text()),("open_face_id",Text()),("open_face_ids",Array(Text())),("thickness",Number()),("previous_thickness",Nullable(Number())),
            ("volume_before",Number()),("volume_after",Number()),("source_representation_replaced",Bool())));
        data["properties"]!["model_feature_suppression_changes"]=Array(Object(("source",Ref("ref")),("body_ref",Ref("ref")),
            ("body_mc_id",Text()),("feature_id",Text()),("feature_type",Text()),("suppressed",Bool()),
            ("volume_before",Number()),("volume_after",Number()),("source_representation_replaced",Bool())));
        data["properties"]!["model_rebuilds"]=Array(Object(("source",Ref("ref")),("body_ref",Ref("ref")),("body_mc_id",Text()),
            ("last_item_id",Text()),("native_rebuild_accepted",Bool()),("model_healthy",Bool()),("source_representation_replaced",Bool()),
            ("unresolved_nodes",Array(Object(("mc_id",Text()),("type",Text()),("status",Text()),("suppressed",Bool()),("beyond_end_of_part",Bool()),("source_verification_error",Nullable(Text()))))),
            ("unresolved_count",Integer()),("unresolved_nodes_complete",Bool()),("suppressed_feature_ids",Array(Text())),
            ("suppressed_feature_count",Integer()),("suppressed_feature_ids_complete",Bool())));
        data["properties"]!["associative_array_changes"]=Array(AssociativeArraySchema431.Change(Ref("ref")));
        data["properties"]!["model_sketch_changes"]=Array(Object(("body_ref",Ref("ref")),("sketch_id",Text()),("element_ids",Array(Text())),("volume_before",Number()),("volume_after",Number())));
        data["properties"]!["model_placement_changes"]=Array(Object(("body_ref",Ref("ref")),("feature_id",Text()),("kind",Text()),("variable_ids",Array(Text())),("anchor_ref",Nullable(Ref("ref")))));
        data["properties"]!["sketch_constraint_changes"]=Array(Object(("sketch_id",Text()),("constraint_id",Text()),("kind",Text()),
            ("first_element_id",Text()),("second_element_id",Nullable(Text())),("variable_id",Nullable(Text())),("value",Nullable(Number()))));
        data["properties"]!["model_variables_created"]=Array(Object(("source_ref",Ref("ref")),("variable_id",Text()),("name",Text()),("expression",Text()),("value",Number()),("value_unit",Text())));
        data["properties"]!["drawing_circle_changes"]=Array(Object(("operation",Text()),("source_ref",Ref("ref")),("constraint_id",Nullable(Text())),("variable_id",Nullable(Text())),
            ("radius",Number()),("body_ref",Nullable(Ref("ref"))),("sketch_id",Nullable(Text())),("projection_id",Nullable(Text())),("feature_id",Nullable(Text())),("rebuilt_bodies",Array(Ref("ref")))));
        data["properties"]!["drawing_profile_changes"]=Array(Object(("operation",Text()),("source_refs",Array(Ref("ref"))),
            ("constraints",Array(Object(("constraint_id",Text()),("kind",Text()),("first_handle",Text()),("second_handle",Nullable(Text())),("variable_id",Nullable(Text())),("value",Nullable(Number()))))),
            ("body_ref",Nullable(Ref("ref"))),("sketch_id",Nullable(Text())),("projection_ids",Array(Text())),("feature_id",Nullable(Text())),("rebuilt_bodies",Array(Ref("ref")))));
        data["properties"]!["model_parameter_names"]=Array(Object(("body_ref",Ref("ref")),("variable_id",Text()),("previous_name",Text()),("name",Text()),("rewritten_expression_ids",Array(Text()))));
        var relation431=data["properties"]!["drawing_profile_changes"]!["items"]!["properties"]!["constraints"]!["items"]!["properties"]!.AsObject();
        relation431["third_handle"]=Nullable(Text());
        foreach(var field in new[]{"first_segment_index","second_segment_index","third_segment_index"})relation431[field]=Nullable(Integer());
        data["properties"]!["drawing_profile_changes"]!["items"]!["properties"]!["variable_changes"]=Array(Object(("variable_id",Text()),("name",Text()),("expression",Text()),("value",Number()),("value_unit",Text())));
        data["properties"]!["drawing_profile_changes"]!["items"]!["properties"]!["removed_constraint_ids"]=Array(Text());
        data["properties"]!["drawing_profile_changes"]!["items"]!["properties"]!["removed_variable_ids"]=Array(Text());
        data["properties"]!["drawing_profile_changes"]!["items"]!["properties"]!["retained_sweep_parts"]=Array(Object(
            ("body_ref",Ref("ref")),("body_id",Text()),("owner_document_id",Text()),("sketch_id",Text()),("constraint_id",Text()),("source_variable_id",Text()),("edited_source_id",Text()),
            ("source_radius_before",Number()),("source_radius_after",Number()),("consumers",Array(Object(("source_id",Text()),("source_handle",Text()),("projection_id",Text()),
                ("profile_id",Text()),("feature_id",Text()),("path_ids",Array(Text())),("taper_variable_id",Text()),("part_operation",Text()),("source_radius",Number()),("profile_radius",Number()),("driven_by_requested_source",Bool())))),("observation_scope",Text())));
        data["properties"]!["solid_boolean_results"]=Array(Object(("operation_index",Integer()),("operator",Text()),("target",Ref("ref")),
            ("tools",Array(Ref("ref"))),("volume_before",Number()),("volume_after",Number())));
        data["properties"]!["solid_imprint_changes"]=Array(SolidImprintSchema393.Change(Ref("ref")));
        data["properties"]!["solid_stock_changes"]=Array(SolidStockSchema353.Change(Ref("ref")));
        data["properties"]!["tolerance_changes"]=Array(ExtendedAnnotationSchema356.Change(Ref("ref")));
        data["properties"]!["table_topology_changes"]=Array(TableTopologySchema354.Change(Ref("ref")));
        data["properties"]!["region_boolean_changes"]=Array(RegionBooleanSchema.Change());
        return data;
    }
    private static JsonObject CameraRendererData370()=>Object(("view_document_id",Text()),("view_window_handle",Text()),
        ("eye_mm",Point()),("target_mm",Point()),("up_direction_wcs",Point()),("perspective",Bool()),
        ("native_scale_or_angle",Number()),("pixel_size_mm",Number()),("viewport_pixels",Point(2)),("requested_projection_matches",Nullable(Bool())),("observation_scope",Text()));
    private static JsonObject CameraViewData()
    {
        var token=Text(); token["pattern"]="^[0-9A-F]{64}$";
        var width=Number();width["description"]="Effective orthographic width in drawing units: view height times current viewport pixel aspect ratio; a saved DXF record may contain a stale width.";
        var style=Text();style["description"]="Opaque native view style handle within this camera lifetime; never resolve it as a drawing entity handle.";
        var mode=Text();mode["description"]="Actual NativeView render mode; FaceNEdgeFine means shaded surfaces with edges.";
        return Object(("camera_token",token),("viewport_number",Integer()),("viewport_handle",Nullable(Text())),
            ("target_wcs",Point()),("direction_wcs",Point()),("center_view",Point(2)),("width",width),("height",Number()),
            ("twist_radians",Number()),("projection",new JsonObject{["const"]="orthographic"}),
            ("front_clip_enabled",new JsonObject{["const"]=false}),("back_clip_enabled",new JsonObject{["const"]=false}),
            ("front_clip_distance",Number()),("back_clip_distance",Number()),("render_mode",mode),("visual_style_handle",Nullable(style)));
    }
    private static JsonObject Color() => Object(("method", Text()), ("aci", Nullable(Integer())), ("rgb", Nullable(Point())));
    private static JsonObject SplineGeometry() => NativeCurveInspectionSchema342.Spline();
    private static JsonObject SolidTopology()
    {
        var faces=Array(Object(("native_id",Text()),("surface_runtime_type",Text()),("area",Nullable(Number())),("area_unavailable_reason",Nullable(Text())),("bounds",Object(("min",Point()),("max",Point()))),("forward",Bool())));
        faces["items"]!["properties"]!["bounds_kind"]=new JsonObject{["type"]="string",["description"]="planar_boundary gives extrema of the complete planar face boundary; body_conservative encloses a curved face using the source body bounds and cannot locate that face precisely. Omitted by older bridges."};
        faces["items"]!["properties"]!["analytic_surface"] = new JsonObject
        {
            ["type"] = "string", ["enum"] = new JsonArray("plane", "circular_cylinder"),
            ["description"] = "Native supporting surface hint when available. Use the face's native_id with analyze_geometry to read a diameter or measure supporting axes and planes."
        };
        var edges=Array(Object(("native_id",Text()),("curve_runtime_type",Text()),("start_vertex",Nullable(Point())),("end_vertex",Nullable(Point())),("forward",Bool())));
        edges["items"]!["properties"]!["circle"]=Nullable(Object(("center",Point()),("normal",Point()),("radius",Number())));
        var result=Object(("source_ref",Ref("ref")),("coordinate_system",Text()),("reference_scope",Text()),
            ("snapshot_token",new JsonObject{["type"]="string",["pattern"]="^[0-9A-F]{64}$"}),("complete",Bool()),
            ("record_history",Bool()),("show_history",Bool()),("faces",faces),("edges",edges));
        var modelFaces=Array(Object(("marker",Text()),("model_face_id",Text()),("surface_type",Nullable(Text())),
            ("plane",Nullable(Object(("origin",Point()),("normal",Point())))),
            ("detail_unavailable_reason",Nullable(Text()))));
        modelFaces["items"]!["properties"]!["cylinder"]=Nullable(Object(("axis_origin",Point()),("axis_direction",Point()),("radius",Number()),("length",Number())));
        result["properties"]!["native_model_edges"]=Object(("source_ref",Ref("ref")),("body_mc_id",Text()),("last_item_id",Text()),("edge_token",Text()),("edges",Array(Object(("marker",Text()),("model_edge_id",Text()),("curve_type",Text()),("start",Point()),("end",Point()),("length",Number()),("circle",Nullable(Object(("center",Point()),("normal",Point()),("radius",Number()))))))));
        result["properties"]!["native_model_faces"]=Object(("source_ref",Ref("ref")),("body_mc_id",Text()),
            ("last_item_id",Text()),("face_token",new JsonObject{["type"]="string",["pattern"]="^[0-9A-F]{64}$"}),
            ("identity_scope",Text()),("complete",Bool()),("faces",modelFaces));
        return result;
    }
    private static JsonObject DrawingViews()
    {
        void AddOptional(JsonObject target,string name,JsonObject schema)=>target["properties"]![name]=schema;
        JsonObject Plane()=>Object(("origin",Point()),("axis_u",Point()),("axis_v",Point()),("normal",Point()));
        JsonObject Section()=>Object(("mc_id",Text()),("state",Text()),("status",Text()),("vertices",Array(Point())),("vertical_direction",Point()),("viewing_direction",Point()),("associated_plane_id",Nullable(Text())),("depth",Number()),("height_above",Number()),("height_below",Number()),("live_section_enabled",Bool()),("view_ids",Array(Text())),("top_source_ids",Array(Text())));
        JsonObject Break()=>Object(("ref",Ref("ref")),("mc_id",Text()),("adapter_schema",Text()),("class_id",Text()),("parameter_space",Text()),("break_type",Text()),("break_type_index",Integer()),("gap_between_lines",Number()),("skew_vector",Point()),("target_view_id",Text()));
        var projected=Object(("view_ref",Ref("ref")),("coordinate_system",Text()),("unit",Text()),
            ("geometry_details_complete",Bool()),("element_identity",Text()),
            ("items",Array(Object(("index",Integer()),("native_geometry_type",Nullable(Text())),
                ("status",Text()),("geometry",Nullable(AnyObject())),("unavailable_reason",Nullable(Text()))))));
        var projectedSource = Object(("status", Text()), ("unavailable_reason", Nullable(Text())),
            ("view_marker", Nullable(Text())), ("source_ref", Nullable(Ref("ref"))),
            ("native_edge_id", Nullable(Text())), ("native_ref_key", Nullable(Text())));
        AddOptional(projected["properties"]!["items"]!["items"]!.AsObject(), "projection_source", projectedSource);
        AddOptional(projected,"geometry_scope",Text());AddOptional(projected,"total_elements",Integer());AddOptional(projected,"start_index",Integer());
        AddOptional(projected,"next_index",Nullable(Integer()));AddOptional(projected,"complete",Bool());
        var result=Object(("layout_ref",Ref("ref")),("layout_name",Text()),("space",Text()),("identity_scope",Text()),
            ("reference_scope",Text()),("coordinate_system",Text()),("geometry_state",Text()),("selected_geometry",Nullable(projected)),("scanned_entities",Integer()),("complete",Bool()),
            ("items",Array(Object(("ref",Ref("ref")),("mc_id",Text()),("name",Text()),("status",Text()),("suppressed",Bool()),("direction",Text()),
                ("section_id",Nullable(Text())),("section",Nullable(Section())),("parent_view_id",Nullable(Text())),("root_view_id",Nullable(Text())),("child_view_ids",Array(Text())),
                ("attached_break_ids",Array(Text())),("breaks",Nullable(Array(Break()))),("breaks_unavailable_reason",Nullable(Text())),
                ("top_source_ids",Nullable(Array(Text()))),("source_inventory_available",Bool()),("view_plane",Plane()),("projection_plane",Plane()),
                ("model_scale",Number()),("design_scale",Number()),("is_slice",Bool()),("is_based_on_section",Bool()),("is_root_section_view",Bool()),
                ("depends_on_slice",Bool()),("auto_update_enabled",Bool()),("show_hidden_lines",Bool()),("show_hatches",Bool()),("show_tangent_edges",Bool())))));
        var item=result["properties"]!["items"]!["items"]!.AsObject();
        AddOptional(item,"native_owner_document_id",Nullable(Text()));
        AddOptional(result,"selected_source_identity",PaperDrawingViewsSchema356.SelectedSources(Ref("ref")));
        AddOptional(item,"align_on_parent",Bool());
        AddOptional(item,"attached_local_cut_ids",Nullable(Array(Text())));
        AddOptional(item,"outer_cut_sketch_id",Nullable(Text()));
        AddOptional(item,"section_cut",Nullable(SectionCutSchema351.Read(Ref("ref"))));
        AddOptional(item,"section_cut_unavailable_reason",Nullable(Text()));
        AddOptional(item,"view_token",Nullable(Text()));
        AddOptional(item,"view_token_unavailable_reason",Nullable(Text()));
        AddOptional(item,"source_refs",Nullable(Array(Ref("ref"))));
        AddOptional(item,"source_refs_unavailable_reason",Nullable(Text()));
        return result;
    }
    private static JsonObject SketchConstraints()
    {
        JsonObject Reference() => Nullable(Object(("object_id",Text()),("subentity_id",Nullable(Text())),("polyline_segment_index",Integer()),
            ("point",Nullable(Point())),("point_unavailable_reason",Nullable(Text()))));
        var data=Object(("source_ref",Ref("ref")),("sketch_id",Text()),("network",Text()),("manager_document_id",Nullable(Text())),("scope_id",Nullable(Text())),
            ("identity_scope",Text()),("reference_coordinate_system",Text()),("solver_status",Text()),
            ("constraints",Array(Object(("mc_id",Text()),("type",Text()),("is_geometrical",Bool()),("is_dimensional",Bool()),
                ("first",Reference()),("second",Reference()),("third",Reference()),("parameter_value",Nullable(Number())),("parameter_value_unit",Text()),
                ("variable_id",Nullable(Text())),("variable_name",Nullable(Text())),("variable_expression",Nullable(Text())),("dimension_id",Nullable(Text()))))));
        data["properties"]!["presentation"]=ModelSketchVisibilitySchema436.Presentation();
        return data;
    }
    private static JsonObject DocumentConstraints()
    {
        JsonObject Reference()=>Nullable(Object(("object_id",Text()),("source_ref",Nullable(Ref("ref"))),("subentity_id",Nullable(Text())),
            ("polyline_segment_index",Integer()),("point",Nullable(Point())),("point_unavailable_reason",Nullable(Text()))));
        var data=Object(("source_refs",Array(Ref("ref"))),("network",Text()),("plane",Text()),("manager_document_id",Text()),("scope_id",Nullable(Text())),
            ("identity_scope",Text()),("reference_coordinate_system",Text()),("solver_status",Text()),("scanned_constraints",Integer()),("complete",Bool()),("completeness_scope",Text()),
            ("constraints",Array(Object(("mc_id",Text()),("type",Text()),("is_geometrical",Bool()),("is_dimensional",Bool()),("first",Reference()),("second",Reference()),("third",Reference()),
                ("parameter_value",Nullable(Number())),("parameter_value_unit",Text()),("parameter_value_source",new JsonObject{["type"]="string",["enum"]=new JsonArray("constraint_manager","document_variable","unavailable","not_applicable")}), ("variable_id",Nullable(Text())),("variable_name",Nullable(Text())),("variable_expression",Nullable(Text())),
                ("dimension_id",Nullable(Text())),("references_outside_selection",Array(Text()))))));
        data["properties"]!["workplane"]=WorkplaneSchema.Input();
        data["properties"]!["variable_inventory"]=Nullable(Object(("scope",Text()),("dependency_direction",Text()),("value_scope",Text()),
            ("variables",Array(Object(("mc_id",Text()),("name",Text()),("type",Text()),("expression",Text()),("value",Nullable(Number())),("value_available",Bool()),
                ("value_unit",Text()),("metadata_source",Text()),("host_object_id",Nullable(Text())),("downstream_variables",Nullable(Array(Text()))),("downstream_objects",Nullable(Array(Text()))),
                ("downstream_constraints",Nullable(Array(Text()))),("constraint_ids",Array(Text())),("source_refs",Array(Ref("ref"))))))));
        return data;
    }
    private static JsonObject ModelVariables()
    {
        var schema=Object(("network", Text()), ("identity_scope", Text()), ("dependency_direction", Text()),
        ("value_scope", Text()), ("mc_document_id", Text()), ("source_ref", Ref("ref")),
        ("variables", Array(Object(("mc_id", Text()), ("name", Text()), ("type", Text()), ("expression", Text()),
            ("value", Nullable(Number())), ("value_available", Bool()), ("value_unit", Text()), ("host_object_id", Nullable(Text())),
            ("downstream_variables", Nullable(Array(Text()))), ("downstream_objects", Nullable(Array(Text())))))));
        var item=schema["properties"]!["variables"]!["items"]!["properties"]!.AsObject();
        foreach(string field in new[]{"owner_kind","sketch_id","constraint_id","control_kind"})item[field]=Nullable(Text());
        schema["properties"]!["parameter_binding_notes"]=Array(Object(("host_object_id",Text()),("code",Text()),("reason",Text())));
        return schema;
    }
    private static JsonObject ModelHistory()
    {
        var node = Object(("mc_id", Text()), ("runtime_type", Text()), ("name", Nullable(Text())),
            ("status", Nullable(Text())), ("suppressed", Nullable(Bool())), ("host_solid_id", Nullable(Text())),
            ("dependencies", Array(Text())), ("parameters", Nullable(AnyObject())), ("detail_unavailable_reason", Nullable(Text())));
        ((JsonObject)node["properties"]!)["native_order_before_id"]=Nullable(Text());
        node["properties"]!["parameters"]=Nullable(new JsonObject{["anyOf"]=new JsonArray(
            ModelProfileLifecycleSchema340.ReadProperties(),AnyObject())});
        var body = Object(("ref", Ref("ref")), ("mc_id", Text()), ("mc_document_id", Text()),
            ("last_item_id", Nullable(Text())), ("complete", Bool()), ("nodes", Array(node)));
        body["properties"]!["edit_routes"]=AnyObject();
        body["properties"]!["reference_planes"]=Array(Object(("name",Text()),("plane_id",Text()),("scope",Text())));
        var result=Object(("identity_scope", Text()), ("graph_scope", Text()), ("items", Array(body)));
        result["properties"]!["document_sketch_inventory"]=Nullable(ModelSketchLifecycleSchema362.InventoryOutput());
        result["properties"]!["document_sketches"]=Array(ModelSketchLifecycleSchema362.ReadOutput());
        result["properties"]!["document_profile_inventory"]=Nullable(ModelProfileLifecycleSchema340.InventoryOutput());
        result["properties"]!["document_profiles"]=Array(ModelProfileLifecycleSchema340.ReadProperties());
        result["properties"]!["profile_regions"]=Array(ModelProfileRegionsSchema350.ReadOutput());
        result["properties"]!["profile_elements"]=Array(ModelProfileElementsSchema358.ReadOutput(Ref("ref")));
        result["properties"]!["extrusion_extents"]=Array(ModelExtrusionExtentsSchema351.ReadOutput());
        result["properties"]!["feature_deletions"]=Array(ModelFeatureDeletionSchema378.ReadOutput(Ref("ref")));
        result["properties"]!["sketch_constraint_parameter_edits"]=Array(ModelSketchConstraintParameterSchema379.ReadOutput());
        result["properties"]!["document_datum_plane_inventory"]=Nullable(ModelDatumPlaneSchema353.Inventory());
        result["properties"]!["datum_planes"]=Array(ModelDatumPlaneSchema353.Read());
        result["properties"]!["datum_plane_revisions"]=Array(ModelDatumPlaneRevisionSchema354.Read(Ref("ref")));
        return result;
    }
    private static JsonObject GeometryAnalysis()
    {
        JsonObject Common(string mode) => Object(("mode", new JsonObject { ["const"] = mode }), ("coordinate_system", new JsonObject { ["const"] = "WCS" }),
            ("units", Object(("name", Text()), ("value", Integer()))), ("solver", new JsonObject { ["const"] = "native_kernel" }));
        void Add(JsonObject target, string name, JsonObject schema)
        {
            target["properties"]![name] = schema;
            target["required"]!.AsArray().Add(name);
        }
        var metrics = Common("metrics");
        metrics["properties"]!["solver"]=new JsonObject{["type"]="string",["enum"]=new JsonArray("native_kernel","native_kernel_with_analytic_linear_spline_metrics")};
        Add(metrics, "items", Array(Object(("ref", Ref("ref")), ("type", Text()), ("length", Number()), ("closed", Bool()), ("area", Nullable(Number())),
            ("area_source",new JsonObject{["type"]="string",["enum"]=new JsonArray("native_kernel","analytic_degree1_simple_xy_polygon","unavailable")}),
            ("bounds", Object(("min", Point()), ("max", Point()))), ("nearest", Nullable(Object(("point", Point()), ("distance", Number())))),
            ("metric_source",new JsonObject{["type"]="string",["enum"]=new JsonArray("native_kernel","analytic_degree1_nurbs_length_and_bounds")}))));
        Add(metrics, "complete", Bool()); Add(metrics, "remaining_handles", Array(Text()));
        Add(metrics, "area_scope", Text()); Add(metrics, "distance_scope", Text());
        var intersections = Common("intersections");
        Add(intersections, "first", Ref("ref")); Add(intersections, "second", Ref("ref")); Add(intersections, "points", Array(Point()));
        Add(intersections, "result_scope", Text()); Add(intersections, "overlap_evaluated", new JsonObject { ["const"] = false }); Add(intersections, "warning", Text());
        var distance=Common("distance");
        distance["properties"]!["solver"]=new JsonObject{["type"]="string",["enum"]=new JsonArray("native_kernel","analytic_finite_segments","analytic_segment_circle","analytic_segment_arc")};
        Add(distance,"first",Ref("ref"));Add(distance,"second",Ref("ref"));Add(distance,"first_point",Point());Add(distance,"second_point",Point());
        var nonnegative=Number();nonnegative["minimum"]=0;Add(distance,"distance",nonnegative);
        Add(distance,"result_scope",new JsonObject{["const"]="minimum_euclidean_distance_between_finite_curves"});
        Add(distance,"overlap_evaluated",new JsonObject{["const"]=false});Add(distance,"warning",Text());
        var relations=Common("relations");Add(relations,"first",Ref("ref"));Add(relations,"second",Ref("ref"));
        var relationPoints=Array(Point());Add(relations,"points",relationPoints);
        var positiveLength=Number();positiveLength["exclusiveMinimum"]=0;
        var overlaps=Array(Object(("first_start",Point()),("first_end",Point()),("second_start",Point()),("second_end",Point()),("length",positiveLength)));
        Add(relations,"overlaps",overlaps);Add(relations,"overlap_evaluated",new JsonObject{["const"]=true});
        Add(relations,"result_scope",new JsonObject{["enum"]=new JsonArray("finite_curves_in_WCS_XY_Z0","finite_curves_in_explicit_workplane")});Add(relations,"warning",Text());
        var containment=Common("containment");Add(containment,"boundary",Ref("ref"));Add(containment,"point",Point());
        containment["properties"]!["solver"]=new JsonObject{["const"]="native_bounds_and_brep"};
        Add(containment,"classification",new JsonObject{["enum"]=new JsonArray("inside","outside","boundary")});
        Add(containment,"method",new JsonObject{["const"]="native_bounds_then_brep_face"});
        Add(containment,"tolerance_policy",new JsonObject{["const"]="native_kernel_with_conservative_bounds_exclusion"});
        Add(containment,"result_scope",new JsonObject{["enum"]=new JsonArray("single_simple_contour_in_WCS_XY_Z0","single_simple_contour_in_explicit_workplane")});
        var result=new JsonObject { ["oneOf"] = new JsonArray(metrics, intersections,distance,relations,containment) };
        SolidGeometryAnalysisSchema.ExtendOutput(result);
        result["oneOf"]!.AsArray().Add(BoundaryTraceSchema408.Output());
        result["oneOf"]!.AsArray().Add(SolidMeasurementSchema409.Output());
        return result;
    }
    private static JsonObject MTextFormattingOutput()
    {
        var format=Object(("text_style_ref",Ref("ref")),("line_spacing_style",Text()),("line_spacing_factor",Number()),
            ("flow_direction",Text()),("column_type",Text()),("background",Object(("enabled",Bool()),("use_drawing_background",Bool()),("scale_factor",Number()),("color",Color()))));
        var editing=Object(("editable",Bool()));
        editing["properties"]!["mtext_token"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-F]{64}$"};
        editing["properties"]!["unavailable_reason"]=Text();
        format["properties"]!["editing"]=editing;
        return format;
    }
    private static JsonObject DimensionGeometry()
    {
        JsonObject Kind(string kind, JsonObject definition)
        {
            var result=Object(
            ("dimension_kind", new JsonObject { ["type"] = "string", ["const"] = kind }), ("definition", definition),
            ("measurement", Nullable(Number())), ("measurement_unavailable_reason", Nullable(Text())), ("measurement_unit", new JsonObject { ["type"] = "string", ["const"] = kind is "LineAngularDimension2" or "Point3AngularDimension" ? "radians" : "drawing_units" }),
            ("normal", Point()), ("elevation", Number()), ("dimension_style", Ref("ref")),
            ("dimension_text", Text()), ("text_position", Point()), ("text_rotation", Number()));
            result["properties"]!["native_details"]=Nullable(ExtendedAnnotationSchema356.ArcDetails());
            result["properties"]!["native_binding"]=Object(("opened_runtime_type",Text()),("definition_runtime_type",Text()),("borrowed_wrapper",Bool()),("scope",Text()));
            result["properties"]!["format"]=Nullable(DimensionFormatSchema.Read());
            result["properties"]!["format_unavailable_reason"]=Nullable(Text());
            result["properties"]!["display"]=Nullable(DimensionEditingSchema341.ReadDisplay());
            result["properties"]!["display_unavailable_reason"]=Nullable(Text());
            result["properties"]!["association"]=DimensionAssociationSchema385.Read();
            return result;
        }
        return new JsonObject { ["oneOf"] = new JsonArray(
            Kind("AlignedDimension", Object(("xline1_point", Point()), ("xline2_point", Point()), ("dimline_point", Point()))),
            Kind("RotatedDimension", Object(("xline1_point", Point()), ("xline2_point", Point()), ("dimline_point", Point()), ("rotation", Number()))),
            Kind("RadialDimension", Object(("center", Point()), ("chord_point", Point()), ("leader_length", Number()))),
            Kind("DiametricDimension", Object(("chord_point", Point()), ("far_chord_point", Point()), ("leader_length", Number()))),
            Kind("LineAngularDimension2", Object(("xline1_start", Point()), ("xline1_end", Point()), ("xline2_start", Point()), ("xline2_end", Point()), ("arc_point", Point()))),
            Kind("Point3AngularDimension", Object(("center_point", Point()), ("xline1_point", Point()), ("xline2_point", Point()), ("arc_point", Point()))),
            Kind("ArcDimension", Object(("center_point",Point()),("xline1_point",Point()),("xline2_point",Point()),("arc_point",Point()))),
            Kind("RadialDimensionLarge", Object(("center",Point()),("chord_point",Point()),("override_center",Point()),("jog_point",Point()),("jog_angle",Number()))),
            Kind("OrdinateDimension", Object(("origin", Point()), ("defining_point", Point()), ("leader_end_point", Point()), ("using_x_axis", Bool())))) };
    }
    public static JsonElement For(string operation)
    {
        if (operation == "get_operation_result") return OperationResultSchema432.Output();
        var definitions = new JsonObject
        {
            ["error"] = Object(("code", Text()), ("message", Text()), ("retryable", Bool()), ("details", Nullable(AnyObject()))),
            ["context"] = Object(("bridge_instance_id", Text()), ("document_id", Text()), ("revision", Text()), ("captured_at_utc", Text())),
            ["ref"] = Object(("bridge_instance_id", Text()), ("document_id", Text()), ("handle", Text())),
            ["layer"] = Object(("name", Text()), ("handle", Nullable(Text()))),
            ["summary"] = Object(("ref", Ref("ref")), ("type", Text()), ("runtime_type", Text()), ("layer", Nullable(Ref("layer"))),
                ("space", Text()), ("detail_support", Text())),
            ["descriptor"] = Object(("protocol_version", Text()), ("bridge_instance_id", Text()), ("pipe_name", Text()), ("process_id", Integer()),
                ("process_start_utc_ticks", Integer()), ("session_id", Integer()), ("host_version", Text()), ("bridge_version", Text()), ("created_at_utc", Text())),
            ["line"] = Object(("start", Point()), ("end", Point()), ("normal", Point()), ("thickness", Number())),
            ["point"] = Object(("position", Point()), ("normal", Point()), ("thickness", Number())),
            ["ray"] = Object(("base_point", Point()), ("unit_direction", Point()), ("extent", new JsonObject { ["const"] = "half_infinite" })),
            ["xline"] = Object(("base_point", Point()), ("unit_direction", Point()), ("extent", new JsonObject { ["const"] = "infinite_both_directions" })),
            ["circle"] = Object(("center", Point()), ("radius", Number()), ("normal", Point()), ("thickness", Number())),
            ["ellipse"] = Object(("center",Point()),("major_axis",Point()),("minor_axis",Point()),("major_radius",Number()),("minor_radius",Number()),
                ("radius_ratio",Number()),("normal",Point()),("start_point",Point()),("end_point",Point()),("start_parameter",Number()),("end_parameter",Number()),("closed",Bool())),
            ["spline"] = SplineGeometry(),
            ["polyline3d"] = Object(("kind",new JsonObject{["const"]="polyline3d"}),("polyline_type",Text()),("closed",Bool()),("planar",Bool()),("vertex_count",Integer()),("vertices",Array(Point()))),
            ["region"] = RegionWithOptionalBooleanGeometry(),
            ["helix"] = Object(("axis_origin",Point()),("axis_direction",Point()),("start_point",Point()),("base_radius",Number()),("top_radius",Number()),("height",Number()),("turns",Number()),("right_handed",Bool())),
            ["block_reference"] = Object(("inspection_scope",new JsonObject { ["const"] = "reference_only", ["description"] = "Stored placement, owner/definition identities and common display properties of the selected top-level native insertion in Model or Paper. Definition members, attributes and evaluated dynamic geometry are not inspected." }),
                ("owner_ref",Ref("ref")),("definition_ref",Ref("ref")),("position",Point()),("rotation",Number()),("scale_factors",Point()),("normal",Point()),("is_dynamic",Bool())),
            ["wipeout"]=Object(("geometry_token",Nullable(Text())),("boundary_world",Nullable(Array(Point()))),("normal",Nullable(Point())),("native_mask",Bool()),("frame_display_scope",Text()),("editing_unavailable_reason",Nullable(Text()))),
            ["block_array"] = NativeBlockArraySchema384.Read(Ref("ref")),
            ["external_reference"] = ExternalReferenceSchema385.Read(Ref("ref")),
            ["arc"] = Object(("center", Point()), ("radius", Number()), ("normal", Point()), ("start_point", Point()), ("end_point", Point()),
                ("start_angle_ocs", Number()), ("end_angle_ocs", Number()), ("sweep_angle", Number()), ("thickness", Number())),
            ["polyline"] = Object(("closed", Bool()), ("elevation", Number()), ("normal", Point()), ("thickness", Number()), ("constant_width", Number()),
                ("vertex_count", Integer()), ("vertices", Array(Object(("point_wcs", Point()), ("point_ocs", Point(2)), ("bulge", Number()), ("start_width", Number()), ("end_width", Number()))))),
            ["polyline2d"] = Object(("representation", new JsonObject { ["const"] = "legacy_native_polyline2d" }), ("polyline_type", Text()),
                ("closed", Bool()), ("elevation", Number()), ("normal", Point()), ("thickness", Number()), ("constant_width", Number()),
                ("vertex_count", Integer()), ("vertices", Array(Object(("handle", Text()), ("point_wcs", Point()), ("stored_position_ocs", Point()),
                    ("vertex_type", Text()), ("bulge", Number()), ("start_width", Number()), ("end_width", Number())))),
                ("vertex_semantics", Text()), ("editing_scope", Text())),
            ["text_formatting"] = TextFormatSchema442.ExtendRead(Object(("text_style_ref",Ref("ref")),("oblique",Number()),("thickness",Number()),("mirrored_in_x",Bool()),("mirrored_in_y",Bool()))),
            ["text"] = Object(("text", Text()), ("position", Point()), ("alignment_point", Point()), ("height", Number()), ("width_factor", Number()),
                ("rotation", Number()), ("normal", Point()), ("horizontal_mode", Text()), ("vertical_mode", Text()), ("text_style", Text()),("formatting",Ref("text_formatting"))),
            ["mtext_formatting"] = MTextFormattingOutput(),
            ["mtext"] = Object(("contents", Text()), ("plain_text", Nullable(Text())), ("location", Point()), ("normal", Point()), ("direction", Point()),
                ("text_height", Number()), ("width", Number()), ("attachment", Text()), ("text_style", Text()),("formatting",Ref("mtext_formatting"))),
            ["tolerance"] = ExtendedAnnotationSchema356.Read(),
            ["dimension"] = DimensionGeometry(),
            ["solid3d"] = Object(("solid_kind", new JsonObject { ["const"] = "Solid3d" }),
                ("body_state", new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("empty", "body") }),
                ("volume", Nullable(Number())), ("centroid", Nullable(Point())), ("bounds", Nullable(Object(("min", Point()), ("max", Point())))),
                ("volume_unit", new JsonObject { ["const"] = "drawing_units_cubed" }), ("record_history", Bool()), ("show_history", Bool()),
                ("history_scope", new JsonObject { ["const"] = "native_flags_only_feature_graph_not_exposed" }),
                ("inspection_duration_ms", Integer()), ("budget_notice", Nullable(Text()))),
            ["mleader"] = Object(("annotation_kind", new JsonObject { ["const"] = "MLeader" }), ("content_type", Text()),
                ("contents", Nullable(Text())), ("plain_text", Nullable(Text())), ("content_location", Nullable(Point())), ("block_ref", Nullable(Ref("ref"))),
                ("normal", Point()), ("line_type", Text()), ("lines", Array(Object(("leader_index", Integer()), ("line_index", Integer()), ("vertices", Array(Point())))))),
            ["hatch"] = Object(("annotation_kind", new JsonObject { ["const"] = "Hatch" }), ("pattern_name", Text()), ("pattern_type", Text()),
                ("pattern_angle", Number()), ("pattern_scale", Number()), ("pattern_double", Bool()), ("associative", Bool()), ("normal", Point()), ("elevation", Number()),
                ("boundary_complete", Bool()), ("loops", Array(Object(("loop_type", Text()), ("boundary_kind", Text()),
                    ("vertices", Array(Object(("point_ocs", Point(2)), ("bulge", Number())))),("associated_handles",Array(Nullable(Text()))))))),
            ["table"] = Object(("annotation_kind", new JsonObject { ["const"] = "Table" }), ("position", Point()), ("direction", Point()), ("normal", Point()),
                ("width", Number()), ("height", Number()), ("rows", Integer()), ("columns", Integer()), ("row_heights",Array(Number())), ("column_widths",Array(Number())),
                ("cells", Array(Object(("row", Integer()), ("column", Integer()), ("text_contents", Array(Text())), ("text_observations", Array(Object(("content_index", Integer()), ("text_source", Text()), ("text_unavailable_reason", Nullable(Text())))))))), ("content_scope", Text())),
            ["properties"] = Object(("visible", Bool()), ("color", Color()),
                ("linetype", Text()), ("lineweight", Object(("mode", Text()), ("value_hundredth_mm", Nullable(Integer())), ("native_value", Integer()))))
        };
        // Observation, not a widening of the legacy curve create/edit schema.
        // Optional for older bridges; all current native curve readers publish it.
        foreach(string curveType in new[]{"line","arc","circle","polyline","polyline3d","ellipse","spline","helix"})
            definitions[curveType]!["properties"]!["native_curve"]=NativeCurveInspectionSchema342.ReadProperties();
        // Exact retained Helix admits honest unavailable optional planar facts.
        // Other curve observation and Spline geometry schemas stay unchanged.
        var helixObservation=definitions["helix"]!["properties"]!["native_curve"]!;
        helixObservation["properties"]!["planar"]=Nullable(Bool());
        helixObservation["properties"]!["planar_unavailable_reason"]=Nullable(Text());
        helixObservation["description"]="Current retained native Helix observation. planar may be null with planar_unavailable_reason if that optional getter is unavailable. curve_token covers complete native NURBS and Helix parameters; optional fit construction metadata and length/plane availability do not gate native face modeling.";
        foreach(string curveType in new[]{"line","polyline"})
        {
            definitions[curveType]!["properties"]!["native_stretch"]=Nullable(NativeStretchSchema345.ReadProperties());
            definitions[curveType]!["properties"]!["native_stretch_unavailable_reason"]=Nullable(Text());
        }
        // Optional for compatibility with bridges that predate the setting editor.
        definitions["properties"]!["properties"]!["linetype_scale"] = Number();
        AppearancePropertySchema.ExtendRead(definitions["properties"]!.AsObject());
        definitions["mleader"]!["properties"]!["style"]=Nullable(MLeaderStyleSchema337.LeaderReadSchema());
        definitions["mleader"]!["properties"]!["style_unavailable_reason"]=Nullable(Text());
        definitions["mleader"]!["properties"]!["leader_editing"]=Nullable(MLeaderGeometrySchema388.Read());
        definitions["mleader"]!["properties"]!["instance_editing"]=Nullable(MLeaderInstanceSchema442.Read());
        definitions["mleader"]!["properties"]!["instance_editing_unavailable_reason"]=Nullable(Text());
        definitions["mleader"]!["properties"]!["leader_editing_unavailable_reason"]=Nullable(Text());
        definitions["table"]!["properties"]!["contents_editing"] = Nullable(TableContentsSchema364.Read());
        definitions["table"]!["properties"]!["contents_editing_unavailable_reason"] = Nullable(Text());
        definitions["table"]!["properties"]!["editing"] = Nullable(TableEditingSchema.Read());
        definitions["table"]!["properties"]!["editing_unavailable_reason"] = Nullable(Text());
        definitions["hatch"]!["properties"]!["fill_editing"] = Nullable(HatchFillSchema377.Read());
        definitions["hatch"]!["properties"]!["fill_editing_unavailable_reason"] = Nullable(Text());
        definitions["hatch"]!["properties"]!["editing"] = Nullable(HatchPropertiesSchema.Read());
        definitions["hatch"]!["properties"]!["editing_unavailable_reason"] = Nullable(Text());
        definitions["hatch"]!["properties"]!["pattern_name"] = Nullable(Text());
        definitions["hatch"]!["properties"]!["pattern_type"] = Nullable(Text());
        definitions["hatch"]!["properties"]!["pattern_angle"] = Nullable(Number());
        definitions["hatch"]!["properties"]!["pattern_scale"] = Nullable(Number());
        definitions["hatch"]!["properties"]!["pattern_double"] = Nullable(Bool());
        definitions["hatch"]!["properties"]!["pattern_space"] = Nullable(Number());
        definitions["hatch"]!["properties"]!["origin_ocs"] = Nullable(Point(2));
        definitions["hatch"]!["properties"]!["hatch_style"] = Nullable(Text());
        foreach(var kind431 in new[]{"block_reference","block_array"})definitions[kind431]!["properties"]!["associative_array"]=AssociativeArraySchema431.Observation();
        definitions["entity"] = Object(("ref", Ref("ref")), ("type", Text()), ("runtime_type", Text()), ("layer", Nullable(Ref("layer"))),
            ("space", Text()), ("detail_support", Text()), ("coordinate_system", Text()),
            ("geometry", new JsonObject { ["oneOf"] = new JsonArray(Ref("line"), Ref("point"), Ref("ray"), Ref("xline"), Ref("circle"), Ref("ellipse"), Ref("spline"), Ref("region"), Ref("helix"), Ref("block_reference"), Ref("block_array"), Ref("wipeout"), Ref("external_reference"), Ref("arc"), Ref("polyline"), Ref("polyline2d"), Ref("polyline3d"), Ref("text"), Ref("mtext"), Ref("dimension"), Ref("tolerance"), Ref("mleader"), Ref("hatch"), Ref("table"), Ref("solid3d")) }),
            ("properties", Ref("properties")), ("warnings", Array(Text())));
        if(operation=="inspect_objects")definitions["compact_entity"]=TargetedReadSchema407.CompactEntityOutput(Ref("ref"));
        definitions["item"] = Object(("handle", Text()), ("success", Bool()),
            ("entity", Nullable(operation=="inspect_objects" ? new JsonObject { ["anyOf"]=new JsonArray(Ref("entity"),Ref("compact_entity")) } : Ref("entity"))), ("error", Nullable(Ref("error"))));
        definitions["document_info"] = Object(("document_id", Text()), ("name", Text()), ("path", Nullable(Text())),
            ("is_named", Bool()), ("is_read_only", Bool()), ("is_active", Bool()), ("revision", Text()));
        var structureChange = Object(("operation_index", Integer()), ("operation", Text()),
            ("ref", Ref("ref")), ("kind", Text()), ("name", Text()), ("created", Bool()));
        var children = Array(Object(("ref", Ref("ref")), ("kind", Text()), ("source_index", Integer()), ("tag", Nullable(Text()))));
        children["description"]="Complete created native children. For define_block, entity/attribute_definition source_index identifies the requested array member. nested_attribute identifies a newly owned attribute of the BlockReference at that entities source_index; tag is its actual tag. inspect_block supplies the actual parent/occurrence path. For set_block_definition_members, added_member/added_nested_attribute source_index identifies add_entities; retired_member/retired_nested_attribute identifies remove_handles, including the actual owned attribute tag. block_reference children are actual retained consumers marked for redraw. Retired IDs are inactive and must not be reused for a later edit. No fixed child-count truncation.";
        structureChange["properties"]!["children"] = children;
        JsonObject data = operation switch
        {
            "analyze_geometry" => GeometryAnalysis(),
            "inspect_model_history" => ModelHistory(),
            "get_model_variables" => ModelVariables(),
            "inspect_sketch_constraints" => SketchConstraints(),
            "inspect_constraints" => DocumentConstraints(),
            "get_drawing_views" => DrawingViews(),
            "inspect_solid_topology" => SolidTopology(),
            "inspect_point_cloud" => PointCloudSchemas.InspectData(Ref("ref")),
            "analyze_point_cloud" => PointCloudSchemas.AnalysisData(Ref("ref")),
            "get_documents" => Object(("bridge_instance_id", Text()), ("active_document_id", Nullable(Text())), ("documents", Array(Ref("document_info")))),
            "manage_document" => Object(("operation_id", Text()), ("status", new JsonObject { ["type"] = "string", ["const"] = "completed" }),
                ("replayed", Bool()), ("action", Text()), ("document", Ref("document_info")), ("written_path", Nullable(Text())),
                ("written_bytes", Nullable(Integer())), ("reused_open_document", Bool())),
            "get_structure" or "inspect_block" => StructureSchema.Output(operation, definitions),
            "set_model_parameter" => ModelParameterData(),
            "modify_structure" => StructureData(structureChange),
            "modify_entities" => ModifiedData(),
            "create_entities" => CreateData409(),
            "get_status" => Object(("server_version", Text()), ("contract_version", Text()), ("available", Bool()),
                ("instances", Array(Object(("descriptor", Ref("descriptor")), ("reachable", Bool()), ("status", Nullable(AnyObject())), ("error", Nullable(Ref("error")))))),
                ("unavailable_reason", Nullable(Text()))),
            "get_document" => DocumentWithOptionalView(),
            "set_view" => Object(("operation_id",Text()),("status",new JsonObject{["const"]="committed"}),("replayed",Bool()),
                ("before",CameraViewData()),("after",CameraViewData()),("restore_token",Text()),("scope",Text()),("saved",new JsonObject{["const"]=false}),
                ("renderer_before",Nullable(CameraRendererData370())),("renderer_after",Nullable(CameraRendererData370())),("renderer_scope",Text())),
            "query_objects" or "get_selection" => Object(("items", Array(Ref("summary"))), ("total_count", Nullable(Integer())), ("returned_count", Integer()),
                ("complete", Bool()), ("next_cursor", Nullable(Text())), ("selection_revision", Nullable(Text()))),
            _ => Object(("items", Array(Ref("item"))), ("complete", Bool()), ("failed_count", Integer()))
        };
        if (operation == "query_objects")
        {
            data["properties"]!["match_semantics"] = TargetedReadSchema407.MatchSemanticsOutput();
            data = new JsonObject { ["anyOf"] = new JsonArray(data, TargetedReadSchema407.SummaryOutput()) };
        }
        if(operation=="inspect_objects")data["properties"]!["erase_assessment"]=EraseAssessmentSchema444.Output();
        definitions["context"]!["properties"]!["write_revision"] = Nullable(Text());
        bool write = NanoCAD.Contracts.Wire.WriteOperations.Contains(operation);
        var preview = Object(("operation", Text()), ("status", new JsonObject { ["const"] = "validated" }),
            ("document_changed", new JsonObject { ["const"] = false }), ("validation_scope", Text()),
            ("predicted_effects", Nullable(AnyObject())));
        var envelope = Object(("contract_version", Text()), ("request_id", Text()), ("success", Bool()), ("context", Nullable(Ref("context"))),
            ("data", Nullable(write
                ? new JsonObject{["anyOf"]=new JsonArray(data,OperationResultSchema432.CompactWriteData(),preview)}
                : data)), ("error", Nullable(Ref("error"))), ("warnings", Array(Text())));
        envelope["properties"]!["receipt"] = OperationResultSchema432.Receipt();
        if (write) envelope["properties"]!["write_state"] = Nullable(Object(("status", Text()), ("context", Nullable(Ref("context"))),
            ("observations", Nullable(AnyObject())), ("reason", Nullable(Text()))));
        if (ReadPreparation433.Readers.Contains(operation, StringComparer.Ordinal)) envelope["properties"]!["preparation"] = ReadPreparationSchema433.Output();
        KeepReferencedDefinitions(envelope,definitions);
        envelope["$defs"] = definitions;
        return JsonSerializer.SerializeToElement(envelope);
    }

    private static void KeepReferencedDefinitions(JsonObject envelope,JsonObject definitions)
    {
        // Every generated reference is a direct local #/$defs/name reference.
        // Retain its transitive closure, including cycles, without rewriting
        // any reachable schema. Unused global geometry DTOs otherwise appear
        // in all sixteen tools, including status and write-result envelopes.
        var used=new HashSet<string>(StringComparer.Ordinal);var pending=new Queue<string>();
        void Visit(JsonNode? node)
        {
            if(node is JsonObject obj)
                foreach(var pair in obj)
                {
                    if(pair.Key is "$id" or "$dynamicRef" or "$recursiveRef")
                        throw new InvalidOperationException("Schema compaction requires direct local references without identifier scopes.");
                    if(pair.Key=="$ref")
                    {
                        string reference=pair.Value!.GetValue<string>();const string prefix="#/$defs/";
                        if(!reference.StartsWith(prefix,StringComparison.Ordinal))throw new InvalidOperationException("Unsupported generated schema reference.");
                        string name=reference[prefix.Length..];
                        if(name.Length==0||name.Contains('/')||name.Contains('~')||name.Contains('%')||!definitions.ContainsKey(name))
                            throw new InvalidOperationException("Missing or unsupported generated schema definition.");
                        if(used.Add(name))pending.Enqueue(name);
                    }
                    else Visit(pair.Value);
                }
            else if(node is JsonArray array)foreach(var child in array)Visit(child);
        }
        Visit(envelope);
        while(pending.Count>0)Visit(definitions[pending.Dequeue()]);
        foreach(var name in definitions.Select(pair=>pair.Key).ToArray())if(!used.Contains(name))definitions.Remove(name);
    }
}
