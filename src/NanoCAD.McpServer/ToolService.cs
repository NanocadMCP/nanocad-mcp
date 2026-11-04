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
    // IPC response deadlines are limited to 10 seconds by the bridge protocol.
    // Started native writes have their own execution budget and pending-result route.
    private const int RequestTimeoutMilliseconds = 10000;
    private const int StatusTimeoutMilliseconds = 2000;
    private const int ResponseBudget = Wire.MaxResponseBytes - 16384;
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
        WriteStateObservation? state = null;
        try
        {
            var input = Wire.Element(arguments ?? new Dictionary<string, JsonElement>());
            (input, preparationSelection) = ReadPreparation433.Extract(operation, input);
            if (operation == "capture_view")
            {
                (result, image) = await VisualCaptureService.CaptureAsync(_client, id, input, cancellationToken);
            }
            else
            {
                WriteRequestOptions? options = null;
                if (Wire.WriteOperations.Contains(operation))
                {
                    options = await WriteRequestOptions.BindAsync(operation, input, Discovery.Choose, ReadDocuments, cancellationToken);
                    input = options.Arguments;
                }
                var bridgeId = Wire.ValidateBridge(operation, input);
                selectedBridge = bridgeId;
                var request = new Request(Wire.Version, id, operation, RequestTimeoutMilliseconds, input);
                if (operation == "get_status")
                {
                    result = await GetStatusAsync(request, bridgeId, cancellationToken);
                }
                else
                {
                    var descriptor = Discovery.Choose(bridgeId);
                    selectedBridge = descriptor.bridge_instance_id;
                    if (options?.ValidateOnly == true)
                        request = request with { operation = WritePreviewRequest.Route, arguments = Wire.Element(new WritePreviewRequest(operation, input)) };
                    result = await _client.CallAsync(descriptor, request, cancellationToken);
                    if (options?.ReturnState == true && result.success)
                    {
                        try
                        {
                            state = await WriteResultState.ReadAsync(operation, result,
                                (reader, arguments, cancel) => ReadBridge(descriptor, reader, arguments, cancel), cancellationToken);
                        }
                        catch (Exception error) when (error is DomainException or OperationCanceledException or IOException or
                            UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or System.ComponentModel.Win32Exception)
                        {
                            state = new("unavailable", result.context, null,
                                error is DomainException domain ? domain.Error.code : "OBSERVATION_UNAVAILABLE");
                        }
                    }
                }
            }
        }
        catch (DomainException e) { result = Result.Fail(id, e.Error); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (JsonException) { result = Result.Fail(id, new("PROTOCOL_MISMATCH", "Invalid JSON in the bridge response.")); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { result = Result.Fail(id, new("BRIDGE_UNAVAILABLE", "Local bridge discovery or transport failed.", true)); }

        if (result.error is { } invalid) result = result with { error = InputError.Complete(invalid) };
        (result, image) = FitResponseBudget(result, operation, image, selectedBridge);
        if (state is not null && result.success)
        {
            var withState = result with { write_state = state };
            if (JsonSerializer.SerializeToUtf8Bytes(Wrap(withState)).Length <= ResponseBudget) result = withState;
            else
            {
                withState = result with { write_state = new("unavailable", result.context, null, "RESPONSE_LIMIT_EXCEEDED") };
                if (JsonSerializer.SerializeToUtf8Bytes(Wrap(withState)).Length <= ResponseBudget) result = withState;
            }
        }
        var preparation = PrepareRead(operation, preparationSelection, result, image);
        var responseResult = Wrap(result, image, preparation);
        _log.Write(operation, id, result.success ? "ok" : result.error!.code, watch.ElapsedMilliseconds);
        return responseResult;
    }

    private Task<Result> ReadBridge(Descriptor descriptor, string operation, object arguments, CancellationToken cancellation) =>
        _client.CallAsync(descriptor, new(Wire.Version, Guid.NewGuid().ToString("D"), operation,
            RequestTimeoutMilliseconds, Wire.Element(arguments)), cancellation);

    private async Task<DocumentList> ReadDocuments(Descriptor descriptor, CancellationToken cancellation)
    {
        var result = await ReadBridge(descriptor, "get_documents", new { descriptor.bridge_instance_id }, cancellation);
        if (!result.success && result.error is { } error) throw new DomainException(error.code, error.message, error.retryable, error.details);
        return (result.data is JsonElement json ? json : Wire.Element(result.data!)).Deserialize<DocumentList>(Wire.Json)
            ?? throw new DomainException("PROTOCOL_MISMATCH", "The bridge did not return an open-document inventory.");
    }

    private async Task<Result> GetStatusAsync(Request request, string? bridgeId, CancellationToken cancellationToken)
    {
        var descriptors = Discovery.List().Where(d => bridgeId == null || d.bridge_instance_id == bridgeId).ToArray();
        var instances = await Task.WhenAll(descriptors.Select(async descriptor =>
        {
            try
            {
                var response = await _client.CallAsync(descriptor, request with { timeoutMs = StatusTimeoutMilliseconds }, cancellationToken);
                return new { descriptor, reachable = response.success, status = response.data, error = response.error };
            }
            catch (DomainException e)
            {
                return new { descriptor, reachable = false, status = (object?)null, error = (DomainError?)e.Error };
            }
        }));
        return Result.Ok(request.requestId, new
        {
            server_version = ServerGuidance.Version,
            contract_version = Wire.Version,
            available = instances.Any(i => i.reachable),
            instances,
            unavailable_reason = instances.Length == 0
                ? "No nanoCAD connection is available. Open nanoCAD and finish any startup dialogs, then check again. If it remains unavailable, check the installed integration; do not use screen automation or begin API experiments."
                : null
        });
    }

    private static (Result Result, byte[]? Image) FitResponseBudget(Result result, string operation, byte[]? image, string? selectedBridge)
    {
        // Bound the actual MCP result, reserving JSON-RPC metadata bytes.
        // Images are read-only visual payloads. If they exhaust the actual
        // wrapper budget, return the ordinary read-page failure without image.
        if (image != null && JsonSerializer.SerializeToUtf8Bytes(Wrap(result, image)).Length > ResponseBudget)
        {
            result = Result.Fail(result.request_id,
                new("RESPONSE_LIMIT_EXCEEDED", "The captured image exceeds the response budget; request a smaller image."), result.context);
            image = null;
        }
        WriteResponseBudget432.TryFit(result, operation, ResponseBudget,
            value => JsonSerializer.SerializeToUtf8Bytes(Wrap(value, image)).Length, out result, selectedBridge);
        return (result, image);
    }

    private static JsonObject? PrepareRead(string operation, ReadPreparationSelection433? selection, Result result, byte[]? image)
    {
        if (selection == null) return null;
        JsonObject observed;
        try { observed = ReadPreparation433.Build(operation, selection, result); }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        {
            observed = new JsonObject
            {
                ["operation"] = selection.Operation,
                ["variant"] = selection.Variant,
                ["status"] = "source_unavailable",
                ["detail"] = "Optional preparation could not represent this observation. The original read is unchanged; use its returned data and focused input contract. This does not restrict any operation."
            };
        }
        return ReadPreparation433.Fit(observed,
            candidate => JsonSerializer.SerializeToUtf8Bytes(Wrap(result, image, candidate)).Length, ResponseBudget);
    }

    internal static CallToolResult Wrap(Result result, byte[]? image = null, JsonObject? preparation = null)
    {
        result = WriteResponseBudget432.WithPresentationWarnings(result);
        var json = Wire.Element(result);
        if (preparation != null) json = ReadPreparation433.Attach(json, preparation);
        var content = new List<ContentBlock> { new TextContentBlock { Text = json.GetRawText() } };
        if (result.success && image != null) content.Add(ImageContentBlock.FromBytes(image, "image/png"));
        return new() { IsError = !result.success, StructuredContent = McpPresentation.Structured ? json : null, Content = content };
    }
}
