using System.Globalization;
using System.Text.Json;
using Multicad.DatabaseServices;
using NanoCAD.Ipc;
using Application = HostMgd.ApplicationServices.Application;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// Native HandOverTo transfers the database identity. It does not provide a
// geometry-only CopyFrom, and does not preserve individual face identities.
internal static class CadSolidReplacement409
{
    const string Warning = "SOLID_REPLACED_NATIVE: The direct-body target root and independent plain source are verified after native hand-over and owning cleanup. Reacquire face/edge topology; this does not replace parametric feature history. Native Undo/rollback is not guaranteed.";
    internal sealed record Plan(ObjectId Target, ObjectId Source, JsonElement TargetRoot, JsonElement SourceRoot, JsonElement SourceGeometry, bool HasAssocManager);
    private static void Need(bool ok, string message, string code = "NATIVE_SOLID_REPLACEMENT_MISMATCH")
    { if (!ok) throw new DomainException(code, message); }
    private static string? Handle(ObjectId id) => id.IsNull ? null : Validation.NormalizeHandle(id.Handle.ToString());
    private static JsonElement Geometry(Solid3d body)
    {
        var box = body.GeometricExtents; var mass = body.MassProperties;
        return Wire.Element(new
        {
            volume = mass.Volume,
            min = new[] { box.MinPoint.X, box.MinPoint.Y, box.MinPoint.Z },
            max = new[] { box.MaxPoint.X, box.MaxPoint.Y, box.MaxPoint.Z },
            centroid = new[] { mass.Centroid.X, mass.Centroid.Y, mass.Centroid.Z },
            body.NumChanges
        });
    }
    private static JsonElement Root(Solid3d body, Transaction tx, Action cancel)
    {
        var budget = new ModelInventoryBudget(cancel); using var data = body.XData; using var links = body.GetPersistentReactorIds();
        var reactors = new List<string?>(); foreach (ObjectId link in links) { cancel(); reactors.Add(Handle(link)); }
        var value = Wire.Element(new
        {
            handle = Handle(body.ObjectId),
            owner = Handle(body.OwnerId),
            extension = Handle(body.ExtensionDictionary),
            body.HasFields,
            body.RecordHistory,
            body.ShowHistory,
            layer_handle = Handle(body.LayerId),
            display = EntityAdapter.StoredProperties(body),
            xdata = data == null ? null : NativeOwnedMetadata.Buffer(data, cancel, budget.Add),
            reactors,
            owned = NativeOwnedMetadata.Capture(body, tx, cancel, observeAssocManager: true)
        });
        SerializedCapacity357.Check(value, Wire.PayloadBudget - 16384, cancel); return value;
    }
    internal static Plan Prepare(SolidReplacement409 edit, Database db, Transaction tx, ObjectId target, ObjectId source, Action cancel)
    {
        cancel(); Need(target != source, "Replacement source and target cannot share an identity.");
        var destination = (Solid3d)tx.GetObject(target, OpenMode.ForRead); var original = (Solid3d)tx.GetObject(source, OpenMode.ForRead);
        CadSolidEditor.CheckSource(destination, db, cancel); var documentedSource = CadSolidEditor.CheckSource(original, db, cancel);
        using var sourceData = original.XData; using var sourceLinks = original.GetPersistentReactorIds();
        Need(!documentedSource && !original.HasFields && original.ExtensionDictionary.IsNull && sourceLinks.Count == 0 &&
            (sourceData == null || sourceData.AsArray().Length == 0),
            "Replacement input must be a plain direct shape without owned source data or documentation; source ownership is not transferred.", "UNSUPPORTED_SOLID_DEPENDENCIES");
        var targetRoot = Root(destination, tx, cancel);
        bool manager = targetRoot.GetProperty("owned").EnumerateArray().Any(record => record.TryGetProperty("assoc_manager_observation", out _));
        return new(target, source, targetRoot, Root(original, tx, cancel), Geometry(original), manager);
    }
    internal static void Verify(Plan plan, Database db, Action cancel)
    {
        using var tx = db.TransactionManager.StartTransaction(); cancel();
        var target = tx.GetObject(plan.Target, OpenMode.ForRead) as Solid3d; var source = tx.GetObject(plan.Source, OpenMode.ForRead) as Solid3d;
        Need(target != null && source != null && target.GetType() == typeof(Solid3d) && source.GetType() == typeof(Solid3d), "Replacement lost a native body identity.");
        CadSolidEditor.CheckGeometry(target!, cancel);
        Need(plan.TargetRoot.GetRawText() == Root(target!, tx, cancel).GetRawText(), "Native hand-over changed retained root data or dependencies.");
        Need(plan.SourceRoot.GetRawText() == Root(source!, tx, cancel).GetRawText() &&
            plan.SourceGeometry.GetRawText() == Geometry(source!).GetRawText(), "Replacement changed the original source.", "NATIVE_SOLID_REPLACEMENT_SOURCE_CHANGED");
        // These observations support the intended copy; the useful shape is
        // assessed visually, rather than requiring an invented volume oracle.
        var current = Geometry(target!); foreach (var field in new[] { "volume", "min", "max", "centroid" })
            Need(ModelParameterSnapshot.Equivalent(plan.SourceGeometry.GetProperty(field), current.GetProperty(field)), "Replacement body differs from the committed source geometry observation.");
    }
    internal static bool Handles(ModifyArguments args) => args.Operations is [SolidReplacement409];

