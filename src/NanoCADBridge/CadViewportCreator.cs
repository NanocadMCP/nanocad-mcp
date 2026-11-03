using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CadViewportCreator
{
    internal sealed record ExistingViewport(ObjectId Id, ViewportProperties Properties);
    internal sealed record Resolved(ObjectId Layout, ObjectId Block, ObjectId Layer, EntityRef Context, string Name,
        CreateViewportEdit Edit, LayoutProperties LayoutBefore, ObjectId[] Entities, ExistingViewport[] Viewports);
    internal sealed record Prepared(Viewport Viewport, ViewportProperties Expected);
    private static EntityRef Reference(EntityRef context, ObjectId id) => new(context.bridge_instance_id, context.document_id, Validation.NormalizeHandle(id.Handle.ToString()));
    private static LayoutProperties Read(Layout layout, EntityRef context, Transaction tr, Database db, Action check) =>
        LayoutMetadataReader.ReadLayout(layout, tr, db, id => Reference(context, id), check, check);
    private static ObjectId[] EntityIds(BlockTableRecord block, Action check)
    {
        var result = new List<ObjectId>();
        foreach (ObjectId id in block)
        { check(); if (!id.IsErased && !id.IsEffectivelyErased) result.Add(id); }
        return result.ToArray();
    }
    public static Resolved Resolve(StructureWriteArguments args, CreateViewportEdit edit, Database db, Transaction tr, Action check)
    {
        ViewportCreateValidation.Validate(edit); check();
        var layoutId = CadStructureWriter.Find(db, edit.LayoutHandle);
        if (tr.GetObject(layoutId, OpenMode.ForRead) is not Layout layout || layout.GetType() != typeof(Layout) || layout.OwnerId != db.LayoutDictionaryId || layout.ModelType)
            throw new DomainException("UNSUPPORTED_LAYOUT", "Expected a native paper layout from this database.");
        var block = tr.GetObject(layout.BlockTableRecordId, OpenMode.ForRead) as BlockTableRecord;
        if (block is null || block.GetType() != typeof(BlockTableRecord) || block.OwnerId != db.BlockTableId || !block.IsLayout || block.LayoutId != layoutId ||
            block.IsDependent || block.IsFromExternalReference || block.IsFromOverlayReference)
            throw new DomainException("UNSUPPORTED_LAYOUT", "Invalid paper-space ownership.");
        var layerId = CadStructureWriter.Find(db, edit.LayerHandle);
        if (tr.GetObject(layerId, OpenMode.ForRead) is not LayerTableRecord layer || layer.GetType() != typeof(LayerTableRecord) || layer.OwnerId != db.LayerTableId ||
            layer.IsDependent || layer.IsOff || layer.IsFrozen || layer.IsLocked)
            throw new DomainException("LAYER_NOT_WRITABLE", "Explicit viewport layer must be local, visible, thawed and unlocked.");
        var context = new EntityRef(args.BridgeId, args.DocumentId, edit.LayoutHandle);
        var before = Read(layout, context, tr, db, check); var ids = EntityIds(block, check); var views = new List<ExistingViewport>();
        foreach (var id in ids)
        {
            check(); var item = tr.GetObject(id, OpenMode.ForRead);
            if (item is Viewport view)
            {
                views.Add(new(id, LayoutMetadataReader.ReadViewport(view, layout, tr, db, value => Reference(context, value), check, check)));
            }
        }
        int additions = args.Operations.OfType<CreateViewportEdit>().Count(op => op.LayoutHandle == edit.LayoutHandle);
        ViewportCreateValidation.RequireLayout(before, views.Select(v => v.Properties.number), additions);
        if (edit.IsOn && (db.TileMode || db.CurrentSpaceId != block.ObjectId))
            throw new DomainException("LAYOUT_NOT_ACTIVE", "Activate the requested paper layout before creating an enabled viewport.");
        return new(layoutId, block.ObjectId, layerId, context, LayoutMetadata.Text(layout.LayoutName), edit, before, ids, views.ToArray());
    }
    public static Prepared Preview(Resolved resolved, Transaction tr, Database db, List<DBObject> owned, Action check)
    {
        check(); var viewport = new Viewport(); owned.Add(viewport); var edit = resolved.Edit;
        Set(viewport, resolved, check);
        var layout = (Layout)tr.GetObject(resolved.Layout, OpenMode.ForRead);
        var expected = LayoutMetadataReader.ReadViewportDetached(viewport, layout, tr, db, resolved.Context,
            id => Reference(resolved.Context, id), check, check);
        ViewportCreateValidation.RequireDesired(edit, expected, false);
        return new(viewport, expected);
    }
    private static void Set(Viewport viewport, Resolved resolved, Action check)
    {
        var edit = resolved.Edit;
        check(); viewport.Locked = false;
        check(); viewport.LayerId = resolved.Layer;
        check(); viewport.CenterPoint = new Point3d(edit.Center[0], edit.Center[1], 0);
        check(); viewport.Width = edit.Width;
        check(); viewport.Height = edit.Height;
        check(); viewport.ViewDirection = Vector3d.ZAxis;
        check(); viewport.ViewTarget = Point3d.Origin;
        check(); viewport.ViewCenter = new Point2d(edit.ViewCenter[0], edit.ViewCenter[1]);
        check(); viewport.ViewHeight = edit.ViewHeight;
        check(); viewport.TwistAngle = edit.TwistAngle;
        check(); viewport.Locked = edit.IsLocked;
    }
    public static StructureChange Apply(int index, Resolved resolved, Prepared prepared, Transaction tr, Database db, WriteJob job, Action<string>? trace = null)
    {
        void Stage(string value) => trace?.Invoke($"viewport_{index}_{value}");
        Stage("open_begin");
        job.CheckCancellation(); var block = (BlockTableRecord)tr.GetObject(resolved.Block, OpenMode.ForWrite);
        Stage("append_begin");
        var viewport = prepared.Viewport; var id = block.AppendEntity(viewport);
        Stage("register_begin"); tr.AddNewlyCreatedDBObject(viewport, true);
        // AppendEntity resets Viewport settings. Reapply the exact setters already
        // checked on the detached preview, now to our newly resident object only.
        Stage("set_begin"); Set(viewport, resolved, job.CheckCancellation);
        // On requires a resident viewport; do not activate a layout or call its unsupported Initialize method.
        Stage("on_begin"); job.CheckCancellation(); viewport.On = resolved.Edit.IsOn;
        Stage("identity_begin");
        if (id.IsNull || viewport.OwnerId != resolved.Block || viewport.LayerId != resolved.Layer)
            throw new DomainException("API_EXCEPTION", "New viewport has unexpected owner or layer.");
        // On/Number are not final until Commit and transaction disposal (097).
        // An early false mismatch used to trigger the known broken Abort path.
        Stage("verification_deferred_until_commit_disposal");
        return new(index, "create_viewport", Reference(resolved.Context, id), "viewport", resolved.Name + ":" + id.Handle.ToString(), true);
    }
    public static void VerifyCommitted(IReadOnlyDictionary<int,Resolved> resolved,IReadOnlyList<StructureChange> changes,Database db,Action check)
    {
        using var tr=db.TransactionManager.StartTransaction();
        foreach(var pair in resolved)
        {
            check();var item=pair.Value;var change=changes.Single(c=>c.operation_index==pair.Key);
            var view=(Viewport)tr.GetObject(CadStructureWriter.Find(db,change.@ref.handle),OpenMode.ForRead);
            var layout=(Layout)tr.GetObject(item.Layout,OpenMode.ForRead);
            if(view.OwnerId!=item.Block||view.LayerId!=item.Layer)throw new DomainException("VIEWPORT_VERIFICATION_FAILED","Committed viewport owner/layer differs from request.");
            var actual=LayoutMetadataReader.ReadViewport(view,layout,tr,db,id=>Reference(item.Context,id),check,check);
            ViewportCreateValidation.RequireDesired(item.Edit,actual,true);
        }
        VerifyExisting(resolved,changes,tr,db,check);
    }
    public static void VerifyExisting(IReadOnlyDictionary<int, Resolved> resolved, IReadOnlyList<StructureChange> changes, Transaction tr, Database db, Action check)
    {
        foreach (var group in resolved.GroupBy(pair => pair.Value.Layout))
        {
            var source = group.First().Value; check(); var layout = (Layout)tr.GetObject(source.Layout, OpenMode.ForRead);
            var block = (BlockTableRecord)tr.GetObject(source.Block, OpenMode.ForRead);
            if (layout.LayoutName != source.Name || block.LayoutId != source.Layout) throw new DomainException("API_EXCEPTION", "Existing layout identity changed.");
            LayoutCreateValidation.RequireSame(source.LayoutBefore, Read(layout, source.Context, tr, db, check));
            var expectedIds = source.Entities.Concat(group.Select(pair => CadStructureWriter.Find(db, changes.Single(c => c.operation_index == pair.Key).@ref.handle))).ToHashSet();
            if (!expectedIds.SetEquals(EntityIds(block, check))) throw new DomainException("API_EXCEPTION", "Unexpected paper-space entities after viewport creation.");
            foreach (var item in source.Viewports)
            {
                check(); var viewport = (Viewport)tr.GetObject(item.Id, OpenMode.ForRead);
                ViewportWriteValidation.RequireSame(item.Properties, LayoutMetadataReader.ReadViewport(viewport, layout, tr, db,
                    id => Reference(source.Context, id), check, check));
            }
        }
    }
}
