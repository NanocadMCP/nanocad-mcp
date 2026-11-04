using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class TargetedReadSchema407
{
    internal const string QueryDescription = " Optional bounds filters finite WCS XY candidate envelopes, never exact geometric intersections. Ordinary entities use reported native extents. MInsert uses a conservative spacing envelope because its native getter can cover only the seed: absolute row/column travel expanded for plausible owner/definition scale and arbitrary grid orientation. This may overselect; within requires the entire conservative envelope inside the query and may exclude other geometrically contained arrays. No grid cells are enumerated or exploded. Unavailable extents and infinite Ray/Xline entities are excluded explicitly in match_semantics. Optional text searches literal TEXT, decoded plain MTEXT/MLeader and explicit dimension overrides, without evaluating Fields or searching Table cells/nested block attributes. Optional block_definition filters canonical named dynamic definitions or direct ordinary definitions. Predicates combine with AND; carry identical filters and space on every cursor page. Empty filtered pages with next_cursor are valid; continue until complete.";
    internal const string InspectDescription = " Optional sections selects compact detail: [] returns identity/type/layer/space and owner only; appearance returns stored display properties without geometry; geometry returns the native stored shape; editing also requests available native edit tokens and complete editor observations in geometry. The default omits sections and retains full detail. Select only information needed for the requested task. Polyline2d reports actual stored vertex kinds/WCS positions without converting a fitted contour; Point/Ray/Xline are readable existing entities. Native fitted/periodic Spline observation does not certify every editing or analysis route.";
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
    private static JsonObject Point2() => new() { ["type"] = "array", ["minItems"] = 2, ["maxItems"] = 2, ["items"] = new JsonObject { ["type"] = "number" } };
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
        var bounds = Object(new JsonObject { ["min"] = Point2(), ["max"] = Point2(), ["mode"] = Enum("intersects", "within") }, "min", "max");
        bounds["description"] = "Finite WCS XY [x,y] rectangle in drawing units, min<=max. mode defaults intersects. Ordinary objects use reported native extents; MInsert uses an explicitly conservative whole-grid spacing envelope because native extents may cover only its seed. May overselect, never exact geometric intersection. within is sufficient containment of that envelope, not exhaustive geometric containment. Infinite/unavailable extents are excluded and counted on each page; no array-cell enumeration.";
        var text = Object(new JsonObject
        {
            ["value"] = new JsonObject { ["type"] = "string", ["minLength"] = 1 },
            ["match"] = Enum("contains", "equals"),
            ["case_sensitive"] = Flag()
        }, "value");
        text["description"] = "Literal ordinary note content or explicit dimension override; decoded plain MTEXT/MLeader when available. No Field evaluation, Table-cell scan or nested block-attribute traversal. match defaults contains, case_sensitive defaults false.";
        var summary = Flag();
        summary["description"] = "Count filtered objects by type and layer and combine available WCS XYZ bounds. Omit page_size and cursor.";
        return new() { ["bounds"] = bounds, ["text"] = text, ["block_definition"] = block, ["summary_only"] = summary };
    }
    internal static JsonObject InspectProperties() => new()
    {
        ["erase_assessment"] = EraseAssessmentSchema444.Input(),
        ["sections"] = new JsonObject
        {
            ["type"] = "array",
            ["uniqueItems"] = true,
            ["items"] = Enum("geometry", "editing", "appearance"),
            ["description"] = "Omit for full detail. [] reads identities and owner only. geometry reads shape; editing reads shape plus available native editing metadata/tokens; appearance reads stored display properties. No geometry arrays/history/fields are read for identity-only or appearance-only requests."
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
            if (node["allOf"] == null) node["allOf"] = new JsonArray();
            node["allOf"]!.AsArray().Add(JsonNode.Parse("""{"if":{"required":["summary_only"],"properties":{"summary_only":{"const":true}}},"then":{"not":{"anyOf":[{"required":["page_size"]},{"required":["cursor"]}]}}}"""));
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
            ["coordinate_system"] = new JsonObject { ["const"] = "WCS" },
            ["owner_ref"] = reference.DeepClone(),
            ["geometry"] = new JsonObject { ["type"] = "object", ["additionalProperties"] = true },
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
            ["coordinate_system"] = new JsonObject { ["type"] = "string", ["const"] = "WCS_XY" },
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
            ["properties"] = new JsonObject { ["spatial"] = new JsonObject { ["anyOf"] = new JsonArray(spatial, new JsonObject { ["type"] = "null" }) } },
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
            ["complete"] = new JsonObject { ["const"] = true },
            ["total_count"] = Count(),
            ["by_type"] = Groups("type"),
            ["by_layer"] = Groups("layer"),
            ["bounds"] = new JsonObject { ["anyOf"] = new JsonArray(bounds, new JsonObject { ["type"] = "null" }) },
            ["bounds_unavailable_count"] = Count(),
            ["spatial_excluded_unavailable_bounds_count"] = Count(),
            ["space"] = Enum("model", "paper")
        }, "summary_only", "complete", "total_count", "by_type", "by_layer", "bounds", "bounds_unavailable_count", "spatial_excluded_unavailable_bounds_count", "space");
    }
}
