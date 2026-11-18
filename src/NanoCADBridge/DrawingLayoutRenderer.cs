using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using NanoCAD.Contracts;
using HostMgd.ApplicationServices;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.GraphicsInterface;
using Teigha.Runtime;
using Application = HostMgd.ApplicationServices.Application;
using Gs = Teigha.GraphicsSystem;

namespace NanoCADBridge;

// Create layout helpers exclusively on an owned, detached database. The
// native helper attaches graphics pointers to that database's viewport records.
internal static class DrawingLayoutRenderer
{
    static void Require(bool value, string detail)
    {
        if (!value) throw new InvalidOperationException(detail);
    }

    sealed record StyleInfo(string Handle, string Clr, string Native, string Dxf, string Owner, string? DictionaryKey,
        string? Name, string? StyleType, bool Resident)
    {
        internal bool InDictionary => DictionaryKey != null;
    }

    static StyleInfo? InspectStyle(Database db, Transaction tx, ObjectId id)
    {
        if (id.IsNull || !id.IsValid || id.IsErased) return null;
        if (id.Database != db) return new(id.Handle.ToString(), "", "", "", "", null, null, null, false);
        var obj = tx.GetObject(id, OpenMode.ForRead);
        using var rx = obj.GetRXClass();
        var dict = (DBDictionary)tx.GetObject(db.VisualStyleDictionaryId, OpenMode.ForRead);
        bool inDictionary = obj.OwnerId == dict.ObjectId && dict.Contains(id);
        var typed = obj as DBVisualStyle;
        return new(id.Handle.ToString(), obj.GetType().FullName!, rx.Name, rx.DxfName, obj.OwnerId.IsNull ? "" : obj.OwnerId.Handle.ToString(),
            inDictionary ? dict.NameAt(id) : null, typed?.Name, typed?.Type.ToString(), obj.Database == db && obj.ObjectId == id);
    }

    static Gs.Device BitmapDevice(Document doc, out Teigha.Runtime.Module? module)
    {
        var modulePath = Path.Combine(Path.GetDirectoryName(typeof(Database).Assembly.Location)!, "WinOpenGL_cc.txv");
        module = null;
        try
        {
            Require(File.Exists(modulePath), "The installed OpenGL vectorization module is unavailable.");
            module = SystemObjects.DynamicLinker.LoadModule(modulePath, false, true);
            if (module is Gs.GsModule gs && gs.CreateBitmapDevice() is { } bitmap) return bitmap;
        }
        catch (System.Exception err) when (err is Teigha.Runtime.Exception or NotSupportedException or InvalidOperationException)
        {
            module?.Dispose();
            module = null;
        }
        return doc.GraphicsManager.CreateAutoCADOffScreenDevice() ??
            throw new InvalidOperationException("Neither the installed module nor the host returned an offscreen device.");
    }

