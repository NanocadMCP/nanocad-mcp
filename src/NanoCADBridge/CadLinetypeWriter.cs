using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class CadLinetypeWriter
{
    internal sealed record Resolved(ObjectId Table, string Name, EntityRef Context, LinetypeProperties Expected);
    private static EntityRef Reference(EntityRef context, ObjectId id) => new(context.bridge_instance_id, context.document_id, Validation.NormalizeHandle(id.Handle.ToString()));
    public static Resolved Resolve(StructureWriteArguments args, CreateLinetypeEdit operation, Database db, Transaction tr, Action check)
    {
        check(); var expected = LinetypeWriteValidation.Expected(operation);
        var table = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
        if (table.Has(operation.Name)) throw new DomainException("LINETYPE_EXISTS", "Linetype already exists; creation never overwrites it.", false, new { name = operation.Name });
        return new(table.ObjectId, operation.Name, new(args.BridgeId, args.DocumentId, Validation.NormalizeHandle(table.Handle.ToString())), expected);
    }
    public static LinetypeTableRecord Preview(Resolved resolved, Transaction tr, Database db, List<DBObject> owned, Action check)
    {
        check(); var record = new LinetypeTableRecord(); owned.Add(record);
        if (record.GetType() != typeof(LinetypeTableRecord) || !record.ObjectId.IsNull)
            throw new DomainException("API_EXCEPTION", "Linetype constructor did not return an exact detached record.");
        check(); record.Name = resolved.Name;
        check(); record.AsciiDescription = resolved.Expected.description;
        check(); record.NumDashes = resolved.Expected.dash_count;
        for (int index = 0; index < resolved.Expected.dash_count; index++)
        { check(); record.SetDashLengthAt(index, resolved.Expected.elements[index].length); }
        check(); record.PatternLength = resolved.Expected.pattern_length;
        check(); record.IsScaledToFit = false;
        if (record.Name != resolved.Name) throw new DomainException("API_EXCEPTION", "Linetype name setter changed the requested name.");
        LinetypeWriteValidation.RequireSame(resolved.Expected, LinetypeMetadataReader.ReadDetached(record, resolved.Context, tr, db,
            id => Reference(resolved.Context, id), check, check));
        return record;
    }
    public static StructureChange Apply(int index, Resolved resolved, LinetypeTableRecord record, Transaction tr, Database db, WriteJob job)
    {
        job.CheckCancellation(); var table = (LinetypeTable)tr.GetObject(resolved.Table, OpenMode.ForWrite);
        var id = table.Add(record); tr.AddNewlyCreatedDBObject(record, true);
        if (id.IsNull || record.ObjectId != id || record.OwnerId != resolved.Table || record.Name != resolved.Name)
            throw new DomainException("API_EXCEPTION", "Linetype insertion produced an unexpected identity, owner or name.");
        LinetypeWriteValidation.RequireSame(resolved.Expected, LinetypeMetadataReader.Read(record, tr, db,
            value => Reference(resolved.Context, value), job.CheckCancellation, job.CheckCancellation));
        return new(index, "create_linetype", Reference(resolved.Context, id), "linetype", record.Name, true);
    }
}
