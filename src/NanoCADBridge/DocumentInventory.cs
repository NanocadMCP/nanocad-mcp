using System.Buffers.Binary;
using System.Security.Cryptography;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class DocumentInventory
{
    // Verification metadata must not serialize one string per object into an IPC frame.
    internal static object Fingerprint(BlockTableRecord model)
    {
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> value=stackalloc byte[8];long count=0;
        foreach(ObjectId id in model)
        {
            if(id.IsErased||id.IsEffectivelyErased)continue;
            BinaryPrimitives.WriteInt64LittleEndian(value,id.Handle.Value);hash.AppendData(value);count++;
        }
        return new{count,sha256=Convert.ToHexString(hash.GetHashAndReset())};
    }
}
