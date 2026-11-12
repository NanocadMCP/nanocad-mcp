using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Own transport ABI only. NativeModelHistory421 owns the single loaded module;
// native object ownership never crosses this boundary as a managed DBObject.
internal static class NativeSolidReplacement409
{
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct NativeReport
    {
        [JsonInclude] public uint Size, Version, Phase, FailurePhase;
        [JsonInclude] public uint ContextChecked, NativeTransactionCount, CloneCreated, CloneQuarantined;
        [JsonInclude] public uint BeginWriteCalled, BeginWriteReturned, HandoverStarted, HandoverReturned;
        [JsonInclude] public uint CleanupEntered, CleanupReturned, CleanupSucceeded, PostHandoverObserved;
        [JsonInclude] public int SourceOpenStatus, TargetOpenStatus, PropertiesStatus, BeginWriteStatus;
        [JsonInclude] public int HandoverStatus, OperationStatus, DonorCleanupStatus, TargetCleanupStatus;
        [JsonInclude] public int SourceCleanupStatus;
        [JsonInclude] public uint DonorCleanupKind, TargetCleanupKind, SourceCleanupKind;
        [JsonInclude] public uint DonorCleanupCompleted, TargetCleanupCompleted, SourceCleanupCompleted;
        [JsonInclude] public ulong TargetInput, SourceInput, ModelOwner;
        [JsonInclude] public ulong TargetCurrentId, TargetCurrentOwner, DonorCurrentId, DonorCurrentOwner;
        [JsonInclude] public ulong SourceCurrentId, SourceCurrentOwner;
    }
    internal sealed record Observation(int Status, NativeReport Native, [property: JsonIgnore] Exception? BeginWriteError)
    {
        public bool HandoverStarted => Native.HandoverStarted != 0;
        public bool CleanupSucceeded => Native.CleanupEntered == 1 && Native.CleanupReturned == 1 && Native.CleanupSucceeded == 1;
        public object? CallbackError => BeginWriteError == null ? null : new { type = BeginWriteError.GetType().FullName, message = BeginWriteError.Message };
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int BeginWrite(IntPtr state);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int InvokeNative(IntPtr database, ulong target, ulong source, BeginWrite beginWrite, IntPtr state, ref NativeReport report);

    static InvokeNative Load()
    {
        if (Marshal.SizeOf<NativeReport>() != 200)
            throw new DomainException("CAPABILITY_UNAVAILABLE", "The native solid-replacement transport layout differs.");
        return NativeModelHistory421.LoadSolidReplacement409();
    }
    internal static void EnsureAvailable() => _ = Load();

    internal static Observation Invoke(IntPtr database, ulong target, ulong source, Action beginWrite)
    {
        ArgumentNullException.ThrowIfNull(beginWrite);
        var invoke = Load();
        Exception? beginWriteError = null;
        BeginWrite callback = _ =>
        {
            try { beginWrite(); return 0; }
            catch (Exception error) { beginWriteError = error; return unchecked((int)0x80004004); }
        };
        var report = new NativeReport { Size = (uint)Marshal.SizeOf<NativeReport>() };
        int status = invoke(database, target, source, callback, IntPtr.Zero, ref report);
        GC.KeepAlive(callback);
        return new(status, report, beginWriteError);
    }

    internal static bool Completed(Observation result, ulong target, ulong source)
    {
        var report = result.Native;
        // Public Nano::ErrorStatus::eObjectToBeDeleted is 41. The installed NRX
        // wrapper returns it on normal hand-over; it is not Teigha ErrorStatus
        // and never a general waiver of nonzero native statuses.
        const int NrxObjectToBeDeleted = 41;
        const uint DetachedDelete = 1, ResidentClose = 2;
        return result.Status == 0 && result.BeginWriteError == null && report.Size == 200 && report.Version == 1 &&
            report.Phase == 12 && report.FailurePhase == 0 && report.OperationStatus == 0 &&
            report.ContextChecked == 1 && report.NativeTransactionCount == 0 && report.CloneCreated == 1 && report.CloneQuarantined == 0 &&
            report.SourceOpenStatus == 0 && report.TargetOpenStatus == 0 && report.PropertiesStatus == 0 &&
            report.BeginWriteCalled == 1 && report.BeginWriteReturned == 1 && report.BeginWriteStatus == 0 &&
            report.HandoverStarted == 1 && report.HandoverReturned == 1 && report.HandoverStatus == NrxObjectToBeDeleted &&
            report.PostHandoverObserved == 1 && result.CleanupSucceeded &&
            report.DonorCleanupCompleted == 1 && report.TargetCleanupCompleted == 1 && report.SourceCleanupCompleted == 1 &&
            report.DonorCleanupKind == ResidentClose && report.SourceCleanupKind == ResidentClose && report.TargetCleanupKind == DetachedDelete &&
            report.DonorCleanupStatus == 0 && report.SourceCleanupStatus == 0 && report.TargetCleanupStatus == int.MinValue &&
            target != 0 && source != 0 && target != source && report.TargetInput == target && report.SourceInput == source &&
            report.TargetCurrentId == 0 && report.TargetCurrentOwner == 0 && report.DonorCurrentId == target && report.SourceCurrentId == source &&
            report.ModelOwner != 0 && report.DonorCurrentOwner == report.ModelOwner && report.SourceCurrentOwner == report.ModelOwner;
    }
}
