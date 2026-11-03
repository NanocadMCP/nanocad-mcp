using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using NanoCAD.McpServer;

string phase = "initialization";
try
{
    await RunServerAsync(args, () => phase = "serving");
    return 0;
}
catch (Exception error)
{
    // Managed startup failures must end this process normally, not open a
    // Windows unhandled-exception dialog. Do not echo paths or exception text.
    Console.Error.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
    {
        server_error = "MCP_SERVER_FAILED", phase, error_type = error.GetType().Name,
        recovery = "Check local configuration and access. If a write may have been submitted, preserve its exact operation_id and payload; never retry it with a new ID."
    }));
    return 3;
}

static async Task RunServerAsync(string[] args, Action serving)
{
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
var service = new ToolService();
builder.Services.AddMcpServer(options =>
{
    options.ServerInfo = new() { Name = "nanocad", Version = ServerGuidance.Version };
    options.ServerInstructions = ServerGuidance.Instructions;
})
.WithStdioServerTransport()
.WithListToolsHandler((_, _) => ValueTask.FromResult(new ListToolsResult { Tools = McpPresentation.ListTools() }))
.WithCallToolHandler(async (context, cancellationToken) =>
{
    var parameters = context.Params ?? throw new McpProtocolException("Missing tool parameters.", McpErrorCode.InvalidParams);
    if (!ToolCatalog.Tools.Any(t => t.Name == parameters.Name))
        throw new McpProtocolException("Unknown tool.", McpErrorCode.InvalidParams);
    return await service.CallAsync(parameters.Name[8..], parameters.Arguments, cancellationToken);
});
using var host = builder.Build();
serving();
await host.RunAsync();
}