    public static byte[] Read(Document doc, SavedDwgInput? saved, int maxEdge, string renderMode, CameraRendererState370? renderCamera, DrawingCaptureEvents events,
        CancellationToken cancel, out DrawingReferenceStatus references)
    {
        Require(doc == Application.DocumentManager.MdiActiveDocument, "The requested drawing is no longer active.");
        Require(maxEdge is >= 256 and <= 2048, "The pixel budget is outside the current capture range.");
        using var camera = doc.Editor.GetCurrentView();
        Require(!camera.PerspectiveEnabled && !camera.FrontClipEnabled && !camera.BackClipEnabled, "Drawing capture requires an unclipped orthographic camera.");
        var db = doc.Database;
        var vp = Convert.ToInt32(Application.GetSystemVariable("CVPORT"));
        Require(db.TileMode && vp > 1 || !db.TileMode && vp == 1, "The active space has an unsupported floating model viewport.");
        Require(Application.GetSystemVariable("SCREENSIZE") is Point2d, "The current viewport size is unavailable.");
        var vpSize = (Point2d)Application.GetSystemVariable("SCREENSIZE");
        Require(vpSize.X > 0 && vpSize.Y > 0, "The current viewport is empty.");
        var ratio = vpSize.X / vpSize.Y;
        int width = ratio >= 1 ? maxEdge : Math.Max(1, (int)Math.Round(maxEdge * ratio));
        int height = ratio >= 1 ? Math.Max(1, (int)Math.Round(maxEdge / ratio)) : maxEdge;
        var style = camera.VisualStyleId;
        string layoutName, spaceName;
        bool model;
        Handle layoutHandle, spaceHandle;
        StyleInfo? liveStyle;
        DrawingReferenceStatus.Inventory? currentReferences = null;
        using (var liveTx = db.TransactionManager.StartOpenCloseTransaction())
        {
            var liveSpace = (BlockTableRecord)liveTx.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
            var liveLayout = (Layout)liveTx.GetObject(liveSpace.LayoutId, OpenMode.ForRead);
            layoutHandle = liveLayout.Handle; spaceHandle = liveSpace.Handle;
            layoutName = liveLayout.LayoutName; model = liveLayout.ModelType; spaceName = liveSpace.Name;
            liveStyle = InspectStyle(db, liveTx, style);
            if (saved == null)
                currentReferences = DrawingReferenceStatus.Placed(db, liveTx, liveSpace.ObjectId, cancel);
        }
        bool wblock = saved == null;
        Require(!db.IsPartiallyOpened, "The active drawing is partially open.");
        bool unverified = false;
        void ObserveDestination(Database database)
        {
            if (database.UnmanagedObject == db.UnmanagedObject || database.UnmanagedObject == IntPtr.Zero) return;
            try { events.Register(database); }
            catch (System.Exception) { unverified = true; }
        }
        void OnConstructed(object? sender, EventArgs args)
        {
            if (sender is Database destination) ObserveDestination(destination);
        }
        void OnMapping(object? sender, IdMappingEventArgs args)
        {
            var map = args.IdMapping;
            if (map.OriginalDatabase == db && map.DestinationDatabase is { } destination)
            {
                ObserveDestination(destination);
                currentReferences?.ObserveMapping(map);
            }
        }
        Database Acquire()
        {
            if (!wblock) return new Database(false, true);
            Database.DatabaseConstructed += OnConstructed;
            db.BeginDeepClone += OnMapping;
            db.BeginDeepCloneTranslation += OnMapping;
            db.WblockMappingAvailable += OnMapping;
            try { return db.Wblock(); }
            finally
            {
                db.WblockMappingAvailable -= OnMapping; db.BeginDeepClone -= OnMapping;
                db.BeginDeepCloneTranslation -= OnMapping;
                Database.DatabaseConstructed -= OnConstructed;
            }
        }
        cancel.ThrowIfCancellationRequested();
        events.Reading(true);
        using var bytes = saved is { IsPrivateCopy: true } ? new FileStreamBuf(saved.RenderPath, true,
            Teigha.Runtime.FileShareMode.DenyNo, FileCreationDisposition.OpenExisting) : null;
        using var namedStream = bytes != null ? new SavedDrawingStream(bytes, saved!.SourcePath) : null;
        using var side = Acquire();
        try
        {
            Require(!unverified, "The whole-database clone reported more than one possible destination.");
            events.Register(side);
            if (saved != null)
            {
                if (namedStream != null)
                {
                    // In this SDK the public IntPtr overload wraps the native
                    // OdStreamBuf* overload, not a Windows file handle. Keep the
                    // managed adapter and underlying file alive until side disposal.
                    side.ReadDwgFile(namedStream.UnmanagedObject, true, "");
                    side.CloseInput(true);
                    Require(string.Equals(Path.GetFullPath(side.Filename), saved.SourcePath, StringComparison.OrdinalIgnoreCase),
                        "The native side database could not bind the saved drawing's original directory for relative references.");
                }
                else side.ReadDwgFile(saved.SourcePath, FileOpenMode.OpenForReadAndAllShare, true, "", false);
            }
            events.Reading(false);
            cancel.ThrowIfCancellationRequested();
            using var tx = side.TransactionManager.StartOpenCloseTransaction();
            var layouts = (DBDictionary)tx.GetObject(side.LayoutDictionaryId, OpenMode.ForRead);
            var sideLayout = wblock ? layouts.GetAt(layoutName) : side.GetObjectId(false, layoutHandle, 0);
            var layout = (Layout)tx.GetObject(sideLayout, OpenMode.ForRead);
            var space = (BlockTableRecord)tx.GetObject(layout.BlockTableRecordId, OpenMode.ForRead);
            Require(layout.OwnerId == layouts.ObjectId && layout.LayoutName == layoutName && layout.ModelType == model && space.LayoutId == sideLayout &&
                (wblock ? space.Name == spaceName : space.Handle == spaceHandle),
                "The source drawing does not contain the corresponding active layout and space.");
            ObjectId styleId = ObjectId.Null;
            if (wblock && liveStyle?.DictionaryKey is string styleKey)
            {
                var styles = (DBDictionary)tx.GetObject(side.VisualStyleDictionaryId, OpenMode.ForRead);
                if (styles.Contains(styleKey)) styleId = styles.GetAt(styleKey);
            }
            else if (!style.IsNull && style.IsValid) side.TryGetObjectId(style.Handle, out styleId);
            var sideStyle = InspectStyle(side, tx, styleId);
            bool named = liveStyle is { Resident: true, InDictionary: true } && sideStyle is { Resident: true, InDictionary: true } &&
                (wblock || liveStyle.Handle == sideStyle.Handle && liveStyle.Owner == sideStyle.Owner) && liveStyle.DictionaryKey == sideStyle.DictionaryKey &&
                liveStyle.Native == sideStyle.Native && liveStyle.Dxf == sideStyle.Dxf && liveStyle.Name == sideStyle.Name && liveStyle.StyleType == sideStyle.StyleType;
            // For a nonresident/null selector, Optimized2d is a measured native
            // display mode with a documented detached-GS Wireframe2D definition.
            // A resident user style is never substituted with a standard style.
            bool basic2d = !named && (liveStyle == null || !liveStyle.InDictionary) && renderMode == "Optimized2d";
            Require(named || basic2d, "The active display has neither a corresponding resident style nor the supported native 2D mode.");
            // Keep the active database entirely out of the layout helper. It
            // installs temporary GS view pointers in the database's viewport records.
            var bitmapDevice = BitmapDevice(doc, out var loadedModule);
            using var module = loadedModule;
            using var device = bitmapDevice;
            // The explicit bitmap rendition uses the standard CAD dark palette;
            // its pixels do not depend on a composited or awake display device.
            device.LogicalPalette = Gs.Device.DarkPalette;
            device.BackgroundColor = Color.Black;
            using var context = new ContextForDbDatabase(side);
            using var helper = Gs.LayoutHelperDevice.SetupLayoutViews(sideLayout, device, context);
            Require(helper != null && helper.LayoutId == sideLayout, "The native helper did not select the exact saved layout.");
            helper!.OnSize(new Size(width, height));
            using var view = helper.ActiveView ?? throw new InvalidOperationException("There is no active side-database layout view.");
            Point3d target;
            double heightInDrawing;
            if (renderCamera != null)
            {
                // The model view can zoom while Editor.GetCurrentView retains its previous field height.
                var eye = renderCamera.eye_mm;
                var center = renderCamera.target_mm;
                var up = renderCamera.up_direction_wcs;
                target = new Point3d(center[0], center[1], center[2]);
                Require(renderCamera.viewport_pixels[0] == vpSize.X && renderCamera.viewport_pixels[1] == vpSize.Y,
                    "The native camera's viewport dimensions changed.");
                heightInDrawing = renderCamera.pixel_size_mm * vpSize.Y;
                view.SetView(new Point3d(eye[0], eye[1], eye[2]), target, new Vector3d(up[0], up[1], up[2]).GetNormal(),
                    heightInDrawing * ratio, heightInDrawing, Gs.Projection.Parallel);
            }
            else
            {
                var direction = camera.ViewDirection;
                var normal = direction.GetNormal();
                var basis = Matrix3d.PlaneToWorld(normal);
                var axisX = Vector3d.XAxis.TransformBy(basis).RotateBy(-camera.ViewTwist, normal);
                var axisY = Vector3d.YAxis.TransformBy(basis).RotateBy(-camera.ViewTwist, normal);
                target = camera.Target + axisX * camera.CenterPoint.X + axisY * camera.CenterPoint.Y;
                heightInDrawing = camera.Height;
                view.SetView(target + direction, target, axisY, heightInDrawing * ratio, heightInDrawing, Gs.Projection.Parallel);
            }
            if (named)
            {
                view.VisualStyleId = styleId;
                Require(view.VisualStyleId == styleId, "The side GS view did not retain the selected resident visual style.");
            }
            else
            {
                using var standard = new VisualStyle(VisualStyleType.Wireframe2D);
                view.VisualStyle = standard;
            }
            Require(view.Target.DistanceTo(target) < 1e-7 &&
                Math.Abs(view.FieldHeight - heightInDrawing) <= heightInDrawing / height &&
                Math.Abs(view.FieldWidth - heightInDrawing * ratio) <= heightInDrawing * ratio / width,
                "The detached view did not retain the source camera center or field of view.");
            references = DrawingReferenceStatus.Render(side, tx, space.ObjectId, currentReferences,
                saved?.SourcePath ?? (doc.IsNamedDrawing ? doc.Name : null), () => helper.Update(), cancel);
            // Read the produced raster property from the bitmap vectorizer itself.
            // There is no window capture, viewport GetSnapshot or DWG thumbnail.
            using var properties = device.Properties;
            Require(properties.Contains("RasterImage"), "The bitmap device does not expose RasterImage.");
            using var value = properties.At("RasterImage");
            var image = value as GIRasterImage ?? throw new InvalidOperationException($"The device returned {value?.GetType().FullName} for RasterImage.");
            Require(image.PixelWidth == width && image.PixelHeight == height, "The device raster dimensions differ from the requested drawing frame.");
            var result = Png(image, width, height);
            return result;
        }
        finally { events.ReleaseSide(); }
    }

    static byte[] Png(GIRasterImage data, int width, int height)
    {
        var format = data.pixelFormat();
        bool bgr = format.isBGR() || format.isBGRA();
        Require(bgr || format.isRGB() || format.isRGBA(), "Only explicit RGB/BGR true-color pixel layouts are supported.");
        int step = checked((int)data.ColorDepth) / 8;
        Require(step is 3 or 4 && data.ScanLineSize >= width * step, "Invalid native true-color raster stride.");
        using var result = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        var output = result.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        try
        {
            var row = new byte[width * 3];
            for (int y = 0; y < height; y++)
            {
                var source = data.ScanLines(height - y - 1, 1);
                Require(source.Length >= width * step, "Incomplete raster scan line.");
                for (int x = 0; x < width; x++)
                {
                    row[3 * x] = source[x * step + (bgr ? 0 : 2)]; row[3 * x + 1] = source[x * step + 1]; row[3 * x + 2] = source[x * step + (bgr ? 2 : 0)];
                }
                Marshal.Copy(row, 0, IntPtr.Add(output.Scan0, y * output.Stride), row.Length);
            }
        }
        finally { result.UnlockBits(output); }
        using var stream = new MemoryStream();
        result.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}
