using System.Diagnostics;
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
        var path = Path.Combine(Instances, descriptor.bridge_instance_id + ".json");
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(descriptor, Wire.Json));
        File.Move(temp, path, true);
        return path;
    }
    public static Descriptor[] List()
    {
        if (!Directory.Exists(Instances)) return Array.Empty<Descriptor>();
        using var own = Process.GetCurrentProcess();
        var found = new List<Descriptor>();
        // Forced host restarts can leave stale descriptors. Limiting the first
        // 64 directory entries hid a live bridge behind those stale files.
        var paths=Directory.EnumerateFiles(Instances,"*.json");var capacity=new SerializedCapacity357(Wire.PayloadBudget-8192);
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
                {capacity.Add(d);found.Add(d);}
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
