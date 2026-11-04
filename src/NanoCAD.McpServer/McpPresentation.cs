using ModelContextProtocol.Protocol;

namespace NanoCAD.McpServer;

internal static class McpPresentation
{
    // Engineers' AI clients already consume TextContent. Send the full result once.
    // Explicit compatibility mode is for typed clients that consume structuredContent.
    public static bool Structured => string.Equals(Environment.GetEnvironmentVariable("NANOCAD_MCP_RESPONSE_MODE"), "structured", StringComparison.OrdinalIgnoreCase);
    public static IList<Tool> ListTools() => Structured ? ToolCatalog.Tools : CompactTools;
    private static readonly IList<Tool> CompactTools = ToolCatalog.Tools.Select(PublishCompact).ToArray();

    private static Tool PublishCompact(Tool tool)
    {
        var description = tool.Name switch
        {
            "nanocad.modify_entities" => "Edit native geometry, annotations or part history, or add the next feature. Read get_guidance(topic=operation, operation=modify_entities, variant=the exact item operation) before writing. For Model Space erase, read inspect_objects with erase_assessment=true for the whole set. Preserve sources and dependencies. Recover pending outcomes by ID. " + ToolUsage.ModifyEntitiesHostLimits,
            "nanocad.modify_structure" => "Edit layers, styles, blocks, views, layouts or viewports. Read get_guidance(topic=operation, operation=modify_structure, variant=the exact item operation) before writing. Follow its batching and ownership rules. Recover pending outcomes by ID.",
            _ => null
        };
        return new Tool
        {
            Name = tool.Name,
            Title = tool.Title,
            Description = description ?? tool.Description,
            InputSchema = description is null ? SchemaCompactor.PublishFlatInput(tool.InputSchema) : SchemaCompactor.PublishOperationIndex(tool.InputSchema),
            Annotations = tool.Annotations
            // outputSchema is optional in MCP; declaring it would require structuredContent.
            // Full schemas remain in ToolCatalog for contract checks and typed clients.
        };
    }
}
