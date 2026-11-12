using System.Runtime.InteropServices;
using System.Text.Json;
using NanoCAD.Contracts;
using NanoCADBridge;
using Report = NanoCADBridge.NativeSolidReplacement409.NativeReport;

internal static class NativeSolidReplacementReportTests
{
    // Actual public transport from the committed private269 v2 case:
    // direct-solid-native-lifetime-combined-19ac103c/native-cleanup-witness.json.
    // These are our own scalar ABI observations, not SDK/vendor drawing data.
    const string AcceptedJson = """{"Size":200,"Version":1,"Phase":12,"FailurePhase":0,"ContextChecked":1,"NativeTransactionCount":0,"CloneCreated":1,"CloneQuarantined":0,"BeginWriteCalled":1,"BeginWriteReturned":1,"HandoverStarted":1,"HandoverReturned":1,"CleanupEntered":1,"CleanupReturned":1,"CleanupSucceeded":1,"PostHandoverObserved":1,"SourceOpenStatus":0,"TargetOpenStatus":0,"PropertiesStatus":0,"BeginWriteStatus":0,"HandoverStatus":41,"OperationStatus":0,"DonorCleanupStatus":0,"TargetCleanupStatus":-2147483648,"SourceCleanupStatus":0,"DonorCleanupKind":2,"TargetCleanupKind":1,"SourceCleanupKind":2,"DonorCleanupCompleted":1,"TargetCleanupCompleted":1,"SourceCleanupCompleted":1,"TargetInput":114,"SourceInput":115,"ModelOwner":23,"TargetCurrentId":0,"TargetCurrentOwner":0,"DonorCurrentId":114,"DonorCurrentOwner":23,"SourceCurrentId":115,"SourceCurrentOwner":23}""";
    static void Need(bool condition, string message)
    { if (!condition) throw new Exception(message); }
    static Report Accepted() => JsonSerializer.Deserialize<Report>(AcceptedJson, Wire.Json);
    static bool Complete(Report report, ulong target = 114, ulong source = 115) =>
        NativeSolidReplacement409.Completed(new(0, report, null), target, source);
    static void Cold()
    {
        var sdkNames = new HashSet<string>(["hostdbmgd", "mapimgd", "MapiBaseTypes", "mapinet", "HostMgd"], StringComparer.OrdinalIgnoreCase);
        Need(!AppDomain.CurrentDomain.GetAssemblies().Any(assembly => sdkNames.Contains(assembly.GetName().Name!)),
            "Pure replacement-report validation loaded a CAD SDK assembly.");
    }

