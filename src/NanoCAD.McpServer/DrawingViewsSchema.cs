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
        properties["geometry_view_handle"] = handle.DeepClone();
        properties["source_identity_view_handle"] = handle.DeepClone();
        properties["source_identity_view_handle"]!["description"] = "Optional exact current view in this layout. Read native simple sources and complete complex insertion chains separately, plus sliced-body IDs and stored native source-generation settings for a Section view, without regeneration or traversal. Stored settings and actual projection participation are separate observations. Default omits this inventory.";
        properties["geometry_start_index"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = int.MaxValue, ["default"] = 0 };
        properties["geometry_source_marker"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = int.MaxValue, ["description"] = "Look up one current view GS marker directly in the native source object and FEV maps. Returns the validated edge and an optional creating feature. Visibility and coincident geometry are not evaluated by this lookup. Use include_geometry_sources on the geometry page for a geometrically verified visible source." };
        properties["geometry_page_size"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = int.MaxValue, ["default"] = 100 };
        properties["include_geometry_sources"] = new JsonObject
        {
            ["type"] = "boolean",
            ["default"] = false,
            ["description"] = "Read native marker, edge key and confirmed creating feature for unique visible lines and circular curves of one local solid. A single-line page in the active view also reports dimension_edge_binding with evaluated endpoints or its unavailable stage."
        };
        schema["dependentRequired"] = new JsonObject { ["geometry_start_index"] = new JsonArray("geometry_view_handle"), ["geometry_page_size"] = new JsonArray("geometry_view_handle"), ["include_geometry_sources"] = new JsonArray("geometry_view_handle"), ["geometry_source_marker"] = new JsonArray("geometry_view_handle") };
        schema["allOf"] = JsonNode.Parse("""[{"if":{"required":["geometry_source_marker"]},"then":{"not":{"anyOf":[{"required":["geometry_start_index"]},{"required":["geometry_page_size"]},{"required":["include_geometry_sources"]}]}}}]""");
        schema["required"] = new JsonArray("bridge_instance_id", "document_id", "expected_revision", "layout_handle");
        return JsonSerializer.SerializeToElement(schema);
    }
}
