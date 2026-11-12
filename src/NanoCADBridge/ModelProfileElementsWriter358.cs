using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Multicad;
using Multicad.Mc3D;
using Multicad.DatabaseServices;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using G = Multicad.Geometry;
using Db = Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class ModelProfileElementsWriter358
{
    sealed record Binding(ModelProfileElementsSpec358 Edit, ModelProfileElementsState358.Snapshot Before, McObjectId[] Removed,
        DrawingProfileWriter.Source[] Additions, SketchConstraintCurve[] Desired, ModelCircleRegion350[]? Regions);
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool value, string code, string message) => ModelProfileElementsState358.Need(value, code, message);
    internal static bool Handles(ModifyArguments args) => args.Operations.Any(edit => edit is ModelProfileElementsSpec358);
    static Mc3dSolid Body(string handle, DocumentState state, Db.Transaction tx, Action cancel)
    {
        var drawing = ModelFeatureReorderState345.Scope(state, tx); var raw = CadStructureWriter.Find(state.Document.Database, handle);
        Need(tx.GetObject(raw, Db.OpenMode.ForRead).GetType() == typeof(Db.Solid3d) && tx.GetObject(raw, Db.OpenMode.ForRead).OwnerId == ModelFeatureReorderState345.Model(state.Document.Database, tx),
            "OUT_OF_SCOPE", "Choose one current top-level native part in named Millimeters Model Space.");
        var id = McObjectId.FromHandle(raw.Handle.Value, drawing.ID); var body = id.GetObjectOfType<Mc3dSolid>();
        Need(body != null && body.ID == id && ModelHistoryEditGuard.CurrentRepresentation(body, state, tx, cancel) == raw, "OBJECT_IDENTITY_MISMATCH", "The exact native body/current representation changed."); return body;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static Binding Resolve(ModifyArguments args, DocumentState state, Db.Transaction tx, Action cancel)
    {
        Need(args.Operations.Length == 1 && args.Operations[0] is ModelProfileElementsSpec358, "INVALID_OPERATION_ORDER", "Supply one profile element lifecycle edit.");
        Need(args.TargetSpace == "model" && args.Units == "Millimeters" && (int)state.Document.Database.Insunits == 4, "OUT_OF_SCOPE", "Use Millimeters in Model Space.");
        var loaded = AppDomain.CurrentDomain.GetAssemblies().Select(item => item.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Need(loaded.Contains("mapimgd") && loaded.Contains("MapiBaseTypes"), "CAPABILITY_UNAVAILABLE", "MultiCAD must already be loaded.");
        var edit = (ModelProfileElementsSpec358)args.Operations[0]; var body = Body(edit.Handles.Single(), state, tx, cancel);
        Need(body.LastPartItemID.ToString().Equals(edit.ExpectedLastItemId, StringComparison.OrdinalIgnoreCase), "PRECONDITION_FAILED", "Refresh the body's native last_item_id.");
        var before = ModelProfileElementsState358.Capture(body, edit, args, state, tx, cancel, includeStandalone: true);
        DrawingViewSketchWriteScope282.RequireSupported(before.Selected.Sketch);
        void WritableLayer(string name)
        {
            cancel(); var table = (Db.LayerTable)tx.GetObject(state.Document.Database.LayerTableId, Db.OpenMode.ForRead);
            Need(table.Has(name), "LAYER_UNAVAILABLE", "The selected native sketch/profile layer has no verified local drawing layer.");
            var layer = (Db.LayerTableRecord)tx.GetObject(table[name], Db.OpenMode.ForRead);
            Need(!layer.IsLocked && !layer.IsFrozen && !layer.IsOff && !layer.IsDependent, "LAYER_UNAVAILABLE", "The selected native sketch/profile element layer is unavailable for revision.");
        }
        WritableLayer(before.Selected.Sketch.DbEntity.Layer); WritableLayer(before.Selected.Profile.DbEntity.Layer);
        Need(before.Selected.Sketch.ID.ToString().Equals(edit.ExpectedSketchId, StringComparison.OrdinalIgnoreCase) && before.Selected.Token == edit.ExpectedElementToken,
            "PRECONDITION_FAILED", "Profile/sketch/element/source/selected-region/constraint state changed; refresh profile_element_ids.");
        var keys = edit.RemoveElementIds.ToHashSet(StringComparer.OrdinalIgnoreCase); var removed = new List<McObjectId>();
        foreach (string key in edit.RemoveElementIds)
        {
            cancel(); var matching = before.Selected.ElementIds.Where(id => id.ToString().Equals(key, StringComparison.OrdinalIgnoreCase)).ToArray();
            Need(matching.Length == 1 && before.Selected.Sketch.ContainsObject(matching[0]), "PRECONDITION_FAILED", "An exact requested element has no current native sketch membership.");
            // A directly included DWG object or another resident native item is
            // not an abstract geometry ID. Its independent lifetime needs its
            // own adapter; do not let RemoveObject silently erase user curves.
            Need(before.Selected.Projections.Ids.Contains(matching[0]) || matching[0].Handle == 0 && matching[0].GetObjectOfType<HistoryItem>() == null,
                "OUT_OF_SCOPE", "Remove real source projections or abstract native sketch geometry IDs; directly included/resident nonprojection objects need their owning retirement adapter.");
            if (before.Selected.Projections.Ids.Contains(matching[0]))
            {
                var projection = matching[0].GetObjectOfType<ProjectionSketchEnt>(); Need(projection != null, "PRECONDITION_FAILED", "The exact selected native projection disappeared.");
                WritableLayer(projection.DbEntity.Layer); var downstream = projection.GetAllDependent(false);
                Need(downstream != null, "DEPENDENCY_INVENTORY_UNAVAILABLE", "Selected projection dependent inventory is unavailable."); ModelInventoryBudget.Ids(downstream, cancel);
                Need(downstream.All(id => id == body.ID || id == before.Selected.Sketch.ID || id == before.Selected.Profile.ID || id == before.Feature?.ID ||
                    before.Downstream.Contains(id.ToString(), StringComparer.OrdinalIgnoreCase) || ModelViewFollowers.OwnsDependent(before.Views, id)),
                    "DEPENDENCY_OWNERSHIP_MISMATCH", "A removed projection has an unclassified consumer outside the selected retained part.");
            }
            removed.Add(matching[0]);
        }
        // Existing constraints are not implicitly deleted or rewritten with an element.
        // An unavailable inventory remains an explicit observation boundary.
        if (ModelSketchConstraintObservation353.Complete(before.Selected.Constraints))
            foreach (var constraint in before.Selected.Constraints.GetProperty("constraints").EnumerateArray())
                foreach (string field in new[] { "first", "second", "third" })
                {
                    cancel(); var reference = constraint.GetProperty(field); Need(reference.ValueKind == JsonValueKind.Null ||
                    !keys.Contains(reference.GetProperty("object_id").GetString()!) &&
                    (reference.GetProperty("subentity_id").ValueKind == JsonValueKind.Null || !keys.Contains(reference.GetProperty("subentity_id").GetString()!)),
                    "CONSTRAINED_ELEMENT", "A listed native constraint references an element selected for removal; resolve that explicit relation first.");
                }
        var desired = ModelProfileLifecycleState340.Boundary(before.Selected.Geometry, before.Selected.Plane, cancel).Where(curve => !keys.Contains(curve.Id)).ToList();
        var additions = new List<DrawingProfileWriter.Source>();
        foreach (string handle in edit.AddSourceHandles)
        {
            cancel(); var source = DrawingProfileWriter.SourceOf(CadStructureWriter.Find(state.Document.Database, handle), state, tx, false, before.Selected.Plane);
            Need(source.Curve.Kind is "line" or "arc" or "circle" && before.Selected.Projections.Sources.All(old => !old.id.Equals(source.Native.ToString(), StringComparison.OrdinalIgnoreCase)) &&
                before.Selected.ElementIds.All(id => id != source.Native), "OUT_OF_SCOPE", "Add distinct coplanar committed Line/Arc/Circle sources not already projected or directly included in this sketch.");
            additions.Add(source); desired.Add(source.Curve with { Id = source.Native.ToString() });
        }
        Need(desired.Count > 0, "INVALID_GEOMETRY", "At least one complete usable profile contour must survive.");
        ModelCircleRegion350[]? regions = null;
        if (edit.RegionPoints.Length > 0)
        {
            Need(desired.All(curve => curve.Kind == "circle"), "OUT_OF_SCOPE", "Explicit region_points currently require separated/nested whole Circle loops.");
            var circles = desired.Select(curve => new ModelCircleLoop350(curve.Id, curve.Center!, curve.Radius, curve.Id)).ToArray();
            regions = ModelCircleRegions350.Select(circles, edit.RegionPoints.Select(point => DrawingWorkplane.Local(before.Selected.Plane, point)).ToArray(), cancel);
        }
        else
        {
            var contour = ModelProfileElementGeometry358.Single(desired.ToArray(), cancel);
            ModelExtrusionGeometry.Measure(new(before.Selected.Plane, contour, 1, "positive", "Surviving profile", "0"), cancel);
        }
        return new(edit, before, removed.ToArray(), additions.ToArray(), desired.ToArray(), regions);
    }
    internal static void Preflight(ModifyArguments args, DocumentState state, Db.Transaction tx, Action? cancel = null) => Resolve(args, state, tx, cancel ?? (() => { }));
    internal static ModelProfileElementsRead358[] Read(string bodyHandle, string[] ids, Arguments args, DocumentState state, Db.Transaction tx, Action cancel)
    {
        var body = Body(bodyHandle, state, tx, cancel); var owner = ModelProfileDiscovery348.OwnerDocument(body, state); var values = new List<ModelProfileElementsRead358>();
        foreach (string id in ids) { cancel(); var profile = ModelProfileDiscovery348.Resolve(id, owner, cancel); var value = ModelProfileElementsReader358.Capture(body, profile, state, tx, cancel); values.Add(ModelProfileElementsReader358.Describe(value, args)); }
        return values.ToArray(); // CadReader jointly measures actual public response bytes.
    }
    static ModelProfileElementsState358.Snapshot Current(Binding binding, ModifyArguments args, DocumentState state, Action cancel)
    {
        using var tx = state.Document.Database.TransactionManager.StartTransaction(); ModelFeatureReorderState345.Scope(state, tx, binding.Before.DrawingDocument);
        return ModelProfileElementsState358.Capture(binding.Before.Body, binding.Edit, args, state, tx, cancel, includeStandalone: true,
            retainedSourceHandles: binding.Before.Sources.EnumerateArray().Select(value => value.GetProperty("handle").GetString()!).ToArray());
    }
    static (SketchConstraintCurve[] Curves, ModelCircleRegion350[]? Regions) Expected(Binding binding, McObjectId[] added, bool estimate = false)
    {
        var map = binding.Additions.Select((source, index) => (Id: source.Native.ToString(), New: estimate ? source.Native.ToString() : added[index].ToString()))
            .ToDictionary(pair => pair.Id, pair => pair.New, StringComparer.OrdinalIgnoreCase);
        string Key(string id) => map.TryGetValue(id, out var value) ? value : id;
        return (binding.Desired.Select(curve => curve with { Id = Key(curve.Id) }).ToArray(), binding.Regions?.Select(region =>
            new ModelCircleRegion350(Key(region.outer_element_id), region.hole_element_ids.Select(Key).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray())).ToArray());
    }
    static ModelRemovedElement358[] Dispositions(Binding binding, ModelProfileElementsState358.Snapshot after, ModifyArguments args, bool estimate, Action cancel)
    {
        var values = new List<ModelRemovedElement358>();
        foreach (var id in binding.Removed)
        {
            cancel(); string key = id.ToString(); var source = binding.Before.Selected.Projections.Sources.SingleOrDefault(value => value.projection_ids.Contains(key, StringComparer.OrdinalIgnoreCase));
            bool resident = after.Construction.Contains(id); ProjectionSketchEnt? actual = estimate || !resident ? null : id.GetObjectOfType<ProjectionSketchEnt>();
            Need(!resident || estimate || actual != null && actual.ID == id && actual.DbEntity.Document != null && actual.DbEntity.Document.ID.ToString() == after.OwnerDocument,
                "PROFILE_OWNERSHIP_MISMATCH", "A retained removed projection has unavailable actual document identity.");
            values.Add(new(key, source?.id, source == null ? null : new(args.BridgeId, args.DocumentId, source.handle), false, false, !estimate && after.Contents.Contains(id), !estimate && resident,
                estimate ? "{FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF}" : actual == null ? null : ModelProfileDiscovery348.Id(actual.HostSketchID),
                estimate ? "{FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF}" : actual == null ? null : ModelProfileDiscovery348.Id(actual.HostSolidID),
                estimate || !resident ? "not_in_observed_document_projection_inventory_no_object_erase_or_release_claim" : "projection_resident_but_removed_from_sketch_membership"));
        }
        return values.ToArray();
    }
    static Result Outcome(Binding binding, ModifyArguments args, WriteJob job, ReadContext context, ModelProfileElementsState358.Snapshot after, McObjectId[] added, bool estimate)
    {
        var source = new EntityRef(args.BridgeId, args.DocumentId, binding.Edit.Handles.Single()); var body = new EntityRef(args.BridgeId, args.DocumentId, estimate ? "FFFFFFFFFFFFFFFF" : Validation.NormalizeHandle(after.Representation.Handle.ToString()));
        bool replaced = estimate || source.handle != body.handle; var ids = estimate ? binding.Additions.Select(_ => "{FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF}").ToArray() : added.Select(id => id.ToString()).ToArray();
        var observation = after.Selected.Constraints; bool complete = ModelSketchConstraintObservation353.Complete(observation);
        var summary = Wire.Element(new
        {
            document_id = observation.GetProperty("document_id"),
            sketch_id = observation.GetProperty("sketch_id"),
            scope_id = observation.GetProperty("scope_id"),
            network = observation.GetProperty("network"),
            solver_status = "not_evaluated",
            constraints_complete = complete,
            constraint_count = estimate ? int.MaxValue : complete ? (int?)observation.GetProperty("constraints").GetArrayLength() : null,
            constraints_sha256 = estimate || complete ? Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(observation.GetProperty("constraints"), Wire.Json))) : null,
            inventory_scope = "summary_only; full_actual_inventory_in_profile_element_ids_read; unavailable_is_not_empty"
        });
        var change = new ModelProfileElementsChange358(0, source, body, after.Body.ID.ToString(), after.Selected.Profile.ID.ToString(), after.Selected.Sketch.ID.ToString(), after.OwnerDocument,
            after.Feature?.ID.ToString(), after.Selected.Token, binding.Before.Views.LastHistoryItem.ToString(), replaced, Dispositions(binding, after, args, estimate, job.CheckCancellation),
            binding.Additions.Select(value => new EntityRef(args.BridgeId, args.DocumentId, value.Id.Handle.ToString())).ToArray(), ids, summary,
            estimate ? int.MaxValue : after.Selected.Outer.Length, estimate ? int.MaxValue : after.Selected.Holes.Sum(holes => holes.Length),
            Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(after.Selected.SelectedGeometry, Wire.Json))),
            "same_native_sketch_profile_consumer; drawing_sources_retained; removed_native_object_residency_observed_without_erasure_or_reusability_promise; private_constraint_state_unobserved_when_inventory_unavailable");
        var warnings = binding.Before.Warnings.Concat(after.Warnings).Append(WriteRecovery.Warning)
            .Append("MODEL_PROFILE_ELEMENTS_CHANGED: Sources remain retained; only source reactor deltas proved bound to the requested native projections/sketch are admitted. Refresh profile_element_ids, exact profile_ids for unused construction reuse, current body/history/variables and linked drawings. Native RemoveObject changes sketch membership; it is not source DWG deletion or a reusable retired-object promise. Available constraints and all existing owning variables are compared; unobservable private constraints are not certified preserved.")
            .Concat(estimate ? [ModelSketchConstraintObservation353.Warning, ModelFeatureReorderState345.ValidityWarning] : Array.Empty<string>()).Distinct(StringComparer.Ordinal).ToArray();
        return Result.Ok(job.OriginalRequestId, new ModelProfileElementsOutput358(args.OperationId, "committed", false, [body], replaced ? [source] : [], replaced ? [body] : [], [], "not_guaranteed", [change]), context, warnings);
    }
    static void Budget(Result result, Action cancel)
    { cancel(); Need(JsonSerializer.SerializeToUtf8Bytes(result, Wire.Json).Length <= Wire.PayloadBudget, "RESPONSE_LIMIT_EXCEEDED", "The complete profile-element result exceeds actual transport bytes."); }
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Result Execute(ModifyArguments args, DocumentState state, WriteJob job, ReadContext context, JsonLog log)
    {
        Binding? binding = null; McUndoPoint? tracker = null; bool started = false; string stage = "preparation"; var added = new List<McObjectId>(); var observed = new List<McObjectId>(); var removed = new List<string>();
        void Trace(string next) { stage = next; log.Write(job.Operation, job.OriginalRequestId, "model_profile_elements358_checkpoint", transaction: next, operationId: job.OperationId); }
        void Close() { var value = tracker; tracker = null; if (value == null) return; try { value.Stop(); } finally { value.Dispose(); } }
        try
        {
            using (var tx = state.Document.Database.TransactionManager.StartTransaction()) binding = Resolve(args, state, tx, job.CheckCancellation);
            var fresh = Current(binding, args, state, job.CheckCancellation);
            Need(binding.Before.Representation == fresh.Representation && binding.Before.Selected.Token == fresh.Selected.Token && binding.Before.Contents.ToHashSet().SetEquals(fresh.Contents) &&
                binding.Before.Construction.ToHashSet().SetEquals(fresh.Construction) && binding.Before.Inventory.SetEquals(fresh.Inventory) &&
                ModelProfileElementsState358.Equal(binding.Before.DrawingInventory, fresh.DrawingInventory, job.CheckCancellation) &&
                ModelProfileElementsState358.Equal(binding.Before.Parameters, fresh.Parameters, job.CheckCancellation) && ModelProfileElementsState358.Equal(binding.Before.Sources, fresh.Sources, job.CheckCancellation) &&
                ModelProfileElementsState358.Equal(binding.Before.OtherProfiles, fresh.OtherProfiles, job.CheckCancellation) && binding.Before.BodyMetadata.GetRawText() == fresh.BodyMetadata.GetRawText() &&
                binding.Before.Display.GetRawText() == fresh.Display.GetRawText() && binding.Before.OwnerDocument == fresh.OwnerDocument && binding.Before.DrawingDocument == fresh.DrawingDocument &&
                binding.Before.FeatureOrder.SequenceEqual(fresh.FeatureOrder, StringComparer.OrdinalIgnoreCase) && binding.Before.Downstream.SequenceEqual(fresh.Downstream, StringComparer.OrdinalIgnoreCase) &&
                binding.Before.DirectDownstream.SequenceEqual(fresh.DirectDownstream, StringComparer.OrdinalIgnoreCase), "PRECONDITION_FAILED", "Retained native profile, owner, source, variable or consumer state changed before writing.");
            // Full boundary readback stays in optional profile_element_ids.
            // The write mapping uses fixed hashes/counts and worst-length owner
            // IDs/dispositions, reserving bounded future observation warnings.
            var projected = Outcome(binding, args, job, context, fresh, [], true);
            Budget(ModelViewFollowers.ReserveWarnings(projected, binding.Before.Views), job.CheckCancellation);
            ModelViewFollowers.CheckStandalone(binding.Before.Body, binding.Before.Views, job.CheckCancellation);
            tracker = new McUndoPoint(false); tracker.Start(); started = true; var sketch = binding.Before.Selected.Sketch; var profile = binding.Before.Selected.Profile;
            var owner = binding.Before.Body.DbEntity.Document; sketch.TryModify((uint)McDbObject.ObjChangesType.Geom);
            foreach (var id in binding.Removed)
            { job.CheckCancellation(); Trace("remove_native_sketch_membership"); Need(sketch.RemoveObject(id), "MODEL_UPDATE_FAILED", "Native sketch rejected exact element removal."); removed.Add(id.ToString()); }
            foreach (var source in binding.Additions)
            {
                job.CheckCancellation(); Trace("add_native_source_projection"); var projection = sketch.ProjectObject(source.Native, true); if (projection != null && !projection.ID.IsNull) observed.Add(projection.ID);
                Need(projection != null && projection.SrcObjectID == source.Native && projection.HostSketchID == sketch.ID && !projection.ID.IsNull &&
                    !binding.Before.Construction.Contains(projection.ID) && !binding.Before.Contents.Contains(projection.ID) && !added.Contains(projection.ID) &&
                    (projection.DbEntity.Document == null || projection.DbEntity.Document.ID == owner.ID), "PROFILE_OWNERSHIP_MISMATCH", "Returned source projection is not a fresh native object in the exact verified owner scope; no owner reset or disposal is attempted.");
                McObjectManager.Add2Document(projection.DbEntity, owner); projection.DbEntity.Update(); added.Add(projection.ID);
            }
            sketch.DbEntity.Update(); McObjectManager.UpdateAll(); profile.TryModify((uint)McDbObject.ObjChangesType.Geom); Trace("clear_selected_native_regions");
            Need(profile.Clear() && profile.IsEmpty, "MODEL_UPDATE_FAILED", "Native profile did not clear selected regions.");
            if (binding.Edit.RegionPoints.Length == 0)
            { Trace("select_single_surviving_contour"); Need(profile.AutoProcessExternalContours(), "MODEL_UPDATE_FAILED", "Native profile could not select the surviving validated closed contour."); }
            else
            {
                var normal = DrawingWorkplane.Normal(binding.Before.Selected.Plane);
                foreach (var point in binding.Edit.RegionPoints)
                {
                    job.CheckCancellation(); Trace("select_explicit_surviving_region"); using var line = new G.Line3d(new G.Point3d(point.X, point.Y, point.Z), new G.Vector3d(normal.X, normal.Y, normal.Z));
                    Need(profile.AddOrRemoveRegionByLine(line), "MODEL_UPDATE_FAILED", "Native profile could not select the explicit surviving bounded region.");
                }
            }
            profile.DbEntity.Update(); McObjectManager.UpdateAll();
            if (binding.Before.Feature != null) { Trace("rebuild_retained_part"); Need(binding.Before.Body.RebuildPart(), "MODEL_UPDATE_FAILED", "Native part rejected the retained profile element revision."); McObjectManager.UpdateAll(); }
            var expected = Expected(binding, added.ToArray()); var after = Current(binding, args, state, job.CheckCancellation);
            ModelProfileElementsState358.Verify(binding.Before, after, binding.Removed, added.ToArray(), expected.Curves, expected.Regions, job.CheckCancellation);
            Trace("native_elements_verified"); job.BeginCommit(); Close(); after = Current(binding, args, state, job.CheckCancellation);
            ModelProfileElementsState358.Verify(binding.Before, after, binding.Removed, added.ToArray(), expected.Curves, expected.Regions, job.CheckCancellation);
            var result = ModelViewFollowers.WithWarnings(Outcome(binding, args, job, context, after, added.ToArray(), false), binding.Before.Views); Budget(result, job.CheckCancellation); job.RecordCommit(result); Trace("commit_recorded"); return result;
        }
        catch (Exception error)
        {
            if (job.CommittedResult is { } committed) return committed; Exception? cleanup = null; try { Close(); } catch (Exception e) { cleanup = e; }
            bool unknown = started || cleanup != null; string cause = error is DomainException domain ? domain.Error.code : error is OperationCanceledException ? "CANCELLED" : "MODEL_PROFILE_ELEMENTS_FAILED";
            string message = error.Message; if (message.Length > 512) message = message[..512];
            return job.Error(unknown ? "WRITE_OUTCOME_UNKNOWN" : cause, unknown ? "Native profile element editing started but remains unverified. Partial removals/projections/regions may remain; reconcile exact current sketch/profile/body before dependent work. Replay only the identical request in this bridge lifetime." : message,
                unknown ? "unknown" : "not_started", context, new
                {
                    cause,
                    stage,
                    message,
                    verification_details = (error as DomainException)?.Error.details,
                    body_mc_id = binding?.Before.Body.ID.ToString(),
                    profile_id = binding?.Edit.ProfileId,
                    sketch_id = binding?.Edit.ExpectedSketchId,
                    removed_membership_calls_completed_count = removed.Count,
                    removed_membership_calls_sha256 = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(removed, Wire.Json))),
                    added_projection_count = added.Count,
                    added_projection_ids_sha256 = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(added.Select(id => id.ToString()).ToArray(), Wire.Json))),
                    observed_projection_count = observed.Count,
                    observed_projection_ids_sha256 = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(observed.Select(id => id.ToString()).ToArray(), Wire.Json))),
                    cleanup_error = cleanup?.GetType().Name
                })
                with
            { warnings = [WriteRecovery.Warning] };
        }
    }
}
