using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Result = NanoCAD.Contracts.Result;

namespace NanoCAD.McpServer;

public sealed class ToolService
{
    private readonly BridgeClient _client = new();
    private readonly JsonLog _log = new("server");
    public async Task<CallToolResult> CallAsync(string operation, IDictionary<string, JsonElement>? arguments, CancellationToken cancellationToken)
    {
        if (operation == "get_guidance") return GuidanceCatalog.Read(arguments);
        string id = Guid.NewGuid().ToString("D");
        var watch = Stopwatch.StartNew();
        Result result;
        byte[]? image = null;
        string? selectedBridge = null;
        ReadPreparationSelection433? preparationSelection = null;
        try
        {
            var input = Wire.Element(arguments ?? new Dictionary<string, JsonElement>());
            (input, preparationSelection) = ReadPreparation433.Extract(operation, input);
            if(operation=="capture_view")
            {
                (result,image)=await VisualCaptureService.CaptureAsync(_client,id,input,cancellationToken);
            }
            else
            {
            var bridgeId = Wire.ValidateBridge(operation, input);
            selectedBridge=bridgeId;
            var request = new Request(Wire.Version, id, operation, 10000, input);
            if (operation == "get_status")
            {
                var descriptors = Discovery.List().Where(d => bridgeId == null || d.bridge_instance_id == bridgeId).ToArray();
                var instances = await Task.WhenAll(descriptors.Select(async descriptor =>
                {
                    try
                    {
                        var response = await _client.CallAsync(descriptor, request with { timeoutMs = 2000 }, cancellationToken);
                        return new { descriptor, reachable = response.success, status = response.data, error = response.error };
                    }
                    catch (DomainException e) { return new { descriptor, reachable = false, status = (object?)null, error = (DomainError?)e.Error }; }
                }));
                result = Result.Ok(id, new { server_version = ServerGuidance.Version, contract_version = Wire.Version, available = instances.Any(i => i.reachable),
                    instances, unavailable_reason = instances.Length == 0 ? "No nanoCAD connection is available. Open nanoCAD and finish any startup dialogs, then check again. If it remains unavailable, check the installed integration; do not use screen automation or begin API experiments." : null });
            }
            else
            {
                var descriptor=Discovery.Choose(bridgeId);selectedBridge=descriptor.bridge_instance_id;
                result = await _client.CallAsync(descriptor, request, cancellationToken);
            }
            }
        }
        catch (DomainException e) { result = Result.Fail(id, e.Error); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (JsonException) { result = Result.Fail(id, new("PROTOCOL_MISMATCH", "Invalid JSON in the bridge response.")); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { result = Result.Fail(id, new("BRIDGE_UNAVAILABLE", "Local bridge discovery or transport failed.", true)); }
        // Bound the actual MCP result, reserving JSON-RPC metadata bytes.
        // Images are read-only visual payloads. If they exhaust the actual
        // wrapper budget, return the ordinary read-page failure without image.
        if(image!=null&&JsonSerializer.SerializeToUtf8Bytes(Wrap(result,image)).Length>Wire.MaxResponseBytes-16384)
        {result=Result.Fail(id,new("RESPONSE_LIMIT_EXCEEDED","The captured image exceeds the response budget; request a smaller image."),result.context);image=null;}
        WriteResponseBudget432.TryFit(result,operation,Wire.MaxResponseBytes-16384,
            value=>JsonSerializer.SerializeToUtf8Bytes(Wrap(value,image)).Length,out result,selectedBridge);
        JsonObject? preparation = null;
        if (preparationSelection != null)
        {
            JsonObject observed;
            try { observed = ReadPreparation433.Build(operation, preparationSelection, result); }
            catch (Exception error) when (error is JsonException or InvalidOperationException)
            {
                observed = new JsonObject { ["operation"] = preparationSelection.Operation, ["variant"] = preparationSelection.Variant,
                    ["status"] = "source_unavailable", ["detail"] = "Optional preparation could not represent this observation. The original read is unchanged; use its returned data and focused input contract. This does not restrict any operation." };
            }
            preparation = ReadPreparation433.Fit(observed,
                candidate => JsonSerializer.SerializeToUtf8Bytes(Wrap(result, image, candidate)).Length, Wire.MaxResponseBytes - 16384);
        }
        var responseResult=Wrap(result,image,preparation);
        _log.Write(operation, id, result.success ? "ok" : result.error!.code, watch.ElapsedMilliseconds);
        return responseResult;
    }
    internal static CallToolResult Wrap(Result result,byte[]? image=null,JsonObject? preparation=null)
    {
        result=WriteResponseBudget432.WithPresentationWarnings(result);
        var json = Wire.Element(result);
        if (preparation != null) json = ReadPreparation433.Attach(json, preparation);
        var content=new List<ContentBlock>{new TextContentBlock { Text = json.GetRawText() }};
        if(result.success&&image!=null)content.Add(ImageContentBlock.FromBytes(image,"image/png"));
        return new() { IsError = !result.success, StructuredContent = McpPresentation.Structured ? json : null, Content = content };
    }
}
