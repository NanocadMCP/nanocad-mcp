using ModelContextProtocol.Protocol;

namespace NanoCAD.McpServer;

internal static class McpPresentation
{
    // Engineers' AI clients already consume TextContent. Send the full result once.
    // Explicit compatibility mode is for typed clients that consume structuredContent.
    public static bool Structured => string.Equals(Environment.GetEnvironmentVariable("NANOCAD_MCP_RESPONSE_MODE"), "structured", StringComparison.OrdinalIgnoreCase);
    public static IList<Tool> ListTools() => Structured ? StructuredTools : CompactTools;
    private static readonly IList<Tool> StructuredTools = ToolCatalog.Tools.Select(tool => Publish(tool, true)).ToArray();
    private static readonly IList<Tool> CompactTools = ToolCatalog.Tools.Select(tool => Publish(tool, false)).ToArray();

    private static Tool Publish(Tool tool, bool structured)
    {
        string? compactDescription = structured ? null : tool.Name switch
        {
            "nanocad.modify_entities" => "Edit native geometry, annotations or part history, or add a feature. Read get_guidance(topic=operation, operation=modify_entities, variant=the exact item operation). For Model Space erase, request inspect_objects with erase_assessment=true for the whole set. " + ToolUsage.ModifyEntitiesHostLimits,
            "nanocad.modify_structure" => "Edit layers, styles, blocks, views, layouts or viewports. Read get_guidance(topic=operation, operation=modify_structure, variant=the exact item operation) for its batching and ownership rules.",
            "nanocad.inspect_model_history" => "Read current native part history in the active named DWG. Use history_focus with a feature ID or issues for dependencies, affected consumers, native status and parameter links. Use exact IDs for detailed sketch, profile, datum or extent observations. Read get_guidance(topic=operation, operation=inspect_model_history) for the full field semantics and editing prerequisites.",
            "nanocad.analyze_geometry" => "Measure selected native geometry in drawing units. Modes include distances, intersections, containment, selected contours, finite sections and material intervals along a segment. Match the mode to the source type, workplane and physical question. Read get_guidance(topic=operation, operation=analyze_geometry) for detailed measurement limits.",
            "nanocad.get_drawing_views" => "Read native paper DrawingView records for the selected layout. Inspect source parts, projection and section geometry, dimension anchors or proposed placement against occupied paper bounds. Use the current revision, view handle and returned page indices. Full scope is available in get_guidance(topic=operation, operation=get_drawing_views).",
            _ => null
        };
        var input = SchemaCompactor.PublishFlatInput(tool.InputSchema);
        if (!structured && tool.Name is "nanocad.inspect_model_history" or "nanocad.set_model_parameter" or "nanocad.analyze_point_cloud")
            input = SchemaCompactor.PublishSelectionHelp(tool.Name["nanocad.".Length..], input);
        if (!structured && tool.Name == "nanocad.create_entities") input = SchemaCompactor.ShareCreationKindAlternatives(input);
        if (!structured && tool.Name is "nanocad.modify_entities" or "nanocad.modify_structure") input = SchemaCompactor.PublishOperationIndex(input);
        return new Tool
        {
            Name = tool.Name,
            Title = tool.Title,
            Description = compactDescription ?? tool.Description,
            InputSchema = input,
            OutputSchema = structured ? tool.OutputSchema : null,
            Annotations = tool.Annotations
            // outputSchema is optional in MCP; declaring it would require structuredContent.
            // Keep it for structured clients only.
        };
    }
}
