using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Text.Json;
using NanoCAD.Contracts;

namespace NanoCAD.Ipc;

public static class Discovery
{
    public static string Root => Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NanoCADMcp");
    public static string Instances => Path.Combine(Root, "instances");
    public static Descriptor Describe(string instanceId, string hostVersion)
    {
        using var process = Process.GetCurrentProcess();
        return new(Wire.Version, instanceId, $"NanoCADMcp-{process.Id}-{instanceId}", process.Id,
            process.StartTime.ToUniversalTime().Ticks, process.SessionId, hostVersion, NanoCAD.Contracts.Wire.ProductVersion, DateTime.UtcNow.ToString("O"));
    }
    public static string Publish(Descriptor descriptor)
    {
        LocalSecurity.SecureDirectory(Root);
        LocalSecurity.SecureDirectory(Instances);
        using var own = Process.GetCurrentProcess();
        PruneRetiredLifetimes(own.SessionId);
        var path = Path.Combine(Instances, descriptor.bridge_instance_id + ".json");
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(descriptor, Wire.Json));
        File.Move(temp, path, true);
        return path;
    }
    // Prune on bridge startup so read-only discovery never modifies the directory.
    private static void PruneRetiredLifetimes(int session)
    {
        try
        {
            foreach (string path in Directory.EnumerateFiles(Instances, "*.json"))
            {
                if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "D", out _)) continue;
                try
                {
                    // Validate and delete the same file handle. The open prevents replacement
                    // or rewriting between the lifetime check and deletion.
                    using var handle = OpenDescriptor(path, ReadAndDelete, FileShare.Read, IntPtr.Zero, FileMode.Open,
                        OpenReparsePoint, IntPtr.Zero);
                    if (handle.IsInvalid || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
                    using var stream = new FileStream(handle, FileAccess.Read);
                    if (stream.Length > 8192) continue;
                    var old = JsonSerializer.Deserialize<Descriptor>(stream, Wire.Json);
                    if (old == null || old.session_id != session || old.process_id <= 0 || old.process_start_utc_ticks <= 0 ||
                        !string.Equals(Path.GetFileNameWithoutExtension(path), old.bridge_instance_id, StringComparison.OrdinalIgnoreCase) ||
                        old.pipe_name != $"NanoCADMcp-{old.process_id}-{old.bridge_instance_id}") continue;
                    if (IsRetired(old))
                    {
                        byte deleteOnClose = 1;
                        SetFileInformationByHandle(handle, 4, ref deleteOnClose, 1); // FileDispositionInfo
                    }
                }
                catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or
                    InvalidOperationException or System.ComponentModel.Win32Exception)
                { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static bool IsRetired(Descriptor descriptor)
    {
        Process process;
        try { process = Process.GetProcessById(descriptor.process_id); }
        catch (ArgumentException) { return true; } // No process with this PID.
        using (process)
            return process.HasExited || process.StartTime.ToUniversalTime().Ticks != descriptor.process_start_utc_ticks;
    }

    private const uint ReadAndDelete = 0x80000000 | 0x00010000;
    private const uint OpenReparsePoint = 0x00200000;
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern SafeFileHandle OpenDescriptor(string path, uint access, FileShare share, IntPtr security,
        FileMode mode, uint flags, IntPtr template);
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle file, int informationClass, ref byte data, int size);

    public static Descriptor[] List()
    {
        if (!Directory.Exists(Instances)) return Array.Empty<Descriptor>();
        using var own = Process.GetCurrentProcess();
        var found = new List<Descriptor>();
        // Forced host restarts can leave stale descriptors. Limiting the first
        // 64 directory entries hid a live bridge behind those stale files.
        var paths = Directory.EnumerateFiles(Instances, "*.json"); var capacity = new SerializedCapacity357(Wire.PayloadBudget - 8192);
        foreach (var path in paths)
        {
            try
            {
                var info = new FileInfo(path);
                if (info.Length > 8192 || (info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                var d = JsonSerializer.Deserialize<Descriptor>(File.ReadAllText(path), Wire.Json);
                if (d == null || !Guid.TryParseExact(d.bridge_instance_id, "D", out _) || d.process_id <= 0 ||
                    d.pipe_name != $"NanoCADMcp-{d.process_id}-{d.bridge_instance_id}" || d.session_id != own.SessionId) continue;
                using var process = Process.GetProcessById(d.process_id);
                if (!process.HasExited && process.StartTime.ToUniversalTime().Ticks == d.process_start_utc_ticks && process.SessionId == own.SessionId)
                { capacity.Add(d); found.Add(d); }
            }
            catch (Exception e) when (e is IOException or JsonException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException) { }
        }
        return found.OrderBy(d => d.process_id).ThenBy(d => d.bridge_instance_id, StringComparer.Ordinal).ToArray();
    }
    public static Descriptor Choose(string? id)
    {
        var all = List();
        if (id != null) return all.SingleOrDefault(d => d.bridge_instance_id == id) ??
            throw new DomainException("BRIDGE_UNAVAILABLE", "The requested bridge is unavailable; call get_status.", true);
        if (all.Length == 0) throw new DomainException("BRIDGE_UNAVAILABLE", "Open nanoCAD and complete any startup dialogs, then call get_status. If the installed connection remains unavailable, check the installation; do not use screen automation.", true);
        if (all.Length > 1) throw new DomainException("AMBIGUOUS_INSTANCE", "Specify bridge_instance_id from get_status.");
        return all[0];
    }
}
