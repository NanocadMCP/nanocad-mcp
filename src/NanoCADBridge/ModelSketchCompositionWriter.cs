using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.DatabaseServices.StandardObjects;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Db = Teigha.DatabaseServices;
using G = Multicad.Geometry;

namespace NanoCADBridge;

// Edits exact retained sources and lets native history rebuild its consumers.
// A detached extrusion oracle is deliberately unnecessary for sweep/revolve/
// loft/finishing compositions. Native health, identities, unchanged drivers and
// exact requested sketch geometry are verified; engineering visual acceptance
// remains a separate grouped check. Rollback is not guaranteed.
internal static class ModelSketchCompositionWriter
{
    static string Key(McObjectId id) => id.ToString();
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok, string code, string message) { if (!ok) throw new DomainException(code, message); }
    sealed record SketchBinding(ModelSketchEdit Edit, PlanarSketch Sketch, JsonElement Before, JsonElement Desired, Dictionary<string, DbGeometry> Storage);
    sealed record Binding(Mc3dSolid Body, Db.ObjectId Source, ModelParameterArguments Args, JsonElement Before,
        SketchBinding[] Sketches, Dictionary<string, JsonElement> OtherSketches, ModelViewFollowers.Snapshot Views,
        Db.ObjectId Layer, HashSet<Db.ObjectId> UnrelatedInventory);

    static HashSet<Db.ObjectId> UnrelatedInventory(DocumentState state, Db.Transaction tx, Db.ObjectId representation)
    {
        var model = ((Db.BlockTable)tx.GetObject(state.Document.Database.BlockTableId, Db.OpenMode.ForRead))[Db.BlockTableRecord.ModelSpace];
        return ((Db.BlockTableRecord)tx.GetObject(model, Db.OpenMode.ForRead)).Cast<Db.ObjectId>()
            .Where(id => id != representation && !id.IsErased && !id.IsEffectivelyErased).ToHashSet();
    }

    // Selection is read-only. No native factory, manager solve, edit mode or
    // datum creation belongs in route selection/preflight.
    internal static bool Handles(ModifyArguments args, DocumentState state, Db.Transaction tx, Action? cancel = null)
    {
        if (args.Operations.Length == 0 || !args.Operations.All(e => e is ModelSketchEdit)) return false;
        var active = McDocument.ActiveDocument; if (active == null) return false;
        var edit = (ModelSketchEdit)args.Operations[0];
        var source = CadStructureWriter.Find(state.Document.Database, edit.Handles[0]);
        if (tx.GetObject(source, Db.OpenMode.ForRead) is not Db.Solid3d) return false;
        var body = McObjectId.FromHandle(source.Handle.Value, active.ID).GetObjectOfType<Mc3dSolid>();
        var ids = body?.GetPartContents(false);
        if (ids != null) ModelInventoryBudget.Ids(ids, cancel);
        if (ids == null) return false;
        // Leave already accepted simple chains to their current writer.
        return ids.Any(id =>
        {
            cancel?.Invoke(); return id.GetObjectOfType<RevolveFeature>() != null || id.GetObjectOfType<ExtrudeAlongPathFeature>() != null ||
            id.GetObjectOfType<LoftFeature>() != null || id.GetObjectOfType<HoleFeature>() != null || id.GetObjectOfType<ThreadFeature>() != null ||
            id.GetObjectOfType<CircularPatternFeature>() != null || id.GetObjectOfType<MirrorFeature>() != null ||
            id.GetObjectOfType<WorkPlane>() != null || id.GetObjectOfType<WorkPoint>() != null || id.GetObjectOfType<WorkAxis>() != null;
        }) ||
            ids.Count(id => { cancel?.Invoke(); return id.GetObjectOfType<SketchProfile>() != null; }) != ids.Count(id => { cancel?.Invoke(); return id.GetObjectOfType<ExtrudeFeature>() != null; }) ||
            ids.Any(id =>
            {
                cancel?.Invoke(); return id.GetObjectOfType<ExtrudeFeature>() is { } f && (f.Extent != ExtrudeParamExtType.Dist ||
                !DrawingProfileGeometry.Near(f.Angle, 0) || !DrawingProfileGeometry.Near(f.Distance2, 0) || !DrawingProfileGeometry.Near(f.Angle2, 0));
            });
    }
    static bool Supported(McObjectId id) => id.GetObjectOfType<Mc3dSolid>() != null || id.GetObjectOfType<PlanarSketch>() != null ||
        id.GetObjectOfType<SketchProfile>() != null || id.GetObjectOfType<ProjectionSketchEnt>() != null || id.GetObjectOfType<ExtrudeFeature>() != null ||
        id.GetObjectOfType<RevolveFeature>() != null || id.GetObjectOfType<ExtrudeAlongPathFeature>() != null || id.GetObjectOfType<LoftFeature>() != null ||
        id.GetObjectOfType<HoleFeature>() != null || id.GetObjectOfType<ThreadFeature>() != null || id.GetObjectOfType<ThicknessFeature>() != null ||
        id.GetObjectOfType<RectangularPatternFeature>() != null || id.GetObjectOfType<CircularPatternFeature>() != null || id.GetObjectOfType<MirrorFeature>() != null ||
        id.GetObjectOfType<FilletFeature>() != null || id.GetObjectOfType<ChamferFeature>() != null || ModelPlacementHistory.IsPlacement(id) ||
        id.GetObjectOfType<WorkPlane>() != null || id.GetObjectOfType<WorkPoint>() != null || id.GetObjectOfType<WorkAxis>() != null || id.GetObjectOfType<DrawingView>() != null;
    static JsonElement Read(G.EntityGeometry geometry)
    {
        if (geometry.GeometryType == G.EntityGeomType.kLine) { using var line = geometry.LineSeg; return Wire.Element(SketchGeometryReader.Line(line)); }
        if (geometry.GeometryType == G.EntityGeomType.kCircArc) { using var arc = geometry.CircArc; return Wire.Element(SketchGeometryReader.Arc(arc)); }
        throw new DomainException("OUT_OF_SCOPE", "Only exact retained Line/Arc source storage is editable.");
    }
    static G.EntityGeometry Create(JsonElement g)
    {
        G.Point3d P(string k) { var a = g.GetProperty(k); return new(a[0].GetDouble(), a[1].GetDouble(), a[2].GetDouble()); }
        G.Vector3d V(string k) { var a = g.GetProperty(k); return new(a[0].GetDouble(), a[1].GetDouble(), a[2].GetDouble()); }
        return g.GetProperty("kind").GetString() == "line" ? new G.LineSeg3d(P("start"), P("end")) :
            new G.CircArc3d(P("center"), V("normal"), V("reference_direction"), g.GetProperty("radius").GetDouble(), g.GetProperty("start_angle").GetDouble(), g.GetProperty("end_angle").GetDouble());
    }
    static void Contour(JsonElement geometry, Action cancel)
    {
        Need(geometry.GetProperty("geometry_details_complete").GetBoolean(), "OUT_OF_SCOPE", "Contour geometry must be complete.");
        Point3 P(JsonElement p) => new(p[0].GetDouble(), p[1].GetDouble(), p[2].GetDouble());
        double Dot(Point3 a, Point3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        var frame = geometry.GetProperty("frame"); var origin = P(frame.GetProperty("origin_wcs"));
        var x = P(frame.GetProperty("x_step_wcs")); var y = P(frame.GetProperty("y_step_wcs"));
        Need(DrawingProfileGeometry.Near(Dot(x, x), 1) && DrawingProfileGeometry.Near(Dot(y, y), 1) && Math.Abs(Dot(x, y)) < 1e-9,
            "OUT_OF_SCOPE", "Contour analysis requires the reported perpendicular unit sketch basis.");
        var n = new Point3(x.Y * y.Z - x.Z * y.Y, x.Z * y.X - x.X * y.Z, x.X * y.Y - x.Y * y.X);
        Point2 Local(JsonElement p)
        {
            var w = P(p); var d = new Point3(w.X - origin.X, w.Y - origin.Y, w.Z - origin.Z);
            Need(Math.Abs(Dot(d, n)) < 1e-7, "INVALID_GEOMETRY", "Requested contour must remain in its native sketch plane."); return new(Dot(d, x), Dot(d, y));
        }
        var curves = new List<SketchConstraintCurve>();
        foreach (var e in geometry.GetProperty("elements").EnumerateArray())
        {
            cancel();
            var g = e.GetProperty("geometry"); var id = e.GetProperty("element_id").GetString()!;
            if (g.GetProperty("kind").GetString() == "line") curves.Add(new(id, "line", Local(g.GetProperty("start")), Local(g.GetProperty("end"))));
            else
            {
                Need(g.GetProperty("kind").GetString() == "circular_arc", "OUT_OF_SCOPE", "Use one closed Line/Arc contour.");
                curves.Add(ModelPolycurve.FromSketchArc(id, P(g.GetProperty("center")), g.GetProperty("radius").GetDouble(), P(g.GetProperty("normal")), P(g.GetProperty("reference_direction")), g.GetProperty("start_angle").GetDouble(), g.GetProperty("end_angle").GetDouble(), new(origin, x, y)));
            }
        }
        // Retain exact contour topology, with serialized-byte accounting and
        // cancellation inside the slow connectivity/intersection walks.
        _ = curves.Any(c => c.Kind == "arc") ? ModelPolycurve.FromCurves(curves.ToArray(), cancel) : DrawingProfileGeometry.Profile(curves.ToArray(), cancel);
    }
    internal static void Preflight(ModifyArguments args, DocumentState state, Db.Transaction tx, Action? cancel = null) => Resolve(args, state, tx, cancel ?? (() => { }));
    static Binding Resolve(ModifyArguments args, DocumentState state, Db.Transaction tx, Action cancel)
    {
        Need(args.Operations.Length > 0 && args.Operations.All(e => e is ModelSketchEdit), "INVALID_ARGUMENT", "Supply only sketch edits.");
        var edits = args.Operations.Cast<ModelSketchEdit>().ToArray();
        Need(edits.Select(e => e.Handles[0]).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1 &&
            edits.Select(e => e.SketchId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == edits.Length, "INVALID_ARGUMENT", "Edit distinct sketches of one body.");
        var db = state.Document.Database; var model = ((Db.BlockTable)tx.GetObject(db.BlockTableId, Db.OpenMode.ForRead))[Db.BlockTableRecord.ModelSpace];
        Need(db.CurrentSpaceId == model, "OUT_OF_SCOPE", "Activate Model Space."); var active = McDocument.ActiveDocument;
        Need(active != null && state.Document.IsNamedDrawing && !string.IsNullOrEmpty(active.Path) && string.Equals(Path.GetFullPath(active.Path), Path.GetFullPath(state.Document.Name), StringComparison.OrdinalIgnoreCase), "DOCUMENT_CONTEXT_MISMATCH", "Use the matching named drawing.");
        var source = CadStructureWriter.Find(db, edits[0].Handles[0]); var solid = tx.GetObject(source, Db.OpenMode.ForRead) as Db.Solid3d;
        Need(solid != null && solid.GetType() == typeof(Db.Solid3d) && solid.OwnerId == model && !solid.IsNull, "OUT_OF_SCOPE", "Select a nonempty top-level native model body.");
        var layer = (Db.LayerTableRecord)tx.GetObject(solid!.LayerId, Db.OpenMode.ForRead);
        Need(!layer.IsLocked && !layer.IsOff && !layer.IsFrozen && !layer.IsDependent, "LAYER_UNAVAILABLE", "Body layer unavailable.");
        var bodyId = McObjectId.FromHandle(source.Handle.Value, active!.ID); var body = bodyId.GetObjectOfType<Mc3dSolid>();
        Need(body != null && body.ID == bodyId && edits.All(e => Key(body.LastPartItemID).Equals(e.ExpectedLastItemId, StringComparison.OrdinalIgnoreCase)), "PRECONDITION_FAILED", "Body identity/history anchor changed.");
        var contents = ModelInventoryBudget.History(body!, cancel).ToArray();
        var views = ModelViewFollowers.Resolve(body, contents, cancel, includeStandalone: true); var external = ProjectedHistorySources.Read(body, contents, state, tx, cancel);
        ProjectedHistorySources.Healthy(contents, external); var nativeSources = NativePathSources.Read(contents, cancel);
        Need(contents!.All(id => Supported(id) || external.Any(s => s.id == Key(id)) || nativeSources.Contains(Key(id))), "OUT_OF_SCOPE", "Unknown native operations remain protected.");
        var sketches = contents.Select(id => id.GetObjectOfType<PlanarSketch>()).Where(s => s != null).ToDictionary(s => Key(s!.ID), s => s!, StringComparer.OrdinalIgnoreCase);
        foreach (var profile in contents.Select(id => id.GetObjectOfType<SketchProfile>()).Where(p => p != null))
            Need(profile!.HostSolidID == bodyId && sketches.ContainsKey(Key(profile.ParentSketchID)), "MODEL_STATE_UNAVAILABLE", "Profile owner/parent unavailable.");
        var bindings = new List<SketchBinding>(); var editedSources = new HashSet<McObjectId>();
        foreach (var edit in edits)
        {
            cancel(); Need(sketches.TryGetValue(edit.SketchId, out var sketch) && sketch!.HostSolidID == bodyId && !sketch.IsInEditMode, "OUT_OF_SCOPE", "Selected sketch is unavailable.");
            DrawingViewSketchWriteScope282.RequireSupported(sketch!);
            Need(contents.Any(id => id.GetObjectOfType<SketchProfile>() is { } p && p.ParentSketchID == sketch.ID), "OUT_OF_SCOPE", "Selected sketch has no current native profile.");
            var before = Wire.Element(SketchGeometryReader.Read(sketch!, cancel)); var desired = JsonNode.Parse(before.GetRawText())!;
            var elements = before.GetProperty("elements").EnumerateArray().ToDictionary(e => e.GetProperty("element_id").GetString()!, StringComparer.OrdinalIgnoreCase);
            var storage = new Dictionary<string, DbGeometry>(StringComparer.OrdinalIgnoreCase);
            foreach (var change in edit.Elements)
            {
                cancel(); Need(elements.TryGetValue(change.ElementId, out var item) && SketchGeometryComparison.Same(item.GetProperty("geometry"), change.ExpectedGeometry), "PRECONDITION_FAILED", "Element identity or geometry changed.");
                var projection = contents.SingleOrDefault(id => Key(id).Equals(change.ElementId, StringComparison.OrdinalIgnoreCase)).GetObjectOfType<ProjectionSketchEnt>();
                Need(projection != null && projection.HostSketchID == sketch!.ID, "SKETCH_STORAGE_UNAVAILABLE", "Only explicit retained projections are editable.");
                var owned = external.SingleOrDefault(s => s.id == Key(projection!.SrcObjectID));
                Need(owned != null && owned.projection_ids.Length == 1 && owned.projection_ids[0] == Key(projection!.ID) && owned.revolution_axis_ids.Length == 0 && owned.rotation_anchor_ids == null && !nativeSources.Contains(owned.id), "OUT_OF_SCOPE", "Shared profile/axis/path sources are protected.");
                var obj = projection!.SrcObjectID.GetObjectOfType<DbGeometry>(); Need(obj != null && obj.ID == projection.SrcObjectID && editedSources.Add(obj.ID), "SKETCH_STORAGE_UNAVAILABLE", "Source storage is unavailable or selected twice.");
                using (var g = obj!.Geometry) Need(g != null && SketchGeometryComparison.Same(Read(g), change.ExpectedGeometry), "SKETCH_STORAGE_UNAVAILABLE", "Storage must match exact reported WCS geometry; no coordinate-space guess.");
                var raw = tx.GetObject(CadStructureWriter.Find(db, owned!.handle), Db.OpenMode.ForRead); var entity = raw as Db.Entity;
                var sourceLayer = (Db.LayerTableRecord)tx.GetObject(entity!.LayerId, Db.OpenMode.ForRead);
                Need(!sourceLayer.IsLocked && !sourceLayer.IsOff && !sourceLayer.IsFrozen && !sourceLayer.IsDependent, "LAYER_UNAVAILABLE", "Source layer unavailable.");
                using (var reactors = raw.GetPersistentReactorIds()) Need(!raw.HasFields && !reactors.Cast<Db.ObjectId>().Any(id => tx.GetObject(id, Db.OpenMode.ForRead) is Db.AssocDependency), "OUT_OF_SCOPE", "Source drawing constraints/dependencies require their supported driver route.");
                storage.Add(change.ElementId, obj);
                desired["elements"]!.AsArray().Single(e => e!["element_id"]!.GetValue<string>().Equals(change.ElementId, StringComparison.OrdinalIgnoreCase))!["geometry"] = JsonNode.Parse(change.Geometry.GetRawText());
            }
            var future = Wire.Element(desired); Contour(future, cancel); bindings.Add(new(edit, sketch!, before, future, storage));
        }
        // Reject references from another body before modifying shared storage.
        foreach (Db.ObjectId other in (Db.BlockTableRecord)tx.GetObject(model, Db.OpenMode.ForRead))
        {
            cancel(); if (other == source || tx.GetObject(other, Db.OpenMode.ForRead) is not Db.Solid3d) continue;
            var otherBody = McObjectId.FromHandle(other.Handle.Value, active.ID).GetObjectOfType<Mc3dSolid>(); if (otherBody == null) continue;
            var otherIds = ModelInventoryBudget.History(otherBody, cancel);
            Need(!otherIds!.Any(id => id.GetObjectOfType<ProjectionSketchEnt>() is { } p && editedSources.Contains(p.SrcObjectID) || id.GetObjectOfType<RevolveFeature>() is { } r && editedSources.Contains(r.Axis.ID)) &&
                !NativePathSources.ReferencedIds(otherIds, cancel).Overlaps(editedSources.Select(Key)), "OUT_OF_SCOPE", "An edited source is consumed by another model body.");
        }
        var read = new ModelParameterArguments(args.BridgeId, args.DocumentId, args.ExpectedRevision, args.OperationId, args.Units, edits[0].Handles[0], "", "", 0);
        var snapshot = ModelParameterSnapshot.Capture(read, state, cancel); ModelParameterSnapshot.Healthy(snapshot);
        var changed = bindings.Select(b => Key(b.Sketch.ID)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var others = sketches.Where(p => !changed.Contains(p.Key)).ToDictionary(p => p.Key, p => Wire.Element(SketchGeometryReader.Read(p.Value, cancel)), StringComparer.OrdinalIgnoreCase);
        ModelFeatureOracle.Read(solid); return new(body, source, read, snapshot, bindings.ToArray(), others, views,
            solid.LayerId, UnrelatedInventory(state, tx, source));
    }
    static JsonElement WithoutGeometry(JsonElement value) { var node = JsonNode.Parse(value.GetRawText())!; foreach (var e in node["elements"]!.AsArray()) e!.AsObject().Remove("geometry"); return Wire.Element(node); }
    static (Db.ObjectId Source, double Volume) Verify(Binding b, DocumentState state, Action cancel)
    {
        cancel(); Db.ObjectId current; double volume;
        using (var tx = state.Document.Database.TransactionManager.StartTransaction())
        {
            current = ModelHistoryEditGuard.CurrentRepresentation(b.Body, state, tx);
            Need(b.UnrelatedInventory.SetEquals(UnrelatedInventory(state, tx, current)), "MODEL_STATE_UNAVAILABLE", "Sketch edit changed unrelated top-level entity membership.");
            var solid = (Db.Solid3d)tx.GetObject(current, Db.OpenMode.ForRead);
            Need(solid.GetType() == typeof(Db.Solid3d) && solid.LayerId == b.Layer, "MODEL_STATE_UNAVAILABLE", "Native representation type or layer changed.");
            volume = ModelFeatureOracle.Read(solid).Volume;
        }
        // Only this body's representation may change its handle. All remaining
        // top-level members are compared above; native body/history IDs stay exact.
        var after = ModelParameterSnapshot.Capture(b.Args with { Handle = Validation.NormalizeHandle(current.Handle.ToString()) }, state); ModelParameterSnapshot.Healthy(after);
        foreach (var field in new[] { "variables", "body", "last_item", "owner", "native_source_ids" })
            Need(ModelParameterSnapshot.Equivalent(b.Before.GetProperty(field), after.GetProperty(field)), "MODEL_STATE_UNAVAILABLE", "Sketch edit changed protected " + field + ".");
        JsonElement Nodes(JsonElement s)
        {
            var a = JsonNode.Parse(s.GetProperty("nodes").GetRawText())!.AsArray(); foreach (var n in a)
            {
                var p = n!["parameters"] as JsonObject; if (p?["kind"]?.GetValue<string>() is "fillet" or "chamfer")
                {
                    p["edge_count"] = p["edge_ids"]?.AsArray().Count; p.Remove("edge_ids");
                }
            }
            return Wire.Element(a);
        }
        Need(ModelParameterSnapshot.Equivalent(Nodes(b.Before), Nodes(after)), "MODEL_STATE_UNAVAILABLE", "Native history/bindings or unaffected feature parameters changed.");
        var selected = b.Sketches.SelectMany(s => s.Storage.Values).Select(o => Key(o.ID)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        JsonElement Sources(JsonElement s) { var a = JsonNode.Parse(s.GetProperty("external_sources").GetRawText())!.AsArray(); foreach (var e in a) if (selected.Contains(e!["id"]!.GetValue<string>())) e!.AsObject().Remove("geometry"); return Wire.Element(a); }
        Need(ModelParameterSnapshot.Equivalent(Sources(b.Before), Sources(after)), "MODEL_STATE_UNAVAILABLE", "Unselected source geometry or projection bindings changed.");
        foreach (var sketch in b.Sketches)
        {
            var actual = Wire.Element(SketchGeometryReader.Read(sketch.Sketch, cancel)); var elements = actual.GetProperty("elements").EnumerateArray().ToDictionary(e => e.GetProperty("element_id").GetString()!, StringComparer.OrdinalIgnoreCase);
            foreach (var e in sketch.Desired.GetProperty("elements").EnumerateArray()) Need(elements.TryGetValue(e.GetProperty("element_id").GetString()!, out var found) && SketchGeometryComparison.Same(e.GetProperty("geometry"), found.GetProperty("geometry")), "NATIVE_GEOMETRY_MISMATCH", "Edited sketch differs from requested WCS geometry.");
            Need(ModelParameterSnapshot.Equivalent(WithoutGeometry(sketch.Desired), WithoutGeometry(actual)), "MODEL_STATE_UNAVAILABLE", "Sketch identities/frame or metadata changed.");
        }
        var ids = ModelInventoryBudget.History(b.Body, cancel);
        foreach (var pair in b.OtherSketches) { var sketch = ids.Single(id => Key(id) == pair.Key).GetObjectOfType<PlanarSketch>(); Need(sketch != null && ModelParameterSnapshot.Equivalent(pair.Value, Wire.Element(SketchGeometryReader.Read(sketch!, cancel))), "MODEL_STATE_UNAVAILABLE", "Another sketch changed."); }
        ModelViewFollowers.Verify(b.Body, b.Views, cancel, refreshStandalone: true);
        return (current, volume);
    }
    internal static Result Execute(ModifyArguments args, DocumentState state, WriteJob job, ReadContext context, JsonLog log)
    {
        McUndoPoint? tracker = null; bool started = false; string stage = "preparation";
        void Close() { var t = tracker; tracker = null; if (t != null) try { t.Stop(); } finally { t.Dispose(); } }
        try
        {
            Binding b; double before; using (var tx = state.Document.Database.TransactionManager.StartTransaction())
            {
                b = Resolve(args, state, tx, job.CheckCancellation); before = ModelFeatureOracle.Read((Db.Solid3d)tx.GetObject(b.Source, Db.OpenMode.ForRead)).Volume;
            }
            ModelViewFollowers.CheckStandalone(b.Body, b.Views, job.CheckCancellation); tracker = new McUndoPoint(false); tracker.Start(); job.CheckCancellation(); stage = "source_geometry_write";
            foreach (var binding in b.Sketches) foreach (var edit in binding.Edit.Elements) { using var geometry = Create(edit.Geometry); started = true; binding.Storage[edit.ElementId].Geometry = geometry; binding.Storage[edit.ElementId].DbEntity.Update(); }
            stage = "native_rebuild"; McObjectManager.UpdateAll(); Need(b.Body.RebuildPart(), "MODEL_UPDATE_FAILED", "Native body rebuild rejected the edited profile."); McObjectManager.UpdateAll();
            stage = "verify"; Verify(b, state, job.CheckCancellation); job.BeginCommit(); Close(); var final = Verify(b, state, job.CheckCancellation);
            var bodyRef = new EntityRef(args.BridgeId, args.DocumentId, Validation.NormalizeHandle(final.Source.Handle.ToString()));
            var oldRef = new EntityRef(args.BridgeId, args.DocumentId, args.Operations[0].Handles[0]); bool replaced = oldRef.handle != bodyRef.handle;
            double after = final.Volume;
            var changes = b.Sketches.Select(s => new ModelSketchChange(bodyRef, Key(s.Sketch.ID), s.Edit.Elements.Select(e => e.ElementId).ToArray(), before, after)).ToArray();
            var result = Result.Ok(job.OriginalRequestId, new ModifiedEntities(args.OperationId, "committed", false, [bodyRef], replaced ? [oldRef] : [], replaced ? [bodyRef] : [], [], "not_guaranteed", model_sketch_changes: changes), context, WriteRecovery.Warning,
                "MODEL_SKETCH_GEOMETRY_CHANGED: Retained sources and native consumers rebuilt together. Refresh history/topology/views and review the shape. Existing identities/driver expressions checked; internal sketch constraints were not created or solved.");
            result = ModelViewFollowers.WithWarnings(result, b.Views); job.RecordCommit(result); return result;
        }
        catch (Exception error)
        {
            if (job.CommittedResult is { } committed) return committed; Exception? cleanup = null; try { Close(); } catch (Exception e) { cleanup = e; }
            string cause = error is DomainException d ? d.Error.code : error is OperationCanceledException ? "CANCELLED" : "MODEL_SKETCH_FAILED"; bool unknown = started || cleanup != null;
            return job.Error(unknown ? "WRITE_OUTCOME_UNKNOWN" : cause, unknown ? "Sketch source edit is unverified. Inspect the body/history before dependent work." : error.Message,
                unknown ? "unknown" : "not_started", context, new { cause, stage, message = error.Message, verification_details = (error as DomainException)?.Error.details, cleanup_error = cleanup?.Message });
        }
    }
}
