using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Runtime;

namespace NanoCADBridge;

internal static class DrawingClonePreflight
{
    const string Container = "SPDSDIMCONTAINER2";
    const string Alternatives = "Use windows_graphics_capture for the live drawing, or saved_drawing for the file on disk.";

    internal sealed record Match(string Instance, string Dimension, string Dictionary, string Block, bool OriginalProxyClass)
    {
        internal DomainException Refusal() => new("DRAWING_CLONE_UNSAFE",
            "Taking a current_drawing snapshot can modify an existing native dimension container. " + Alternatives,
            false, new
            {
                native_class = Container,
                instance_handle = Instance,
                dimension_handle = Dimension,
                dictionary_handle = Dictionary,
                block_record_handle = Block,
                original_proxy_class = OriginalProxyClass,
                clone_started = false
            });
    }

    static bool Live(ObjectId id, Database db) => !id.IsNull && id.IsValid && !id.IsErased && !id.IsEffectivelyErased &&
        id.Database?.UnmanagedObject == db.UnmanagedObject;

    static bool Owned(DBObject item, ObjectId id, ObjectId owner, Database db) =>
        item.ObjectId == id && item.OwnerId == owner && item.Database?.UnmanagedObject == db.UnmanagedObject;

    static Match? FindInDimension(Transaction tx, Database db, DBObject dimension, ObjectId block, CancellationToken cancel)
    {
        var extension = dimension.ExtensionDictionary;
        if (!Live(extension, db)) return null;
        var pending = new Stack<(ObjectId Id, ObjectId Owner)>();
        var seen = new HashSet<ObjectId>();
        pending.Push((extension, dimension.ObjectId));
        while (pending.TryPop(out var next))
        {
            cancel.ThrowIfCancellationRequested();
            var value = tx.GetObject(next.Id, OpenMode.ForRead);
            if (!Owned(value, next.Id, next.Owner, db) || !seen.Add(next.Id)) continue;
            using var type = value.GetRXClass();
            if (type.DxfName == Container || value is ProxyObject { OriginalDxfName: Container })
                return new(next.Id.Handle.ToString(), dimension.ObjectId.Handle.ToString(), next.Owner.Handle.ToString(),
                    block.Handle.ToString(), type.DxfName != Container);
            if (value is not DBDictionary dictionary) continue;
            foreach (DBDictionaryEntry child in dictionary)
            {
                cancel.ThrowIfCancellationRequested();
                if (Live(child.Value, db)) pending.Push((child.Value, next.Id));
            }
        }
        return null;
    }

    // Wblock operates on the complete database. A registered DWG class or its
    // reported instance count cannot identify a live object or its owner.
    internal static Match? Find(Database db, CancellationToken cancel)
    {
        try
        {
            using var tx = db.TransactionManager.StartOpenCloseTransaction();
            using var dimensionType = RXObject.GetClass(typeof(Dimension));
            using var proxyType = RXObject.GetClass(typeof(ProxyEntity));
            var table = (BlockTable)tx.GetObject(db.BlockTableId, OpenMode.ForRead);
            foreach (ObjectId block in table)
            {
                cancel.ThrowIfCancellationRequested();
                if (!Live(block, db)) continue;
                var definition = (BlockTableRecord)tx.GetObject(block, OpenMode.ForRead);
                if (!Owned(definition, block, table.ObjectId, db)) continue;
                foreach (ObjectId id in definition)
                {
                    cancel.ThrowIfCancellationRequested();
                    if (!Live(id, db)) continue;
                    using var type = id.ObjectClass;
                    bool nativeDimension = type.IsDerivedFrom(dimensionType);
                    if (!nativeDimension && !type.IsDerivedFrom(proxyType)) continue;
                    var item = tx.GetObject(id, OpenMode.ForRead);
                    if (!Owned(item, id, block, db)) continue;
                    if (!nativeDimension && item is not ProxyEntity { OriginalDxfName: "DIMENSION" or "ARC_DIMENSION" or "LARGE_RADIAL_DIMENSION" }) continue;
                    if (FindInDimension(tx, db, item, block, cancel) is { } match) return match;
                }
            }
            return null;
        }
        catch (System.Exception e) when (e is Teigha.Runtime.Exception or InvalidCastException)
        {
            throw new DomainException("DRAWING_CLONE_STATUS_UNAVAILABLE",
                "The active drawing's dimension ownership could not be checked before taking a snapshot. " + Alternatives);
        }
    }
}
