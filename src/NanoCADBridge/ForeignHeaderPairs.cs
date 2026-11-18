namespace NanoCADBridge;

internal sealed class ForeignHeaderPairs : IDisposable
{
    [ThreadStatic] static ForeignHeaderPairs? active;
    static readonly HashSet<string> Known = new(StringComparer.Ordinal)
        { "INSUNITS", "MEASUREMENT", "CTABLESTYLE", "CMLEADERSTYLE", "CANNOSCALE", "LAYEREVAL", "LAYERNOTIFY" };
    readonly IntPtr original;
    readonly List<(string Name, Action<string> Invalidate)> deferred = new();
    IntPtr observedOwner;
    string? pending;
    bool conflict, done, reading;
    public string? ConflictReason { get; private set; }

    public ForeignHeaderPairs(IntPtr original)
    {
        if (active != null) throw new InvalidOperationException("An offscreen capture is already active on this thread.");
        this.original = original;
        active = this;
    }

    public bool Reading
    {
        set
        {
            if (!value && pending != null) Conflict("unpaired_side_header");
            reading = value;
        }
    }

    public void Conflict(string reason = "unattributed_header")
    {
        ConflictReason ??= reason;
        conflict = true;
        pending = null;
    }

    public void ObserveSide(string name, IntPtr owner)
    {
        if (!reading || owner == IntPtr.Zero || owner == original || !Known.Contains(name) || pending != null ||
            observedOwner != IntPtr.Zero && observedOwner != owner)
        {
            Conflict();
            return;
        }
        pending = name;
        observedOwner = owner;
    }

    public static bool AttributeForeign(string name, Action<string> invalidate)
    {
        var scope = active;
        if (scope == null) return false;
        if (scope.conflict || !scope.reading || scope.pending != name)
        {
            scope.Conflict("unpaired_application_header");
            return false;
        }
        scope.pending = null;
        scope.deferred.Add((name, invalidate));
        return true;
    }

    public bool Finish(bool unchanged, IntPtr actualSide)
    {
        if (done) throw new InvalidOperationException("The offscreen observation has already ended.");
        done = true;
        active = null;
        bool accepted = unchanged && !conflict && pending == null && actualSide != IntPtr.Zero &&
            actualSide != original && (observedOwner == IntPtr.Zero || observedOwner == actualSide);
        if (!accepted) foreach (var item in deferred) item.Invalidate(item.Name);
        return accepted;
    }

    public void Dispose()
    {
        if (!done) Finish(false, IntPtr.Zero);
    }
}
