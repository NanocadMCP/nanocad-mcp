using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class DrawingViewsSchema
{
    public static JsonElement Input()
    {
        var schema = JsonNode.Parse(ModelVariablesSchema.Input().GetRawText())!.AsObject();
        var properties = schema["properties"]!.AsObject(); var handle = properties["handle"]!.DeepClone();
        properties.Remove("handle"); properties["layout_handle"] = handle;
        JsonNode NumericArray(int length) => new JsonObject
        {
            ["type"] = "array",
            ["minItems"] = length,
            ["maxItems"] = length,
            ["items"] = new JsonObject { ["type"] = "number" }
        };
        properties["sheet_placement"] = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("view_handle"),
            ["description"] = "Check an ordinary paper projection at its current or proposed origin. Supply a free rectangle [minX,minY,maxX,maxY] in sheet WCS, optional clearance in drawing units, and inspect obstacle bounds and suggested alignment-preserving translation. Use include_aligned_group on a root view to fit aligned descendants and their attached dimension envelopes together. The result supplies native property operations for the same revision. Inspect the sheet again after applying them.",
            ["properties"] = new JsonObject
            {
                ["view_handle"] = handle.DeepClone(),
                ["include_aligned_group"] = new JsonObject { ["type"] = "boolean", ["default"] = false },
                ["compact_spacing"] = new JsonObject { ["type"] = "boolean", ["description"] = "Prefer compact spacing even when the existing spacing fits at the requested origin." },
                ["frame_handle"] = handle.DeepClone(),
                ["annotation_margin"] = new JsonObject { ["type"] = "number", ["minimum"] = 0, ["description"] = "Reserve this much paper space around each view for future annotation in aligned group mode." },
                ["reserved_areas"] = new JsonObject { ["type"] = "array", ["items"] = NumericArray(4), ["description"] = "Keep these paper rectangles free, including blank title-block cells and space reserved for notes." },
                ["member_sides"] = new JsonObject { ["type"] = "object", ["additionalProperties"] = new JsonObject { ["enum"] = new JsonArray("left", "right", "above", "below") }, ["description"] = "Optional child-handle to paper-side mapping for coincident aligned origins. Must agree with any measured alignment." },
                ["proposed_origin"] = NumericArray(2),
                ["available_area"] = NumericArray(4),
                ["clearance"] = new JsonObject { ["type"] = "number", ["minimum"] = 0 },
                ["start_index"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = int.MaxValue },
                ["page_size"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = int.MaxValue }
            }
        };
        properties["geometry_view_handle"] = handle.DeepClone();
        properties["source_identity_view_handle"] = handle.DeepClone();
        properties["source_identity_view_handle"]!["description"] = "Optional exact current view in this layout. Read native simple sources and complete complex insertion chains separately, plus sliced-body IDs and stored native source-generation settings for a Section view, without regeneration or traversal. Stored settings and actual projection participation are separate observations. Default omits this inventory.";
        properties["geometry_start_index"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = int.MaxValue, ["default"] = 0 };
        properties["geometry_source_marker"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = int.MaxValue, ["description"] = "Look up one current view GS marker in the native source object and topology maps. Returns the validated face, edge or vertex and its creating feature when available. Use include_geometry_sources to check visibility and coincident primitives." };
        properties["geometry_page_size"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = int.MaxValue, ["default"] = 100 };
        properties["marker_dimensions"] = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["description"] = "Find dimensions with verified stored anchors on this exact layout, view and GS marker. Pages count examined layout dimensions, including unclassified dimensions. Continue at next_index at the same revision.",
            ["properties"] = new JsonObject { ["start_index"] = properties["geometry_start_index"]!.DeepClone(), ["page_size"] = properties["geometry_page_size"]!.DeepClone() }
        };
        properties["include_geometry_sources"] = new JsonObject
        {
            ["type"] = "boolean",
            ["default"] = false,
            ["description"] = "Read native marker, topology key and confirmed creating feature for unique visible lines and circular curves of one local solid. A single-line page in the active view also reports dimension_edge_binding with evaluated endpoints or its unavailable stage."
        };
        schema["dependentRequired"] = new JsonObject { ["geometry_start_index"] = new JsonArray("geometry_view_handle"), ["geometry_page_size"] = new JsonArray("geometry_view_handle"), ["include_geometry_sources"] = new JsonArray("geometry_view_handle"), ["geometry_source_marker"] = new JsonArray("geometry_view_handle") };
        schema["allOf"] = JsonNode.Parse("""[{"if":{"required":["geometry_source_marker"]},"then":{"not":{"anyOf":[{"required":["geometry_start_index"]},{"required":["geometry_page_size"]},{"required":["include_geometry_sources"]}]}}}]""");
        schema["allOf"]!.AsArray().Add(JsonNode.Parse("""{"if":{"required":["sheet_placement"]},"then":{"not":{"anyOf":[{"required":["geometry_view_handle"]},{"required":["source_identity_view_handle"]}]}}}"""));
        schema["dependentRequired"]!["marker_dimensions"] = new JsonArray("geometry_view_handle", "geometry_source_marker");
        schema["allOf"]!.AsArray().Add(JsonNode.Parse("""{"if":{"required":["marker_dimensions"]},"then":{"not":{"required":["source_identity_view_handle"]}}}"""));
        schema["required"] = new JsonArray("bridge_instance_id", "document_id", "expected_revision", "layout_handle");
        return JsonSerializer.SerializeToElement(schema);
    }
}
