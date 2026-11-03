using System.Buffers.Binary;
using System.Text.Json;
using NanoCAD.Contracts;

namespace NanoCAD.Ipc;

public static class Framing
{
    public static async Task<T> ReadAsync<T>(Stream stream, int limit, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        await ReadExactlyAsync(stream, header, cancellationToken).ConfigureAwait(false);
        var size = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (size <= 0 || size > limit) throw new InvalidDataException("Invalid frame length.");
        var body = new byte[size];
        await ReadExactlyAsync(stream, body, cancellationToken).ConfigureAwait(false);
        // JsonSerializer enforces the depth bound; malformed UTF-8 is rejected by its reader.
        return JsonSerializer.Deserialize<T>(body, Wire.Json) ?? throw new InvalidDataException("Null frame.");
    }
    public static async Task WriteAsync<T>(Stream stream, T value, int limit, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(value, Wire.Json);
        if (body.Length > limit) throw new DomainException("RESPONSE_LIMIT_EXCEEDED", "Response exceeds the byte budget; request a smaller batch.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, body.Length);
        await stream.WriteAsync(header.AsMemory(), cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body.AsMemory(), cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (n == 0) throw new EndOfStreamException("Peer disconnected during a frame.");
            offset += n;
        }
    }
}
