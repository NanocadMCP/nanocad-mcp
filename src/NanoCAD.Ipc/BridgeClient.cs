using System.IO.Pipes;
using System.Security.Principal;
using NanoCAD.Contracts;

namespace NanoCAD.Ipc;

public sealed class BridgeClient
{
    public async Task<Result> CallAsync(Descriptor descriptor, Request request, CancellationToken cancellationToken)
    {
        if (descriptor.protocol_version != Wire.Version) throw new DomainException("PROTOCOL_MISMATCH", "Bridge protocol does not match server.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(request.timeoutMs);
        bool write = Wire.WriteOperations.Contains(request.operation), attemptedSend = false;
        string operationId = request.arguments.ValueKind == System.Text.Json.JsonValueKind.Object && request.arguments.TryGetProperty("operation_id", out var operation) && operation.ValueKind == System.Text.Json.JsonValueKind.String ? operation.GetString()! : "";
        DomainException Unknown(string cause) => new("WRITE_OUTCOME_UNKNOWN",
            "The write may have reached the bridge. Use get_operation_result with the original bridge and operation_id. Exact replay is available in the same bridge lifetime.", false,
            new WriteErrorDetails(operationId, "unknown", false, "unknown", new { bridge_instance_id = descriptor.bridge_instance_id, transport_error = cause }));
        try
        {
            deadline.Token.ThrowIfCancellationRequested();
            var body = Framing.Encode(request, Wire.MaxRequestBytes);
            using var pipe = new NamedPipeClientStream(".", descriptor.pipe_name, PipeDirection.InOut,
                PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
            await pipe.ConnectAsync(2000, deadline.Token).ConfigureAwait(false);
            // The bridge assigns the user's SID explicitly. An elevated token's
            // default owner can be the Administrators group, which is a different SID.
            if (pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner ||
                owner.Value != LocalSecurity.UserSid)
                throw new UnauthorizedAccessException("Pipe owner does not match the Windows user.");
            LocalSecurity.VerifyPeer(pipe.SafePipeHandle, false, descriptor.process_id, descriptor.process_start_utc_ticks);
            deadline.Token.ThrowIfCancellationRequested();
            attemptedSend = true;
            await Framing.WriteFrameAsync(pipe, body, deadline.Token).ConfigureAwait(false);
            var result = await Framing.ReadAsync<Result>(pipe, Wire.MaxResponseBytes, deadline.Token).ConfigureAwait(false);
            if (result.request_id != request.requestId || result.contract_version != Wire.Version ||
                result.warnings == null || result.success != (result.error == null) ||
                (result.context != null && result.context.bridge_instance_id != descriptor.bridge_instance_id))
                throw new DomainException("PROTOCOL_MISMATCH", "Invalid bridge response envelope or identity.");
            return result;
        }
        catch (OperationCanceledException) when (write && attemptedSend)
        { throw Unknown(cancellationToken.IsCancellationRequested ? "CALLER_CANCELLED" : "REQUEST_TIMEOUT"); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new DomainException("REQUEST_TIMEOUT", "Bridge did not respond before the deadline.", true); }
        catch (DomainException e) when (write && attemptedSend) { throw Unknown(e.Error.code); }
        catch (Exception e) when (attemptedSend && e is InvalidDataException or System.Text.Json.JsonException)
        { throw write ? Unknown("INVALID_BRIDGE_RESPONSE") : new DomainException("PROTOCOL_MISMATCH", "The bridge returned an invalid response frame."); }
        catch (UnauthorizedAccessException)
        { throw write && attemptedSend ? Unknown("ACCESS_DENIED") : new DomainException("ACCESS_DENIED", "Run Codex and nanoCAD as the same Windows user and elevation level."); }
        catch (Exception e) when (e is IOException or TimeoutException or System.ComponentModel.Win32Exception or ArgumentException or InvalidOperationException)
        { throw write && attemptedSend ? Unknown(e.GetType().Name) : new DomainException("BRIDGE_UNAVAILABLE", "The local bridge connection or peer process is unavailable.", true); }
    }
}
