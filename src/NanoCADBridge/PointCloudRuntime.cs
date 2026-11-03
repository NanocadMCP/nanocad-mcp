using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Resolve the optional managed shim once from the already running host installation.
// There are no cloud-source loading, clipping or CAD write calls in this resolver.
internal static class PointCloudRuntime
{
    private const string Name = "hostPointCloudsMgd";
    private static readonly object Gate = new();
    private static LoadResult? _result;
    private static Assembly[]? _observed;

    internal static bool TryEnsureLoaded() => Ensure().Available;

    internal static void RequireLoaded()
    {
        var result = Ensure();
        if (!result.Available)
            throw new DomainException("CAPABILITY_UNAVAILABLE", "Point cloud managed runtime is unavailable: " + result.Reason);
    }

    private static LoadResult Ensure()
    {
        lock (Gate)
        {
            if (_result?.Available == true) return _result;
            var loaded = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == Name).ToArray();
            if (_result != null && _observed != null && _observed.SequenceEqual(loaded)) return _result;
            _observed = loaded;
            return _result = Load();
        }
    }

    // A failed attempt is not repeated for each entity. If the host subsequently loads the
    // dependency itself, verify that newly observed assembly instead of permanently disabling it.
    private static LoadResult Load()
    {
        try
        {
            var expected = typeof(PointCloudRuntime).Assembly.GetReferencedAssemblies()
                .SingleOrDefault(a => a.Name == Name);
            if (expected == null) return new(false, "The bridge has no point-cloud managed reference.");

            var hostAssembly = typeof(HostMgd.ApplicationServices.Document).Assembly;
            var databaseAssembly = typeof(Teigha.DatabaseServices.Database).Assembly;
            var hostDirectory = Path.GetDirectoryName(hostAssembly.Location);
            var databaseDirectory = Path.GetDirectoryName(databaseAssembly.Location);
            using var process = Process.GetCurrentProcess();
            var executable = process.MainModule?.FileName;
            if (string.IsNullOrEmpty(hostDirectory) || string.IsNullOrEmpty(databaseDirectory) || string.IsNullOrEmpty(executable) ||
                !string.Equals(Path.GetFileName(executable), "nCad.exe", StringComparison.OrdinalIgnoreCase) ||
                !SamePath(hostDirectory, databaseDirectory) ||
                !SamePath(hostDirectory, Path.Combine(Path.GetDirectoryName(executable)!, "bin")))
                return new(false, "Loaded host libraries do not identify the current nanoCAD installation.");

            var context = AssemblyLoadContext.GetLoadContext(typeof(PointCloudRuntime).Assembly);
            if (context == null) return new(false, "The bridge managed load context is unavailable.");

            var path = Path.GetFullPath(Path.Combine(hostDirectory, Name + ".dll"));
            var installed = AssemblyName.GetAssemblyName(path);
            if (!SameIdentity(expected, installed))
                return new(false, "Installed point-cloud managed assembly does not match the bridge reference.");

            var assembly = context.Assemblies.FirstOrDefault(a => a.GetName().Name == Name)
                ?? context.LoadFromAssemblyPath(path);

            if (!SameIdentity(expected, assembly.GetName()) || !SamePath(path, assembly.Location))
                return new(false, "Loaded point-cloud managed assembly is not the expected installed file.");
            return new(true, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or
            System.Security.SecurityException or NotSupportedException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            return new(false, ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static bool SameIdentity(AssemblyName expected, AssemblyName actual) =>
        string.Equals(expected.FullName, actual.FullName, StringComparison.OrdinalIgnoreCase);
    private static bool SamePath(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    private sealed record LoadResult(bool Available, string? Reason);
}
