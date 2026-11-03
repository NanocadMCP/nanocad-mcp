using System.IO.Pipes;
using System.Text.Json;
using NanoCAD.Contracts;

namespace NanoCAD.Ipc;

public sealed class BridgeListener : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Descriptor _descriptor;
    private readonly Func<Request, CancellationToken, Task<Result>> _handler;
    private readonly JsonLog _log;
    private readonly Task[] _workers;
    private readonly string _descriptorPath;
    private int _disposed;
    public BridgeListener(Descriptor descriptor, Func<Request, CancellationToken, Task<Result>> handler, JsonLog log)
    {
        _descriptor = descriptor; _handler = handler; _log = log;
        // Create the first listener synchronously, so a published descriptor has a real endpoint.
        var first = LocalSecurity.CreateServer(descriptor.pipe_name);
        try { _descriptorPath = Discovery.Publish(descriptor); }
        catch { first.Dispose(); throw; }
        _workers = Enumerable.Range(0, 8).Select(i => Task.Run(() => RunAsync(i == 0 ? first : null))).ToArray();
    }
    private async Task RunAsync(NamedPipeServerStream? first)
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = first ?? LocalSecurity.CreateServer(_descriptor.pipe_name);
                first = null;
                await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                deadline.CancelAfter(10000);
                LocalSecurity.VerifyPeer(pipe.SafePipeHandle, true);
                var request = await Framing.ReadAsync<Request>(pipe, Wire.MaxRequestBytes, deadline.Token).ConfigureAwait(false);
                Result response;
                try
                {
                    if (request.protocolVersion != Wire.Version) throw new DomainException("PROTOCOL_MISMATCH", "Unsupported IPC protocol.");
                    if (!Guid.TryParseExact(request.requestId, "D", out _) || request.timeoutMs is < 1 or > 10000)
                        throw new DomainException("INVALID_ARGUMENT", "Invalid request identity/deadline.");
                    var bridgeId = Wire.ValidateBridge(request.operation, request.arguments);
                    if (bridgeId != null && bridgeId != _descriptor.bridge_instance_id)
                        throw new DomainException("BRIDGE_UNAVAILABLE", "Request addresses another bridge instance.");
                    deadline.CancelAfter(request.timeoutMs);
                    // One request per connection. EOF or extra client data cancels queued work.
                    var peerGone = WatchPeerAsync(pipe, deadline.Token);
                    var pending = _handler(request, deadline.Token);
                    if (await Task.WhenAny(pending, peerGone).ConfigureAwait(false) == peerGone)
                    {
                        deadline.Cancel();
                        try { await pending.ConfigureAwait(false); } catch (OperationCanceledException) { }
                        continue;
                    }
                    response = await pending.ConfigureAwait(false);
                }
                catch (DomainException e) { response = Result.Fail(request.requestId ?? "", e.Error); }
                // Inline PNG is an internal binary payload, not a public JSON object page.
                int responseBudget = request.operation == "capture_window" ? Wire.MaxResponseBytes : Wire.PayloadBudget;
                WriteResponseBudget432.TryFit(response,request.operation,responseBudget,
                    value=>JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json).Length,out response,_descriptor.bridge_instance_id);
                await Framing.WriteAsync(pipe, response, Wire.MaxResponseBytes, deadline.Token).ConfigureAwait(false);
                deadline.Cancel();
            }
            catch (OperationCanceledException) { }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or DomainException or ArgumentException or System.ComponentModel.Win32Exception)
            {
                _log.Write("ipc", "", e.GetType().Name);
                if (!_stop.IsCancellationRequested)
                    try { await Task.Delay(100, _stop.Token).ConfigureAwait(false); } catch (OperationCanceledException) { }
            }
        }
    }
    private static async Task WatchPeerAsync(Stream pipe, CancellationToken cancellationToken)
    {
        try { await pipe.ReadAsync(new byte[1].AsMemory(), cancellationToken).ConfigureAwait(false); }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        try { File.Delete(_descriptorPath); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        // Never block nanoCAD's UI during shutdown. Workers own and dispose their pipe handles.
        _ = Task.WhenAll(_workers).ContinueWith(_ => _stop.Dispose(), TaskScheduler.Default);
    }
}
