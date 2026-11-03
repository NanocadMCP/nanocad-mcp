using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class CadLayoutEditor
{
    internal sealed record Existing(ObjectId Id, string Name, LayoutProperties Properties);
    internal sealed record Resolved(ObjectId Target, ObjectId Dictionary, ObjectId ActiveSpace, bool TileMode,
        EntityRef Context, StructureEdit Edit, Existing[] Existing, Dictionary<ObjectId, LayoutProperties> Expected, string Name);
    private static EntityRef Ref(EntityRef context, ObjectId id) => new(context.bridge_instance_id, context.document_id, Validation.NormalizeHandle(id.Handle.ToString()));
    private static LayoutProperties Read(Layout layout, Resolved resolved, Transaction tr, Database db, Action check) =>
        LayoutMetadataReader.ReadLayout(layout, tr, db, id => Ref(resolved.Context, id), check, check);
    public static Resolved Resolve(StructureWriteArguments args, StructureEdit edit, Database db, Transaction tr, Action check)
    {
        string handle = edit is RenameLayoutEdit rename ? rename.Handle : ((SetLayoutPropertiesEdit)edit).Handle;
        string expectedName = edit is RenameLayoutEdit renamed ? renamed.ExpectedName : ((SetLayoutPropertiesEdit)edit).ExpectedName;
        var target = CadStructureWriter.Find(db, handle);
        var context = new EntityRef(args.BridgeId, args.DocumentId, handle);
        var dictionary = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
        var existing = new List<Existing>();
        foreach (DBDictionaryEntry entry in dictionary)
        {
            check(); if (entry.Value.IsErased || entry.Value.IsEffectivelyErased) continue;
            if (tr.GetObject(entry.Value, OpenMode.ForRead) is not Layout layout || layout.GetType() != typeof(Layout) || layout.OwnerId != dictionary.ObjectId || layout.LayoutName != entry.Key)
                throw new DomainException("UNSUPPORTED_LAYOUT", "Layout dictionary has inconsistent native ownership or names.");
            var block = tr.GetObject(layout.BlockTableRecordId, OpenMode.ForRead);
            if (block is not BlockTableRecord space || space.GetType() != typeof(BlockTableRecord) || space.OwnerId != db.BlockTableId || !space.IsLayout || space.LayoutId != layout.ObjectId || space.IsDependent)
                throw new DomainException("UNSUPPORTED_LAYOUT", "Layout paper/model block ownership is inconsistent.");
            existing.Add(new(layout.ObjectId, layout.LayoutName, LayoutMetadataReader.ReadLayout(layout, tr, db, id => Ref(context, id), check, check)));
        }
        var source = existing.SingleOrDefault(e => e.Id == target);
        if (source is null || source.Properties.is_model) throw new DomainException("OUT_OF_SCOPE", "Expected an existing native paper layout; Model is protected.");
        if (source.Name != expectedName) throw new DomainException("LAYOUT_NAME_MISMATCH", "Layout differs from expected_name; refresh get_structure layouts.");
        var desired = existing.ToDictionary(e => e.Id, e => e.Properties);
        string name = source.Name;
        if (edit is RenameLayoutEdit renameEdit)
        {
            if (existing.Any(e => string.Equals(e.Name, renameEdit.Name, StringComparison.OrdinalIgnoreCase))) throw new DomainException("LAYOUT_EXISTS", "Destination layout name already exists.");
            name = renameEdit.Name;
        }
        else
        {
            var p = ((SetLayoutPropertiesEdit)edit).Properties;
            int order = p.TabOrder;
            {
                var paper = existing.Where(e => !e.Properties.is_model).OrderBy(e => e.Properties.tab_order).ToList();
                if (paper.Any(e => e.Properties.tab_order < 1) || paper.Select(e => e.Properties.tab_order).Distinct().Count() != paper.Count)
                    throw new DomainException("UNSUPPORTED_LAYOUT", "Existing paper tab order is invalid or ambiguous; no automatic repair.");
                if (order > paper.Count) throw new DomainException("INVALID_ARGUMENT", "tab_order exceeds the paper layout count.");
                paper.Remove(source); paper.Insert(order - 1, source);
                for (int i = 0; i < paper.Count; i++) desired[paper[i].Id] = desired[paper[i].Id] with { tab_order = i + 1 };
            }
        }
        return new(target, dictionary.ObjectId, db.CurrentSpaceId, db.TileMode, context, edit, existing.ToArray(), desired, name);
    }
    public static void Preview(Resolved resolved, Transaction tr, List<DBObject> owned, Action check)
    {
        // Layout name/order belongs to the resident dictionary and sheet tabs.
        // A detached clone cannot establish that bookkeeping. Resolve validates
        // the requested destination/order; this phase rechecks actual source
        // state, and Apply verifies the resident dictionary/name/order afterward.
        foreach (var existing in resolved.Existing)
        {
            check();
            if (tr.GetObject(existing.Id, OpenMode.ForRead) is not Layout layout || layout.GetType() != typeof(Layout) ||
                layout.OwnerId != resolved.Dictionary || layout.LayoutName != existing.Name || layout.Database is not {} database)
                throw new DomainException("API_EXCEPTION", "Layout identity or name changed before resident editing.");
            LayoutCreateValidation.RequireSame(existing.Properties, Read(layout, resolved, tr, database, check));
            if (database.CurrentSpaceId != resolved.ActiveSpace || database.TileMode != resolved.TileMode)
                throw new DomainException("API_EXCEPTION", "Layout editing context changed before resident editing.");
        }
    }
    public static StructureChange Apply(int index, Resolved resolved, Transaction tr, Database db, WriteJob job)
    {
        foreach (var existing in resolved.Existing)
        {
            job.CheckCancellation(); var layout = (Layout)tr.GetObject(existing.Id, OpenMode.ForRead);
            if (layout.OwnerId != resolved.Dictionary || layout.LayoutName != existing.Name) throw new DomainException("API_EXCEPTION", "Layout identity or name changed before apply.");
            LayoutCreateValidation.RequireSame(existing.Properties, Read(layout, resolved, tr, db, job.CheckCancellation));
        }
        if (resolved.Edit is RenameLayoutEdit)
        {
            // The native LayoutName setter owns the resident layout rename.
            // Pre-renaming its dictionary entry makes that destination already
            // exist when the setter performs its own native bookkeeping.
            ((Layout)tr.GetObject(resolved.Target, OpenMode.ForWrite)).LayoutName = resolved.Name;
        }
        else
        {
            // Apply final order in its native property; each affected layout is opened once.
            foreach (var existing in resolved.Existing)
            {
                var expected = resolved.Expected[existing.Id];
                if (existing.Id != resolved.Target && expected.tab_order == existing.Properties.tab_order) continue;
                job.CheckCancellation(); var layout = (Layout)tr.GetObject(existing.Id, OpenMode.ForWrite);
                if (expected.tab_order != existing.Properties.tab_order) layout.TabOrder = expected.tab_order;
            }
        }
        var afterDictionary = (DBDictionary)tr.GetObject(resolved.Dictionary, OpenMode.ForRead);
        foreach (var existing in resolved.Existing)
        {
            job.CheckCancellation(); var layout = (Layout)tr.GetObject(existing.Id, OpenMode.ForRead);
            string expectedName = existing.Id == resolved.Target ? resolved.Name : existing.Name;
            if (layout.OwnerId != resolved.Dictionary || layout.LayoutName != expectedName || !afterDictionary.Contains(expectedName) || afterDictionary.GetAt(expectedName) != existing.Id)
                throw new DomainException("API_EXCEPTION", "Layout name or dictionary identity differs after edit.");
            LayoutCreateValidation.RequireSame(resolved.Expected[existing.Id], Read(layout, resolved, tr, db, job.CheckCancellation));
        }
        if (db.CurrentSpaceId != resolved.ActiveSpace || db.TileMode != resolved.TileMode) throw new DomainException("API_EXCEPTION", "Layout edit changed the active Model/Paper space.");
        return new(index, resolved.Edit.Operation, resolved.Context, "layout", resolved.Name, false);
    }
}
