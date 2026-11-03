using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class PointCloudSchemas
{
    internal const string InspectDescription = "Read one top-level Model point-cloud insertion. mode=metadata (default) discloses loaded state, available source metadata, drawing units and insertion bounds; unavailable observations remain null. The observed native counter is not a verified raw-point total. mode=features requires an already-loaded source and pages existing native feature identities and observed geometry, without fitting or changing features. Keep the same explicit bridge/document/revision/handle and request on cursor continuation. No loading, import, display, clipping or storage mutation.";
    internal const string AnalysisDescription = "Read actual raw point-cloud data in drawing_units. points pages native point indices and the insertion iterator's actual Vertex coordinates in WCS; do not apply the reported CloudInfo matrices again. Mapping for moved or rotated insertions has not been independently confirmed. section selects an arbitrary oriented slab and also returns its local coordinates. statistics scans the requested subset and can find the nearest actual point to query_point; it is final only when complete=true. measure reads actual native indices for point_distance or point_plane_distance, or an existing Cylinder/Sphere feature for feature_diameter. No fitting, source projection, CAD geometry creation, import or cloud mutation. A time-limited page may contain no points and still have complete=false; continue its query/revision-bound cursor.";

    static JsonObject Object(params (string Name, JsonNode Schema)[] fields) => new()
    {
        ["type"] = "object", ["additionalProperties"] = false,
        ["properties"] = new JsonObject(fields.Select(f => KeyValuePair.Create<string, JsonNode?>(f.Name, f.Schema))),
        ["required"] = new JsonArray(fields.Select(f => (JsonNode?)JsonValue.Create(f.Name)).ToArray())
    };
    static JsonObject Text(string? description = null)
    {
        var schema = new JsonObject { ["type"] = "string" };
        if (description != null) schema["description"] = description;
        return schema;
    }
    static JsonObject Number() => new() { ["type"] = "number" };
    static JsonObject Boolean() => new() { ["type"] = "boolean" };
    static JsonObject Described(JsonObject schema, string description) { schema["description"] = description; return schema; }
    static JsonObject Array(JsonNode item) => new() { ["type"] = "array", ["items"] = item };
    static JsonObject Point() => new() { ["type"] = "array", ["minItems"] = 3, ["maxItems"] = 3, ["items"] = Number() };
    static JsonObject Index() => new() { ["type"] = "string", ["pattern"] = "^(0|[1-9][0-9]{0,19})$", ["description"] = "Actual native UInt64 point index as canonical decimal text, at most18446744073709551615. It is scoped to the current cloud and document revision; use a returned index, not a row number or CloudInfo count." };
    static JsonObject FeatureId() => new() { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = 4294967294L, ["description"] = "Current native feature ID from mode=features;4294967295 is the null sentinel." };
    static JsonObject PageSize() => new() { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = int.MaxValue, ["default"] = 100, ["description"] = "Requested page size; actual byte and scan-time budgets can return a shorter or empty incomplete page. This does not impose a total cloud-size limit." };
    static JsonObject Cursor() => new() { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 8192, ["description"] = "Opaque continuation from the preceding response. Keep every query field and the bridge/document/revision/handle unchanged; continue until complete=true. It is not a native point index." };
    static JsonObject Context()
    {
        var root = Object(
            ("bridge_instance_id", new JsonObject { ["type"] = "string", ["format"] = "uuid" }),
            ("document_id", new JsonObject { ["type"] = "string", ["format"] = "uuid" }),
            ("expected_revision", new JsonObject { ["type"] = "string", ["pattern"] = "^[0-9]{1,20}$" }),
            ("handle", new JsonObject { ["type"] = "string", ["pattern"] = "^[0-9A-Fa-f]{1,16}$", ["description"] = "One existing top-level Model cloud insertion handle; not its definition handle or a file path." }));
        return root;
    }
    static JsonObject IsMode(string mode) => new() { ["properties"] = new JsonObject { ["mode"] = new JsonObject { ["const"] = mode } } };
    static JsonObject Forbidden(params string[] names) => new() { ["not"] = new JsonObject { ["anyOf"] = new JsonArray(names.Select(n => (JsonNode?)new JsonObject { ["required"] = new JsonArray(n) }).ToArray()) } };

    internal static JsonElement InspectInput()
    {
        var root = Context(); var properties = root["properties"]!.AsObject();
        properties["mode"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("metadata", "features"), ["default"] = "metadata" };
        properties["page_size"] = PageSize(); properties["cursor"] = Cursor();
        root["allOf"] = new JsonArray(new JsonObject { ["if"] = IsMode("metadata"), ["then"] = Forbidden("page_size", "cursor") });
        root["description"] = InspectDescription;
        return JsonSerializer.SerializeToElement(root);
    }

    internal static JsonElement AnalysisInput()
    {
        var root = Context(); var properties = root["properties"]!.AsObject(); var required = root["required"]!.AsArray();
        required.Add("mode"); required.Add("units");
        properties["mode"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("points", "section", "statistics", "measure") };
        properties["units"] = new JsonObject { ["type"] = "string", ["const"] = "drawing_units", ["description"] = "Coordinates, slab thickness and linear results use actual drawing units. No millimeter or import-source unit assumption or conversion is made." };
        var section = Object(("origin", Point()), ("normal", Point()), ("x_axis", Point()),
            ("thickness", new JsonObject { ["type"] = "number", ["exclusiveMinimum"] = 0 }));
        section["description"] = "Oriented WCS slab centered on origin. Nonzero normal and perpendicular x_axis are normalized; y_axis=normal cross x_axis. thickness is the total width, so accepted points have absolute normal offset<=thickness/2. Statistics can use the same slab. No drawing/UCS/visibility change.";
        properties["section"] = section;
        properties["region"] = Object(("min", Point()), ("max", Point()));
        properties["region"]!["description"] = "Optional inclusive WCS box, min<=max on each axis; intersects the slab when both are present. It filters actual returned/scanned points, without changing cloud clipping.";
        properties["page_size"] = PageSize(); properties["cursor"] = Cursor();
        properties["query_point"] = Point();
        properties["query_point"]!["description"] = "Statistics only: WCS query point for the nearest actual raw point in the requested subset. A partial scan is not a final nearest-point answer.";
        properties["include_attributes"] = new JsonObject { ["type"] = "boolean", ["default"] = false, ["description"] = "points/section only: request available native point attributes. Unavailable attributes are reported explicitly; default pages contain indices and coordinates only." };
        JsonObject Kind(string kind) => new() { ["type"] = "string", ["const"] = kind };
        properties["measurement"] = new JsonObject { ["oneOf"] = new JsonArray(
            Object(("kind", Kind("point_distance")), ("point_index", Index()), ("second_point_index", Index())),
            Object(("kind", Kind("point_plane_distance")), ("point_index", Index()),
                ("reference_plane", Object(("origin", Point()), ("normal", Point())))),
            Object(("kind", Kind("feature_diameter")), ("feature_id", FeatureId()))) };
        properties["measurement"]!["description"] = "point_distance reads two current native cloud points; point_plane_distance reads one native cloud point and an explicit reference plane with nonzero normal. feature_diameter reads the stored native Cylinder/Sphere radius; it does not fit raw points or establish manufacturing tolerance.";
        root["allOf"] = new JsonArray(
            new JsonObject { ["if"] = IsMode("measure"), ["then"] = new JsonObject
                { ["required"] = new JsonArray("measurement"), ["allOf"] = new JsonArray(Forbidden("section", "region", "page_size", "cursor", "query_point", "include_attributes")) },
                ["else"] = Forbidden("measurement") },
            new JsonObject { ["if"] = IsMode("section"), ["then"] = new JsonObject { ["required"] = new JsonArray("section") } },
            new JsonObject { ["if"] = IsMode("points"), ["then"] = Forbidden("section") },
            new JsonObject { ["if"] = new JsonObject { ["required"] = new JsonArray("query_point") }, ["then"] = IsMode("statistics") },
            new JsonObject { ["if"] = new JsonObject { ["required"] = new JsonArray("include_attributes") },
                ["then"] = new JsonObject { ["properties"] = new JsonObject { ["mode"] = new JsonObject { ["enum"] = new JsonArray("points", "section") } } } });
        root["description"] = AnalysisDescription;
        return JsonSerializer.SerializeToElement(root);
    }

    static JsonObject Nullable(JsonNode value) => new() { ["anyOf"] = new JsonArray(value, new JsonObject { ["type"] = "null" }) };
    static JsonObject Integer() => new() { ["type"] = "integer" };
    static JsonObject DecimalCount() => new() { ["type"] = "string", ["pattern"] = "^(0|[1-9][0-9]{0,19})$" };
    static JsonObject DrawingUnit() => Object(("name", Text()), ("value", Integer()));
    static JsonObject Bounds() => Object(("min", Point()), ("max", Point()));
    static JsonObject Pair() => new() { ["type"] = "array", ["minItems"] = 2, ["maxItems"] = 2, ["items"] = Number() };
    static JsonObject Matrix() => new() { ["type"] = "array", ["minItems"] = 16, ["maxItems"] = 16, ["items"] = Number() };
    static JsonObject Feature()
    {
        var geometry = Object(
            ("coordinate_frame", new JsonObject { ["type"] = "string", ["const"] = "native_feature" }),
            ("origin", Nullable(Point())), ("normal", Nullable(Point())), ("center", Nullable(Point())),
            ("guide", Nullable(Point())), ("start", Nullable(Point())), ("end", Nullable(Point())), ("point", Nullable(Point())),
            ("radius", Nullable(Number())), ("radius1", Nullable(Number())), ("radius2", Nullable(Number())),
            ("height", Nullable(Number())), ("major_radius", Nullable(Number())), ("minor_radius", Nullable(Number())),
            ("plane_ids", Nullable(Array(FeatureId()))));
        return Object(("feature_id", FeatureId()), ("kind", Text()), ("native_type", Integer()),
            ("name", Nullable(Text())), ("population", DecimalCount()), ("deviation", Number()), ("geometry", Nullable(geometry)));
    }
    static JsonObject PointRecord() => Object(
        ("point_index", Index()),
        ("point_wcs", Described(Point(), "Actual insertion-scoped native iterator Vertex, used directly for WCS filtering and measurements. No additional CloudInfo transform is applied; moved/rotated insertion mapping has not been independently confirmed.")),
        ("base_point", Described(Point(), "Native BaseVertex observation. Do not infer its WCS mapping from the reported CloudInfo matrices.")),
        ("section_coordinates", Nullable(Pair())), ("signed_section_distance", Nullable(Number())),
        ("attributes", Nullable(Object(
            ("intensity", Nullable(new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = 65535 })),
            ("classification", Nullable(new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = 255 })),
            ("rgb", Nullable(new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = 4294967295L }))))));
    static JsonObject Statistics() => Object(
        ("count", DecimalCount()), ("bounds_wcs", Nullable(Bounds())), ("centroid_wcs", Nullable(Point())),
        ("standard_deviation_wcs", Nullable(Point())),
        ("section_bounds", Nullable(Object(("min", Pair()), ("max", Pair())))),
        ("signed_section_distance_range", Nullable(Object(("min", Number()), ("max", Number())))),
        ("query_point", Nullable(Point())), ("nearest_point", Nullable(PointRecord())),
        ("nearest_distance", Nullable(Number())), ("scope", Text()));
    static JsonObject Measurement() => Object(
        ("kind", new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("point_distance", "point_plane_distance", "feature_diameter") }),
        ("status", new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("available", "unavailable") }),
        ("value", Nullable(Number())), ("signed_value", Nullable(Number())), ("reason", Nullable(Text())),
        ("point", Nullable(PointRecord())), ("second_point", Nullable(PointRecord())),
        ("reference_plane", Nullable(Object(("origin", Point()), ("normal", Point()),
            ("provenance", new JsonObject { ["type"] = "string", ["const"] = "explicit_input_reference" })))),
        ("feature", Nullable(Feature())), ("provenance", Text()),
        ("coordinate_frame", new JsonObject { ["type"] = "string", ["const"] = "WCS" }),
        ("native_value", Nullable(Number())));

    internal static JsonObject InspectData(JsonNode reference) => Object(
        ("mode", new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("metadata", "features") }),
        ("source_ref", reference.DeepClone()), ("definition_ref", reference.DeepClone()), ("loaded", Boolean()),
        ("source_path", Nullable(Text())), ("cloud_name", Nullable(Text())), ("point_count", Nullable(DecimalCount())),
        ("insertion_bounds", Nullable(Bounds())),
        ("coordinate_system", new JsonObject { ["type"] = "string", ["const"] = "WCS" }),
        ("units", DrawingUnit()), ("source_units_known", new JsonObject { ["type"] = "boolean", ["const"] = false }),
        ("source_units_note", Text()), ("data_scope", Text()), ("data_scheme", Nullable(DecimalCount())),
        ("has_intensity", Nullable(Boolean())), ("has_classification", Nullable(Boolean())), ("has_rgb", Nullable(Boolean())),
        ("tree_depth", Nullable(Integer())),
        ("transformation", Described(Nullable(Matrix()), "Native CloudInfo metadata, not a further transformation to apply to point_wcs or a confirmed BaseVertex-to-WCS mapping.")),
        ("back_transformation", Described(Nullable(Matrix()), "Native CloudInfo inverse metadata; its relationship to Vertex/BaseVertex is a diagnostic observation, not a guaranteed coordinate conversion.")),
        ("features", Nullable(Array(Feature()))), ("features_geometry_scope", Text()),
        ("complete", Nullable(Boolean())), ("next_cursor", Nullable(Text())));

    internal static JsonObject AnalysisData(JsonNode reference) => Object(
        ("mode", new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("points", "section", "statistics", "measure") }),
        ("source_ref", reference.DeepClone()), ("definition_ref", reference.DeepClone()),
        ("units", new JsonObject { ["type"] = "string", ["const"] = "drawing_units" }),
        ("drawing_unit", DrawingUnit()), ("source_units_known", new JsonObject { ["type"] = "boolean", ["const"] = false }),
        ("source_units_note", Text()), ("coordinate_frame", new JsonObject { ["type"] = "string", ["const"] = "WCS" }),
        ("selection_method", new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("native_all_iterator_exact_predicate", "native_feature_identity") }),
        ("items", Array(PointRecord())),
        ("scan", Object(("page_scanned_count", DecimalCount()), ("total_scanned_count", DecimalCount()),
            ("total_matched_count", DecimalCount()), ("complete", Boolean()), ("next_cursor", Nullable(Text())),
            ("metadata_point_count", Nullable(DecimalCount())))),
        ("frame_observation", Described(Object(("checked_point_count", DecimalCount()),
            ("vertex_matches_transformed_base", Described(Nullable(Boolean()), "Cumulative diagnostic comparison of native Vertex with CloudInfo-transformed BaseVertex. false is an observed mismatch; null means the comparison is unavailable. Neither rejects returned native points.")),
            ("back_transform_matches_base", Described(Nullable(Boolean()), "Cumulative diagnostic comparison of CloudInfo-back-transformed Vertex with native BaseVertex. false or null does not invalidate the returned native Vertex.")),
            ("status", Text())), "Coordinates use actual insertion-scoped native Vertex. CloudInfo/BaseVertex agreement is diagnostic and may be false or unavailable; moved/rotated insertion mapping has not been independently confirmed.")),
        ("statistics", Nullable(Statistics())), ("measurement", Nullable(Measurement())));
}
