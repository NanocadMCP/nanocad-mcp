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

// Change existing storage only. AddGeometry explicitly requires a new ID and
// is never used as a replacement setter. No delete/recreate or edit mode.
internal static class ModelSketchWriter
{
    private static void Need(bool ok, string code, string message) { if (!ok) throw new DomainException(code, message); }
    private static string Key(McObjectId id) => id.ToString();
    internal static bool Handles(ModifyArguments args) => args.Operations.Any(e => e is ModelSketchEdit);
    private sealed record Binding(Mc3dSolid Body, PlanarSketch Sketch, Db.ObjectId Source, ModelParameterArguments SnapshotArgs,
        JsonElement Before, JsonElement Geometry, JsonElement Desired, Dictionary<string, DbGeometry> Storage,
        DrawingProfileWriter.TailFeature[] Features, Dictionary<string, JsonElement> OtherSketches, McObjectId[] Finishes,
        ModelViewFollowers.Snapshot Views);
    internal static void Preflight(ModifyArguments args, DocumentState state, Db.Transaction tx, Action? cancel = null)
    {
        if (ModelSketchCompositionWriter.Handles(args, state, tx, cancel)) { ModelSketchCompositionWriter.Preflight(args, state, tx, cancel); return; }
        if (args.Operations.Length > 1) { ResolveBatch(args, state, tx, cancel ?? (() => { })); return; }
        Need(args.Operations.Length == 1 && args.Operations[0] is ModelSketchEdit, "INVALID_ARGUMENT", "One sketch edit must be the sole operation.");
        Resolve(args, state, tx, cancel ?? (() => { }));
    }
    private static JsonElement Read(G.EntityGeometry geometry)
    {
        if (geometry.GeometryType == G.EntityGeomType.kLine) { using var line = geometry.LineSeg; return Wire.Element(SketchGeometryReader.Line(line)); }
        if (geometry.GeometryType == G.EntityGeomType.kCircArc) { using var arc = geometry.CircArc; return Wire.Element(SketchGeometryReader.Arc(arc)); }
        throw new DomainException("OUT_OF_SCOPE", "Only exact line/circular-arc storage is editable.");
    }
    private static G.EntityGeometry Create(JsonElement geometry)
    {
        G.Point3d P(string key) { var a = geometry.GetProperty(key); return new(a[0].GetDouble(), a[1].GetDouble(), a[2].GetDouble()); }
        G.Vector3d V(string key) { var a = geometry.GetProperty(key); return new(a[0].GetDouble(), a[1].GetDouble(), a[2].GetDouble()); }
        if (geometry.GetProperty("kind").GetString() == "line") return new G.LineSeg3d(P("start"), P("end"));
        return new G.CircArc3d(P("center"), V("normal"), V("reference_direction"), geometry.GetProperty("radius").GetDouble(), geometry.GetProperty("start_angle").GetDouble(), geometry.GetProperty("end_angle").GetDouble());
    }
    private static Binding Resolve(ModifyArguments args, DocumentState state, Db.Transaction tx, Action cancel)
    {
        var edit = (ModelSketchEdit)args.Operations.Single(); var db = state.Document.Database; var model = ((Db.BlockTable)tx.GetObject(db.BlockTableId, Db.OpenMode.ForRead))[Db.BlockTableRecord.ModelSpace];
        Need(db.CurrentSpaceId == model, "OUT_OF_SCOPE", "Activate Model Space."); var active = McDocument.ActiveDocument;
        Need(active != null && state.Document.IsNamedDrawing && !string.IsNullOrEmpty(active.Path) && string.Equals(Path.GetFullPath(active.Path), Path.GetFullPath(state.Document.Name), StringComparison.OrdinalIgnoreCase), "DOCUMENT_CONTEXT_MISMATCH", "Use the matching named native document.");
        var source = CadStructureWriter.Find(db, edit.Handles.Single()); var solid = tx.GetObject(source, Db.OpenMode.ForRead) as Db.Solid3d;
        Need(solid != null && solid.GetType() == typeof(Db.Solid3d) && solid.OwnerId == model, "OUT_OF_SCOPE", "Select a top-level native Mc3dSolid representation.");
        var layer = (Db.LayerTableRecord)tx.GetObject(solid!.LayerId, Db.OpenMode.ForRead); Need(!layer.IsLocked && !layer.IsFrozen && !layer.IsOff && !layer.IsDependent, "LAYER_UNAVAILABLE", "Body layer is unavailable.");
        var bodyId = McObjectId.FromHandle(source.Handle.Value, active!.ID); var body = bodyId.GetObjectOfType<Mc3dSolid>(); Need(body != null && body.ID == bodyId, "OBJECT_IDENTITY_MISMATCH", "Body identity unavailable.");
        Need(Key(body!.LastPartItemID).Equals(edit.ExpectedLastItemId, StringComparison.OrdinalIgnoreCase), "PRECONDITION_FAILED", "Read the current last_item_id.");
        var contents = ModelInventoryBudget.History(body, cancel); var ids = contents.ToArray();
        var views = ModelViewFollowers.Resolve(body, ids, cancel, includeStandalone: true);
        var external = ProjectedHistorySources.Read(body, ids, state, tx, cancel); ProjectedHistorySources.Healthy(ids, external);
        var sketches = ids.Select(id => id.GetObjectOfType<PlanarSketch>()).Where(s => s != null).ToDictionary(s => Key(s!.ID), s => s!, StringComparer.OrdinalIgnoreCase);
        Need(sketches.TryGetValue(edit.SketchId, out var sketch) && sketch!.HostSolidID == body.ID && !sketch.IsInEditMode, "OUT_OF_SCOPE", "Selected sketch is not an available member of this body.");
        DrawingViewSketchWriteScope282.RequireSupported(sketch!);
        var profiles = ids.Select(id => id.GetObjectOfType<SketchProfile>()).Where(p => p != null).ToDictionary(p => p!.ID, p => p!);
        var placements = ModelPlacementHistory.Ids(ids);
        var features = ids.Select(id => id.GetObjectOfType<ExtrudeFeature>()).Where(f => f != null).ToDictionary(f => Key(f!.ID), f => f!);
        var finishing = ids.Where(id => id.GetObjectOfType<FilletFeature>() != null || id.GetObjectOfType<ChamferFeature>() != null).ToArray();
        var followers = ids.Where(id => id.GetObjectOfType<ThicknessFeature>() != null || id.GetObjectOfType<RectangularPatternFeature>() != null).ToArray();
        foreach (var id in finishing.Concat(followers))
        {
            cancel();
            var item = id.GetObjectOfType<HistoryItem>();
            Need(item != null && item.HostSolidID == body.ID && !item.IsSuppressed && item.Status.ToString() == "Ok", "MODEL_STATE_UNAVAILABLE", "Downstream feature must be healthy and owned by this body.");
            if (id.GetObjectOfType<ThicknessFeature>() is { } shell)
                Need(shell.SurfaceId.IsNull && shell.Direction == FeatureExtentDirection.Negative, "OUT_OF_SCOPE", "This profile-edit route supports inward solid shells; surface offsets require their own geometry route.");
            if (id.GetObjectOfType<RectangularPatternFeature>() is { } pattern)
                ModelInventoryBudget.Pattern(pattern, cancel);
        }
        Need(features.Count >= 1 && profiles.Count == features.Count && sketches.Count == features.Count, "OUT_OF_SCOPE", "Each straight extrusion must retain one distinct sketch and profile.");
        var allowed = sketches.Values.Select(s => s.ID).Concat(profiles.Keys).Concat(features.Values.Select(f => f.ID)).Append(body.ID).Concat(ids.Where(id => id.GetObjectOfType<ProjectionSketchEnt>() != null)).Concat(placements).Concat(ids.Where(id => external.Any(s => Key(id) == s.id))).Concat(finishing).Concat(followers).Concat(views.ViewIds).ToHashSet();
        Need(allowed.SetEquals(ids), "OUT_OF_SCOPE", "Additional operations are outside the verified extrusion-chain scope.");
        foreach (var f in features.Values) { DrawingProfileWriter.StraightExtrusion(f, body); Need(profiles.ContainsKey(f.ProfileID), "OUT_OF_SCOPE", "Feature profile binding unavailable."); }
        Need(features.Values.Select(f => f.ProfileID).Distinct().Count() == profiles.Count && profiles.Values.All(p => sketches.ContainsKey(Key(p.ParentSketchID)) && p.HostSolidID == body.ID) && profiles.Values.Select(p => p.ParentSketchID).Distinct().Count() == sketches.Count, "OUT_OF_SCOPE", "Sketch/profile bindings must be one-to-one.");
        var links = ids.Select(id => { var h = id.GetObjectOfType<HistoryItem>(); var prev = h?.OrderObjBefore ?? McObjectId.Null; return new ModelSequenceLink(Key(id), prev.IsNull ? null : Key(prev)); }).ToArray();
        var last = views.LastModelItem;
        Need(!last.IsNull, "MODEL_ORDER_UNAVAILABLE", "Finished body has no predecessor before its terminal edge treatment.");
        var chain = features.Keys.Concat(placements.Select(Key)).Concat(finishing.Select(Key)).Concat(followers.Select(Key)).ToArray();
        var orders = new List<string[]>(); foreach (var root in features.Keys) try { cancel(); orders.Add(ModelFeatureSequence.FromHistory(links, chain, root, Key(last), cancel)); } catch (DomainException e) when (e.Error.code == "MODEL_ORDER_UNAVAILABLE") { }
        Need(orders.Count == 1 && features[orders[0][0]].PartOperation == PartFeatureOperation.Join, "MODEL_ORDER_UNAVAILABLE", "One ordered additive base and following cut/join chain required.");
        var geometry = Wire.Element(SketchGeometryReader.Read(sketch!, cancel)); var desired = JsonNode.Parse(geometry.GetRawText())!;
        var current = geometry.GetProperty("elements").EnumerateArray().ToDictionary(e => e.GetProperty("element_id").GetString()!, StringComparer.OrdinalIgnoreCase);
        var storage = new Dictionary<string, DbGeometry>(StringComparer.OrdinalIgnoreCase);
        var nativeGeometry = sketch!.GetAllItemsGeometry(false); Need(nativeGeometry != null, "SKETCH_GEOMETRY_UNAVAILABLE", "Sketch geometry is unavailable.");
        try
        {
            foreach (var e in edit.Elements)
            {
                cancel(); Need(current.TryGetValue(e.ElementId, out var item) && SketchGeometryComparison.Same(item.GetProperty("geometry"), e.ExpectedGeometry), "PRECONDITION_FAILED", "Element identity or expected WCS geometry changed.");
                var nativeId = nativeGeometry!.Keys.Single(k => Key(k).Equals(e.ElementId, StringComparison.OrdinalIgnoreCase)); var projection = nativeId.GetObjectOfType<ProjectionSketchEnt>();
                Need(projection != null && projection.HostSketchID == sketch!.ID && ids.Contains(projection.SrcObjectID), "SKETCH_STORAGE_UNAVAILABLE", "The element must have an explicit retained source projection; anonymous geometry is protected.");
                var storageId = projection!.SrcObjectID; var obj = storageId.GetObjectOfType<DbGeometry>();
                Need(obj != null && obj.ID == storageId, "SKETCH_STORAGE_UNAVAILABLE", "The projection source has no supported editable DbGeometry storage.");
                using var stored = obj!.Geometry; var observed = stored == null ? JsonSerializer.SerializeToElement<object?>(null) : Read(stored);
                if (stored == null || !SketchGeometryComparison.Same(observed, e.ExpectedGeometry)) throw new DomainException("SKETCH_STORAGE_UNAVAILABLE", "Storage geometry is not geometrically equal to reported WCS geometry; no coordinate-space guess.", details: new { element_id = e.ElementId, source_id = Key(storageId), observed, expected = e.ExpectedGeometry });
                storage.Add(e.ElementId, obj);
                var owned = external.SingleOrDefault(s => s.id == Key(storageId));
                Need(owned != null && owned.projection_ids.Length == 1 && owned.projection_ids[0] == Key(projection.ID) && owned.revolution_axis_ids.Length == 0, "OUT_OF_SCOPE", "The source must have one projection in this body's selected sketch and no axis consumer.");
                var sourceObject = tx.GetObject(CadStructureWriter.Find(db, owned!.handle), Db.OpenMode.ForRead);
                var sourceLayer = (Db.LayerTableRecord)tx.GetObject(((Db.Entity)sourceObject).LayerId, Db.OpenMode.ForRead); Need(!sourceLayer.IsLocked && !sourceLayer.IsFrozen && !sourceLayer.IsOff && !sourceLayer.IsDependent, "LAYER_UNAVAILABLE", "Profile-source layer is unavailable.");
                using (var reactors = sourceObject.GetPersistentReactorIds()) Need(!sourceObject.HasFields && !reactors.Cast<Db.ObjectId>().Any(id => tx.GetObject(id, Db.OpenMode.ForRead) is Db.AssocDependency), "OUT_OF_SCOPE", "Associative drawing constraints/dependencies require their source-driven route.");
                var node = desired["elements"]!.AsArray().Single(n => n!["element_id"]!.GetValue<string>().Equals(e.ElementId, StringComparison.OrdinalIgnoreCase))!; node["geometry"] = JsonNode.Parse(e.Geometry.GetRawText());
            }
        }
        finally { if (nativeGeometry != null) foreach (var v in nativeGeometry.Values.Distinct(ReferenceEqualityComparer.Instance)) if (v is IDisposable d) d.Dispose(); }
        var future = Wire.Element(desired); var ordered = orders[0].Where(key => features.ContainsKey(key) || placements.Any(p => Key(p) == key)).Select(key => { if (!features.TryGetValue(key, out var f)) return ModelPlacementHistory.Step(placements.Single(p => Key(p) == key)); return DrawingProfileWriter.ReadTail(f, profiles[f.ProfileID].ParentSketchID, cancel); }).ToArray();
        // Validate the entire changed contour now, including joins and arc sweeps.
        var changed = features.Values.Single(f => profiles[f.ProfileID].ParentSketchID == sketch.ID); DrawingProfileWriter.ReadTail(changed, sketch.ID, cancel, future);
        var snapshotArgs = new ModelParameterArguments(args.BridgeId, args.DocumentId, args.ExpectedRevision, args.OperationId, args.Units, edit.Handles[0], "", "", 0);
        var before = ModelParameterSnapshot.Capture(snapshotArgs, state, cancel); ModelParameterSnapshot.Healthy(before);
        var others = sketches.Values.Where(s => s.ID != sketch.ID).ToDictionary(s => Key(s.ID), s => Wire.Element(SketchGeometryReader.Read(s, cancel)));
        ModelFeatureOracle.Read(solid);
        return new(body, sketch, source, snapshotArgs, before, geometry, future, storage, ordered, others, finishing, views);
    }
    private static Binding[] ResolveBatch(ModifyArguments args, DocumentState state, Db.Transaction tx, Action cancel)
    {
        Need(args.Operations.Length >= 2 && args.Operations.All(e => e is ModelSketchEdit),
            "INVALID_ARGUMENT", "Batch requires model sketch edits and no other operations.");
        var edits = args.Operations.Cast<ModelSketchEdit>().ToArray();
        Need(edits.Select(e => e.Handles.Single()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1 &&
             edits.Select(e => e.SketchId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == edits.Length,
             "INVALID_ARGUMENT", "Batch must edit distinct sketches of one body.");
        // The complete request is already checked by the transport parser.
        // Account each selected edit locally before resolving any native state;
        // then every Resolve captures and budgets the full retained body state.
        var batchBudget = new ModelInventoryBudget(cancel); foreach (var edit in edits) batchBudget.Add(edit);
        var bindings = edits.Select(edit => Resolve(args with { Operations = new EditSpec[] { edit } }, state, tx, cancel)).ToArray();
        var first = bindings[0];
        Need(bindings.All(b => b.Finishes.SequenceEqual(first.Finishes) && b.Body.ID == first.Body.ID && b.Source == first.Source &&
             ModelParameterSnapshot.Equivalent(b.Before, first.Before) &&
             ModelParameterSnapshot.Equivalent(b.Views.Metadata, first.Views.Metadata) &&
             b.Features.Select(f => Key(f.Feature)).SequenceEqual(first.Features.Select(f => Key(f.Feature)))),
             "OUT_OF_SCOPE", "Batch requires one unchanged native history and the same downstream features.");
        Need(bindings.SelectMany(b => b.Storage.Values.Select(obj => Key(obj.ID))).Distinct(StringComparer.OrdinalIgnoreCase).Count() ==
             bindings.Sum(b => b.Storage.Count),
             "OUT_OF_SCOPE", "A projected source may be edited only once in the batch.");
        return bindings;
    }
    private static Result ExecuteBatch(ModifyArguments args, DocumentState state, WriteJob job, ReadContext context, JsonLog log)
    {
        McUndoPoint? tracker = null; bool started = false; string stage = "preparation";
        void Trace(string value) { stage = value; log.Write(job.Operation, job.OriginalRequestId, "model_sketch_batch_checkpoint", transaction: value, operationId: job.OperationId); }
        void Close() { var t = tracker; tracker = null; if (t == null) return; try { t.Stop(); } finally { t.Dispose(); } }
        try
        {
            Binding[] bindings; ModelFeatureOracle.Metrics original;
            using (var tx = state.Document.Database.TransactionManager.StartTransaction())
            {
                bindings = ResolveBatch(args, state, tx, job.CheckCancellation);
                original = ModelFeatureOracle.Read((Db.Solid3d)tx.GetObject(bindings[0].Source, Db.OpenMode.ForRead));
            }
            Trace("edited_contours_validated");
            foreach (var value in bindings) ModelViewFollowers.CheckStandalone(value.Body, value.Views, job.CheckCancellation); tracker = new McUndoPoint(false); tracker.Start(); job.CheckCancellation();
            foreach (var binding in bindings)
                foreach (var edit in args.Operations.Cast<ModelSketchEdit>().Single(e => e.SketchId.Equals(Key(binding.Sketch.ID), StringComparison.OrdinalIgnoreCase)).Elements)
                {
                    Trace("geometry_set_before"); using var g = Create(edit.Geometry); started = true;
                    binding.Storage[edit.ElementId].Geometry = g; binding.Storage[edit.ElementId].DbEntity.Update();
                }
            Trace("update_before"); McObjectManager.UpdateAll(); Trace("update_returned");
            ModelFeatureOracle.Metrics Verify()
            {
                job.CheckCancellation(); var first = bindings[0];
                var after = ModelParameterSnapshot.Capture(first.SnapshotArgs, state, job.CheckCancellation); ModelParameterSnapshot.Healthy(after);
                foreach (var field in new[] { "handles", "variables", "body", "last_item", "owner" })
                    Need(ModelParameterSnapshot.Equivalent(first.Before.GetProperty(field), after.GetProperty(field)),
                        "MODEL_STATE_UNAVAILABLE", "Batch changed protected " + field + ".");
                JsonElement ProtectedNodes(JsonElement snapshot)
                {
                    var nodes = JsonNode.Parse(snapshot.GetProperty("nodes").GetRawText())!.AsArray();
                    if (first.Finishes.Length > 0)
                        foreach (var node in nodes)
                            if (first.Finishes.Any(id => node!["mc_id"]!.GetValue<string>().Equals(Key(id), StringComparison.OrdinalIgnoreCase)))
                            { var p = node!["parameters"]!.AsObject(); p["edge_count"] = p["edge_ids"]?.AsArray().Count; p.Remove("edge_ids"); }
                    return Wire.Element(nodes);
                }
                Need(ModelParameterSnapshot.Equivalent(ProtectedNodes(first.Before), ProtectedNodes(after)),
                    "MODEL_STATE_UNAVAILABLE", "Batch changed protected native history nodes.");
                var editedSources = bindings.SelectMany(b => b.Storage.Values).Select(obj => Key(obj.ID)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                JsonElement ProtectedSources(JsonElement snapshot)
                {
                    var array = JsonNode.Parse(snapshot.GetProperty("external_sources").GetRawText())!.AsArray();
                    foreach (var entry in array)
                        if (editedSources.Contains(entry!["id"]!.GetValue<string>())) entry!.AsObject().Remove("geometry");
                    return Wire.Element(array);
                }
                Need(ModelParameterSnapshot.Equivalent(ProtectedSources(first.Before), ProtectedSources(after)),
                    "MODEL_STATE_UNAVAILABLE", "Unselected source geometry or projection bindings changed.");
                var changes = bindings.ToDictionary(b => Key(b.Sketch.ID), StringComparer.OrdinalIgnoreCase);
                var old = new Dictionary<string, JsonElement>(first.OtherSketches, StringComparer.OrdinalIgnoreCase)
                { [Key(first.Sketch.ID)] = first.Geometry };
                foreach (var feature in first.Features)
                {
                    if (feature.Sketch.IsNull) continue;
                    var id = Key(feature.Sketch);
                    var actual = Wire.Element(SketchGeometryReader.Read(feature.Sketch.GetObjectOfType<PlanarSketch>()!, job.CheckCancellation));
                    if (!changes.TryGetValue(id, out var binding))
                    {
                        Need(ModelParameterSnapshot.Equivalent(old[id], actual),
                            "MODEL_STATE_UNAVAILABLE", "Unselected model sketch changed.");
                        continue;
                    }
                    var entries = actual.GetProperty("elements").EnumerateArray()
                        .ToDictionary(e => e.GetProperty("element_id").GetString()!, StringComparer.OrdinalIgnoreCase);
                    foreach (var item in binding.Desired.GetProperty("elements").EnumerateArray())
                        Need(entries.TryGetValue(item.GetProperty("element_id").GetString()!, out var found) &&
                             SketchGeometryComparison.Same(item.GetProperty("geometry"), found.GetProperty("geometry")),
                             "NATIVE_GEOMETRY_MISMATCH", "Edited sketch differs from requested WCS geometry.");
                    JsonElement WithoutGeometry(JsonElement source)
                    {
                        var node = JsonNode.Parse(source.GetRawText())!;
                        foreach (var element in node["elements"]!.AsArray()) element!.AsObject().Remove("geometry");
                        return Wire.Element(node);
                    }
                    Need(ModelParameterSnapshot.Equivalent(WithoutGeometry(binding.Desired), WithoutGeometry(actual)),
                        "MODEL_STATE_UNAVAILABLE", "Sketch identity or metadata changed.");
                }
                using var tx = state.Document.Database.TransactionManager.StartTransaction();
                var observed = ModelFeatureOracle.Read((Db.Solid3d)tx.GetObject(first.Source, Db.OpenMode.ForRead));
                ModelViewFollowers.Verify(first.Body, first.Views, job.CheckCancellation, refreshStandalone: true);
                return observed;
            }
            Verify(); Trace("geometry_and_history_verified"); job.BeginCommit(); Close(); var final = Verify();
            var bodyRef = new EntityRef(args.BridgeId, args.DocumentId, args.Operations[0].Handles[0]);
            var changes = bindings.Select(b => new ModelSketchChange(bodyRef, Key(b.Sketch.ID),
                args.Operations.Cast<ModelSketchEdit>().Single(e => e.SketchId.Equals(Key(b.Sketch.ID), StringComparison.OrdinalIgnoreCase))
                    .Elements.Select(e => e.ElementId).ToArray(), original.Volume, final.Volume)).ToArray();
            var result = Result.Ok(job.OriginalRequestId, new ModifiedEntities(args.OperationId, "committed", false,
                new[] { bodyRef }, Array.Empty<EntityRef>(), Array.Empty<EntityRef>(), Array.Empty<CopyMapping>(),
                "not_guaranteed", model_sketch_changes: changes), context, WriteRecovery.Warning,
                "MODEL_SKETCH_BATCH_CHANGED: One body and all selected sketches rebuilt together. Refresh history, topology and views. MCS constraints were not created or solved.");
            result = ModelViewFollowers.WithWarnings(result, bindings.Select(value => value.Views).ToArray()); job.RecordCommit(result); Trace("commit_recorded"); return result;
        }
        catch (Exception error)
        {
            if (job.CommittedResult is { } committed) return committed;
            Exception? cleanup = null; try { Close(); } catch (Exception e) { cleanup = e; }
            bool unknown = started || cleanup != null;
            string code = error is DomainException d ? d.Error.code : error is OperationCanceledException ? "CANCELLED" : "MODEL_SKETCH_FAILED";
            return job.Error(unknown ? "WRITE_OUTCOME_UNKNOWN" : code,
                unknown ? "Sketch batch is unverified; inspect this model and refresh its history before another dependent edit." : "Sketch batch did not start.",
                unknown ? "unknown" : "not_started", context,
                new { cause = code, stage, message = error.Message, verification_details = (error as DomainException)?.Error.details, cleanup_error = cleanup?.Message });
        }
    }
    internal static Result Execute(ModifyArguments args, DocumentState state, WriteJob job, ReadContext context, JsonLog log)
    {
        bool composed;
        using (var tx = state.Document.Database.TransactionManager.StartTransaction())
            composed = ModelSketchCompositionWriter.Handles(args, state, tx, job.CheckCancellation);
        if (composed) return ModelSketchCompositionWriter.Execute(args, state, job, context, log);
        if (args.Operations.Length > 1) return ExecuteBatch(args, state, job, context, log);
        McUndoPoint? tracker = null; bool started = false; string stage = "preparation"; Binding? b = null;
        void Trace(string value) { stage = value; log.Write(job.Operation, job.OriginalRequestId, "model_sketch_checkpoint", transaction: value, operationId: job.OperationId); }
        void Close() { var t = tracker; tracker = null; if (t == null) return; try { t.Stop(); } finally { t.Dispose(); } }
        try
        {
            var edit = (ModelSketchEdit)args.Operations.Single(); ModelFeatureOracle.Metrics original;
            using (var tx = state.Document.Database.TransactionManager.StartTransaction()) { b = Resolve(args, state, tx, job.CheckCancellation); original = ModelFeatureOracle.Read((Db.Solid3d)tx.GetObject(b.Source, Db.OpenMode.ForRead)); }
            Trace("edited_contour_validated");
            ModelViewFollowers.CheckStandalone(b.Body, b.Views, job.CheckCancellation); tracker = new McUndoPoint(false); tracker.Start(); job.CheckCancellation();
            foreach (var e in edit.Elements)
            { Trace("geometry_set_before"); using var g = Create(e.Geometry); started = true; b.Storage[e.ElementId].Geometry = g; b.Storage[e.ElementId].DbEntity.Update(); }
            Trace("update_before"); McObjectManager.UpdateAll(); Trace("update_returned");
            ModelFeatureOracle.Metrics Verify()
            {
                job.CheckCancellation(); var after = ModelParameterSnapshot.Capture(b.SnapshotArgs, state, job.CheckCancellation); ModelParameterSnapshot.Healthy(after);
                foreach (var field in new[] { "handles", "variables", "body", "last_item", "owner" }) Need(ModelParameterSnapshot.Equivalent(b.Before.GetProperty(field), after.GetProperty(field)), "MODEL_STATE_UNAVAILABLE", "Sketch edit changed protected " + field + ".");
                JsonElement ProtectedNodes(JsonElement snapshot)
                {
                    var nodes = JsonNode.Parse(snapshot.GetProperty("nodes").GetRawText())!.AsArray();
                    if (b.Finishes.Length > 0)
                        foreach (var node in nodes)
                            if (b.Finishes.Any(id => node!["mc_id"]!.GetValue<string>().Equals(Key(id), StringComparison.OrdinalIgnoreCase)))
                            { var p = node!["parameters"]!.AsObject(); p["edge_count"] = p["edge_ids"]?.AsArray().Count; p.Remove("edge_ids"); }
                    return Wire.Element(nodes);
                }
                Need(ModelParameterSnapshot.Equivalent(ProtectedNodes(b.Before), ProtectedNodes(after)),
                    "MODEL_STATE_UNAVAILABLE", "Sketch edit changed protected model history.");
                JsonElement ProtectedSources(JsonElement snapshot)
                {
                    var array = JsonNode.Parse(snapshot.GetProperty("external_sources").GetRawText())!.AsArray();
                    foreach (var entry in array) if (b.Storage.Values.Any(obj => Key(obj.ID).Equals(entry!["id"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase))) entry!.AsObject().Remove("geometry");
                    return Wire.Element(array);
                }
                Need(ModelParameterSnapshot.Equivalent(ProtectedSources(b.Before), ProtectedSources(after)), "MODEL_STATE_UNAVAILABLE", "Unselected source geometry or source bindings changed.");
                var sketchAfter = Wire.Element(SketchGeometryReader.Read(b.Sketch, job.CheckCancellation));
                var actualElements = sketchAfter.GetProperty("elements").EnumerateArray().ToDictionary(e => e.GetProperty("element_id").GetString()!, StringComparer.OrdinalIgnoreCase);
                foreach (var entry in b.Desired.GetProperty("elements").EnumerateArray()) Need(actualElements.TryGetValue(entry.GetProperty("element_id").GetString()!, out var actual) && SketchGeometryComparison.Same(entry.GetProperty("geometry"), actual.GetProperty("geometry")), "NATIVE_GEOMETRY_MISMATCH", "Sketch element geometry differs from the requested shape.");
                JsonElement WithoutGeometry(JsonElement s) { var n = JsonNode.Parse(s.GetRawText())!; foreach (var e in n["elements"]!.AsArray()) e!.AsObject().Remove("geometry"); return Wire.Element(n); }
                Need(ModelParameterSnapshot.Equivalent(WithoutGeometry(b.Desired), WithoutGeometry(sketchAfter)), "MODEL_STATE_UNAVAILABLE", "Sketch frame, metadata or element identities changed.");
                foreach (var pair in b.OtherSketches) { var id = b.Features.Single(f => Key(f.Sketch) == pair.Key).Sketch; Need(ModelParameterSnapshot.Equivalent(pair.Value, Wire.Element(SketchGeometryReader.Read(id.GetObjectOfType<PlanarSketch>()!, job.CheckCancellation))), "MODEL_STATE_UNAVAILABLE", "Another sketch changed."); }
                using var tx = state.Document.Database.TransactionManager.StartTransaction(); var observed = ModelFeatureOracle.Read((Db.Solid3d)tx.GetObject(b.Source, Db.OpenMode.ForRead));
                ModelViewFollowers.Verify(b.Body, b.Views, job.CheckCancellation, refreshStandalone: true);
                return observed;
            }
            Verify(); Trace("geometry_and_history_verified"); job.BeginCommit(); Close(); var final = Verify(); var bodyRef = new EntityRef(args.BridgeId, args.DocumentId, edit.Handles[0]);
            var result = Result.Ok(job.OriginalRequestId, new ModifiedEntities(args.OperationId, "committed", false, new[] { bodyRef }, Array.Empty<EntityRef>(), Array.Empty<EntityRef>(), Array.Empty<CopyMapping>(), "not_guaranteed", model_sketch_changes: new[] { new ModelSketchChange(bodyRef, edit.SketchId, edit.Elements.Select(e => e.ElementId).ToArray(), original.Volume, final.Volume) }), context, WriteRecovery.Warning, "MODEL_SKETCH_GEOMETRY_CHANGED: Existing identities and parameter values/expressions retained. Refresh history, topology and views. This direct geometry edit does not create or solve sketch constraints.");
            result = ModelViewFollowers.WithWarnings(result, b.Views); job.RecordCommit(result); Trace("commit_recorded"); return result;
        }
        catch (Exception error)
        {
            if (job.CommittedResult is { } committed) return committed; Exception? cleanup = null; try { Close(); } catch (Exception e) { cleanup = e; }
            bool unknown = started || cleanup != null; string code = error is DomainException d ? d.Error.code : error is OperationCanceledException ? "CANCELLED" : "MODEL_SKETCH_FAILED";
            return job.Error(unknown ? "WRITE_OUTCOME_UNKNOWN" : code, unknown ? "Sketch edit is unverified; inspect this model and refresh its history before another dependent edit." : "Sketch edit did not start.", unknown ? "unknown" : "not_started", context, new { cause = code, stage, message = error.Message, verification_details = (error as DomainException)?.Error.details, cleanup_error = cleanup?.Message });
        }
    }
}

