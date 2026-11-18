using NanoCAD.Contracts;
using NanoCAD.Ipc;
using System.Text.Json;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class CadLayoutWriter
{
    internal sealed record Existing(ObjectId Id, string Name, ObjectId Block, string BlockName, LayoutProperties Properties);
    internal sealed record Resolved(ObjectId Dictionary, ObjectId BlockTable, Existing Source, Existing[] Existing,
        EntityRef Context, string Name, string BlockName, int TabOrder, CadLayoutDecoration.Item[] Decorations,
        JsonElement? SourceDictionary);
    internal sealed record Prepared(Layout Layout, BlockTableRecord Block, LayoutProperties Expected, CadLayoutDecoration.Prepared[] Decorations);
    private static EntityRef Reference(EntityRef context, ObjectId id) =>
        new(context.bridge_instance_id, context.document_id, Validation.NormalizeHandle(id.Handle.ToString()));
    private static LayoutProperties Read(Layout source, EntityRef context, Transaction tr, Database db, Action check) =>
        LayoutMetadataReader.ReadLayout(source, tr, db, id => Reference(context, id), check, check);
    private static JsonElement? CheckBase(Layout source, Transaction tr, Action check, bool selectedDecoration)
    {
        check();
        if (source.HasFields) throw new DomainException("UNSUPPORTED_LAYOUT", "Base layout contains fields.");
        using (var data = source.XData)
        { if (data is not null) throw new DomainException("UNSUPPORTED_LAYOUT", "Base layout has extension application data."); }
        var extension = source.ExtensionDictionary;
        if (!extension.IsNull)
        {
            check(); var dictionary = tr.GetObject(extension, OpenMode.ForRead);
            if (dictionary.GetType() != typeof(DBDictionary) || !selectedDecoration && ((DBDictionary)dictionary).Count != 0)
                throw new DomainException("UNSUPPORTED_LAYOUT", "Base layout has nonempty extension dictionary data.");
            if (selectedDecoration)
            {
                var entries = new List<object>();
                foreach (DBDictionaryEntry entry in (DBDictionary)dictionary)
                {
                    check(); var item = tr.GetObject(entry.Value, OpenMode.ForRead);
                    object? values = null;
                    if (item is Xrecord record)
                    {
                        using var data = record.Data;
                        if (data != null) values = NativeOwnedMetadata.Buffer(data, check, new ModelInventoryBudget(check).Add);
                    }
                    entries.Add(new
                    {
                        entry.Key,
                        id = item.ObjectId.Handle.ToString(),
                        owner = item.OwnerId.IsNull ? null : item.OwnerId.Handle.ToString(),
                        type = item.GetType().FullName,
                        values
                    });
                }
                return Wire.Element(new { handle = extension.Handle.ToString(), entries });
            }
        }
        return null;
    }
    public static Resolved Resolve(StructureWriteArguments args, CreateLayoutEdit operation, int index, Database db, Transaction tr, Action check)
    {
        LayoutCreateValidation.Validate(operation); check();
        var dictionary = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
        var blocks = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        var context = new EntityRef(args.BridgeId, args.DocumentId, operation.BaseLayoutHandle);
        var existing = new List<Existing>();
        foreach (DBDictionaryEntry entry in dictionary)
        {
            check();
            if (tr.GetObject(entry.Value, OpenMode.ForRead) is not Layout layout || layout.GetType() != typeof(Layout) ||
                layout.OwnerId != dictionary.ObjectId || layout.LayoutName != entry.Key)
                throw new DomainException("UNSUPPORTED_LAYOUT", "Layout dictionary has unsupported objects or inconsistent names.");
            if (string.Equals(layout.LayoutName, operation.Name, StringComparison.OrdinalIgnoreCase))
                throw new DomainException("LAYOUT_EXISTS", "Layout name already exists; creation never overwrites it.");
            var properties = Read(layout, context, tr, db, check);
            var block = (BlockTableRecord)tr.GetObject(layout.BlockTableRecordId, OpenMode.ForRead);
            if (!block.IsLayout || block.LayoutId != layout.ObjectId || block.IsDependent || block.IsFromExternalReference || block.IsFromOverlayReference)
                throw new DomainException("UNSUPPORTED_LAYOUT", "Layout block ownership is inconsistent or dependent.");
            existing.Add(new(layout.ObjectId, LayoutMetadata.Text(layout.LayoutName), block.ObjectId, LayoutMetadata.Text(block.Name), properties));
        }
        var source = existing.SingleOrDefault(item => Reference(context, item.Id).handle == operation.BaseLayoutHandle);
        if (source is null || source.Properties.is_model) throw new DomainException("LAYOUT_UNAVAILABLE", "Base handle must refer to an existing native paper layout.");
        var sourceDictionary = CheckBase((Layout)tr.GetObject(source.Id, OpenMode.ForRead), tr, check, operation.DecorationHandles is { Length: > 0 });
        int order = LayoutCreateValidation.NextTabOrder(existing.Select(item => item.Properties.tab_order), index, args.Operations.Length);
        // Reserve the index-th free internal paper BTR name without renaming any existing record.
        string? blockName = null; int free = 0;
        for (int suffix = 0; suffix < 4096; suffix++)
        {
            check(); string name = "*Paper_Space" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!blocks.Has(name) && free++ == index) { blockName = name; break; }
        }
        if (blockName is null) throw new DomainException("UNSUPPORTED_LAYOUT", "No unused paper block name within the bounded search.");
        var decorations = CadLayoutDecoration.Resolve(operation.DecorationHandles, source.Block, db, tr, check);
        return new(dictionary.ObjectId, blocks.ObjectId, source, existing.ToArray(), context, operation.Name, blockName, order, decorations, sourceDictionary);
    }
    private static void VerifyExisting(Resolved resolved, Transaction tr, Database db, Action check)
    {
        foreach (var item in resolved.Existing)
        {
            check(); var layout = (Layout)tr.GetObject(item.Id, OpenMode.ForRead);
            var block = (BlockTableRecord)tr.GetObject(item.Block, OpenMode.ForRead);
            if (layout.OwnerId != resolved.Dictionary || layout.LayoutName != item.Name || layout.BlockTableRecordId != item.Block ||
                block.OwnerId != resolved.BlockTable || block.LayoutId != item.Id || block.Name != item.BlockName)
                throw new DomainException("API_EXCEPTION", "Existing layout identity, name or paper block changed.");
            LayoutCreateValidation.RequireSame(item.Properties, Read(layout, resolved.Context, tr, db, check));
        }
    }
    public static Prepared Preview(Resolved resolved, Transaction tr, Database db, List<DBObject> owned, Action check)
    {
        VerifyExisting(resolved, tr, db, check);
        // CopyFrom copies PlotSettings; a new layout has its own name, limits
        // and paper-space bookkeeping. Do not treat it as an entity/layout clone.
        var layout = new Layout(); owned.Add(layout);
        check(); layout.CopyFrom(tr.GetObject(resolved.Source.Id, OpenMode.ForRead));
        if (!layout.ObjectId.IsNull) throw new DomainException("API_EXCEPTION", "Copied layout is not detached; no database write was opened.");
        if (resolved.SourceDictionary != null)
        {
            using var copiedData = layout.XData;
            if (layout.HasFields || !layout.ExtensionDictionary.IsNull || copiedData != null)
                throw new DomainException("UNSUPPORTED_LAYOUT", "Detached plot settings copied layout-owned metadata from the base sheet.");
        }
        check(); layout.LayoutName = resolved.Name;
        check(); layout.TabOrder = resolved.TabOrder;
        check(); layout.TabSelected = false;
        var expected = LayoutCreateValidation.Expected(resolved.Source.Properties, resolved.TabOrder, resolved.Source.Properties.block_record);
        if (layout.LayoutName != resolved.Name) throw new DomainException("API_EXCEPTION", "Native layout name differs from the requested name.");
        LayoutCreateValidation.RequireNew(expected, LayoutMetadataReader.ReadLayoutDetached(layout, resolved.Context, expected.block_record, check));
        var block = new BlockTableRecord(); owned.Add(block); check(); block.Name = resolved.BlockName;
        if (!block.ObjectId.IsNull || block.Name != resolved.BlockName) throw new DomainException("API_EXCEPTION", "Paper block preview is not detached or changed its name.");
        VerifyExisting(resolved, tr, db, check);
        return new(layout, block, expected, CadLayoutDecoration.Prepare(resolved.Decorations, tr, owned, check));
    }
    public static StructureChange Apply(int index, Resolved resolved, Prepared prepared, Transaction tr, Database db, WriteJob job)
    {
        job.CheckCancellation();
        var table = (BlockTable)tr.GetObject(resolved.BlockTable, OpenMode.ForWrite);
        var blockId = table.Add(prepared.Block); tr.AddNewlyCreatedDBObject(prepared.Block, true);
        job.CheckCancellation();
        tr.GetObject(resolved.Dictionary, OpenMode.ForWrite);
        prepared.Layout.AddToLayoutDictionary(db, blockId); tr.AddNewlyCreatedDBObject(prepared.Layout, true);
        var layoutId = prepared.Layout.ObjectId;
        if (layoutId.IsNull) throw new DomainException("API_EXCEPTION", "Native layout insertion did not assign an identity.");
        // AddToLayoutDictionary associates Layout -> paper BTR. Complete the
        // reciprocal link only on our newly added BTR; never replace a foreign ID.
        if (prepared.Block.LayoutId.IsNull)
        { job.CheckCancellation(); prepared.Block.LayoutId = layoutId; }
        // The symbol table can replace the temporary internal BTR name while
        // adding it. Identity and both layout links, not that temporary spelling,
        // define ownership; every pre-existing record name remains protected.
        string nativeBlockName = LayoutMetadata.Text(prepared.Block.Name);
        if (nativeBlockName.Length is < 1 or > 255 || !table.Has(nativeBlockName) || table[nativeBlockName] != blockId)
            throw new DomainException("API_EXCEPTION", "New paper block is not indexed under its native name.");
        if (layoutId.IsNull || prepared.Layout.OwnerId != resolved.Dictionary || prepared.Layout.LayoutName != resolved.Name ||
            prepared.Layout.BlockTableRecordId != blockId || prepared.Block.ObjectId != blockId || prepared.Block.OwnerId != resolved.BlockTable ||
            !prepared.Block.IsLayout || prepared.Block.LayoutId != layoutId)
            throw new DomainException("API_EXCEPTION", "Native layout insertion has unexpected identity, name or ownership.", false,
                new
                {
                    layout_owner_matches = prepared.Layout.OwnerId == resolved.Dictionary,
                    layout_name_matches = prepared.Layout.LayoutName == resolved.Name,
                    layout_block_matches = prepared.Layout.BlockTableRecordId == blockId,
                    block_identity_matches = prepared.Block.ObjectId == blockId,
                    block_owner_matches = prepared.Block.OwnerId == resolved.BlockTable,
                    block_is_layout = prepared.Block.IsLayout,
                    reverse_layout_matches = prepared.Block.LayoutId == layoutId,
                    block_name_matches = prepared.Block.Name == resolved.BlockName
                });
        foreach (ObjectId unused in prepared.Block)
        { job.CheckCancellation(); throw new DomainException("API_EXCEPTION", "New layout contains unexpected entities; batch cannot commit."); }
        var expected = prepared.Expected with { block_record = Reference(resolved.Context, blockId) };
        LayoutCreateValidation.RequireNew(expected, Read(prepared.Layout, resolved.Context, tr, db, job.CheckCancellation));
        VerifyExisting(resolved, tr, db, job.CheckCancellation);
        var copies = CadLayoutDecoration.Copy(resolved.Decorations, prepared.Decorations, resolved.Source.Block, prepared.Block, resolved.Context, tr, job.CheckCancellation);
        if (resolved.SourceDictionary is { } before)
        {
            var current = CheckBase((Layout)tr.GetObject(resolved.Source.Id, OpenMode.ForRead), tr, job.CheckCancellation, true);
            if (current == null || !NativeMetadataEquality.Same(before, current.Value))
                throw new DomainException("UNSUPPORTED_LAYOUT", "Creating the new sheet changed the source layout's dictionary membership or stored records.");
            CheckBase(prepared.Layout, tr, job.CheckCancellation, false);
        }
        return new(index, "create_layout", Reference(resolved.Context, layoutId), "layout", resolved.Name, true, decoration_copies: copies);
    }
}
