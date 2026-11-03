using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class CadDimensionStyleWriter
{
    internal sealed record Resolved(ObjectId Table, ObjectId Source, string SourceName, string Name,
        EntityRef SourceRef, DimensionStyleProperties Original, DimensionStyleProperties Expected, bool Editing = false,
        DimensionStyleChanges? Changes = null, CadDimensionStyleDefaults364.State? RetainedDefaults = null, string? NotationWarning = null);
    private static EntityRef Reference(EntityRef context, ObjectId id) =>
        new(context.bridge_instance_id, context.document_id, Validation.NormalizeHandle(id.Handle.ToString()));
    private static DimensionStyleProperties Read(DimStyleTableRecord record, EntityRef context, Transaction tr, Database db, Action check) =>
        DimensionStyleMetadataReader.Read(record, tr, db, id => Reference(context, id), check, check);
    public static Resolved Resolve(StructureWriteArguments args, CreateDimensionStyleEdit operation, Database db, Transaction tr, Action check)
        => Resolve(args, operation, db, tr, check, false);
    public static Resolved Resolve(StructureWriteArguments args, SetDimensionStylePropertiesEdit operation, Database db, Transaction tr, Action check)
        => Resolve(args, new CreateDimensionStyleEdit(operation.ExpectedName, operation.Handle, operation.Properties), db, tr, check, true);
    private static Resolved Resolve(StructureWriteArguments args, CreateDimensionStyleEdit operation, Database db, Transaction tr, Action check, bool editing)
    {
        check();
        var table = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
        if (!editing && table.Has(operation.Name)) throw new DomainException("STYLE_EXISTS", "Dimension style already exists; creation never overwrites it.", false, new { name = operation.Name });
        ObjectId sourceId;
        try { sourceId = CadStructureWriter.Find(db, operation.BaseStyleHandle); }
        catch (DomainException error) when (error.Error.code is "OBJECT_NOT_FOUND" or "OBJECT_ERASED")
        { throw new DomainException("STYLE_UNAVAILABLE", "Base dimension style is missing or erased."); }
        if (tr.GetObject(sourceId, OpenMode.ForRead) is not DimStyleTableRecord source || source.GetType() != typeof(DimStyleTableRecord) || source.OwnerId != table.ObjectId)
            throw new DomainException("STYLE_UNAVAILABLE", "Base handle must refer to an exact native dimension style in this document's table.");
        var sourceRef = new EntityRef(args.BridgeId, args.DocumentId, Validation.NormalizeHandle(sourceId.Handle.ToString()));
        var original = Read(source, sourceRef, tr, db, check);
        if (editing && source.Name != operation.Name) throw new DomainException("STYLE_NAME_MISMATCH", "Dimension style name differs from expected_name; refresh get_structure dimension_styles.");
        var expected = DimensionStyleWriteValidation.Expected(source.Name, sourceRef, original, operation.Properties);
        foreach (var dependency in DimensionStyleWriteValidation.Dependencies(original))
        {
            check();
            if (dependency.state == "null") continue;
            var id = CadStructureWriter.Find(db, dependency.@ref!.handle);
            var record = tr.GetObject(id, OpenMode.ForRead);
            // The reader already verified exact table type and owner. Keep the
            // references intact; do not traverse or instantiate arrow content.
            if (record is not SymbolTableRecord symbol || symbol.IsDependent ||
                record is BlockTableRecord block && (block.IsAnonymous || block.IsDynamicBlock || block.IsLayout || block.IsFromExternalReference || block.IsFromOverlayReference) ||
                record is TextStyleTableRecord text && (text.IsShapeFile || text.IsVertical))
                throw new DomainException("UNSUPPORTED_STYLE_TYPE", "Dimension style dependencies must be ordinary local records.");
        }
        var retained = operation.Properties.Defaults == null ? null : CadDimensionStyleDefaults364.Capture(sourceId, sourceRef, db, tr, check, editing);
        var notice = operation.Properties.Defaults == null ? null : CadDimensionStyleDefaults364.RequireNotation(source, operation.Properties.Defaults, tr, check);
        return new(table.ObjectId, sourceId, source.Name, operation.Name, sourceRef, original, expected, editing, operation.Properties, retained, notice);
    }
    private static void VerifySource(Resolved resolved, Transaction tr, Database db, Action check)
    {
        check();
        var source = (DimStyleTableRecord)tr.GetObject(resolved.Source, OpenMode.ForRead);
        if (source.ObjectId != resolved.Source || source.OwnerId != resolved.Table || source.Name != resolved.SourceName)
            throw new DomainException("API_EXCEPTION", "Source dimension style identity or name changed during creation.");
        DimensionStyleWriteValidation.RequireSame(resolved.Original, Read(source, resolved.SourceRef, tr, db, check));
    }
    public static DimStyleTableRecord Preview(Resolved resolved, Transaction tr, Database db, List<DBObject> owned, Action check)
    {
        VerifySource(resolved, tr, db, check);
        var source = (DimStyleTableRecord)tr.GetObject(resolved.Source, OpenMode.ForRead);
        var nativeClone = source.Clone();
        if (ReferenceEquals(nativeClone, source)) throw new DomainException("API_EXCEPTION", "Clone returned the source wrapper; no setters were called.");
        if (nativeClone is not DBObject clone)
        {
            (nativeClone as IDisposable)?.Dispose();
            throw new DomainException("API_EXCEPTION", "Clone did not return a database object.");
        }
        owned.Add(clone);
        if (clone.GetType() != typeof(DimStyleTableRecord) || !clone.ObjectId.IsNull)
            throw new DomainException("API_EXCEPTION", "Dimension style clone is not exact and detached; no setters were called.");
        var copy = (DimStyleTableRecord)clone;
        DimensionStyleProperties Snapshot() => DimensionStyleMetadataReader.ReadDetached(copy, resolved.SourceRef, tr, db,
            id => Reference(resolved.SourceRef, id), check, check);
        if (copy.Name != resolved.SourceName) throw new DomainException("API_EXCEPTION", "Clone changed the original style name.");
        DimensionStyleWriteValidation.RequireSame(resolved.Original, Snapshot());
        check(); copy.Name = resolved.Name;
        check(); copy.Dimscale = resolved.Expected.dimension_scale;
        check(); copy.Dimtxt = resolved.Expected.text_height;
        check(); copy.Dimasz = resolved.Expected.arrow_size;
        if (resolved.Changes?.Defaults is { } defaults) CadDimensionStyleDefaults364.Set(copy, defaults, check);
        if (copy.Name != resolved.Name) throw new DomainException("API_EXCEPTION", "Native setter changed the requested style name.");
        DimensionStyleWriteValidation.RequireSame(resolved.Expected, Snapshot());
        VerifySource(resolved, tr, db, check);
        if (resolved.RetainedDefaults is { } retained)
            CadDimensionStyleDefaults364.Verify(retained, resolved.SourceRef, resolved.Changes!, resolved.Expected, false, db, tr, check);
        return copy;
    }
    public static StructureChange Apply(StructureWriteArguments args, int index, Resolved resolved, DimStyleTableRecord prepared,
        Transaction tr, Database db, WriteJob job)
    {
        job.CheckCancellation();
        if (resolved.Editing)
        {
            VerifySource(resolved, tr, db, job.CheckCancellation);
            var existing = (DimStyleTableRecord)tr.GetObject(resolved.Source, OpenMode.ForWrite);
            existing.Dimscale = resolved.Expected.dimension_scale;
            existing.Dimtxt = resolved.Expected.text_height;
            existing.Dimasz = resolved.Expected.arrow_size;
            if (resolved.Changes?.Defaults is { } defaults) CadDimensionStyleDefaults364.Set(existing, defaults, job.CheckCancellation);
            DimensionStyleWriteValidation.RequireSame(resolved.Expected, Read(existing, resolved.SourceRef, tr, db, job.CheckCancellation));
            var dimensions = new List<ObjectId>();
            foreach (ObjectId blockId in (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead))
            {
                job.CheckCancellation(); if (blockId.IsErased || blockId.IsEffectivelyErased) continue;
                var block = (BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead);
                if (block.IsDependent || block.IsFromExternalReference || block.IsFromOverlayReference) continue;
                foreach (ObjectId entityId in block)
                {
                    job.CheckCancellation(); if (entityId.IsErased || entityId.IsEffectivelyErased) continue;
                    if (tr.GetObject(entityId, OpenMode.ForRead) is Dimension dimension && dimension.DimensionStyle == resolved.Source) dimensions.Add(entityId);
                }
            }
            foreach (var dimensionId in dimensions)
            {
                job.CheckCancellation(); ((Dimension)tr.GetObject(dimensionId, OpenMode.ForWrite)).RecomputeDimensionBlock(true);
            }
            if (resolved.RetainedDefaults is { } retained)
                CadDimensionStyleDefaults364.Verify(retained, resolved.SourceRef, resolved.Changes!, resolved.Expected, true, db, tr, job.CheckCancellation);
            return new(index, "set_dimension_style_properties", resolved.SourceRef, "dimension_style", existing.Name, false);
        }
        var table = (DimStyleTable)tr.GetObject(resolved.Table, OpenMode.ForWrite);
        var id = table.Add(prepared); tr.AddNewlyCreatedDBObject(prepared, true);
        if (id.IsNull || prepared.ObjectId != id || prepared.OwnerId != resolved.Table || prepared.Name != resolved.Name)
            throw new DomainException("API_EXCEPTION", "Native dimension style insertion produced an unexpected identity/owner/name.");
        DimensionStyleWriteValidation.RequireSame(resolved.Expected, Read(prepared, resolved.SourceRef, tr, db, job.CheckCancellation));
        VerifySource(resolved, tr, db, job.CheckCancellation);
        if (resolved.RetainedDefaults is { } retainedCreated)
            CadDimensionStyleDefaults364.Verify(retainedCreated, resolved.SourceRef, resolved.Changes!, resolved.Expected, false, db, tr, job.CheckCancellation);
        return new(index, "create_dimension_style", Reference(resolved.SourceRef, id), "dimension_style", prepared.Name, true);
    }
}