    internal static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("native replacement report is a cold 200-byte transport with native64 alignment", () =>
        {
            Cold();
            Need(Marshal.SizeOf<Report>() == 200 && Marshal.OffsetOf<Report>(nameof(Report.TargetInput)).ToInt32() == 128 &&
                Marshal.OffsetOf<Report>(nameof(Report.SourceCurrentOwner)).ToInt32() == 192,
                "The owning replacement report no longer matches its native200-byte layout/64-bit slots.");
            Need(!Complete(default), "An uninitialized native report claimed completed ownership/cleanup.");
            Cold(); return Task.CompletedTask;
        });
        await test("native replacement completion accepts the actual269 lifecycle41 and three cleanup receipts", () =>
        {
            var report = Accepted();
            Need(Complete(report), "The actual committed269 hand-over/cleanup observation was rejected.");
            using var document = JsonDocument.Parse(AcceptedJson);
            Need(NativeMetadataEquality.Same(document.RootElement, Wire.Element(report)),
                "Public JsonInclude report fields lost the exact accepted native observations during JSON transport.");
            Need(report.HandoverStatus == 41 && report.TargetCleanupStatus == int.MinValue && report.TargetCleanupKind == 1 &&
                report.DonorCleanupKind == 2 && report.SourceCleanupKind == 2,
                "Detached deletion was confused with resident close or its unavailable close-status sentinel.");
            Cold(); return Task.CompletedTask;
        });
        await test("native replacement completion refuses partial incompatible or unowned context reports", () =>
        {
            var report = Accepted();
            Need(!Complete(report with { Size = 196 }) && !Complete(report with { Version = 2 }),
                "A truncated or incompatible report was accepted.");
            Need(!Complete(report with { Phase = 11 }) && !Complete(report with { FailurePhase = 7 }) &&
                !Complete(report with { PostHandoverObserved = 0 }), "A partial/failing hand-over report was accepted as final.");
            Need(!Complete(report with { ContextChecked = 0 }) && !Complete(report with { NativeTransactionCount = 1 }) &&
                !Complete(report with { CloneQuarantined = 1 }) && !Complete(report with { CloneCreated = 0 }),
                "Unproved context, active native transaction or unowned/aliased donor bypassed completion checks.");
            return Task.CompletedTask;
        });
        await test("native replacement requires the exact identity exchange and preserves full unsigned64 slots", () =>
        {
            var report = Accepted();
            Need(!Complete(report with { TargetCurrentId = 114 }) && !Complete(report with { TargetCurrentOwner = 23 }),
                "A still-resident displaced target was accepted for detached deletion.");
            Need(!Complete(report with { DonorCurrentId = 115 }) && !Complete(report with { SourceCurrentId = 114 }) &&
                !Complete(report with { DonorCurrentOwner = 24 }) && !Complete(report with { SourceCurrentOwner = 24 }),
                "The new target/source identity or Model ownership differed from the actual requested exchange.");
            Need(!Complete(report, 115, 114) && !Complete(report, 0, 115) && !Complete(report, 114, 114) &&
                !Complete(report with { ModelOwner = 0 }), "Wrong, null or aliased input identities passed completion.");
            const ulong target = 0x8000000000000001UL, source = 0xFFFFFFFFFFFFFFFEUL, owner = 0xFFFFFFFF00000001UL;
            var full = report with
            {
                TargetInput = target,
                SourceInput = source,
                ModelOwner = owner,
                DonorCurrentId = target,
                SourceCurrentId = source,
                DonorCurrentOwner = owner,
                SourceCurrentOwner = owner
            };
            var transported = JsonSerializer.Deserialize<Report>(Wire.Element(full).GetRawText(), Wire.Json);
            Need(transported.TargetInput == target && transported.SourceInput == source && transported.ModelOwner == owner &&
                Complete(transported, target, source), "Opaque unsigned64 ABI identities lost high bits during actual report transport/validation.");
            Need(!Complete(transported, target + 1, source), "Adjacent high-bit identities became indistinguishable.");
            return Task.CompletedTask;
        });
        await test("native replacement requires distinct completed resident-close and detached-delete cleanup", () =>
        {
            var report = Accepted();
            Need(!Complete(report with { CleanupSucceeded = 0 }) && !Complete(report with { CleanupReturned = 0 }) &&
                !Complete(report with { DonorCleanupCompleted = 0 }) && !Complete(report with { TargetCleanupCompleted = 0 }) &&
                !Complete(report with { SourceCleanupCompleted = 0 }), "An incomplete/unverified owner cleanup was accepted.");
            Need(!Complete(report with { DonorCleanupKind = 1 }) && !Complete(report with { SourceCleanupKind = 1 }) &&
                !Complete(report with { TargetCleanupKind = 2 }), "Cleanup was accepted for the wrong current object residency.");
            Need(!Complete(report with { DonorCleanupStatus = 1 }) && !Complete(report with { SourceCleanupStatus = 1 }) &&
                !Complete(report with { TargetCleanupStatus = 0 }), "A failed close or invented detached-delete close status was accepted.");
            return Task.CompletedTask;
        });
        await test("native replacement completion never waives failed callbacks or arbitrary native statuses", () =>
        {
            var report = Accepted();
            Need(!NativeSolidReplacement409.Completed(new(unchecked((int)0x80004005), report, null), 114, 115),
                "An unsuccessful export return was accepted from an otherwise complete report.");
            Need(!NativeSolidReplacement409.Completed(new(0, report, new InvalidOperationException("Admission callback stopped")), 114, 115),
                "A failed managed admission callback was hidden by native report flags.");
            Need(!Complete(report with { BeginWriteStatus = unchecked((int)0x80004004) }) &&
                !Complete(report with { BeginWriteReturned = 0 }) && !Complete(report with { HandoverReturned = 0 }),
                "The callback/hand-over did not actually return successfully.");
            Need(!Complete(report with { HandoverStatus = 0 }) && !Complete(report with { HandoverStatus = 42 }) &&
                !Complete(report with { OperationStatus = unchecked((int)0x80004005) }),
                "The one observed NRX lifecycle status became a general nonzero/native-success waiver.");
            Cold(); return Task.CompletedTask;
        });
    }
}

namespace NanoCADBridge
{
    // Tests link the production report class without its CAD-bound loader.
    // Any accidental loader access fails immediately; no CAD SDK/native module
    // is referenced or loaded by these compiled pure report assertions.
    internal static class NativeModelHistory421
    {
        internal static NativeSolidReplacement409.InvokeNative LoadSolidReplacement409() =>
            throw new InvalidOperationException("Pure report validation unexpectedly attempted native module loading.");
    }
}
