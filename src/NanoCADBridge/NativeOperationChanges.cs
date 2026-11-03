using Teigha.DatabaseServices;

namespace NanoCADBridge;

// Keep only objects touched by this native operation, not a copy of the entire drawing.
// Construct/dispose on the CAD thread. No open DBObject survives an event callback.
internal sealed class NativeOperationChanges : IDisposable
{
    private readonly Database _db;
    private readonly HashSet<ObjectId> _appended = new();
    public IEnumerable<ObjectId> Appended => _appended.Where(id=>id.IsValid&&!id.IsErased&&!id.IsEffectivelyErased);
    public NativeOperationChanges(Database db){_db=db;db.ObjectAppended+=Added;db.ObjectReappended+=Added;}
    private void Added(object? sender,ObjectEventArgs e)=>_appended.Add(e.DBObject.ObjectId);
    public void Dispose(){_db.ObjectAppended-=Added;_db.ObjectReappended-=Added;}
}
