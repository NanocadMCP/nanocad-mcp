using System.Buffers;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace NanoCADBridge;

// The caller supplies the active document's saved filename and the app's
// user-only staging directory. Dispose after the side DB has closed its input.
internal sealed class SavedDwgInput : IDisposable
{
    const FileShare ReadShared = FileShare.ReadWrite | FileShare.Delete;
    readonly FileStream lease;
    readonly FileIdentity identity;
    readonly string? privateFile;
    public string SourcePath { get; }
    public string RenderPath => privateFile ?? SourcePath;
    public bool IsPrivateCopy => privateFile != null;
    public string Sha256 { get; }
    public long Length => identity.Length;

    SavedDwgInput(string path, string? copy, FileStream lease, FileIdentity id, string hash)
    {
        SourcePath = path;
        privateFile = copy;
        this.lease = lease;
        identity = id;
        Sha256 = hash;
    }

    public static SavedDwgInput Acquire(string savedPath, string staging, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        if (!Path.IsPathFullyQualified(savedPath) || !Path.GetExtension(savedPath).Equals(".dwg", StringComparison.OrdinalIgnoreCase))
            throw new IOException("The active document has no saved DWG path.");
        string path = Path.GetFullPath(savedPath);
        FileStream locked;
        try { locked = Open(path, FileShare.Read); }
        catch (IOException error) when ((error.HResult & 0xffff) == 32)
        {
            return AcquirePrivateCopy(path, staging, cancel);
        }
        try
        {
            var id = Identify(locked);
            string hash = Transfer(locked, null, cancel);
            RequireSame(id, Identify(locked));
            return new SavedDwgInput(path, null, locked, id, hash);
        }
        catch { locked.Dispose(); throw; }
    }

    static SavedDwgInput AcquirePrivateCopy(string path, string staging, CancellationToken cancel)
    {
        using var original = Open(path, ReadShared);
        var id = Identify(original);
        string copy = Path.Combine(Path.GetFullPath(staging), Guid.NewGuid().ToString("N") + ".dwg");
        // The owner handle keeps subsequent opens read-only until native disposal.
        // ReadDwgFile(OpenForReadAndAllShare) can open this file concurrently.
        var lease = new FileStream(copy, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read,
            65536, FileOptions.SequentialScan);
        try
        {
            string hash = Transfer(original, lease, cancel);
            lease.Flush();
            if (lease.Length != id.Length) throw Changed();
            RequireSame(id, Identify(original));
            using var atPath = Open(path, ReadShared);
            RequireSame(id, Identify(atPath));
            return new SavedDwgInput(path, copy, lease, id, hash);
        }
        catch
        {
            lease.Dispose();
            File.Delete(copy);
            throw;
        }
    }

    // Perform on the last accepted camera/revision check before delivery. The
    // receiving snapshot has its own immutable bytes even if this check fails.
    public void VerifySavedBasis(CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        using var atPath = Open(SourcePath, ReadShared);
        RequireSame(identity, Identify(atPath));
        if (IsPrivateCopy && Transfer(atPath, null, cancel) != Sha256) throw Changed();
        RequireSame(identity, Identify(atPath));
        // A writer can replace a pathname while the shared verification handle
        // remains readable. Reopen the name to confirm it still maps to the file.
        if (IsPrivateCopy)
        {
            using var final = Open(SourcePath, ReadShared);
            RequireSame(identity, Identify(final));
        }
    }

    static FileStream Open(string path, FileShare sharing) =>
        new(path, FileMode.Open, FileAccess.Read, sharing, 65536, FileOptions.SequentialScan);

    static string Transfer(FileStream source, FileStream? copy, CancellationToken cancel)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(65536);
        try
        {
            int count;
            do
            {
                cancel.ThrowIfCancellationRequested();
                count = source.Read(buffer, 0, buffer.Length);
                hash.AppendData(buffer, 0, count);
                copy?.Write(buffer, 0, count);
            } while (count > 0);
            return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }
        finally { ArrayPool<byte>.Shared.Return(buffer, clearArray: true); }
    }

    readonly record struct FileIdentity(uint Volume, ulong FileId, long Length, long WriteTime);
    static IOException Changed() => new("The saved drawing changed while its rendering was being prepared.");
    static void RequireSame(FileIdentity expected, FileIdentity actual)
    {
        if (actual != expected) throw Changed();
    }

    static FileIdentity Identify(FileStream stream)
    {
        if (!GetFileInformationByHandle(stream.SafeFileHandle, out var data))
            throw new IOException("The identity of the saved drawing is unavailable.", new Win32Exception(Marshal.GetLastWin32Error()));
        ulong fileId = ((ulong)data.IndexHigh << 32) | data.IndexLow;
        if (fileId == 0) throw new IOException("The filesystem did not supply a stable identifier for the saved drawing.");
        return new(data.Volume, fileId, ((long)data.SizeHigh << 32) | data.SizeLow, data.WriteTime);
    }

    // FILETIME consists of two DWORDs and has four-byte alignment.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    struct FileInformation
    {
        public uint Attributes;
        public long CreationTime, AccessTime, WriteTime;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }

    [DllImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation data);

    public void Dispose()
    {
        lease.Dispose();
        if (privateFile != null) File.Delete(privateFile);
    }
}
