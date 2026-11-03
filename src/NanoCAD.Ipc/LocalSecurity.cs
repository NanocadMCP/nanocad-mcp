using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace NanoCAD.Ipc;

public static class LocalSecurity
{
    public static string UserSid => WindowsIdentity.GetCurrent().User!.Value;
    public static string SecureDirectory(string directory)
    {
        var info = Directory.CreateDirectory(directory);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0) throw new UnauthorizedAccessException("Discovery/log directory cannot be a reparse point.");
        var sid = new SecurityIdentifier(UserSid);
        // Avoid reapplying an inheritable DACL to already secured directories
        // and their descendants. The actual current directory must match in full.
        if (HasExpectedDirectoryAcl(info, sid)) return info.FullName;
        var acl = new DirectorySecurity();
        // Keep the creator's owner unchanged; setting owner unnecessarily requires WRITE_OWNER.
        acl.SetAccessRuleProtection(true, false);
        acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        info.SetAccessControl(acl);
        return info.FullName;
    }

    private static bool HasExpectedDirectoryAcl(DirectoryInfo info, SecurityIdentifier sid)
    {
        DirectorySecurity current;
        try { current = info.GetAccessControl(AccessControlSections.Access); }
        catch (UnauthorizedAccessException) { return false; } // Retain the existing WRITE_DAC path.
        if (!current.AreAccessRulesProtected || !current.AreAccessRulesCanonical) return false;
        var descriptor = new RawSecurityDescriptor(current.GetSecurityDescriptorBinaryForm(), 0);
        var rules = descriptor.DiscretionaryAcl;
        // Inspect the raw complete DACL: filtered access-rule inventories can
        // omit unknown/object/callback ACEs. No extra allow/deny rule is admitted.
        return rules != null && rules.Count == 1 && rules[0] is CommonAce ace &&
            ace.AceType == AceType.AccessAllowed && !ace.IsCallback && ace.OpaqueLength == 0 &&
            ace.SecurityIdentifier == sid && ace.AccessMask == (int)FileSystemRights.FullControl &&
            ace.AceFlags == (AceFlags.ContainerInherit | AceFlags.ObjectInherit);
    }

    public static NamedPipeServerStream CreateServer(string name)
    {
        // Explicit SID-only DACL and PIPE_REJECT_REMOTE_CLIENTS. No network listener.
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor($"O:{UserSid}D:P(A;;GA;;;{UserSid})", 1, out var descriptor, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var attributes = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Descriptor = descriptor };
            var handle = CreateNamedPipe($@"\\.\pipe\{name}", 0x40000003, 0x00000008, 16, 65536, 65536, 0, ref attributes);
            if (handle.IsInvalid) { handle.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error()); }
            try { return new NamedPipeServerStream(PipeDirection.InOut, true, false, handle); }
            catch { handle.Dispose(); throw; }
        }
        finally { LocalFree(descriptor); }
    }

    public static void VerifyPeer(SafePipeHandle handle, bool serverSide, int? expectedPid = null, long? startTicks = null)
    {
        uint pid;
        bool ok = serverSide ? GetNamedPipeClientProcessId(handle, out pid) : GetNamedPipeServerProcessId(handle, out pid);
        if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error());
        using var peer = Process.GetProcessById(checked((int)pid));
        using var own = Process.GetCurrentProcess();
        if ((expectedPid.HasValue && peer.Id != expectedPid) || peer.SessionId != own.SessionId ||
            (startTicks.HasValue && peer.StartTime.ToUniversalTime().Ticks != startTicks.Value))
            throw new UnauthorizedAccessException("Pipe peer process/session does not match discovery.");
        using var processHandle = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
        if (processHandle.IsInvalid || !OpenProcessToken(processHandle, 8, out var token))
            throw new UnauthorizedAccessException("Cannot verify pipe peer token.");
        using (token)
        using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
        using (var current = WindowsIdentity.GetCurrent())
        {
            if (identity.User?.Value != current.User?.Value || Elevation(token) != Elevation(current.AccessToken))
                throw new UnauthorizedAccessException("Pipe peer user/elevation differs from this process.");
        }
    }
    private static int Elevation(SafeAccessTokenHandle token)
    {
        if (!GetTokenInformation(token, 20, out int elevation, 4, out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return elevation;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes { public int Length; public IntPtr Descriptor; public int InheritHandle; }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW")]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string text, uint revision, out IntPtr descriptor, out uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateNamedPipeW")]
    private static extern SafePipeHandle CreateNamedPipe(string name, uint openMode, uint pipeMode, uint maxInstances, uint outSize, uint inSize, uint timeout, ref SecurityAttributes security);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inheritHandle, uint pid);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int type, out int value, int length, out int returnedLength);
}
