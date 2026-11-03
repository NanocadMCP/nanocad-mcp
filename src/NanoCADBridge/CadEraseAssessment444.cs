using NanoCAD.Contracts;
using Db = Teigha.DatabaseServices;

namespace NanoCADBridge;

internal sealed record EraseAssessmentTarget444(EntityRef @ref, string entity_type, string route,
    string? native_owner_id, string[] native_owned_history_ids);

// Observed current admission for one explicit selection; never authorization,
// a persistent plan/token, or a promise that native storage cleanup is complete.
internal sealed record EraseAssessment444(string status, bool complete, string target_space,
    string[] requested_handles, EraseAssessmentTarget444[] targets, string[] native_owned_scope,
    DomainError[] blockers, bool first_blocker_only, string coverage, string authorization,
    string provenance, string write_guards, string detail)
{
    private const string Coverage = "same_current_native_and_ordinary_erase_admission_for_selected_set";
    private const string GuardPolicy = "rechecked_at_write";

    internal static EraseAssessment444 Eligible(string[] handles, EraseAssessmentTarget444[] targets, string[] scope) =>
        new("eligible", true, "model", handles, targets, scope, [], false, Coverage,
            "not_assessed", "not_assessed", GuardPolicy,
            "Technical current Model Space admission only. The caller must establish the selected objects' task role and intended removal. Native-owned scope lists observed selected roots, owned history and proven-owned auto-erased sketch inputs; it does not promise complete private storage cleanup. No erase or write operation_id was created or submitted.");

    internal static EraseAssessment444 Failed(string[] handles, string status, DomainError error) =>
        new(status, false, "model", handles, [], [], [error], true, Coverage,
            "not_assessed", "not_assessed", GuardPolicy,
            "Admission did not complete; only the first actual blocker or unavailable observation is reported. Empty target/scope arrays mean not assessed, never absence of dependencies. This read grants no authorization and submits no erase.");
}

internal static class CadEraseAssessment444
{
    internal static EraseAssessment444 Read(Arguments read, DocumentState state, Db.Transaction transaction,
        string instance, CancellationToken cancellationToken)
    {
        void Cancel() => cancellationToken.ThrowIfCancellationRequested();
        var handles = read.Handles!;
        try
        {
            Cancel();
            if (state.Document.IsReadOnly)
                throw new DomainException("DOCUMENT_READ_ONLY", "The document is read-only.");
            // This inert sentinel only satisfies the existing internal argument
            // record used by admission. No job, request or write ID is allocated,
            // exposed, fingerprinted, cached or submitted by the read.
            var args = new ModifyArguments(instance, state.Id, state.Revision, Guid.Empty.ToString("D"),
                "Millimeters", [new EraseSpec(handles)]) { TargetSpace = "model" };
            if (ModelBodyEraseWriter429.Handles(args, state, transaction))
                return ModelBodyEraseWriter429.ReadAssessment(args, state, transaction, Cancel);

            var resolved = CadModifier.Resolve(args, state.Document.Database, transaction, Cancel);
            CadModifier.Preview(args, resolved, transaction, Cancel);
            var targets = handles.Select(handle =>
            {
                Cancel();
                var entity = (Db.Entity)transaction.GetObject(resolved.Sources[handle], Db.OpenMode.ForRead);
                return new EraseAssessmentTarget444(new(instance, state.Id, handle),
                    EntityAdapter.Summary(entity, resolved.Model, instance, state.Id).type,
                    "ordinary", null, []);
            }).ToArray();
            return EraseAssessment444.Eligible(handles, targets, []);
        }
        catch (DomainException error) when (error.Error.code is not ("CANCELLED" or "STALE_DOCUMENT" or "DOCUMENT_NOT_ACTIVE" or "DOCUMENT_CLOSED"))
        {
            bool blocked = error.Error.code is "DOCUMENT_READ_ONLY" or "OUT_OF_SCOPE" or "LAYER_UNAVAILABLE" or
                "MODEL_HAS_EXTERNAL_DEPENDENTS" or "MODEL_ERASE_SHARED_INPUT" or "OBJECT_NOT_FOUND" or "OBJECT_ERASED" ||
                error.Error.code.StartsWith("UNSUPPORTED_", StringComparison.Ordinal) ||
                error.Error.code.StartsWith("INVALID_", StringComparison.Ordinal);
            return EraseAssessment444.Failed(handles, blocked ? "blocked" : "unavailable", error.Error);
        }
        catch (System.Exception error) when (error is not (OperationCanceledException or OutOfMemoryException or DomainException))
        {
            return EraseAssessment444.Failed(handles, "unavailable", new("ERASE_ASSESSMENT_UNAVAILABLE",
                "The current native erasure admission could not be observed.", false,
                new { error_type = error.GetType().FullName, native_message = error.Message }));
        }
    }
}
