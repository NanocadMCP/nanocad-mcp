using NanoCAD.Contracts;
using System.Text.Json;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// Existing UI-thread ForRead transaction only; plot configuration strings are never opened.
internal static class LayoutMetadataReader
{
    private static double[] Point(Point2d p) => new[] { p.X, p.Y };
    private static double[] Point(Point3d p) => new[] { p.X, p.Y, p.Z };
    public static LayoutProperties ReadLayout(Layout source, Transaction tr, Database db, Func<ObjectId, EntityRef> reference, Action check, Action visit)
    {
        T Get<T>(Func<T> getter) { check(); var value = getter(); check(); return value; }
        if (source.GetType() != typeof(Layout) || Get(() => source.OwnerId) != Get(() => db.LayoutDictionaryId))
            throw new DomainException("OBJECT_READ_FAILED", "Expected a native layout in the active layout dictionary.");
        var blockId = Get(() => source.BlockTableRecordId); visit();
        var block = Get(() => tr.GetObject(blockId, OpenMode.ForRead));
        if (block.GetType() != typeof(BlockTableRecord) || Get(() => block.OwnerId) != Get(() => db.BlockTableId))
            throw new DomainException("OBJECT_READ_FAILED", "Invalid layout block record.");
        return ReadLayoutCore(source, reference(source.ObjectId), reference(blockId), check);
    }
    public static LayoutProperties ReadLayoutDetached(Layout source, EntityRef context, EntityRef block, Action check)
    {
        check();
        if (source.GetType() != typeof(Layout) || !source.ObjectId.IsNull)
            throw new DomainException("OBJECT_READ_FAILED", "Expected an exact detached layout preview.");
        return ReadLayoutCore(source, context, block, check);
    }
    private static LayoutProperties ReadLayoutCore(Layout source, EntityRef context, EntityRef block, Action check)
    {
        T Get<T>(Func<T> getter) { check(); var value = getter(); check(); return value; }
        var limits = Get(() => source.Limits); var margins = Get(() => source.PlotPaperMargins);
        var window = Get(() => source.PlotWindowArea); var scale = Get(() => source.CustomPrintScale);
        string Text(Func<string> getter) => LayoutMetadata.Text(Get(getter));
        var plot = new PlotMetadata(Text(() => source.PlotConfigurationName), Text(() => source.CanonicalMediaName),
            Text(() => source.CurrentStyleSheet), Text(() => source.PlotSettingsName), Text(() => source.PlotViewName),
            Get(() => (int)source.PlotPaperUnits), Get(() => (int)source.PlotRotation), Get(() => (int)source.PlotType),
            Point(Get(() => source.PlotPaperSize)), Point(Get(() => source.PlotOrigin)), Point(margins.MinPoint), Point(margins.MaxPoint),
            Point(window.MinPoint), Point(window.MaxPoint), Get(() => source.UseStandardScale), Get(() => (int)source.StdScaleType),
            Get(() => source.StdScale), scale.Numerator, scale.Denominator, Get(() => source.PlotCentered),
            Get(() => source.PrintLineweights), Get(() => source.ScaleLineweights), Get(() => source.PlotPlotStyles));
        var result = LayoutMetadata.Validate(context, new LayoutProperties(Get(() => source.TabOrder), Get(() => source.ModelType),
            block, Get(() => source.TabSelected), Point(limits.MinPoint), Point(limits.MaxPoint), plot));
        check(); return result;
    }
    public static ViewportProperties ReadViewport(Viewport source, Layout layout, Transaction tr, Database db,
        Func<ObjectId, EntityRef> reference, Action check, Action visit)
    {
        T Get<T>(Func<T> getter) { check(); var value = getter(); check(); return value; }
        if (source.GetType() != typeof(Viewport) || Get(() => source.OwnerId) != Get(() => layout.BlockTableRecordId))
            throw new DomainException("OBJECT_READ_FAILED", "Expected a native viewport owned by the enclosing layout.");
        return ReadViewportCore(source, layout, tr, db, reference(source.ObjectId), reference, check, visit);
    }
    public static ViewportProperties ReadViewportDetached(Viewport source, Layout layout, Transaction tr, Database db,
        EntityRef context, Func<ObjectId, EntityRef> reference, Action check, Action visit)
    {
        check();
        if (source.GetType() != typeof(Viewport) || !source.ObjectId.IsNull)
            throw new DomainException("OBJECT_READ_FAILED", "Expected an exact detached viewport preview.");
        return ReadViewportCore(source, layout, tr, db, context, reference, check, visit);
    }
    private static ViewportProperties ReadViewportCore(Viewport source, Layout layout, Transaction tr, Database db,
        EntityRef context, Func<ObjectId, EntityRef> reference, Action check, Action visit)
    {
        T Get<T>(Func<T> getter) { check(); var value = getter(); check(); return value; }
        StructureDependency Dependency(ObjectId id, string kind, ObjectId expectedOwner)
        {
            StructureDependency Unavailable(string reason) => new(kind, "unavailable", null, null, reason);
            if (Get(() => id.IsNull)) return new(kind, "null", null, null, null);
            if (!Get(() => id.IsValid)) return Unavailable("invalid_id");
            if (Get(() => id.IsErased) || Get(() => id.IsEffectivelyErased)) return Unavailable("erased");
            try
            {
                visit(); var target = Get(() => tr.GetObject(id, OpenMode.ForRead));
                if (kind == "layer" ? target.GetType() != typeof(LayerTableRecord) : target is not Entity || target is ProxyEntity)
                    return Unavailable("wrong_type");
                if (Get(() => target.OwnerId) != expectedOwner) return Unavailable("wrong_owner");
                var label = kind == "layer" ? Get(() => ((LayerTableRecord)target).Name) : target.GetType().Name;
                return new(kind, "resolved", Get(() => reference(id)), LayoutMetadata.Text(label), null);
            }
            catch (Teigha.Runtime.Exception) { check(); return Unavailable("read_failed"); }
        }
        var boundary = Dependency(Get(() => source.NonRectClipEntityId), "entity", Get(() => layout.BlockTableRecordId));
        StructureDependency[] layers;
        using (var ids = Get(() => source.GetFrozenLayers()))
        {
            int count = LayoutMetadata.FrozenCount(Get(() => ids.Count)); var values=new List<StructureDependency>();int bytes=2;
            var owner = Get(() => db.LayerTableId);
            for(int i=0;i<count;i++)
            {
                visit();var layer=Dependency(Get(()=>ids[i]),"layer",owner);check();
                bytes+=JsonSerializer.SerializeToUtf8Bytes(layer,Wire.Json).Length+1;
                if(bytes>Wire.PayloadBudget-8192)throw new DomainException("RESPONSE_LIMIT_EXCEEDED","Viewport frozen-layer metadata exceeds the actual serialized payload budget; no partial list.");
                values.Add(layer);
            }
            layers=values.ToArray();
        }
        var direction = Get(() => source.ViewDirection);
        var result = LayoutMetadata.Validate(context, new ViewportProperties(reference(layout.ObjectId), Get(() => source.Number),
            Point(Get(() => source.CenterPoint)), Get(() => source.Width), Get(() => source.Height), Point(Get(() => source.ViewCenter)),
            Point(Get(() => source.ViewTarget)), new[] { direction.X, direction.Y, direction.Z }, Get(() => source.ViewHeight), Get(() => source.TwistAngle),
            Get(() => source.On), Get(() => source.Locked), Get(() => source.PerspectiveOn), Get(() => source.CustomScale), Get(() => (int)source.StandardScale),
            Get(() => source.LensLength), Get(() => source.FrontClipOn), Get(() => source.BackClipOn), Get(() => source.FrontClipAtEyeOn),
            Get(() => source.FrontClipDistance), Get(() => source.BackClipDistance), Get(() => source.NonRectClipOn), boundary, layers),check);
        check(); return result;
    }
}
