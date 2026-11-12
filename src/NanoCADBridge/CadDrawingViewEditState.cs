using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Geometry;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db = Teigha.DatabaseServices;

namespace NanoCADBridge;

// Read-only selector/snapshot. Native writes never retain its Teigha transaction.
internal static class CadDrawingViewEditState
{
    internal sealed record View(McObjectId Id, Db.ObjectId Entity, Db.ObjectId Layout, Db.ObjectId Space, DrawingView Native, JsonElement Metadata);
    internal sealed record Body(McObjectId Id, Db.ObjectId Entity, bool Model, JsonElement Geometry, McObjectId[] Contents, McObjectId Last, Dictionary<McObjectId, McObjectId> Previous, JsonElement Nodes);
    internal sealed record Snapshot(JsonElement Context, JsonElement Layouts, JsonElement Entities, View[] Views, Body[] Bodies, JsonElement Sections);
    internal static void Need(bool value, string message, string code = "DRAWING_VIEW_STATE_UNAVAILABLE")
    { if (!value) throw new DomainException(code, message); }
    internal static bool Gone(Db.ObjectId id) => !id.IsValid || id.IsErased || id.IsEffectivelyErased;
    private static string Handle(Db.ObjectId id) => Validation.NormalizeHandle(id.Handle.ToString());
    internal static string? Id(McObjectId id) => id.IsNull ? null : id.ToString();
    private static double Finite(double value)
    { Need(double.IsFinite(value), "Native view/source metadata contains a nonfinite number."); return value; }
    private static double[] Point(Point3d point) => new[] { Finite(point.X), Finite(point.Y), Finite(point.Z) };
    private static double[] Vector(Vector3d vector) => new[] { Finite(vector.X), Finite(vector.Y), Finite(vector.Z) };
    internal static object Plane(Plane3d plane) => new { origin = Point(plane.Origin), axis_u = Vector(plane.DirU), axis_v = Vector(plane.DirV), normal = Vector(plane.Normal) };
    internal static string[] Ids(IEnumerable<McObjectId>? ids, string description)
    {
        Need(ids != null, description + " inventory is unavailable."); var result = ids!.ToArray();
        Need(result.All(id => !id.IsNull) && result.Distinct().Count() == result.Length, description + " inventory has null/duplicate identities.");
        return result.Select(id => id.ToString()).OrderBy(id => id, StringComparer.Ordinal).ToArray();
    }
    internal static McDocument Context(DocumentState state)
    {
        Need(HostMgd.ApplicationServices.Application.DocumentManager.MdiActiveDocument == state.Document,
            "Requested host drawing is no longer active.", "DOCUMENT_CONTEXT_MISMATCH");
        var loaded = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Need(loaded.Contains("mapimgd") && loaded.Contains("MapiBaseTypes"), "MultiCAD runtime is not already loaded.", "CAPABILITY_UNAVAILABLE");
        var document = McDocument.ActiveDocument;
        Need(document != null && state.Document.IsNamedDrawing && string.Equals(Path.GetFullPath(document.Path), Path.GetFullPath(state.Document.Name), StringComparison.OrdinalIgnoreCase),
            "Named MultiCAD document must match the active requested drawing.", "DOCUMENT_CONTEXT_MISMATCH");
        return document!;
    }
    internal static JsonElement Metadata(DrawingView view)
    {
        var db = view.DbEntity; Need(db != null, "Native view representation is unavailable.");
        return Wire.Element(new
        {
            id = view.ID.ToString(),
            name = view.Name,
            status = view.Status.ToString(),
            suppressed = view.IsSuppressed,
            direction = view.DirectionType.ToString(),
            parent = Id(view.ParentViewID),
            root = Id(view.RootViewID),
            children = Ids(view.ChildViews, "View children"),
            previous = Id(view.OrderObjBefore),
            host_solid = Id(view.HostSolidID),
            sources = Ids(view.GetTop3dEntitiesInView(), "View sources"),
            db_parent = Id(db!.ParentId),
            db_children = Ids(db.ChildrenIds, "Native entity children"),
            section = Id(view.SectionID),
            breaks = Ids(view.Attached2dBreaks, "View breaks"),
            local_cuts = Ids(view.AttachedLocalCuts, "View local cuts"),
            outer_cut = Id(view.OuterCutSketch),
            is_slice = view.IsSlice,
            is_based_on_section = view.IsBasedOnSection,
            is_root_section_view = view.IsRootSectionView,
            depends_on_slice = view.DependsOnSlice,
            use_sectioned_models = view.UseSectionedModels,
            auto_update = view.EnableAutoUpdate,
            preview_mode = view.PreviewMode,
            align_on_parent = view.AlignOnParent,
            auto_projection_plane = view.AutoCalcProjPlaneByViewDirection,
            view_plane = Plane(view.ViewPlane),
            projection_plane = Plane(view.ProjPlane),
            model_scale = Finite(db.DScale),
            design_scale = Finite(db.Scale),
            hidden = view.ShowHiddenLines,
            hatches = view.ShowHatches,
            tangent = view.ShowTangentEdges
        });
    }
    private static JsonElement SectionMetadata(Section section, Action cancel)
    {
        cancel(); var vertices = section.GetVertices(); Need(vertices != null && vertices.Count >= 2, "Section vertices are unavailable/invalid.");
        return Wire.Element(new
        {
            id = section.ID.ToString(),
            state = section.State.ToString(),
            status = section.Status.ToString(),
            vertices = vertices!.Select(Point).ToArray(),
            vertical = Vector(section.VerticalDirection),
            viewing = Vector(section.ViewingDirection),
            associated_plane = Id(section.AssociatedPlanarObjectID),
            depth = Finite(section.Depth),
            height_above = Finite(section.HeightAboveSection),
            height_below = Finite(section.HeightBelowSection),
            live = section.LiveSectionEnabled,
            views = Ids(section.GetViews(), "Section views"),
            sources = Ids(section.GetTop3dEntitiesForSectioning(), "Section sources")
        });
    }
    internal static Snapshot Capture(DocumentState state, Db.Transaction tx, Action cancel, Action<JsonElement>? account = null, bool allowUnselectedForeignSources = false)
    {
        var document = Context(state); var database = state.Document.Database;
        var dictionary = (Db.DBDictionary)tx.GetObject(database.LayoutDictionaryId, Db.OpenMode.ForRead);
        var layouts = new List<JsonElement>(); var entities = new List<JsonElement>(); var views = new List<View>(); var bodies = new List<Body>();
        var sectionIds = new HashSet<McObjectId>(); var nativeEntities = new Dictionary<McObjectId, Db.ObjectId>();
        foreach (Db.DBDictionaryEntry entry in dictionary)
        {
            cancel(); Need(!Gone(entry.Value), "Layout dictionary contains an unavailable record.");
            var layout = tx.GetObject(entry.Value, Db.OpenMode.ForRead) as Db.Layout;
            Need(layout != null && layout.GetType() == typeof(Db.Layout) && layout.OwnerId == dictionary.ObjectId && layout.LayoutName == entry.Key, "Layout identity/name/owner is inconsistent.");
            var space = tx.GetObject(layout!.BlockTableRecordId, Db.OpenMode.ForRead) as Db.BlockTableRecord;
            Need(space != null && space.GetType() == typeof(Db.BlockTableRecord) && space.IsLayout && space.LayoutId == layout.ObjectId && space.OwnerId == database.BlockTableId &&
                !space.IsDependent && !space.IsFromExternalReference && !space.IsFromOverlayReference, "Layout space has inconsistent/dependent ownership.");
            layouts.Add(Wire.Element(new
            {
                id = Handle(layout.ObjectId),
                space = Handle(space!.ObjectId),
                name = layout.LayoutName,
                model = layout.ModelType,
                layout.TabOrder,
                current_space = database.CurrentSpaceId == space.ObjectId
            }));
            account?.Invoke(layouts[^1]);
            foreach (Db.ObjectId id in space)
            {
                cancel(); if (Gone(id)) continue; var entity = tx.GetObject(id, Db.OpenMode.ForRead) as Db.Entity;
                Need(entity != null && entity.OwnerId == space.ObjectId, "Layout member has inconsistent native ownership.");
                entities.Add(Wire.Element(new
                {
                    id = Handle(id),
                    owner = Handle(entity!.OwnerId),
                    type = entity.GetType().FullName,
                    layer = Handle(entity.LayerId),
                    properties = EntityAdapter.StoredProperties(entity)
                }));
                account?.Invoke(entities[^1]);
                var nativeId = McObjectId.FromHandle(id.Handle.Value, document.ID);
                if (!nativeId.IsNull) Need(nativeEntities.TryAdd(nativeId, id), "Native entity has ambiguous layout representations.");
                var view = nativeId.IsNull ? null : nativeId.GetObjectOfType<DrawingView>();
                if (view != null)
                {
                    Need(view.ID == nativeId && entity.GetType().FullName == "Teigha.DatabaseServices.ImpCurve", "DrawingView must have one exact native ImpCurve representation.");
                    views.Add(new(nativeId, id, layout.ObjectId, space.ObjectId, view, Metadata(view))); account?.Invoke(views[^1].Metadata);
                    if (!view.SectionID.IsNull) sectionIds.Add(view.SectionID);
                }
                var section = nativeId.IsNull ? null : nativeId.GetObjectOfType<Section>();
                if (section != null) { Need(section.ID == nativeId, "Section identity differs from its representation."); sectionIds.Add(nativeId); }
                if (entity is not Db.Solid3d solid || entity.GetType() != typeof(Db.Solid3d)) continue;
                var mass = solid.MassProperties; var bounds = solid.GeometricExtents;
                var geometry = Wire.Element(new
                {
                    volume = Finite(mass.Volume),
                    centroid = new[] { Finite(mass.Centroid.X), Finite(mass.Centroid.Y), Finite(mass.Centroid.Z) },
                    min = new[] { Finite(bounds.MinPoint.X), Finite(bounds.MinPoint.Y), Finite(bounds.MinPoint.Z) },
                    max = new[] { Finite(bounds.MaxPoint.X), Finite(bounds.MaxPoint.Y), Finite(bounds.MaxPoint.Z) }
                });
                var body = nativeId.IsNull ? null : nativeId.GetObjectOfType<Mc3dSolid>(); var contents = body?.GetPartContents(false);
                Need(body == null || body.ID == nativeId && contents != null, "Native source body history is unavailable.");
                var parts = contents?.ToArray() ?? Array.Empty<McObjectId>(); Ids(parts, "Body history"); var previous = new Dictionary<McObjectId, McObjectId>(); var nodes = new List<JsonElement>();
                foreach (var part in parts)
                {
                    cancel(); var item = part.GetObjectOfType<HistoryItem>();
                    if (item != null) { Need(item.ID == part, "History identity differs from its native lookup."); previous.Add(part, item.OrderObjBefore); }
                    nodes.Add(Wire.Element(new
                    {
                        id = part.ToString(),
                        previous = item == null ? null : Id(item.OrderObjBefore),
                        status = item?.Status.ToString(),
                        suppressed = item == null ? (bool?)null : item.IsSuppressed
                    }));
                    account?.Invoke(nodes[^1]);
                }
                account?.Invoke(geometry);
                bodies.Add(new(nativeId, id, layout.ModelType, geometry, parts, body?.LastPartItemID ?? McObjectId.Null, previous,
                    Wire.Element(nodes.OrderBy(node => node.GetProperty("id").GetString(), StringComparer.Ordinal))));
            }
        }
        // Sources use native opaque IDs, not the layout handles supplied by callers.
        if (!allowUnselectedForeignSources) foreach (var view in views)
            foreach (var source in view.Native.GetTop3dEntitiesInView())
                Need(bodies.Any(body => body.Id == source && body.Model), "A drawing view references a source outside the addressable top-level Model solids.", "OUT_OF_SCOPE");
        var sections = new List<JsonElement>();
        foreach (var id in sectionIds.OrderBy(id => id.ToString(), StringComparer.Ordinal))
        {
            cancel(); var section = id.GetObjectOfType<Section>(); Need(section != null && section.ID == id, "Associated section identity is unavailable.");
            sections.Add(SectionMetadata(section!, cancel));
            account?.Invoke(sections[^1]);
        }
        string activeName = Db.LayoutManager.Current.CurrentLayout; var activeLayout = Db.LayoutManager.Current.GetLayoutId(activeName);
        Need(layouts.Any(item => item.GetProperty("id").GetString() == Handle(activeLayout) && item.GetProperty("name").GetString() == activeName &&
            item.GetProperty("model").GetBoolean() == database.TileMode), "Native current layout does not belong to this drawing/context.", "DOCUMENT_CONTEXT_MISMATCH");
        var context = Wire.Element(new
        {
            active_layout = Handle(activeLayout),
            active_layout_name = activeName,
            tile_mode = database.TileMode,
            current_space = Handle(database.CurrentSpaceId),
            mc_document = document.ID.ToString()
        });
        return new(context, Wire.Element(layouts.OrderBy(item => item.GetProperty("id").GetString(), StringComparer.Ordinal)),
            Wire.Element(entities.OrderBy(item => item.GetProperty("id").GetString(), StringComparer.Ordinal)), views.ToArray(), bodies.ToArray(), Wire.Element(sections));
    }
    internal static View Resolve(DrawingViewTarget target, Snapshot snapshot, Db.Transaction tx, Action cancel)
    {
        cancel(); var selected = snapshot.Views.SingleOrDefault(view => Handle(view.Entity) == target.Handle);
        Need(selected != null && Handle(selected.Layout) == target.LayoutHandle, "Exact DrawingView is not a top-level member of the requested layout.", "OUT_OF_SCOPE");
        Need(selected!.Native.Name == target.ExpectedName, "View name differs from expected_name; refresh get_drawing_views.", "STRUCTURE_NAME_MISMATCH");
        Need(selected.Native.Status.ToString() == "Ok" && !selected.Native.IsSuppressed && selected.Native.EnableAutoUpdate,
            "Requested view must be unsuppressed, auto-updating and native status Ok.", "MODEL_NOT_READY");
        var entity = (Db.Entity)tx.GetObject(selected.Entity, Db.OpenMode.ForRead); var layer = (Db.LayerTableRecord)tx.GetObject(entity.LayerId, Db.OpenMode.ForRead);
        Need(entity.Visible && !layer.IsLocked && !layer.IsFrozen && !layer.IsOff && !layer.IsDependent, "View must be visible on a local visible/thawed/unlocked layer.", "LAYER_UNAVAILABLE");
        Need(!entity.HasFields, "Native view fields require their owning edit route.", "OUT_OF_SCOPE");
        Need(selected.Native.DbEntity.DScale > 0 && selected.Native.DbEntity.Scale > 0, "Native view has invalid stored scale.");
        if (!selected.Native.HostSolidID.IsNull)
            Need(snapshot.Bodies.Any(body => body.Model && body.Id == selected.Native.HostSolidID && body.Contents.Contains(selected.Id)),
                "Claimed native host body/history does not contain this view; its owning history cannot be observed safely.", "OUT_OF_SCOPE");
        return selected;
    }
    internal static void Tree(Snapshot snapshot, Action cancel)
    {
        var byId = snapshot.Views.ToDictionary(view => view.Id);
        foreach (var view in snapshot.Views)
        {
            cancel();
            var children = view.Native.ChildViews; Need(children != null, "View child inventory is unavailable.");
            foreach (var childId in children!) Need(byId.TryGetValue(childId, out var child) && child.Native.ParentViewID == view.Id, "View child/parent relation is inconsistent.");
            if (!view.Native.ParentViewID.IsNull) Need(byId.TryGetValue(view.Native.ParentViewID, out var parent) && parent.Native.ChildViews.Contains(view.Id), "View parent/child relation is inconsistent.");
            var visited = new HashSet<McObjectId>(); var current = view;
            while (!current.Native.ParentViewID.IsNull)
            {
                cancel(); Need(visited.Add(current.Id), "View parent tree contains a cycle."); current = byId[current.Native.ParentViewID];
            }
            Need(view.Native.RootViewID == current.Id || view.Id == current.Id && view.Native.RootViewID.IsNull, "Native root view identity is inconsistent.");
        }
    }
    internal static void Plain(View view)
    {
        var native = view.Native;
        Need(native.SectionID.IsNull && !native.IsSlice && !native.IsBasedOnSection && !native.IsRootSectionView && !native.DependsOnSlice &&
            native.Attached2dBreaks is { Length: 0 } && native.AttachedLocalCuts is { Length: 0 } && native.OuterCutSketch.IsNull,
            "Section/slice views or views with breaks/local cuts require their owning deletion adapter.", "OUT_OF_SCOPE");
    }
    internal static void Placement(View view)
    {
        Need(view.Native.Attached2dBreaks is { Length: 0 } && view.Native.AttachedLocalCuts is { Length: 0 } && view.Native.OuterCutSketch.IsNull,
            "Placement/scale of a view with breaks/local cuts requires its owning adapter.", "OUT_OF_SCOPE");
    }
    internal static JsonElement Expected(View before, SetDrawingViewPropertiesEdit? edit, HashSet<McObjectId> deleted, Dictionary<McObjectId, McObjectId> predecessors)
    {
        var value = JsonNode.Parse(before.Metadata.GetRawText())!.AsObject();
        if (edit != null)
        {
            if (edit.Origin is { } origin)
            {
                var point = value["view_plane"]!["origin"]!.AsArray(); point[0] = origin.X; point[1] = origin.Y;
            }
            if (edit.ModelScale is { } scale) value["model_scale"] = scale;
            if (edit.ShowHiddenLines is { } hidden) value["hidden"] = hidden;
            if (edit.ShowHatches is { } hatches) value["hatches"] = hatches;
            if (edit.ShowTangentEdges is { } tangent) value["tangent"] = tangent;
            if (edit.PreviewMode is { } preview) value["preview_mode"] = preview;
        }
        if (deleted.Count > 0)
        {
            var removed = deleted.Select(id => id.ToString()).ToHashSet(StringComparer.Ordinal);
            foreach (string key in new[] { "children", "db_children" })
                value[key] = JsonSerializer.SerializeToNode(before.Metadata.GetProperty(key).EnumerateArray().Select(item => item.GetString()).Where(id => !removed.Contains(id!)), Wire.Json);
            value["previous"] = Id(Skip(predecessors[before.Id], deleted, predecessors));
        }
        return JsonSerializer.SerializeToElement(value, Wire.Json);
    }
    internal static McObjectId Skip(McObjectId id, HashSet<McObjectId> deleted, Dictionary<McObjectId, McObjectId> previous)
    {
        var seen = new HashSet<McObjectId>(); while (deleted.Contains(id))
        { Need(seen.Add(id) && previous.ContainsKey(id), "Erased view predecessor chain is unavailable/cyclic."); id = previous[id]; }
        return id;
    }
}
