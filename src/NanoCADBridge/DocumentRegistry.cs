using HostMgd.ApplicationServices;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Application = HostMgd.ApplicationServices.Application;

namespace NanoCADBridge;

internal sealed class DocumentState : IDisposable
{
    public Document Document { get; }
    public string Id { get; } = Guid.NewGuid().ToString("D");
    private readonly DocumentVersion _version;
    public string Revision => _version.Revision;
    public string SelectionRevision => _version.SelectionRevision;
    public DocumentState(Document document, ContextEpoch context)
    {
        Document = document;
        _version = new(context);
        var db = document.Database;
        db.ObjectAppended += Appended;
        db.ObjectModified += Modified;
        db.ObjectErased += Erased;
        db.ObjectUnappended += Unappended;
        db.ObjectReappended += Reappended;
        document.ImpliedSelectionChanged += SelectionChanged;
        // Conservative invalidation also covers native command/undo paths without detailed events.
        document.CommandEnded += CommandFinished;
        document.CommandCancelled += CommandFinished;
        document.CommandFailed += CommandFinished;
    }
    private void Appended(object? sender, ObjectEventArgs args)
    { PointCloudReadScope402.Other(this, "ObjectAppended", args); _version.Changed(); }
    private void Unappended(object? sender, ObjectEventArgs args)
    { PointCloudReadScope402.Other(this, "ObjectUnappended", args); _version.Changed(); }
    private void Reappended(object? sender, ObjectEventArgs args)
    { PointCloudReadScope402.Other(this, "ObjectReappended", args); _version.Changed(); }
    private void Modified(object? sender, ObjectEventArgs args)
    { if (!PointCloudReadScope402.Modified(this, args)) _version.Changed(); }
    private void Erased(object? sender, ObjectErasedEventArgs args)
    { PointCloudReadScope402.Other(this, "ObjectErased"); _version.Changed(); }
    private void SelectionChanged(object? sender, EventArgs args)
    { PointCloudReadScope402.Other(this, "SelectionChanged"); _version.SelectionChanged(); }
    private void CommandFinished(object? sender, CommandEventArgs args)
    { PointCloudReadScope402.Other(this, "CommandFinished", detail: args.GlobalCommandName); _version.Changed(); }
    internal void RecordDeferredPointCloudChange() => _version.Changed();
    public void Dispose()
    {
        var db = Document.Database;
        db.ObjectAppended -= Appended;
        db.ObjectModified -= Modified;
        db.ObjectErased -= Erased;
        db.ObjectUnappended -= Unappended;
        db.ObjectReappended -= Reappended;
        Document.ImpliedSelectionChanged -= SelectionChanged;
        Document.CommandEnded -= CommandFinished;
        Document.CommandCancelled -= CommandFinished;
        Document.CommandFailed -= CommandFinished;
    }
}

internal sealed class DocumentRegistry : IDisposable
{
    // All registry access is restricted to nanoCAD's UI thread. Event counters above are atomic.
    private readonly Dictionary<Document, DocumentState> _open = new();
    private readonly ContextEpoch _context = new();
    public DocumentRegistry()
    {
        var manager = Application.DocumentManager;
        manager.DocumentCreated += Created;
        manager.DocumentToBeDestroyed += Closing;
        manager.DocumentActivated += ContextChanged;
        Application.SystemVariableChanged += SystemVariableChanged;
        foreach (Document document in manager) Get(document);
    }
    public DocumentState Get(Document document)
    {
        if (!_open.TryGetValue(document, out var state)) { state = new(document, _context); _open.Add(document, state); }
        return state;
    }
    public bool IsOpen(string id) => _open.Values.Any(s => s.Id == id);
    private void Created(object? sender, DocumentCollectionEventArgs args) => Get(args.Document);
    private void ContextChanged(object? sender, DocumentCollectionEventArgs args)
    {
        PointCloudReadScope402.ContextChanged("DocumentActivated", _open.TryGetValue(args.Document, out var active) ? active.Id : "unregistered_document");
        _context.Advance();
    }
    private void SystemVariableChanged(object? sender, HostMgd.ApplicationServices.SystemVariableChangedEventArgs args)
    {
        if (args.Changed) { PointCloudReadScope402.ContextChanged("SystemVariableChanged", args.Name); _context.Advance(); }
    }
    private void Closing(object? sender, DocumentCollectionEventArgs args)
    {
        if (_open.Remove(args.Document, out var state)) state.Dispose();
    }
    public void Dispose()
    {
        Application.DocumentManager.DocumentCreated -= Created;
        Application.DocumentManager.DocumentToBeDestroyed -= Closing;
        Application.DocumentManager.DocumentActivated -= ContextChanged;
        Application.SystemVariableChanged -= SystemVariableChanged;
        foreach (var state in _open.Values) state.Dispose();
        _open.Clear();
    }
}
