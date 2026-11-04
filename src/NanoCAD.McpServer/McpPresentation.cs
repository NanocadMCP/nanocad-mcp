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
            _ => null
        };
        var input = SchemaCompactor.PublishFlatInput(tool.InputSchema);
        if (compactDescription is not null) input = SchemaCompactor.PublishOperationIndex(input);
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
