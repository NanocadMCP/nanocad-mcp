using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class TargetedReadSchema407
{
    internal const string QueryDescription = " Optional bounds filters finite WCS XY or XYZ candidate envelopes, never exact geometric intersections. Ordinary entities use reported native extents. MInsert uses a conservative spacing envelope because its native getter can cover only the seed: absolute row/column travel expanded for plausible owner/definition scale and arbitrary grid orientation. This may overselect; within requires the entire conservative envelope inside the query and may exclude other geometrically contained arrays. No grid cells are enumerated or exploded. Unavailable extents and infinite Ray/Xline entities are excluded explicitly in match_semantics. Optional text searches literal TEXT, decoded plain MTEXT/MLeader and explicit dimension overrides, without evaluating Fields or searching Table cells/nested block attributes. Optional block_definition filters canonical named dynamic definitions or direct ordinary definitions. Predicates combine with AND; carry identical filters and space on every cursor page. Empty filtered pages with next_cursor are valid; continue until complete.";
    internal const string InspectDescription = " Optional sections selects compact detail: [] returns identity/type/layer/space and owner only; appearance returns stored display properties without geometry; geometry returns the native stored shape; editing also requests available native edit tokens and complete editor observations in geometry. drawing_dependencies is opt-in: inspect a curve before changing a boundary or hole, or distinguish a block insertion from the definition shared with other insertions. The default retains the original full detail. Select only information needed for the requested task. Polyline2d reports actual stored vertex kinds/WCS positions without converting a fitted contour; Point/Ray/Xline are readable existing entities. Native fitted/periodic Spline observation does not certify every editing or analysis route.";
    private static JsonObject Text() => new() { ["type"] = "string" };
    private static JsonObject Flag() => new() { ["type"] = "boolean", ["default"] = false };
    private static JsonObject Enum(params string[] values) => new() { ["type"] = "string", ["enum"] = new JsonArray(values.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()) };
    private static JsonObject Object(JsonObject properties, params string[] required) => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["properties"] = properties,
        ["required"] = new JsonArray(required.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray())
    };
    private static JsonObject BoundsPoint(int dimension) => new() { ["type"] = "array", ["minItems"] = dimension, ["maxItems"] = dimension, ["items"] = new JsonObject { ["type"] = "number" } };
    internal static JsonObject QueryProperties()
    {
        var block = Object(new JsonObject
        {
            ["handle"] = new JsonObject { ["type"] = "string", ["pattern"] = "^[0-9A-Fa-f]{1,16}$" },
            ["name"] = new JsonObject { ["type"] = "string", ["minLength"] = 1 },
            ["case_sensitive"] = Flag()
        });
        block["anyOf"] = new JsonArray(new JsonObject { ["required"] = new JsonArray("handle") }, new JsonObject { ["required"] = new JsonArray("name") });
        block["description"] = "Exact definition handle and/or name; both supplied predicates must match. Named canonical definition for dynamic insertions, direct definition for ordinary insertions/MInsert arrays. Name comparison defaults case-insensitive.";
        JsonObject Bounds(int dimension) => Object(new JsonObject { ["min"] = BoundsPoint(dimension), ["max"] = BoundsPoint(dimension), ["mode"] = Enum("intersects", "within") }, "min", "max");
        var bounds = new JsonObject { ["oneOf"] = new JsonArray(Bounds(2), Bounds(3)), ["description"] = "WCS rectangle [x,y] or spatial box [x,y,z] in drawing units. Both corners use the same dimension. Candidate extents use native bounds or a conservative whole-grid envelope for MInsert. Unavailable extents are counted separately." };
        var text = Object(new JsonObject
        {
            ["value"] = new JsonObject { ["type"] = "string", ["minLength"] = 1 },
            ["match"] = Enum("contains", "equals"),
            ["case_sensitive"] = Flag()
        }, "value");
        text["description"] = "Literal ordinary note content or explicit dimension override; decoded plain MTEXT/MLeader when available. No Field evaluation, Table-cell scan or nested block-attribute traversal. match defaults contains, case_sensitive defaults false.";
        var summary = Flag();
        summary["description"] = "Count filtered objects by type and layer and combine WCS XYZ bounds. For a paged summary, follow next_cursor with the same filters, sum page counts by type/layer and take the union of page bounds. aggregation_scope distinguishes entire_query from this_page.";
        var bodyName = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["description"] = "Find a top-level Model solid by exact part name or its legacy label, case-insensitive. body_name_match_source identifies caption and alias matches. Names may repeat." };
        return new() { ["bounds"] = bounds, ["text"] = text, ["block_definition"] = block, ["body_name"] = bodyName, ["summary_only"] = summary };
    }
    internal static JsonObject InspectProperties() => new()
    {
        ["erase_assessment"] = EraseAssessmentSchema444.Input(),
        ["sections"] = new JsonObject
        {
            ["type"] = "array",
            ["uniqueItems"] = true,
            ["items"] = Enum("geometry", "editing", "appearance", "drawing_dependencies"),
            ["description"] = "Omit for full detail. [] reads identities and owner only. geometry reads shape; editing reads shape plus available native editing metadata/tokens; appearance reads stored display properties. drawing_dependencies reads selected curve reactors, verified Hatch loop members and native dimension point roles, or an insertion and its current shared definition and direct references. Independent annotations and model history require separate inspection. No geometry arrays/history/fields are read for identity-only or appearance-only requests."
        }
    };
    // Root catalog integration can call this once after building ordinary input;
    // unlike compact publication this mutates the actual validation input contract.
    internal static JsonElement ApplyInput(string operation, JsonElement input)
    {
        if (operation is not ("query_objects" or "inspect_objects")) return input;
        var node = JsonNode.Parse(input.GetRawText())!.AsObject(); var properties = node["properties"]!.AsObject();
        foreach (var pair in operation == "query_objects" ? QueryProperties() : InspectProperties()) properties[pair.Key] = pair.Value!.DeepClone();
        if (operation == "query_objects")
        {
            if (properties["types"]?["items"]?["enum"] is JsonArray types)
                foreach (var type in new[] { "Polyline2d", "Polyline3d", "Point", "Ray", "Xline" }) if (!types.Any(value => value?.GetValue<string>() == type)) types.Add(type);
        }
        return JsonSerializer.SerializeToElement(node);
    }
    internal static JsonObject CompactEntityOutput(JsonObject reference)
    {
        var node = Object(new JsonObject
        {
            ["ref"] = reference.DeepClone(),
            ["type"] = Text(),
            ["runtime_type"] = Text(),
            ["layer"] = new JsonObject { ["anyOf"] = new JsonArray(Object(new JsonObject { ["name"] = Text(), ["handle"] = new JsonObject { ["anyOf"] = new JsonArray(Text(), new JsonObject { ["type"] = "null" }) } }, "name", "handle"), new JsonObject { ["type"] = "null" }) },
            ["space"] = Text(),
            ["detail_support"] = Text(),
            ["body_name"] = Text(),
            ["coordinate_system"] = new JsonObject { ["const"] = "WCS" },
            ["owner_ref"] = reference.DeepClone(),
            ["geometry"] = new JsonObject { ["type"] = "object", ["additionalProperties"] = true },
            ["drawing_dependencies"] = new JsonObject { ["type"] = "object", ["additionalProperties"] = true },
            ["properties"] = new JsonObject { ["type"] = "object", ["additionalProperties"] = true },
            ["warnings"] = new JsonObject { ["type"] = "array", ["items"] = Text() }
        }, "ref", "type", "runtime_type", "layer", "space", "detail_support", "coordinate_system", "owner_ref");
        node["description"] = "Compact selected native sections, with required identity and owner. Omitted geometry/properties were not requested; this is not an unavailable or truncated observation.";
        return node;
    }
    internal static JsonObject MatchSemanticsOutput()
    {
        var spatial = Object(new JsonObject
        {
            ["coordinate_system"] = Enum("WCS_XY", "WCS_XYZ"),
            ["predicate"] = Text(),
            ["exact_geometry_intersection"] = new JsonObject { ["type"] = "boolean", ["const"] = false },
            ["unavailable_bounds_policy"] = new JsonObject { ["type"] = "string", ["const"] = "excluded" },
            ["unavailable_bounds_count_on_page"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0 },
            ["bounds_kind"] = Text(),
            ["ordinary_bounds_kind"] = Text(),
            ["minsert_bounds_kind"] = new JsonObject { ["type"] = "string", ["const"] = "conservative_minsert_spacing_envelope" },
            ["minsert_bounds_scope"] = Text(),
            ["minsert_within_semantics"] = Text()
        }, "coordinate_system", "predicate", "exact_geometry_intersection", "unavailable_bounds_policy", "unavailable_bounds_count_on_page");
        spatial["description"] = "Ordinary reported native extents plus an explicitly conservative MInsert whole-grid spacing envelope. Intersects may overselect; within requires full conservative-envelope containment and is not exhaustive geometric containment. No cell enumeration or exact bbox claim.";
        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = true,
            ["properties"] = new JsonObject
            {
                ["spatial"] = new JsonObject { ["anyOf"] = new JsonArray(spatial, new JsonObject { ["type"] = "null" }) },
                ["body_name"] = new JsonObject
                {
                    ["anyOf"] = new JsonArray(Object(new JsonObject
                    {
                        ["scope"] = Enum("top_level_model_solid_part_name_history_name_or_group_description", "top_level_model_solid_effective_name"),
                        ["match"] = Enum("ordinal_ignore_case_equals"),
                        ["page_solids_excluded_unavailable_name"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0 }
                    }, "scope", "match", "page_solids_excluded_unavailable_name"), new JsonObject { ["type"] = "null" })
                }
            },
            ["description"] = "Actual combined-filter scope and per-page unavailable count; literal text/definition scope. Omitted when no additional filters were requested. bounds_kind identifies native versus conservative array envelope semantics."
        };
    }
    internal static JsonObject SummaryOutput()
    {
        static JsonObject Count() => new() { ["type"] = "integer", ["minimum"] = 0 };
        static JsonObject Groups(string key) => new()
        {
            ["type"] = "array",
            ["items"] = Object(new JsonObject { [key] = Text(), ["count"] = Count() }, key, "count")
        };
        static JsonObject Point3() => new() { ["type"] = "array", ["minItems"] = 3, ["maxItems"] = 3, ["items"] = new JsonObject { ["type"] = "number" } };
        var bounds = Object(new JsonObject
        {
            ["min"] = Point3(),
            ["max"] = Point3(),
            ["coordinate_system"] = new JsonObject { ["const"] = "WCS" },
            ["kind"] = new JsonObject { ["const"] = "combined_candidate_envelope" }
        }, "min", "max", "coordinate_system", "kind");
        return Object(new JsonObject
        {
            ["summary_only"] = new JsonObject { ["const"] = true },
            ["complete"] = new JsonObject { ["type"] = "boolean" },
            ["next_cursor"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
            ["aggregation_scope"] = Enum("entire_query", "this_page"),
            ["total_count"] = Count(),
            ["by_type"] = Groups("type"),
            ["by_layer"] = Groups("layer"),
            ["by_body_name_match_source"] = Groups("source"),
            ["bounds"] = new JsonObject { ["anyOf"] = new JsonArray(bounds, new JsonObject { ["type"] = "null" }) },
            ["bounds_unavailable_count"] = Count(),
            ["spatial_excluded_unavailable_bounds_count"] = Count(),
            ["page_solids_excluded_unavailable_name"] = Count(),
            ["space"] = Enum("model", "paper")
        }, "summary_only", "complete", "total_count", "by_type", "by_layer", "bounds", "bounds_unavailable_count", "spatial_excluded_unavailable_bounds_count", "space");
    }
}
