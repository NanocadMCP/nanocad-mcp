using ModelContextProtocol.Protocol;

namespace NanoCAD.McpServer;

internal static class McpPresentation
{
    // Engineers' AI clients already consume TextContent. Send the full result once.
    // Explicit compatibility mode is for typed clients that consume structuredContent.
    public static bool Structured => string.Equals(Environment.GetEnvironmentVariable("NANOCAD_MCP_RESPONSE_MODE"),"structured",StringComparison.OrdinalIgnoreCase);
    public static IList<Tool> ListTools()=>Structured?ToolCatalog.Tools:CompactTools;
    private static readonly IList<Tool> CompactTools=ToolCatalog.Tools.Select(t=>new Tool
    {
        Name=t.Name,Title=t.Title,Description=t.Description,InputSchema=t.InputSchema,Annotations=t.Annotations
        // outputSchema is optional in MCP; declaring it would require structuredContent.
        // Full schemas remain in ToolCatalog for contract checks and typed clients.
    }).ToArray();
}
