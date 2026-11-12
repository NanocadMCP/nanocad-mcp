namespace NanoCAD.Contracts;

// Observed native ObjectId predicates, not inferred handle/name roles. The
// bridge must establish these facts from the actual current database.
public readonly record struct NativePersistentReferenceState(bool IsNull, bool IsValid, bool SameDatabase,
    bool IsErased, bool IsEffectivelyErased);

public static class NativePersistentReferences
{
    public static bool IsInactive(NativePersistentReferenceState actual)
    {
        string? refusal = actual.IsNull ? "null_reference" : !actual.IsValid ? "invalid_reference" :
            !actual.SameDatabase ? "foreign_database" : null;
        if (refusal != null)
            throw new DomainException("UNSUPPORTED_PERSISTENT_REFERENCE", "The actual persistent-reference identity is unavailable or foreign.", false,
                new { reason = refusal, actual });
        return actual.IsErased || actual.IsEffectivelyErased;
    }

    public static bool ExcludeKnownErased<TKey>(TKey identity, NativePersistentReferenceState actual,
        IReadOnlySet<TKey> permittedOriginal) where TKey : notnull
    {
        if (!IsInactive(actual)) return false; // A live entry is never hidden.
        if (!permittedOriginal.Contains(identity))
            throw new DomainException("UNSUPPORTED_PERSISTENT_REFERENCE", "An erased persistent reference is outside the exact original retirement group.", false,
                new { reason = "unpermitted_erased_reference", actual });
        return true;
    }
}
