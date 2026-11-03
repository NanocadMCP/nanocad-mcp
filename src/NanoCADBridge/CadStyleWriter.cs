using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class CadStyleWriter
{
    internal sealed record Resolved(ObjectId Table, ObjectId Source, string SourceName, string Name,
        TextStyleProperties Original, TextStyleProperties Expected, bool Editing = false);
    public static Resolved Resolve(CreateTextStyleEdit operation, Database db, Transaction tr, Action check)
        => Resolve(operation, db, tr, check, false);
    public static Resolved Resolve(SetTextStylePropertiesEdit operation, Database db, Transaction tr, Action check)
        => Resolve(new CreateTextStyleEdit(operation.ExpectedName, operation.Handle, operation.Properties), db, tr, check, true);
    private static Resolved Resolve(CreateTextStyleEdit operation, Database db, Transaction tr, Action check, bool editing)
    {
        check();
        var table = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
        if (!editing && table.Has(operation.Name)) throw new DomainException("STYLE_EXISTS", "Text style already exists; creation never overwrites it.", false, new { name = operation.Name });
        ObjectId sourceId;
        try { sourceId = CadStructureWriter.Find(db, operation.BaseStyleHandle); }
        catch (DomainException error) when (error.Error.code is "OBJECT_NOT_FOUND" or "OBJECT_ERASED")
        { throw new DomainException("STYLE_UNAVAILABLE", "Base text style is missing or erased.", false, new { handle = operation.BaseStyleHandle }); }
        if (tr.GetObject(sourceId, OpenMode.ForRead) is not TextStyleTableRecord source || source.GetType() != typeof(TextStyleTableRecord) || source.OwnerId != table.ObjectId)
            throw new DomainException("STYLE_UNAVAILABLE", "Base handle must refer to an exact native text style in this document's style table.");
        var original = TextStyleMetadataReader.Read(source, check);
        if (editing && source.Name != operation.Name) throw new DomainException("STYLE_NAME_MISMATCH", "Text style name differs from expected_name; refresh get_structure text_styles.");
        var expected = StyleCloneValidation.Expected(original, operation.Properties);
        return new(table.ObjectId, sourceId, source.Name, operation.Name, original, expected, editing);
    }
    public static TextStyleTableRecord Preview(Resolved resolved, Transaction tr, List<DBObject> owned, Action check)
    {
        check();
        var source = (TextStyleTableRecord)tr.GetObject(resolved.Source, OpenMode.ForRead);
        void VerifySource()
        {
            if (source.ObjectId != resolved.Source || source.OwnerId != resolved.Table || source.Name != resolved.SourceName)
                throw new DomainException("API_EXCEPTION", "Source style identity or name changed during preview.");
            StyleCloneValidation.RequireSame(resolved.Original, TextStyleMetadataReader.Read(source, check));
        }
        VerifySource();
        var nativeClone = source.Clone();
        if (ReferenceEquals(nativeClone, source)) throw new DomainException("API_EXCEPTION", "Clone returned the source wrapper; no setters were called.");
        if (nativeClone is not DBObject clone)
        {
            (nativeClone as IDisposable)?.Dispose();
            throw new DomainException("API_EXCEPTION", "Clone did not return a database object.");
        }
        owned.Add(clone);
        if (clone.GetType() != typeof(TextStyleTableRecord) || !clone.ObjectId.IsNull)
            throw new DomainException("API_EXCEPTION", "Style clone is not an exact detached native text style; no setters were called.");
        var copy = (TextStyleTableRecord)clone;
        if (copy.Name != resolved.SourceName) throw new DomainException("API_EXCEPTION", "Clone changed the original style name.");
        StyleCloneValidation.RequireSame(resolved.Original, TextStyleMetadataReader.Read(copy, check));
        check();
        copy.Name = resolved.Name; copy.TextSize = resolved.Expected.height; copy.XScale = resolved.Expected.width_factor;
        if (copy.Name != resolved.Name) throw new DomainException("API_EXCEPTION", "Native setter changed the requested style name.");
        StyleCloneValidation.RequireSame(resolved.Expected, TextStyleMetadataReader.Read(copy, check));
        VerifySource();
        return copy;
    }
    public static StructureChange Apply(StructureWriteArguments args, int index, Resolved resolved, TextStyleTableRecord prepared, Transaction tr, WriteJob job)
    {
        job.CheckCancellation();
        if (resolved.Editing)
        {
            var existing = (TextStyleTableRecord)tr.GetObject(resolved.Source, OpenMode.ForWrite);
            if (existing.GetType() != typeof(TextStyleTableRecord) || existing.OwnerId != resolved.Table || existing.Name != resolved.SourceName)
                throw new DomainException("API_EXCEPTION", "Text style identity or name changed before apply.");
            StyleCloneValidation.RequireSame(resolved.Original, TextStyleMetadataReader.Read(existing, job.CheckCancellation));
            existing.TextSize = resolved.Expected.height; existing.XScale = resolved.Expected.width_factor;
            StyleCloneValidation.RequireSame(resolved.Expected, TextStyleMetadataReader.Read(existing, job.CheckCancellation));
            FontCoverage.RefreshDimensions(existing.Database, tr, resolved.Source, job.CheckCancellation);
            return new(index, "set_text_style_properties", new(args.BridgeId, args.DocumentId, Validation.NormalizeHandle(resolved.Source.Handle.ToString())), "text_style", existing.Name, false);
        }
        var table = (TextStyleTable)tr.GetObject(resolved.Table, OpenMode.ForWrite);
        var id = table.Add(prepared); tr.AddNewlyCreatedDBObject(prepared, true);
        if (id.IsNull || prepared.ObjectId != id || prepared.OwnerId != resolved.Table || prepared.Name != resolved.Name)
            throw new DomainException("API_EXCEPTION", "Native style insertion produced an unexpected identity/owner/name.");
        StyleCloneValidation.RequireSame(resolved.Expected, TextStyleMetadataReader.Read(prepared, job.CheckCancellation));
        return new(index, "create_text_style", new(args.BridgeId, args.DocumentId, Validation.NormalizeHandle(id.Handle.ToString())), "text_style", prepared.Name, true);
    }
}