    static void Require(bool ok, string code, string message)
    { if (!ok) throw new DomainException(code, message); }

    internal static Result Execute(ModifyArguments args, DocumentState state, WriteJob job, ReadContext context, JsonLog log)
    {
        CadSolidReplacement409.Plan? plan = null;
        DirectSolidDocumentation.Snapshot? documentation = null;
        DirectSolidDocumentation.Snapshot? settledDocumentation = null;
        NativeSolidReplacement409.Observation? native = null;
        McUndoPoint? tracker = null;
        bool nativeInvoked = false, nativeReturned = false, started = false;
        int managedTransactionsBeforeNative = -1;
        string stage = "preparation";
        var document = state.Document;
        var database = document.Database;
        IntPtr expectedDatabase = database.UnmanagedObject;
        ReadContext CurrentContext() => new(args.BridgeId, args.DocumentId, state.Revision, DateTime.UtcNow.ToString("O"));
        void Trace(string phase)
        {
            try
            {
                log.Write(job.Operation, job.OriginalRequestId, "solid_replacement_native",
                    transaction: JsonSerializer.Serialize(new
                    {
                        phase,
                        native_invoked = nativeInvoked,
                        native_returned = nativeReturned,
                        managed_transactions_before_native = managedTransactionsBeforeNative,
                        started,
                        native
                    }, Wire.Json), operationId: job.OperationId);
            }
            catch (Exception) { /* Optional tracing cannot change native verification. */ }
        }
        void CloseTracker()
        {
            var current = tracker; tracker = null;
            if (current == null) return;
            try { current.Stop(); } finally { current.Dispose(); }
        }
        void Current(bool beforeWrite)
        {
            job.CheckCancellation();
            Require(ReferenceEquals(Application.DocumentManager.MdiActiveDocument, document) && state.Id == args.DocumentId &&
                document.Database.UnmanagedObject == expectedDatabase && expectedDatabase != IntPtr.Zero,
                "DOCUMENT_CONTEXT_MISMATCH", "Solid replacement requires the exact original active document and native database.");
            Require(!document.IsReadOnly, "DOCUMENT_READ_ONLY", "The original document became read-only.");
            if (beforeWrite)
            {
                // Installed public getter reports this actual managed manager;
                // the native helper observes its own NRX manager separately.
                managedTransactionsBeforeNative = database.TransactionManager.NumberOfActiveTransactions;
                Require(managedTransactionsBeforeNative == 0, "ACTIVE_TRANSACTION", "Close managed transactions before entering owning native solid replacement.");
                Require(state.Revision == args.ExpectedRevision, "STALE_DOCUMENT", "The original revision changed before the native hand-over.");
                Require((int)database.Insunits == 4, "UNSUPPORTED_UNITS", "Native solid replacement requires Millimeters without implicit scaling.");
            }
        }
        Result Failure(Exception error)
        {
            if (job.CommittedResult is { } committed) return committed;
            Exception? cleanup = null;
            try { CloseTracker(); } catch (Exception failed) { cleanup = failed; }
            bool unknown = started || cleanup != null || nativeInvoked && (!nativeReturned || native == null || !native.CleanupSucceeded);
            string cause = error is DomainException domain ? domain.Error.code : error is OperationCanceledException ? "CANCELLED" : "NATIVE_SOLID_REPLACEMENT_FAILED";
            return job.Error(unknown ? "WRITE_OUTCOME_UNKNOWN" : cause,
                unknown ? "Native solid replacement or its cleanup is unverified. Inspect the retained target, source and views; no retry or rollback is claimed." : error.Message,
                unknown ? "unknown" : "not_started", context,
                new
                {
                    cause,
                    stage,
                    native_invoked = nativeInvoked,
                    native_returned = nativeReturned,
                    started,
                    native,
                    message = error.Message,
                    native_details = (error as DomainException)?.Error.details,
                    cleanup_error = cleanup?.GetType().Name,
                    cleanup_message = cleanup?.Message
                })
                with
            { warnings = [Warning, WriteRecovery.Warning] };
        }
        try
        {
            Require(Handles(args), "INVALID_OPERATION_ORDER", "Solid replacement must be the sole existing replace_solid operation.");
            Current(true);
            // Resolve performs the production type/owner/layer/history/metadata
            // and plain-source guards, including the existing Prepare snapshot.
            using (var selector = database.TransactionManager.StartTransaction())
            {
                var resolved = CadModifier.Resolve(args, database, selector, job.CheckCancellation);
                CadModifier.Preview(args, resolved, selector, job.CheckCancellation);
                plan = resolved.SolidReplacement ?? throw new DomainException("INVALID_OPERATION_ORDER", "The current production replacement plan is unavailable.");
                if (resolved.DocumentedSolids.Count > 0)
                    documentation = DirectSolidDocumentation.Capture(document, args.Operations.SelectMany(edit => edit.Handles), job.CheckCancellation);
            }
            // No selector/managed transaction or managed entity wrapper is held.
            Current(true);
            NativeSolidReplacement409.EnsureAvailable();
            var edit = (SolidReplacement409)args.Operations[0];
            ulong targetHandle = ulong.Parse(edit.Handles[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            ulong sourceHandle = ulong.Parse(edit.SourceHandle, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var output = new ModifiedEntities(args.OperationId, "committed", false,
                [new(args.BridgeId, args.DocumentId, edit.Handles[0])], [], [], [], "not_guaranteed");
            SerializedCapacity357.Check(output, Wire.PayloadBudget - 8192, job.CheckCancellation);
            tracker = new McUndoPoint(false); tracker.Start();
            Current(true); stage = "native_hand_over_and_cleanup";
            nativeInvoked = true;
            native = NativeSolidReplacement409.Invoke(expectedDatabase, targetHandle, sourceHandle, () =>
            {
                Current(true); started = true; Trace("native_hand_over_begin");
            });
            nativeReturned = true;
            started |= native.HandoverStarted;
            Trace("native_returned");
            Require(NativeSolidReplacement409.Completed(native, targetHandle, sourceHandle),
                "NATIVE_SOLID_REPLACEMENT_FAILED", "The owning native hand-over or its exact object cleanup did not complete.");
            Current(false); stage = "fresh_native_readback";
            CadSolidReplacement409.Verify(plan, database, job.CheckCancellation);
            if (documentation != null)
            {
                stage = "documentation_refresh";
                DirectSolidDocumentation.RefreshAndVerify(document, documentation);
                CadSolidReplacement409.Verify(plan, database, job.CheckCancellation);
            }
            // Tracker finalization may finish host work. Command-end verification
            // below is read-only; no delayed setter or refresh is installed.
            stage = "tracker_close"; CloseTracker();
            Current(false);
            if (documentation != null)
                settledDocumentation = DirectSolidDocumentation.Capture(document, documentation.Targets, job.CheckCancellation);
            var verifiedPlan = plan;
            stage = "awaiting_post_command_verification"; job.CheckCancellation(); job.BeginCommit();
            return NativePostCommandVerification422.Defer(job, () =>
            {
                stage = "post_command_readback"; Current(false);
                CadSolidReplacement409.Verify(verifiedPlan, database, job.CheckCancellation);
                if (settledDocumentation != null)
                {
                    var final = DirectSolidDocumentation.Capture(document, settledDocumentation.Targets, job.CheckCancellation);
                    Require(ModelParameterSnapshot.Equivalent(settledDocumentation.Common, final.Common) &&
                        settledDocumentation.Sections.Count == final.Sections.Count && settledDocumentation.Sections.All(pair =>
                            final.Sections.TryGetValue(pair.Key, out var value) && ModelParameterSnapshot.Equivalent(pair.Value, value)),
                        "NATIVE_SOLID_REPLACEMENT_MISMATCH", "Documentation or source/owner data changed after command finalization.");
                }
                var warnings = new List<string> { Warning, WriteRecovery.Warning };
                if (verifiedPlan.HasAssocManager)
                    warnings.Add("SOLID_REPLACEMENT_MANAGER_SCOPE: Actual resident owner/data/reactor identities were retained; private AssocManager evaluation is not completely observed.");
                var result = Result.Ok(job.OriginalRequestId, output, CurrentContext(), warnings.ToArray());
                job.RecordCommit(result); Trace("committed_after_command_readback"); return result;
            }, Failure);
        }
        catch (Exception error) { return Failure(error); }
    }
}
