using System.Globalization;

namespace NanoCAD.Contracts;

// Session context changes invalidate snapshots in every document. Event callbacks
// only advance a counter: they never open a database or query the CAD application.
public sealed class ContextEpoch
{
    private long _value;
    internal long Value => Interlocked.Read(ref _value);
    public void Advance() => Interlocked.Increment(ref _value);
}

public sealed class DocumentVersion
{
    private readonly ContextEpoch _context;
    private long _revision, _selection;
    public DocumentVersion(ContextEpoch context) => _context = context;
    public string Revision => unchecked((ulong)(Interlocked.Read(ref _revision) + _context.Value)).ToString(CultureInfo.InvariantCulture);
    public string SelectionRevision => unchecked((ulong)(Interlocked.Read(ref _selection) + _context.Value)).ToString(CultureInfo.InvariantCulture);
    public void Changed() => Interlocked.Increment(ref _revision);
    public void SelectionChanged() => Interlocked.Increment(ref _selection);
}
