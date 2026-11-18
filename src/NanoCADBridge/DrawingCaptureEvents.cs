using HostMgd.ApplicationServices;
using Teigha.DatabaseServices;
using Application = HostMgd.ApplicationServices.Application;

namespace NanoCADBridge;

// A source/header change is always independent of an owned side-DB event,
// including when their property names happen to be the same.
internal sealed class DrawingCaptureEvents : IDisposable
{
    readonly Database source;
    readonly ForeignHeaderPairs pairs;
    Database? side;
    IntPtr sidePointer;
    public string? FirstSourceChange { get; private set; }
    public string? FirstSourceHandle { get; private set; }
    public string? HeaderConflict => pairs.ConflictReason;

    public DrawingCaptureEvents(Database source)
    {
        this.source = source;
        pairs = new ForeignHeaderPairs(source.UnmanagedObject);
        source.ObjectModified += Modified;
        source.ObjectErased += Erased;
        source.ObjectAppended += Appended;
        source.SystemVariableChanged += SourceHeader;
    }

    void Original(string change, DBObject item)
    {
        if (FirstSourceChange != null) return;
        FirstSourceChange = change;
        try { FirstSourceHandle = item.Handle.ToString(); }
        catch (Teigha.Runtime.Exception) { }
        pairs.Conflict("source_object_changed");
    }

    void Modified(object sender, ObjectEventArgs e) => Original("modified", e.DBObject);
    void Erased(object sender, ObjectErasedEventArgs e) => Original("erased", e.DBObject);
    void Appended(object sender, ObjectEventArgs e) => Original("appended", e.DBObject);

    void SourceHeader(object sender, Teigha.DatabaseServices.SystemVariableChangedEventArgs header)
    {
        if (header.Changed) pairs.Conflict("source_header_changed");
    }

    void SideHeader(object sender, Teigha.DatabaseServices.SystemVariableChangedEventArgs header)
    {
        if (header.Changed) pairs.ObserveSide(header.Name, (sender as Database)?.UnmanagedObject ?? IntPtr.Zero);
    }

    public void Register(Database database)
    {
        var pointer = database.UnmanagedObject;
        if (pointer == IntPtr.Zero || pointer == source.UnmanagedObject || sidePointer != IntPtr.Zero && sidePointer != pointer)
            throw new InvalidOperationException("The layout source is not the observed independent database.");
        if (sidePointer != IntPtr.Zero) return;
        side = database;
        sidePointer = pointer;
        database.SystemVariableChanged += SideHeader;
    }

    // The stream/open/clone stage is the only time that a foreign database
    // can legitimately produce the paired application header notifications.
    public void Reading(bool reading) => pairs.Reading = reading;

    public void ReleaseSide()
    {
        if (side != null) side.SystemVariableChanged -= SideHeader;
        side = null;
        Reading(false);
    }

    public bool Finish(bool unchanged) => pairs.Finish(unchanged, sidePointer);

    public void Dispose()
    {
        try { ReleaseSide(); }
        finally
        {
            source.ObjectModified -= Modified;
            source.ObjectErased -= Erased;
            source.ObjectAppended -= Appended;
            source.SystemVariableChanged -= SourceHeader;
            pairs.Dispose();
        }
    }
}
