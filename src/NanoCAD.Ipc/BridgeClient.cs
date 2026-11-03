using System.IO.Pipes;
using NanoCAD.Contracts;

namespace NanoCAD.Ipc;

public sealed class BridgeClient
{
    public async Task<Result> CallAsync(Descriptor descriptor, Request request, CancellationToken cancellationToken)
    {
        if (descriptor.protocol_version != Wire.Version) throw new DomainException("PROTOCOL_MISMATCH", "Bridge protocol does not match server.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(request.timeoutMs);
        using var pipe = new NamedPipeClientStream(".", descriptor.pipe_name, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        bool write = Wire.WriteOperations.Contains(request.operation), attemptedSend = false;
        string operationId = request.arguments.TryGetProperty("operation_id", out var operation) && operation.ValueKind == System.Text.Json.JsonValueKind.String ? operation.GetString()! : "";
        DomainException Unknown(string cause) => new("WRITE_OUTCOME_UNKNOWN",
            "Connection ended after a write might have reached the bridge. Repeat exactly the same operation_id and payload to retrieve its outcome; never retry with a new ID.", false,
            new WriteErrorDetails(operationId, "unknown", false, "unknown", new { transport_error = cause }));
        try
        {
            await pipe.ConnectAsync(2000, deadline.Token).ConfigureAwait(false);
            LocalSecurity.VerifyPeer(pipe.SafePipeHandle, false, descriptor.process_id, descriptor.process_start_utc_ticks);
            attemptedSend = true;
            await Framing.WriteAsync(pipe, request, Wire.MaxRequestBytes, deadline.Token).ConfigureAwait(false);
            var result = await Framing.ReadAsync<Result>(pipe, Wire.MaxResponseBytes, deadline.Token).ConfigureAwait(false);
            if (result.request_id != request.requestId || result.contract_version != Wire.Version ||
                (result.context != null && result.context.bridge_instance_id != descriptor.bridge_instance_id))
                throw new DomainException("PROTOCOL_MISMATCH", "Mismatched bridge response identity.");
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw write && attemptedSend ? Unknown("REQUEST_TIMEOUT") : new DomainException("REQUEST_TIMEOUT", "Bridge did not respond before the deadline. Native work may still be finishing.", true); }
        catch (DomainException e) when (write && attemptedSend) { throw Unknown(e.Error.code); }
        catch (System.Text.Json.JsonException) when (write && attemptedSend) { throw Unknown("INVALID_RESPONSE_JSON"); }
        catch (TimeoutException) { throw new DomainException("BRIDGE_UNAVAILABLE", "Pipe connection timed out.", true); }
        catch (UnauthorizedAccessException) { throw new DomainException("ACCESS_DENIED", "Run Codex and nanoCAD as the same Windows user and elevation level."); }
        catch (IOException) { throw write && attemptedSend ? Unknown("CONNECTION_CLOSED") : new DomainException("BRIDGE_UNAVAILABLE", "Bridge connection closed or returned an invalid frame.", true); }
    }
}
