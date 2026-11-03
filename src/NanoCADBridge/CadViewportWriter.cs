using System.Globalization;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CadViewportWriter
{
    internal sealed record Resolved(ObjectId Layout, ObjectId Viewport, ObjectId Owner, EntityRef Context,
        string Name, SetViewportViewEdit Edit, ViewportProperties Before, ViewportDataValue[] NativeData, ObjectId[]? FrozenLayers);
    internal sealed record Prepared(ViewportProperties Expected, ViewportDataValue[] NativeData)
    {
        // Populated only after complete post-disposal verification. Subsequent
        // preservation comparisons bind the observed native number, not a guess.
        internal int? CommittedNumber { get; set; }
    }
    private static EntityRef Reference(EntityRef context, ObjectId id) => new(context.bridge_instance_id, context.document_id, Validation.NormalizeHandle(id.Handle.ToString()));
    public static Resolved Resolve(StructureWriteArguments args, SetViewportViewEdit edit, Database db, Transaction tr, Action check)
    {
        check(); var layoutId = CadStructureWriter.Find(db, edit.LayoutHandle); var id = CadStructureWriter.Find(db, edit.Handle);
        var layoutObject = tr.GetObject(layoutId, OpenMode.ForRead);
        if (layoutObject is not Layout layout || layout.GetType() != typeof(Layout) || layout.OwnerId != db.LayoutDictionaryId || layout.ModelType)
            throw new DomainException("UNSUPPORTED_VIEWPORT", "Expected an ordinary paper layout from this database.");
        var space = tr.GetObject(layout.BlockTableRecordId, OpenMode.ForRead);
        if (space.GetType() != typeof(BlockTableRecord) || space.OwnerId != db.BlockTableId)
            throw new DomainException("UNSUPPORTED_VIEWPORT", "Layout block record has an unexpected owner/type.");
        var target = tr.GetObject(id, OpenMode.ForRead);
        if (target is not Viewport viewport || target.GetType() != typeof(Viewport) || target.OwnerId != space.ObjectId)
            throw new DomainException("VIEWPORT_NOT_IN_LAYOUT", "Viewport is not owned by the explicit paper layout.");
        var layer = (LayerTableRecord)tr.GetObject(viewport.LayerId, OpenMode.ForRead);
        if (layer.GetType() != typeof(LayerTableRecord) || layer.OwnerId != db.LayerTableId || layer.IsDependent || layer.IsLocked || layer.IsOff || layer.IsFrozen)
            throw new DomainException("LAYER_NOT_WRITABLE", "Viewport layer must be local, visible, thawed and unlocked.");
        var context = new EntityRef(args.BridgeId, args.DocumentId, edit.Handle);
        var before = LayoutMetadataReader.ReadViewport(viewport, layout, tr, db, value => Reference(context, value), check, check);
        ViewportWriteValidation.RequireWritable(before, edit); check();
        if (edit.Properties.IsOn == true && (db.TileMode || db.CurrentSpaceId != space.ObjectId))
            throw new DomainException("LAYOUT_NOT_ACTIVE", "Activate the explicit paper layout before enabling this viewport.");
        ObjectId[]? frozen = null;
        if (edit.Properties.FrozenLayerHandles is { } handles)
        {
            frozen = handles.Select(handle =>
            {
                check(); var layerId = CadStructureWriter.Find(db, handle);
                if (tr.GetObject(layerId, OpenMode.ForRead) is not LayerTableRecord freezeLayer || freezeLayer.GetType() != typeof(LayerTableRecord) || freezeLayer.OwnerId != db.LayerTableId || freezeLayer.IsDependent || freezeLayer.Name.Contains('|'))
                    throw new DomainException("LAYER_UNAVAILABLE", "Viewport frozen layers must be existing independent native layers in this document.");
                return layerId;
            }).ToArray();
        }
        RequireNoExtensionContent(viewport, tr, check);
        var nativeData = ReadNativeData(viewport, check);
        return new(layoutId, id, space.ObjectId, context, layout.LayoutName + ":" + viewport.Number.ToString(CultureInfo.InvariantCulture), edit, before, nativeData, frozen);
    }
    internal static ViewportDataValue[] ReadNativeData(Viewport viewport, Action check)
    {
        check();
        using (var data = viewport.XData)
        {
            check();
            if (data is null) return Array.Empty<ViewportDataValue>();
            return ViewportMviewData.Read(data.GetEnumerator(), value =>
            {
                if (value is not TypedValue item) throw new DomainException("UNSUPPORTED_VIEWPORT", "Unexpected native viewport XData element.");
                return new(item.TypeCode, item.Value switch {
                    Point3d point => new[] { point.X, point.Y, point.Z },
                    Vector3d vector => new[] { vector.X, vector.Y, vector.Z },
                    var scalar => scalar });
            }, check);
        }
    }
    internal static void RequireNoExtensionContent(Viewport viewport, Transaction tr, Action check)
    {
        check(); bool fields = viewport.HasFields; var extensionId = viewport.ExtensionDictionary;
        string[] keys = Array.Empty<string>(); bool more = false;
        if (!extensionId.IsNull)
        {
            check(); var obj = tr.GetObject(extensionId, OpenMode.ForRead);
            if (obj is not DBDictionary dictionary || obj.GetType() != typeof(DBDictionary) || obj.OwnerId != viewport.ObjectId)
                throw new DomainException("UNSUPPORTED_VIEWPORT", "Viewport extension dictionary has an unexpected owner/type.");
            var names = new List<string>(); var entries = dictionary.GetEnumerator();
            try
            {
                while (true)
                {
                    check(); bool next = entries.MoveNext(); check(); if (!next) break;
                    if (names.Count == 8) { more = true; break; }
                    string name = entries.Key; names.Add(name.Length <= 128 ? name : "name_exceeds_128_characters");
                }
            }
            finally { (entries as IDisposable)?.Dispose(); }
            keys = names.ToArray();
        }
        check();
        if (fields || keys.Length > 0 || more)
            throw new DomainException("UNSUPPORTED_VIEWPORT", "Viewport contains extension content outside the supported view edit scope.", false,
                new { has_fields = fields, extension_dictionary_keys = keys, more_extension_keys = more });
    }
    private static void Set(Viewport target, Resolved resolved, bool resident, Action check)
    {
        var p = resolved.Edit.Properties;
        var originalHeight=target.ViewHeight;var originalCenter=target.ViewCenter;
        if (p.IsLocked == false) { check(); target.Locked = false; }
        if(p.Center is {} paper){check();target.CenterPoint=new Point3d(paper[0],paper[1],target.CenterPoint.Z);}
        if(p.Width is {} width){check();target.Width=width;}
        if(p.Height is {} paperHeight){check();target.Height=paperHeight;}
        if(p.Width.HasValue||p.Height.HasValue){target.ViewHeight=originalHeight;target.ViewCenter=originalCenter;}
        if (p.ViewDirection is { } direction) { check(); target.ViewDirection = new Vector3d(direction[0], direction[1], direction[2]); }
        if (p.ViewTarget is { } targetPoint) { check(); target.ViewTarget = new Point3d(targetPoint[0], targetPoint[1], targetPoint[2]); }
        if (p.ViewCenter is { } center) { check(); target.ViewCenter = new Point2d(center[0], center[1]); }
        if (p.ViewHeight is { } height) { check(); target.ViewHeight = height; }
        if (p.TwistAngle is { } angle) { check(); target.TwistAngle = angle; }
        if (resolved.FrozenLayers is { } frozen)
        {
            check(); target.ThawAllLayersInViewport();
            if (frozen.Length > 0)
            {
                System.Collections.IEnumerator ids = frozen.GetEnumerator();
                try { check(); target.FreezeLayersInViewport(ids); }
                finally { (ids as IDisposable)?.Dispose(); }
            }
        }
        // On is resident display activation; a detached clone cannot prove it.
        if (resident && p.IsOn is { } on) { check(); target.On = on; }
        if (p.IsLocked == true) { check(); target.Locked = true; }
        check();
    }
    public static Prepared Preview(Resolved resolved, Transaction tr, Database db, List<DBObject> owned, Action check)
    {
        check(); var source = tr.GetObject(resolved.Viewport, OpenMode.ForRead);
        var clone = (DBObject)source.Clone();
        if (!clone.ObjectId.IsNull || !clone.OwnerId.IsNull || clone.UnmanagedObject == IntPtr.Zero ||
            clone.UnmanagedObject == source.UnmanagedObject || owned.Any(item => item.UnmanagedObject == clone.UnmanagedObject))
            throw new DomainException("API_EXCEPTION", "Viewport clone returned a resident/source/owned alias; unsafe ownership was not adopted.");
        owned.Add(clone);
        if (clone is not Viewport viewport || clone.GetType() != typeof(Viewport))
            throw new DomainException("API_EXCEPTION", "Viewport clone is not an exact detached preview.");
        var layout = (Layout)tr.GetObject(resolved.Layout, OpenMode.ForRead);
        // Number and display activation belong to a resident layout. A detached
        // clone can report On=false even when its source is on. Preview overlays
        // the requested resident display state; final activation is verified only
        // after Commit and transaction disposal.
        bool edited = false;
        ViewportProperties Read() => LayoutMetadataReader.ReadViewportDetached(viewport, layout, tr, db, resolved.Context,
            id => Reference(resolved.Context, id), check, check) with { number = resolved.Before.number, is_on = edited ? resolved.Edit.Properties.IsOn ?? resolved.Before.is_on : resolved.Before.is_on };
        ViewportWriteValidation.RequireSame(resolved.Before, Read());
        ViewportMviewData.RequireSame(resolved.NativeData, ReadNativeData(viewport, check),check);
        Set(viewport, resolved, false, check); edited = true;
        var expected = Read(); ViewportWriteValidation.RequireDesired(resolved.Before, expected, resolved.Edit);
        var nativeData = ReadNativeData(viewport, check); ViewportMviewData.RequireDesired(resolved.NativeData, nativeData, resolved.Edit,check);
        return new(expected, nativeData);
    }
    private static bool ActivationChanged(Resolved resolved) =>
        resolved.Edit.Properties.IsOn is { } on && on != resolved.Before.is_on;
    private static void VerifyState(Resolved resolved, Prepared prepared, ViewportProperties actual, bool finalized)
    {
        var expected = prepared.Expected;
        bool activationChanged = ActivationChanged(resolved);
        if (prepared.CommittedNumber is { } number) expected = expected with { number = number };
        else if (activationChanged)
        {
            // Explicit Off/On changes native display numbering. That number is
            // read-only SDK bookkeeping; it is not a preserved source identity.
            if (finalized && (actual.number == 1 || actual.number <= 1 && actual.number != -1 || actual.is_on && actual.number <= 1))
                throw new DomainException("API_EXCEPTION", "Committed viewport activation has an invalid/reserved native number.", false,
                    new { expected_is_on = expected.is_on, actual_is_on = actual.is_on, actual_number = actual.number });
            expected = expected with { number = actual.number };
        }
        // The native activation transition is not final before disposal. All
        // other fields remain verified here; requested On is strict afterward.
        if (activationChanged && !finalized) expected = expected with { is_on = actual.is_on };
        ViewportWriteValidation.RequireSame(expected, actual);
    }
    public static StructureChange Apply(int index, Resolved resolved, Prepared prepared, Transaction tr, Database db, WriteJob job)
    {
        job.CheckCancellation(); var viewport = (Viewport)tr.GetObject(resolved.Viewport, OpenMode.ForWrite);
        if (viewport.GetType() != typeof(Viewport) || viewport.OwnerId != resolved.Owner)
            throw new DomainException("API_EXCEPTION", "Viewport identity or owner changed before apply.");
        Set(viewport, resolved, true, job.CheckCancellation);
        RequireNoExtensionContent(viewport, tr, job.CheckCancellation);
        var layout = (Layout)tr.GetObject(resolved.Layout, OpenMode.ForRead);
        var actual = LayoutMetadataReader.ReadViewport(viewport, layout, tr, db,
            id => Reference(resolved.Context, id), job.CheckCancellation, job.CheckCancellation);
        VerifyState(resolved, prepared, actual, false);
        ViewportMviewData.RequireSame(prepared.NativeData, ReadNativeData(viewport, job.CheckCancellation),job.CheckCancellation);
        return new(index, "set_viewport_view", resolved.Context, "viewport", resolved.Name, false);
    }
    public static void VerifyCommitted(IReadOnlyDictionary<int, Resolved> resolved, IReadOnlyDictionary<int, Prepared> prepared, Database db, Action check)
    {
        using var tr = db.TransactionManager.StartTransaction();
        foreach (var pair in resolved)
        {
            check(); var item = pair.Value;
            if (tr.GetObject(item.Viewport, OpenMode.ForRead) is not Viewport viewport || viewport.GetType() != typeof(Viewport) || viewport.OwnerId != item.Owner)
                throw new DomainException("API_EXCEPTION", "Committed viewport has an unexpected identity or owner.");
            var layout = (Layout)tr.GetObject(item.Layout, OpenMode.ForRead);
            var actual = LayoutMetadataReader.ReadViewport(viewport, layout, tr, db, id => Reference(item.Context, id), check, check);
            VerifyState(item, prepared[pair.Key], actual, true);
            ViewportMviewData.RequireSame(prepared[pair.Key].NativeData, ReadNativeData(viewport, check),check);
            if (ActivationChanged(item)) prepared[pair.Key].CommittedNumber = actual.number;
        }
    }
}
