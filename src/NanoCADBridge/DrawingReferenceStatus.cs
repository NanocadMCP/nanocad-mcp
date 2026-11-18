using Teigha.DatabaseServices;
using Teigha.Runtime;

namespace NanoCADBridge;

// Count placed resources reachable through actual insertions. Unused named
// definitions are not drawing content. Native clone IDs, when available, can
// identify a raster insertion whose definition the whole-DWG clone dropped.
internal sealed record DrawingReferenceStatus(int dwg_definitions, int raster_definitions, int underlay_definitions,
    int unavailable_or_unloaded, int relative_path_unconfirmed, int raster_insertions, int loaded_raster_insertions,
    int? current_source_insertions, int? current_source_raster_insertions, int? current_source_unavailable,
    int current_insertions_missing_in_clone,
    int detached_raster_loads, int restored_raster_links, string source_basis, string frame_completeness,
    string external_file_basis, string? warning, string[] issues, object[] resource_sample)
{
    public string? raster_orientation_warning => loaded_raster_insertions > 0
        ? "The detached renderer can mirror raster pixels vertically. Before tracing or measuring a raster, read the external image and verify its placement. For the matching active RasterImage, inspect_objects returns raster_pixel_frame. Map pixel column c and row r to origin_wcs + c * step_x_wcs + r * step_y_wcs."
        : null;

    internal sealed record Resource(string Kind, ObjectId Definition, string Stored, string? Active, bool Loaded,
        double Width = 0, double Height = 0);

    internal sealed class Inventory
    {
        internal readonly Dictionary<ObjectId, Resource> Items = new();
        internal readonly Dictionary<ObjectId, ObjectId> Destination = new();
        readonly HashSet<ObjectId> ambiguous = new();

        internal void ObserveMapping(IdMapping map)
        {
            foreach (var id in Items.Keys)
            {
                if (ambiguous.Contains(id) || !map.Contains(id)) continue;
                var pair = map.Lookup(id);
                if (pair.IsCloned && pair.Key == id && !pair.Value.IsNull && pair.Value.Database == map.DestinationDatabase)
                {
                    if (Destination.TryGetValue(id, out var previous) && previous != pair.Value)
                    {
                        ambiguous.Add(id);
                        Destination.Remove(id);
                    }
                    else Destination[id] = pair.Value;
                }
            }
        }
    }

    static bool Owned(ObjectId id, Database db) => !id.IsNull && id.IsValid && !id.IsErased && id.Database == db;

    internal static Inventory Placed(Database db, Transaction tx, ObjectId initialSpace, CancellationToken cancel)
    {
        var result = new Inventory();
        var blockClass = RXObject.GetClass(typeof(BlockReference));
        var imageClass = RXObject.GetClass(typeof(RasterImage));
        var wipeoutClass = RXObject.GetClass(typeof(Wipeout));
        var underlayClass = RXObject.GetClass(typeof(UnderlayReference));
        var viewportClass = RXObject.GetClass(typeof(Viewport));
        var seen = new HashSet<ObjectId>();
        var todo = new Stack<ObjectId>();
        todo.Push(initialSpace);
        while (todo.TryPop(out var owner))
        {
            cancel.ThrowIfCancellationRequested();
            if (!seen.Add(owner)) continue;
            var block = (BlockTableRecord)tx.GetObject(owner, OpenMode.ForRead);
            foreach (ObjectId id in block)
            {
                cancel.ThrowIfCancellationRequested();
                if (!Owned(id, db)) continue;
                var type = id.ObjectClass;
                if (type.IsDerivedFrom(blockClass))
                {
                    var insert = (BlockReference)tx.GetObject(id, OpenMode.ForRead);
                    var target = insert.BlockTableRecord;
                    if (!Owned(target, db))
                    {
                        result.Items[id] = new("unbound_block", ObjectId.Null, "", null, false);
                        continue;
                    }
                    var child = (BlockTableRecord)tx.GetObject(target, OpenMode.ForRead);
                    if (child.IsFromExternalReference || child.IsFromOverlayReference)
                    {
                        var active = CadExternalReferences385.ActiveSource(child);
                        result.Items[id] = new("dwg", target, child.PathName, active, active != null);
                    }
                    else todo.Push(target);
                }
                else if (type.IsDerivedFrom(imageClass) && !type.IsDerivedFrom(wipeoutClass))
                {
                    var insert = (RasterImage)tx.GetObject(id, OpenMode.ForRead);
                    var target = insert.ImageDefId;
                    result.Items[id] = Owned(target, db) && tx.GetObject(target, OpenMode.ForRead) is RasterImageDef image
                        ? Raster(target, image) : new("raster", ObjectId.Null, "", null, false);
                }
                else if (type.IsDerivedFrom(underlayClass))
                {
                    var insert = (UnderlayReference)tx.GetObject(id, OpenMode.ForRead);
                    var target = insert.DefinitionId;
                    result.Items[id] = Owned(target, db) && tx.GetObject(target, OpenMode.ForRead) is UnderlayDefinition external
                        ? new("underlay", target, external.SourceFileName, CadExternalReferences385.ActiveSource(external), external.Loaded)
                        : new("underlay", ObjectId.Null, "", null, false);
                }
                else if (type.IsDerivedFrom(viewportClass) && tx.GetObject(id, OpenMode.ForRead) is Viewport { On: true } viewport && viewport.Number != 1)
                {
                    var table = (BlockTable)tx.GetObject(db.BlockTableId, OpenMode.ForRead);
                    todo.Push(table[BlockTableRecord.ModelSpace]);
                }
            }
        }
        return result;
    }

    static Resource Raster(ObjectId id, RasterImageDef image)
    {
        // hostdbmgd 26 implements both IsEmbedded and ImageModified as
        // unconditional NotImplementedException stubs. A loaded image with no
        // verified external filename may still have resident pixels.
        bool loaded = image.IsLoaded;
        var size = loaded ? image.Size : default;
        return new("raster", id, image.SourceFileName, CadExternalReferences385.ActiveSource(image), loaded, size.X, size.Y);
    }

    static string? PathAt(string? stored, string? drawingPath)
    {
        if (string.IsNullOrWhiteSpace(stored)) return null;
        try
        {
            if (Path.IsPathFullyQualified(stored)) return Path.GetFullPath(stored);
            if (Path.IsPathRooted(stored) || drawingPath == null || !Path.IsPathFullyQualified(drawingPath)) return null;
            return Path.GetFullPath(stored, Path.GetDirectoryName(drawingPath)!);
        }
        catch (System.Exception e) when (e is ArgumentException or NotSupportedException or IOException) { return null; }
    }

    static bool SameFile(string? a, string? b) => a != null && b != null && StringComparer.OrdinalIgnoreCase.Equals(a, b);
    static bool Ready(Resource r) => r.Loaded && (r.Kind != "raster" || r.Width > 0 && r.Height > 0) && PathAt(r.Active, null) != null;

    // This index verifies external files only. It supplies no source or clone
    // object IDs and cannot authorize relinking an insertion.
    static Dictionary<string, (double, double)?> CurrentRasterFiles(Inventory? source, string? drawingPath)
    {
        var files = new Dictionary<string, (double, double)?>(StringComparer.OrdinalIgnoreCase);
        if (source == null) return files;
        foreach (var item in source.Items.Values)
        {
            if (item.Kind != "raster" || PathAt(item.Stored, drawingPath) is not { } path) continue;
            (double, double)? size = Ready(item) && SameFile(path, PathAt(item.Active, null)) ? (item.Width, item.Height) : null;
            if (files.TryGetValue(path, out var existing) && existing != size) size = null;
            files[path] = size;
        }
        return files;
    }

    internal static DrawingReferenceStatus Render(Database side, Transaction tx, ObjectId sideSpace, Inventory? source,
        string? drawingPath, Action update, CancellationToken cancel)
    {
        var placed = Placed(side, tx, sideSpace, cancel);
        var byTarget = new Dictionary<ObjectId, Resource>();
        if (source != null)
            foreach (var group in source.Destination.GroupBy(p => p.Value))
                if (group.Count() == 1 && Owned(group.Key, side) && placed.Items.ContainsKey(group.Key))
                    byTarget.Add(group.Key, source.Items[group.Single().Key]);
        var currentFiles = CurrentRasterFiles(source, drawingPath);
        int loaded = 0, restored = 0, unavailable = 0, unconfirmed = 0;
        var samples = new List<string>();
        static string Bounded(string value) => value.Length <= 260 ? value : "..." + value[^257..];
        void Sample(string value) { if (samples.Count < 8) samples.Add(Bounded(value)); }
        var restoredDefs = new Dictionary<ObjectId, ObjectId>();
        var failed = new HashSet<ObjectId>();
        foreach (var (occurrence, seen) in placed.Items.ToArray())
        {
            cancel.ThrowIfCancellationRequested();
            if (seen.Kind != "raster") continue;
            byTarget.TryGetValue(occurrence, out var original);
            // An unbound clone may only be repaired from the original native
            // mapped insertion and its loaded external definition. No handles,
            // presentation names, array indices or positions are used as aliases.
            try
            {
                var target = seen.Definition;
                bool missing = target.IsNull;
                if (original != null && (original.Kind != "raster" || !Ready(original)))
                {
                    failed.Add(occurrence);
                    Sample($"Current source for detached raster {occurrence.Handle} has no verified loaded external file.");
                    continue;
                }
                if (missing && original == null) continue;
                var expected = original == null ? PathAt(seen.Stored, drawingPath) : PathAt(original.Active, null);
                if (expected == null) continue;
                bool needsExternalPixels = seen.Loaded && PathAt(seen.Active, null) == null;
                if (needsExternalPixels)
                {
                    // Wblock can preserve the size and stored path and still
                    // render only the filename. Keep ambiguous resident images
                    // intact. The disposable clone may reload its own file
                    // only with independent current external-file evidence.
                    bool verified = original != null
                        ? original.Width == seen.Width && original.Height == seen.Height
                        : currentFiles.TryGetValue(expected, out var fileSize) && fileSize == (seen.Width, seen.Height);
                    if (!verified) continue;
                }
                if (!missing && Ready(seen) && SameFile(expected, PathAt(seen.Active, null)) &&
                    (original == null || original.Width == seen.Width && original.Height == seen.Height)) continue;
                // The loader receives a verified absolute filename on the
                // detached definition only. Never resolve against process CWD.
                if (!File.Exists(expected)) continue;
                using var created = missing && !restoredDefs.ContainsKey(original!.Definition) ? new RasterImageDef() : null;
                if (missing)
                {
                    if (created != null)
                    {
                        var dictionaryId = RasterImageDef.CreateImageDictionary(side);
                        var dictionary = (DBDictionary)tx.GetObject(dictionaryId, OpenMode.ForWrite);
                        // The private dictionary key identifies no source object.
                        target = dictionary.SetAt(Guid.NewGuid().ToString("N"), created);
                        tx.AddNewlyCreatedDBObject(created, true);
                        created.SourceFileName = expected;
                        restoredDefs.Add(original!.Definition, target);
                    }
                    else target = restoredDefs[original!.Definition];
                }
                var image = created ?? (RasterImageDef)tx.GetObject(target, OpenMode.ForWrite);
                var actual = Raster(target, image);
                if (!Ready(actual) || !SameFile(expected, PathAt(actual.Active, null)))
                {
                    if (image.IsLoaded) image.Unload(true);
                    image.SourceFileName = expected;
                    image.Load();
                    actual = Raster(target, image);
                    if (!Ready(actual) || !SameFile(expected, PathAt(actual.Active, null)))
                        throw new InvalidOperationException("The detached loader did not retain the requested raster file.");
                    loaded++;
                }
                if ((original != null && (original.Width != actual.Width || original.Height != actual.Height)) ||
                    (needsExternalPixels && (seen.Width != actual.Width || seen.Height != actual.Height)))
                    throw new InvalidOperationException("The detached file dimensions differ from the loaded source raster.");
                if (missing)
                {
                    var insert = (RasterImage)tx.GetObject(occurrence, OpenMode.ForWrite);
                    insert.ImageDefId = target;
                    if (insert.ImageDefId != target) throw new InvalidOperationException("The detached insertion did not retain its definition.");
                    restored++;
                }
            }
            catch (System.Exception e) when (e is Teigha.Runtime.Exception or InvalidOperationException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                failed.Add(occurrence);
                Sample($"Detached raster {occurrence.Handle}: {e.Message}");
            }
        }

        cancel.ThrowIfCancellationRequested();
        update();
        if (placed.Items.Count != 0 || source?.Items.Count > 0)
            placed = Placed(side, tx, sideSpace, cancel);
        static string Label(Resource r) => string.IsNullOrEmpty(r.Stored) ? "(unbound definition)" :
            r.Stored.Length <= 120 ? r.Stored : "..." + r.Stored[^117..];
        foreach (var (id, item) in placed.Items)
        {
            var expected = PathAt(item.Stored, drawingPath);
            if (failed.Contains(id) || !Ready(item))
            {
                unavailable++;
                Sample($"Detached {item.Kind} insertion {id.Handle} {Label(item)} has no verified loaded resource. Its outline or an empty area may be shown.");
            }
            if (!string.IsNullOrEmpty(item.Stored) && !Path.IsPathFullyQualified(item.Stored) &&
                !SameFile(expected, PathAt(item.Active, null)))
            {
                unconfirmed++;
                Sample($"Detached {item.Kind} insertion {id.Handle} has no confirmed drawing-relative file for {Label(item)}.");
            }
        }
        int missingFromClone = 0, sourceUnavailable = 0;
        if (source != null)
        {
            // Compare resource multiplicities independently of the clone map.
            // This detects dropped insertions even when Wblock emits no map.
            // These keys never authorize rewiring an insertion.
            string Key(Resource r) => r.Kind + "\0" + (PathAt(r.Active, null) ?? PathAt(r.Stored, drawingPath) ?? r.Stored);
            var counts = placed.Items.Values.GroupBy(Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            foreach (var (id, item) in source.Items)
            {
                if (!Ready(item)) { sourceUnavailable++; Sample($"Current {item.Kind} insertion {id.Handle} {Label(item)} has no verified loaded external resource."); }
                var key = Key(item);
                if (counts.TryGetValue(key, out int count) && count > 0) counts[key] = count - 1;
                else { missingFromClone++; Sample($"Current {item.Kind} insertion {id.Handle} {Label(item)} has no corresponding detached resource in the inventory."); }
            }
        }
        int Definitions(string kind) => placed.Items.Values.Where(r => r.Kind == kind && !r.Definition.IsNull).Select(r => r.Definition).Distinct().Count();
        bool incomplete = unavailable != 0 || unconfirmed != 0 || missingFromClone != 0 || sourceUnavailable != 0;
        return new(Definitions("dwg"), Definitions("raster"), Definitions("underlay"), unavailable, unconfirmed,
            placed.Items.Values.Count(r => r.Kind == "raster"), placed.Items.Count(p => p.Value.Kind == "raster" && Ready(p.Value) && !failed.Contains(p.Key)),
            source?.Items.Count, source?.Items.Values.Count(r => r.Kind == "raster"), source == null ? null : sourceUnavailable, missingFromClone, loaded, restored,
            source == null ? "placed_in_saved_layout" : "placed_in_current_layout_compared_to_detached_clone",
            incomplete ? "incomplete_external_references" : "placed_external_resources_resolved",
            "The inventory covers placed insertions in this layout, its internal blocks and model space used by enabled paper viewports. Reloaded rasters use the external file at render time. The installed SDK does not report embedded-image or in-memory pixel-edit state. Pixel coverage and nested contents of referenced DWGs require separate inspection." +
                (source != null && loaded > 0 ? " Current drawing geometry uses external file pixels for reloaded rasters; unsaved edits to those pixels cannot be verified." : ""),
            incomplete ? "Some placed external resources were unavailable or could not be verified. The frame may contain reference outlines or omit content." : null,
            samples.ToArray(), placed.Items.Take(8).Select(p => (object)new
            {
                kind = p.Value.Kind,
                detached_insertion = p.Key.Handle.ToString(),
                detached_definition = p.Value.Definition.IsNull ? null : p.Value.Definition.Handle.ToString(),
                detached_stored_path = Bounded(p.Value.Stored ?? ""),
                detached_active_path = p.Value.Active == null ? null : Bounded(p.Value.Active),
                verified_loaded = Ready(p.Value) && !failed.Contains(p.Key),
                raster_pixel_size = p.Value.Kind == "raster" && p.Value.Loaded ? new[] { p.Value.Width, p.Value.Height } : null,
                raster_embedded_status = p.Value.Kind == "raster" ? "unknown" : null,
                raster_in_memory_pixel_edits = p.Value.Kind == "raster" ? "unknown" : null
            }).ToArray());
    }
}
