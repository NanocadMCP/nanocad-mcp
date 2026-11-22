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
                    var descriptor = options?.SelectedBridge ?? Discovery.Choose(bridgeId);
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
                        catch (Exception error) when (Recoverable(error))
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
        catch (Exception e) when (Recoverable(e))
        { result = Result.Fail(id, new("INTERNAL_ERROR", "The MCP server could not complete this tool call.", false, new { exception_type = e.GetType().Name })); }

        if (result.error is { } invalid) result = result with { error = InputError.Complete(invalid) };
        var (presented, responseResult) = Present(result, operation, image, selectedBridge, state, preparationSelection);
        _log.Write(operation, id, presented.success ? "ok" : presented.error!.code, watch.ElapsedMilliseconds);
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

    private static bool Recoverable(Exception error) =>
        error is not (OutOfMemoryException or StackOverflowException or AccessViolationException);

    internal static (Result Result, CallToolResult Response) Present(Result result, string operation, byte[]? image,
        string? selectedBridge, WriteStateObservation? state, ReadPreparationSelection433? selection)
    {
        if (image != null)
        {
            // capture_view has no optional write state or read preparation.
            var captured = Wrap(result, image);
            if (Bytes(captured) <= ResponseBudget) return (result, captured);
            result = Result.Fail(result.request_id,
                new("RESPONSE_LIMIT_EXCEEDED", "The captured image exceeds the response budget; request a smaller image."), result.context);
            return (result, Wrap(result));
        }

        Result? lastFit = null;
        CallToolResult? response = null;
        int Measure(Result candidate)
        {
            var wrapped = Wrap(candidate);
            int size = Bytes(wrapped);
            if (size <= ResponseBudget) (lastFit, response) = (candidate, wrapped);
            return size;
        }
        WriteResponseBudget432.TryFit(result, operation, ResponseBudget, Measure, out result, selectedBridge);
        if (!ReferenceEquals(lastFit, result) || response == null) response = Wrap(result);
        if (state != null && result.success)
        {
            var withState = result with { write_state = state };
            if (Measure(withState) > ResponseBudget)
                Measure(result with { write_state = new("unavailable", result.context, null, "RESPONSE_LIMIT_EXCEEDED") });
        }
        if (selection == null) return (result, response!);
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
        ReadPreparation433.Fit(observed, candidate =>
        {
            var wrapped = Wrap(result, preparation: candidate);
            int size = Bytes(wrapped);
            if (size <= ResponseBudget) response = wrapped;
            return size;
        }, ResponseBudget);
        return (result, response!);
    }

    private static int Bytes(CallToolResult response) => JsonSerializer.SerializeToUtf8Bytes(response).Length;

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
