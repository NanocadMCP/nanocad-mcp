using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Geometry;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Db = Teigha.DatabaseServices;
using static NanoCADBridge.PaperDrawingViewScope356;

namespace NanoCADBridge;

internal static class PaperDrawingViewWriter356
{
    internal sealed record Placement(string Name, DrawingView.ViewDirectionType Direction, Point2 Origin);
    internal static Placement[] Placements(CreateDrawingViewsEdit spec)
    {
        var values = new List<Placement> { new("Front", DrawingView.ViewDirectionType.kViewFront, spec.FrontOrigin) };
        if (spec.TopOrigin is { } top) values.Add(new("Top", DrawingView.ViewDirectionType.kViewTop, top));
        foreach (var child in spec.ProjectedViews)
        {
            var item = child.Direction switch
            {
                "bottom" => ("Bottom", DrawingView.ViewDirectionType.kViewBottom),
                "left" => ("Left", DrawingView.ViewDirectionType.kViewLeft),
                "right" => ("Right", DrawingView.ViewDirectionType.kViewRight),
                "iso_top_left" => ("IsoTopLeft", DrawingView.ViewDirectionType.kViewIsoTopLeft),
                "iso_top_right" => ("IsoTopRight", DrawingView.ViewDirectionType.kViewIsoTopRight),
                "iso_bottom_left" => ("IsoBottomLeft", DrawingView.ViewDirectionType.kViewIsoBottomLeft),
                "iso_bottom_right" => ("IsoBottomRight", DrawingView.ViewDirectionType.kViewIsoBottomRight),
                _ => throw new DomainException("INVALID_ARGUMENT", "Unsupported native child direction.")
            };
            values.Add(new(item.Item1, item.Item2, child.Origin));
        }
        return values.ToArray();
    }
    private static Plane3d Place(Point2 point) => new(new Point3d(point.X, point.Y, 0), Vector3d.XAxis, Vector3d.YAxis);
    private static Plane3d Projection(CreateDrawingViewsEdit spec) => spec.ProjectionPlane is { } plane ?
        new(new Point3d(plane.Origin.X, plane.Origin.Y, plane.Origin.Z), new Vector3d(plane.XAxis.X, plane.XAxis.Y, plane.XAxis.Z), new Vector3d(plane.YAxis.X, plane.YAxis.Y, plane.YAxis.Z)) :
        new(Point3d.Origin, Vector3d.XAxis);
    private static bool Resident(DrawingView view, Scope scope, DocumentState state, Action cancel)
    {
        using var tx = state.Document.Database.TransactionManager.StartTransaction();
        var layouts = (Db.DBDictionary)tx.GetObject(state.Document.Database.LayoutDictionaryId, Db.OpenMode.ForRead);
        var found = new List<(Db.ObjectId Entity, Db.ObjectId Space, Db.ObjectId Layout)>();
        foreach (Db.DBDictionaryEntry entry in layouts)
        {
            cancel(); var layout = (Db.Layout)tx.GetObject(entry.Value, Db.OpenMode.ForRead);
            var space = (Db.BlockTableRecord)tx.GetObject(layout.BlockTableRecordId, Db.OpenMode.ForRead);
            foreach (Db.ObjectId id in space)
            {
                cancel(); if (CadDrawingViewEditState.Gone(id)) continue;
                if (McObjectId.FromHandle(id.Handle.Value, scope.Lookup.ID) != view.ID) continue;
                var entity = tx.GetObject(id, Db.OpenMode.ForRead) as Db.Entity;
                Need(entity != null && entity.Database == state.Document.Database && entity.OwnerId == space.ObjectId &&
                    entity.GetType().FullName == "Teigha.DatabaseServices.ImpCurve",
                    "Fresh native view has an invalid resident representation.", "OBJECT_IDENTITY_MISMATCH");
                found.Add((id, space.ObjectId, layout.ObjectId));
            }
        }
        Need(found.Count <= 1, "Fresh native view has multiple physical representations.", "OBJECT_IDENTITY_MISMATCH");
        if (found.Count == 0) return false;
        Need(found[0].Space == scope.Space && found[0].Layout == scope.Layout && view.DbEntity.DocumentID == scope.Sheet.ID &&
            view.DbEntity.Document.ID == scope.Sheet.ID,
            "Native factory placed a fresh view in another layout or native document.", "OBJECT_IDENTITY_MISMATCH");
        return true;
    }
    private static void Register(DrawingView view, Scope scope, DocumentState state, HashSet<McObjectId> created, Action cancel)
    {
        cancel(); var old = view.ID;
        Need(!old.IsNull && scope.Before.Views.All(item => item.Id != old) && !created.Contains(old), "Native factory returned an existing/duplicate/null view identity.", "OBJECT_IDENTITY_MISMATCH");
        var db = view.DbEntity; Need(db != null && (db.DocumentID.IsNull || db.DocumentID == scope.Sheet.ID), "Fresh native view has foreign document affinity.", "OBJECT_IDENTITY_MISMATCH");
        // CreateChildView may return a child already resident in its parent's
        // sheet. Native document affinity alone is not physical residency.
        // Reuse only a unique exact current representation; never repost it.
        if (!Resident(view, scope, state, cancel))
        {
            Need(McObjectManager.Add2Document(db!, scope.Sheet), "Native registration in the exact Paper sheet was rejected.", "NATIVE_INSERT_FAILED");
            Need(Resident(view, scope, state, cancel), "Native registration supplied no unique exact Paper representation.", "OBJECT_IDENTITY_MISMATCH");
        }
        Need(view.ID == old && view.DbEntity.DocumentID == scope.Sheet.ID && view.DbEntity.Document.ID == scope.Sheet.ID && created.Add(old),
            "New native view registration changed identity or acquired another document.", "OBJECT_IDENTITY_MISMATCH");
    }
    private static string Geometry(DrawingView view, Action cancel)
    {
        cancel(); List<EntityGeometry>? values = null; string? reason = null;
        try { values = view.GetViewGeometry(true, 0); }
        catch (NotSupportedException) { cancel(); reason = "native_projection_getter_not_supported"; }
        catch (NotImplementedException) { cancel(); reason = "native_projection_getter_not_implemented"; }
        string observation = reason ?? (values == null ? "native_projection_inventory_null" : values.Count == 0 ? "native_projection_empty" : "native_projection_elements_observed_rendered_image_pending");
        var errors = new List<Exception>();
        if (values != null) foreach (var item in values.Distinct(ReferenceEqualityComparer.Instance))
            if (item is IDisposable resource) try { resource.Dispose(); } catch (Exception error) { errors.Add(error); }
        if (errors.Count > 0) throw new AggregateException("Paper projection wrapper cleanup failed.", errors);
        cancel(); return observation;
    }
    private static bool Same(JsonElement left, JsonElement right) => ModelParameterSnapshot.Equivalent(left, right);
    private static bool RequestedPlane(Plane3d actual, Plane3d requested)
    {
        var observed = Wire.Element(CadDrawingViewEditState.Plane(actual)); var expected = Wire.Element(CadDrawingViewEditState.Plane(requested));
        foreach (string field in new[] { "origin", "axis_u", "axis_v", "normal" })
            for (int index = 0; index < 3; index++) if (Math.Abs(observed.GetProperty(field)[index].GetDouble() - expected.GetProperty(field)[index].GetDouble()) > 1e-8) return false;
        return true;
    }
    private static void Retained(Scope scope, CadDrawingViewEditState.Snapshot after, McObjectId[] created, Db.Database database, Db.Transaction tx, Action cancel)
    {
        var before = scope.Before; var newSet = created.ToHashSet();
        Need(Same(before.Context, after.Context) && Same(before.Layouts, after.Layouts), "Native view creation changed active scope/layout records.", "DRAWING_VIEW_UPDATE_MISMATCH");
        Need(after.Views.Length == before.Views.Length + created.Length && before.Views.All(old => after.Views.Any(current => current.Id == old.Id && current.Entity == old.Entity && current.Space == old.Space && current.Layout == old.Layout && Same(old.Metadata, current.Metadata))),
            "An existing native view identity, owner, tree, source or stored setting changed.", "DRAWING_VIEW_UPDATE_MISMATCH");
        var newHandles = after.Views.Where(view => newSet.Contains(view.Id)).Select(view => CadDrawingViewLifecycleState345.Handle(view.Entity)).ToHashSet(StringComparer.Ordinal);
        var retained = Wire.Element(after.Entities.EnumerateArray().Where(item => !newHandles.Contains(item.GetProperty("id").GetString()!)).ToArray());
        Need(Same(before.Entities, retained) && after.Entities.GetArrayLength() == before.Entities.GetArrayLength() + created.Length && Same(before.Sections, after.Sections),
            "Native Paper creation changed unrelated layout entities or Section state.", "DRAWING_VIEW_UPDATE_MISMATCH");
        Need(before.Bodies.Length == after.Bodies.Length, "A Model solid was added/removed by Paper projection creation.", "DRAWING_VIEW_UPDATE_MISMATCH");
        foreach (var old in before.Bodies)
        {
            cancel(); var current = after.Bodies.SingleOrDefault(body => body.Id == old.Id && body.Entity == old.Entity);
            Need(current != null && current.Model == old.Model && Same(old.Geometry, current.Geometry), "A Model source identity/geometry changed.", "DRAWING_VIEW_UPDATE_MISMATCH");
            var appended = current!.Contents.Where(id => !old.Contents.Contains(id)).ToHashSet();
            Need(old.Contents.All(current.Contents.Contains) && appended.All(newSet.Contains), "Native history lost a retained item or gained unrelated items.", "DRAWING_VIEW_UPDATE_MISMATCH");
            var createdViews = after.Views.Where(view => appended.Contains(view.Id)).ToArray();
            Need(createdViews.All(view => scope.Sources.Length == 1 && scope.Sources[0] == old.Id && view.Native.HostSolidID == old.Id),
                "New Paper view history membership is not its uniquely selected native source host.", "DRAWING_VIEW_UPDATE_MISMATCH");
            McObjectId Skip(McObjectId id)
            {
                var seen = new HashSet<McObjectId>(); while (appended.Contains(id))
                { Need(seen.Add(id), "Created view history chain is cyclic."); Need(current.Previous.TryGetValue(id, out var next), "Created view predecessor unavailable."); id = next; }
                return id;
            }
            var retainedLast = Skip(current.Last);
            if (retainedLast != old.Last)
                throw new DomainException("DRAWING_VIEW_UPDATE_MISMATCH", "Native source terminal changed outside the exact added view chain.", false,
                    new { expected = CadDrawingViewEditState.Id(old.Last), actual = CadDrawingViewEditState.Id(current.Last), retained = CadDrawingViewEditState.Id(retainedLast) });
            foreach (var node in old.Nodes.EnumerateArray())
            {
                var identity = node.GetProperty("id").GetString(); var retainedNode = current.Nodes.EnumerateArray().Single(item => item.GetProperty("id").GetString() == identity);
                var expected = JsonNode.Parse(retainedNode.GetRawText())!.AsObject();
                var native = old.Contents.Single(id => id.ToString() == identity);
                if (current.Previous.TryGetValue(native, out var previous)) expected["previous"] = CadDrawingViewEditState.Id(Skip(previous));
                Need(Same(node, JsonSerializer.SerializeToElement(expected, Wire.Json)), "Retained source feature status/order changed.", "DRAWING_VIEW_UPDATE_MISMATCH");
            }
            if (scope.SourceMetadata.TryGetValue(old.Entity, out var metadata))
            {
                var actual = CadNativeStyleIdentityMetadata352.Metadata(tx.GetObject(old.Entity, Db.OpenMode.ForRead), database, tx, cancel);
                var expected = JsonNode.Parse(actual.GetRawText())!.AsObject(); var oldReactors = metadata.GetProperty("reactors").EnumerateArray().Select(value => value.GetString()).ToHashSet(StringComparer.Ordinal);
                var actualReactors = actual.GetProperty("reactors").EnumerateArray().Select(value => value.GetString()).ToHashSet(StringComparer.Ordinal);
                Need(oldReactors.IsSubsetOf(actualReactors) && actualReactors.Where(id => !oldReactors.Contains(id)).All(id => newHandles.Contains(id!)),
                    "A selected source persistent dependency changed outside the exact new native views.", "DRAWING_VIEW_UPDATE_MISMATCH");
                expected["reactors"] = JsonNode.Parse(metadata.GetProperty("reactors").GetRawText());
                Need(Same(metadata, JsonSerializer.SerializeToElement(expected, Wire.Json)), "Selected source readable metadata/ownership changed.", "DRAWING_VIEW_UPDATE_MISMATCH");
            }
        }
        CadDrawingViewEditState.Tree(after, cancel);
    }
    internal static Result Execute(StructureWriteArguments args, DocumentState state, WriteJob job, ReadContext context, JsonLog log)
    {
        McUndoPoint? tracker = null; bool started = false; var observedIds = new List<string>();
        void Close() { var owned = tracker; tracker = null; if (owned != null) try { owned.Stop(); } finally { owned.Dispose(); } }
        Result Failure(Exception error, Exception? cleanup = null) => job.Error(
            started || cleanup != null ? "WRITE_OUTCOME_UNKNOWN" : error is DomainException domain ? domain.Error.code : "MODEL_UPDATE_FAILED",
            "Native Paper view creation did not complete verified readback; inspect the exact affected sheet and Model sources.", started || cleanup != null ? "unknown" : "not_started", context,
            new { error_type = error.GetType().Name, message = error.Message, native_details = (error as DomainException)?.Error.details, observed_native_view_ids = observedIds, cleanup_error = cleanup?.GetType().Name });
        try
        {
            Scope scope; using (var tx = state.Document.Database.TransactionManager.StartTransaction()) scope = Prepare(args, state, tx, job.CheckCancellation);
            var spec = (CreateDrawingViewsEdit)args.Operations[0]; var placements = Placements(spec); Current(scope, state, job.CheckCancellation);
            tracker = new McUndoPoint(false); tracker.Start(); started = true; var created = new HashSet<McObjectId>(); var views = new List<DrawingView>();
            // Register first; registration cannot reset the assigned source,
            // section/tree link, projection basis, name or final scale later.
            var front = new DrawingView(); observedIds.Add(front.ID.ToString()); Register(front, scope, state, created, job.CheckCancellation);
            front.DirectionType = DrawingView.ViewDirectionType.kViewFront; front.SetIsSection(false); front.ProjPlane = Projection(spec);
            front.ViewPlane = Place(spec.FrontOrigin); front.Set3dEntsIds(scope.Sources, true); front.Name = spec.NamePrefix + " Front";
            front.DbEntity.DScale = spec.Scale; front.DbEntity.Scale = 1; views.Add(front);
            foreach (var placement in placements.Skip(1))
            {
                job.CheckCancellation(); var child = front.CreateChildView(placement.Direction, Place(placement.Origin));
                Need(child != null, "Native projection factory returned no child.", "NATIVE_INSERT_FAILED"); observedIds.Add(child!.ID.ToString()); Register(child, scope, state, created, job.CheckCancellation);
                child.Name = spec.NamePrefix + " " + placement.Name; child.DbEntity.DScale = spec.Scale; child.DbEntity.Scale = 1; views.Add(child);
            }
            McObjectManager.UpdateAll(); job.CheckCancellation();
            var viewIds = views.Select(view => view.ID).ToArray(); var rootId = viewIds[0];
            (StructureChange[] Changes, PaperDrawingViewCreation356[] Details) Readback()
            {
                Current(scope, state, job.CheckCancellation); using var tx = state.Document.Database.TransactionManager.StartTransaction();
                var after = CadDrawingViewLifecycleState345.Capture(state, tx, job.CheckCancellation); Retained(scope, after, viewIds, state.Document.Database, tx, job.CheckCancellation);
                var changes = new List<StructureChange>(); var details = new List<PaperDrawingViewCreation356>();
                for (int index = 0; index < viewIds.Length; index++)
                {
                    var item = after.Views.SingleOrDefault(view => view.Id == viewIds[index]); var placement = placements[index];
                    Need(item != null && item.Space == scope.Space && item.Layout == scope.Layout && item.Native.DbEntity.DocumentID == scope.Sheet.ID,
                        "New native view has no unique representation in the exact requested Paper layout.", "OBJECT_IDENTITY_MISMATCH");
                    var native = item.Native;
                    Need(native.Name == spec.NamePrefix + " " + placement.Name && native.DirectionType == placement.Direction && native.Status.ToString() == "Ok" && !native.IsSuppressed &&
                        native.DbEntity.DScale == spec.Scale && native.DbEntity.Scale == 1 && native.SectionID.IsNull && !native.IsBasedOnSection && !native.IsSlice,
                        "New ordinary Paper view name/direction/status/scale/section state differs.", "DRAWING_VIEW_UPDATE_MISMATCH");
                    Need(native.RootViewID == rootId && (index == 0 ? native.ParentViewID.IsNull : native.ParentViewID == rootId), "Created native projection tree differs.", "DRAWING_VIEW_UPDATE_MISMATCH");
                    Need(Math.Abs(native.ViewPlane.Origin.X - placement.Origin.X) <= 1e-8 && Math.Abs(native.ViewPlane.Origin.Y - placement.Origin.Y) <= 1e-8 && Math.Abs(native.ViewPlane.Origin.Z) <= 1e-8,
                        "Native Paper placement differs from requested sheet XY coordinates.", "DRAWING_VIEW_UPDATE_MISMATCH");
                    if (index == 0) Need(RequestedPlane(native.ProjPlane, Projection(spec)),
                        "Native root projection basis differs from requested WCS basis.", "DRAWING_VIEW_UPDATE_MISMATCH");
                    var sources = DrawingViewSources356.Capture(native, job.CheckCancellation); DrawingViewSources356.Exact(sources, scope.Sources, job.CheckCancellation);
                    var refs = CadDrawingViewLifecycleState345.SourceRefs(scope.Sources, state.Document.Database, tx, args.BridgeId, args.DocumentId, job.CheckCancellation);
                    Need(refs.Refs != null, "New native Paper sources cannot resolve to current Model references.", "SOURCE_SCOPE_MISMATCH");
                    var reference = new EntityRef(args.BridgeId, args.DocumentId, CadDrawingViewLifecycleState345.Handle(item!.Entity));
                    var entity = (Db.Entity)tx.GetObject(item.Entity, Db.OpenMode.ForRead);
                    // Missing specialized token serialization cannot disable a
                    // healthy fresh projection. Generic ownership/readable data
                    // remains verified; only explicitly unsupported inner data
                    // is disclosed, never foreign/invalid ownership or bytes.
                    _ = CadNativeStyleIdentityMetadata352.Metadata(entity, state.Document.Database, tx, job.CheckCancellation);
                    string? token = null, tokenReason = null;
                    try { token = CadDrawingViewLifecycleState345.Token(native, entity, item.Layout, tx, job.CheckCancellation); }
                    catch (DomainException error) when (error.Error.code == "UNSUPPORTED_OWNED_METADATA")
                    { job.CheckCancellation(); tokenReason = "native_owned_inner_state_adapter_unavailable; refresh get_drawing_views before lifecycle edits"; }
                    changes.Add(new(0, spec.Operation, reference, "drawing_view", native.Name, true));
                    details.Add(new(reference, new(args.BridgeId, args.DocumentId, spec.LayoutHandle!), scope.Sheet.ID.ToString(), token, tokenReason, refs.Refs!, sources.top_source_ids,
                        CadDrawingViewEditState.Id(native.ParentViewID), native.RootViewID.ToString(), CadDrawingViewEditState.Ids(native.ChildViews, "Created view children"), Geometry(native, job.CheckCancellation)));
                }
                var group = after.Views.Where(view => viewIds.Contains(view.Id)).ToArray();
                Need(group.Single(view => view.Id == rootId).Native.ChildViews.ToHashSet().SetEquals(viewIds.Skip(1)) &&
                    group.Where(view => view.Id != rootId).All(view => view.Native.ChildViews.Count == 0), "New native group has unexpected children.", "DRAWING_VIEW_UPDATE_MISMATCH");
                return (changes.ToArray(), details.ToArray());
            }
            var warnings = new[] { WriteRecovery.Warning, "NATIVE_PAPER_VIEW_CREATION: Fresh views belong to the explicit active native Paper sheet; their selected sources remain Model solids. Opaque source internals are unobserved; layout transfer and annotation association are not inferred. Inspect the rendered sheet before relying on projection detail." };
            // Source history can settle after the host finishes the command.
            // Keep the same ownership and terminal checks at that boundary.
            job.CheckCancellation(); job.BeginCommit(); Close();
            return NativePostCommandVerification422.Defer(job, () =>
            {
                var value = Readback();
                var result = Result.Ok(job.OriginalRequestId,
                    new ModifiedStructure(args.OperationId, "committed", false, value.Changes, "not_guaranteed", drawing_view_creation_results: value.Details), context, warnings);
                Need(JsonSerializer.SerializeToUtf8Bytes(result, Wire.Json).Length <= Wire.PayloadBudget - 8192, "Committed Paper view response exceeds wire bytes.", "RESPONSE_LIMIT_EXCEEDED");
                job.RecordCommit(result); return result;
            }, error => Failure(error));
        }
        catch (Exception error)
        {
            if (job.CommittedResult is { } committed) return committed; Exception? cleanup = null; try { Close(); } catch (Exception failure) { cleanup = failure; }
            return Failure(error, cleanup);
        }
    }
}
