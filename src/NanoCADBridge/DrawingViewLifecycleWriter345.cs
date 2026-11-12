using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Geometry;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Db = Teigha.DatabaseServices;
using static NanoCADBridge.CadDrawingViewEditState;
using static NanoCADBridge.CadDrawingViewLifecycleState345;

namespace NanoCADBridge;

// Native MultiCAD lifecycle only. The selectors never retain a write transaction.
internal static class DrawingViewLifecycleWriter345
{
    internal sealed record Prepared(Snapshot Before, View[] Selected, McObjectId[] Sources, Dictionary<McObjectId, JsonElement> Owned);
    public static bool Handles(StructureWriteArguments args) => args.Operations.Length == 1 &&
        args.Operations[0] is SetDrawingViewSources345 or RenameDrawingView345 or AddDrawingViewChild345;
    private static DrawingViewLifecycleTarget345[] Targets(StructureEdit edit) => edit switch
    {
        SetDrawingViewSources345 sources => sources.Targets,
        RenameDrawingView345 rename => new[] { rename.Target },
        AddDrawingViewChild345 child => new[] { child.Parent },
        _ => throw new DomainException("INVALID_OPERATION_ORDER", "Use one native view lifecycle operation.")
    };
    private static DrawingView.ViewDirectionType Direction(string value) => value switch
    {
        "top" => DrawingView.ViewDirectionType.kViewTop,
        "bottom" => DrawingView.ViewDirectionType.kViewBottom,
        "left" => DrawingView.ViewDirectionType.kViewLeft,
        "right" => DrawingView.ViewDirectionType.kViewRight,
        "iso_top_left" => DrawingView.ViewDirectionType.kViewIsoTopLeft,
        "iso_top_right" => DrawingView.ViewDirectionType.kViewIsoTopRight,
        "iso_bottom_left" => DrawingView.ViewDirectionType.kViewIsoBottomLeft,
        "iso_bottom_right" => DrawingView.ViewDirectionType.kViewIsoBottomRight,
        _ => throw new DomainException("INVALID_ARGUMENT", "Unsupported native child direction.")
    };
    private static McObjectId[] Sources(SetDrawingViewSources345 edit, Snapshot snapshot, Db.Transaction tx, Action cancel)
    {
        var result = new List<McObjectId>(); var budget = new ModelInventoryBudget(cancel);
        foreach (string handle in edit.SourceHandles)
        {
            cancel(); var source = snapshot.Bodies.SingleOrDefault(body => body.Model && CadDrawingViewLifecycleState345.Handle(body.Entity) == handle);
            Need(source != null && !source.Id.IsNull, "Source is not an addressable top-level Model solid.", "DRAWING_VIEW_SOURCE_UNAVAILABLE");
            var entity = tx.GetObject(source!.Entity, Db.OpenMode.ForRead) as Db.Solid3d;
            Need(entity != null && entity.GetType() == typeof(Db.Solid3d), "Source must be an exact current ordinary native Solid3d.", "OUT_OF_SCOPE");
            var layer = (Db.LayerTableRecord)tx.GetObject(entity!.LayerId, Db.OpenMode.ForRead);
            Need(entity.Visible && !layer.IsDependent && !layer.IsLocked && !layer.IsOff && !layer.IsFrozen, "Source layer must be local, visible, thawed and unlocked.", "LAYER_UNAVAILABLE");
            budget.Add(source.Id.ToString()); result.Add(source.Id);
        }
        Need(result.Distinct().Count() == result.Count, "Source identities are duplicated."); return result.ToArray();
    }
    private static Prepared Prepare(StructureWriteArguments args, DocumentState state, Db.Transaction tx, Action cancel)
    {
        Need(Handles(args), "Native lifecycle operation must be sole.", "INVALID_OPERATION_ORDER");
        var snapshot = CadDrawingViewLifecycleState345.Capture(state, tx, cancel); Tree(snapshot, cancel);
        var selected = Targets(args.Operations[0]).Select(target => CadDrawingViewLifecycleState345.Resolve(target, snapshot, tx, cancel)).ToArray();
        CadDrawingViewEditWriter.RequireSupportedViews(selected, cancel);
        var selectedIds = selected.Select(view => view.Id).ToHashSet(); Need(selectedIds.Count == selected.Length, "Each view appears once.");
        var owned = new Dictionary<McObjectId, JsonElement>(); var budget = new ModelInventoryBudget(cancel);
        foreach (var view in selected)
        {
            cancel(); owned.Add(view.Id, Owned(view, tx, cancel)); budget.Add(owned[view.Id]);
            var refs = SourceRefs(view.Native.GetTop3dEntitiesInView(), state.Document.Database, tx, args.BridgeId, args.DocumentId, cancel);
            Need(refs.Refs != null, "Current selected view sources need their owning source adapter.", "DRAWING_VIEW_SOURCE_UNAVAILABLE");
        }
        McObjectId[] sources = Array.Empty<McObjectId>();
        if (args.Operations[0] is SetDrawingViewSources345 sourceEdit)
        {
            sources = Sources(sourceEdit, snapshot, tx, cancel); var sourceIds = sources.ToHashSet();
            foreach (var view in selected)
            {
                cancel(); Ordinary(view);
                Need(view.Native.ParentViewID.IsNull || selectedIds.Contains(view.Native.ParentViewID), "Select the ordinary root and its complete descendants explicitly.", "DRAWING_VIEW_DEPENDENTS");
                Need(view.Native.ChildViews.All(child => { cancel(); return selectedIds.Contains(child); }), "Include every existing descendant in the explicit source group.", "DRAWING_VIEW_DEPENDENTS");
                Need(view.Native.HostSolidID.IsNull || sourceIds.Contains(view.Native.HostSolidID), "Retain the current native history host in the requested sources; host reassignment requires its owning adapter.", "DRAWING_VIEW_DEPENDENTS");
                Dependencies(view, selectedIds, cancel);
                ReactorGroup(view, snapshot, selectedIds, tx, cancel);
            }
        }
        string? name = args.Operations[0] switch { RenameDrawingView345 rename => rename.Name, AddDrawingViewChild345 child => child.Name, _ => null };
        if (name != null) Need(!snapshot.Views.Any(view => !selectedIds.Contains(view.Id) && string.Equals(view.Native.Name, name, StringComparison.OrdinalIgnoreCase)), "View name already exists.", "NAME_COLLISION");
        if (args.Operations[0] is AddDrawingViewChild345)
        {
            var parent = selected[0]; Ordinary(parent);
            Need(parent.Native.ParentViewID.IsNull, "Add a child to an ordinary root projection.", "OUT_OF_SCOPE");
            Need(state.Document.Database.CurrentSpaceId == parent.Space, "Activate the parent's owning Model/Paper space before adding its child.", "DOCUMENT_CONTEXT_MISMATCH");
            var refs = SourceRefs(parent.Native.GetTop3dEntitiesInView(), state.Document.Database, tx, args.BridgeId, args.DocumentId, cancel);
            Need(refs.Refs is { Length: > 0 }, "Parent must have an explicit addressable Model solid source set.", "DRAWING_VIEW_SOURCE_UNAVAILABLE");
            var related = snapshot.Views.Where(view => view.Id == parent.Id || view.Native.RootViewID == parent.Id).Select(view => view.Id).ToHashSet(); Dependencies(parent, related, cancel); ReactorGroup(parent, snapshot, related, tx, cancel);
        }
        CheckResultBudget(args, selected, sources, cancel); return new(snapshot, selected, sources, owned);
    }
    private static void Dependencies(View view, HashSet<McObjectId> group, Action cancel)
    {
        cancel(); var dependents = view.Native.GetAllDependent(false);
        Need(dependents != null, "Native view dependent inventory is unavailable.", "DRAWING_VIEW_DEPENDENTS");
        var budget = new ModelInventoryBudget(cancel);
        foreach (var id in dependents!) { cancel(); budget.Add(id.ToString()); Need(group.Contains(id), "A selected view has an unselected native dependent; use its owning lifecycle route.", "DRAWING_VIEW_DEPENDENTS"); }
        foreach (var id in view.Native.DbEntity.ChildrenIds) { cancel(); budget.Add(id.ToString()); Need(group.Contains(id), "Selected view owns an entity outside its native view group.", "DRAWING_VIEW_DEPENDENTS"); }
    }
    private static void CheckResultBudget(StructureWriteArguments args, View[] selected, McObjectId[] sources, Action cancel)
    {
        string maximumHandle = "FFFFFFFFFFFFFFFF"; bool child = args.Operations[0] is AddDrawingViewChild345;
        var counts = new List<object>();
        foreach (var view in selected)
        {
            cancel(); var ids = args.Operations[0] is SetDrawingViewSources345 ? sources : view.Native.GetTop3dEntitiesInView()?.ToArray() ?? Array.Empty<McObjectId>();
            counts.Add(new
            {
                operation_index = 0,
                view_ref = new EntityRef(args.BridgeId, args.DocumentId, maximumHandle),
                view_token = new string('F', 64),
                source_refs = ids.Select(_ => new EntityRef(args.BridgeId, args.DocumentId, maximumHandle)),
                top_source_ids = ids.Select(id => id.ToString()),
                parent_view_id = Id(view.Native.ParentViewID),
                root_view_id = Id(view.Native.RootViewID),
                child_view_ids = view.Native.ChildViews.Select(id => id.ToString()).Concat(child ? new[] { new string('F', 128) } : Array.Empty<string>())
            });
        }
        if (child) counts.Add(counts[0]);
        string name = args.Operations[0] switch { RenameDrawingView345 rename => rename.Name, AddDrawingViewChild345 add => add.Name, _ => selected[0].Native.Name };
        var changes = selected.Select(view => new StructureChange(0, args.Operations[0].Operation, new(args.BridgeId, args.DocumentId, maximumHandle), "drawing_view",
            args.Operations[0] is RenameDrawingView345 rename ? rename.Name : view.Native.Name, false)).ToList();
        if (child) changes.Add(new(0, args.Operations[0].Operation, new(args.BridgeId, args.DocumentId, maximumHandle), "drawing_view", name, true));
        Bytes(new { operation_id = args.OperationId, status = "committed", replayed = false, changes, undo_history = "not_guaranteed", drawing_view_lifecycle_changes = counts }, cancel, Wire.PayloadBudget - 16384);
    }
    public static void Preflight(StructureWriteArguments args, DocumentState state, Db.Transaction tx, Action cancel) => Prepare(args, state, tx, cancel);
    private static JsonElement Expected(View view, StructureEdit edit, View? child)
    {
        var result = JsonNode.Parse(view.Metadata.GetRawText())!.AsObject();
        if (edit is RenameDrawingView345 rename && rename.Target.Target.Handle == CadDrawingViewLifecycleState345.Handle(view.Entity)) result["name"] = rename.Name;
        if (child != null && edit is AddDrawingViewChild345 add && add.Parent.Target.Handle == CadDrawingViewLifecycleState345.Handle(view.Entity))
        {
            result["children"] = JsonSerializer.SerializeToNode(view.Metadata.GetProperty("children").EnumerateArray().Select(item => item.GetString()).Append(child.Id.ToString()).OrderBy(id => id, StringComparer.Ordinal), Wire.Json);
            if (child.Native.DbEntity.ParentId == view.Id) result["db_children"] = JsonSerializer.SerializeToNode(view.Metadata.GetProperty("db_children").EnumerateArray().Select(item => item.GetString()).Append(child.Id.ToString()).OrderBy(id => id, StringComparer.Ordinal), Wire.Json);
        }
        return JsonSerializer.SerializeToElement(result, Wire.Json);
    }
    private static McObjectId[] Verify(Prepared plan, Snapshot after, StructureEdit edit, View? child, Db.Transaction tx, Action cancel, Action<string, JsonElement?, JsonElement?>? diagnostic = null)
    {
        var before = plan.Before; diagnostic?.Invoke("document_context_and_layouts", Wire.Element(new { before.Context, before.Layouts }), Wire.Element(new { after.Context, after.Layouts }));
        Need(ModelParameterSnapshot.Equivalent(before.Context, after.Context) && ModelParameterSnapshot.Equivalent(before.Layouts, after.Layouts), "Native view lifecycle changed drawing/layout context.", "DOCUMENT_CONTEXT_MISMATCH");
        var additions = after.Views.Where(view => before.Views.All(old => old.Id != view.Id)).ToArray();
        diagnostic?.Invoke("view_addition_identity", null, null);
        Need(child == null ? additions.Length == 0 : additions.Length == 1 && additions[0].Id == child.Id, "Native lifecycle added an unexpected view.", "DRAWING_VIEW_UPDATE_MISMATCH");
        var entities = after.Entities.EnumerateArray().Where(item => child == null || item.GetProperty("id").GetString() != CadDrawingViewLifecycleState345.Handle(child.Entity)).ToArray();
        var retainedEntities = Wire.Element(entities); diagnostic?.Invoke("retained_entity_identity_owner_display", before.Entities, retainedEntities);
        Need(ModelParameterSnapshot.Equivalent(before.Entities, retainedEntities), "Unselected native entity identity, ownership or display changed.", "DRAWING_VIEW_UPDATE_MISMATCH");
        diagnostic?.Invoke("section_global_processing", before.Sections, after.Sections);
        Need(ModelParameterSnapshot.Equivalent(before.Sections, after.Sections), "Ordinary view lifecycle changed a section/global processing set.", "DRAWING_VIEW_UPDATE_MISMATCH");
        var selected = plan.Selected.Select(view => view.Id).ToHashSet();
        diagnostic?.Invoke("source_history_transition", null, null);
        var sourceHistory = edit is SetDrawingViewSources345 ?
            DrawingViewSourceHistory349.Observe(before, after, plan.Selected, plan.Sources, cancel) :
            new DrawingViewSourceHistory349.Plan(new HashSet<McObjectId>(), new Dictionary<McObjectId, DrawingViewSourceHistory349.BodyState>());
        foreach (var old in before.Views)
        {
            cancel(); var current = after.Views.SingleOrDefault(view => view.Id == old.Id);
            diagnostic?.Invoke("retained_view_representation_owner", old.Metadata, current?.Metadata);
            Need(current != null && current.Entity == old.Entity && current.Layout == old.Layout && current.Space == old.Space, "Retained view lost its exact native representation/owner.", "DRAWING_VIEW_UPDATE_MISMATCH");
            var expected = JsonNode.Parse(Expected(old, edit, child).GetRawText())!.AsObject();
            if (edit is SetDrawingViewSources345 && selected.Contains(old.Id))
            {
                expected["sources"] = JsonSerializer.SerializeToNode(plan.Sources.Select(id => id.ToString()).OrderBy(id => id, StringComparer.Ordinal), Wire.Json);
                DrawingViewSourceHistory349.ExpectedView(expected, old.Id, sourceHistory);
                // Automatic source-centering may move the projection-plane origin.
                // The actual direction/axes and placed 2D view plane stay guarded.
                if (old.Metadata.GetProperty("auto_projection_plane").GetBoolean()) expected["projection_plane"]!["origin"] = JsonNode.Parse(current!.Metadata.GetProperty("projection_plane").GetProperty("origin").GetRawText());
            }
            var expectedMetadata = JsonSerializer.SerializeToElement(expected, Wire.Json);
            diagnostic?.Invoke("expected_view_sources_tree_history_properties", expectedMetadata, current!.Metadata);
            Need(ModelParameterSnapshot.Equivalent(expectedMetadata, current!.Metadata), "Native sources/tree/history or unrequested view properties changed.", "DRAWING_VIEW_UPDATE_MISMATCH");
            if (selected.Contains(old.Id))
            {
                var actualOwned = Owned(current, tx, cancel); var expectedOwned = plan.Owned[old.Id];
                if (child != null && edit is AddDrawingViewChild345 add && add.Parent.Target.Handle == CadDrawingViewLifecycleState345.Handle(old.Entity))
                {
                    string childHandle = CadDrawingViewLifecycleState345.Handle(child.Entity);
                    var actualLinks = actualOwned.GetProperty("persistent_reactors");
                    if (actualLinks.EnumerateArray().Any(item => item.GetProperty("handle").GetString() == childHandle))
                    {
                        // The only permitted added root reactor is the exact
                        // newly verified child on its requested retained parent.
                        var entity = (Db.Entity)tx.GetObject(child.Entity, Db.OpenMode.ForRead);
                        var value = JsonNode.Parse(expectedOwned.GetRawText())!.AsObject();
                        var links = expectedOwned.GetProperty("persistent_reactors").EnumerateArray().Select(item => item.Clone()).ToList();
                        links.Add(Wire.Element(new { handle = childHandle, owner_handle = entity.OwnerId.IsNull ? null : CadDrawingViewLifecycleState345.Handle(entity.OwnerId), runtime_type = entity.GetType().FullName }));
                        value["persistent_reactors"] = JsonSerializer.SerializeToNode(links.OrderBy(item => item.GetProperty("handle").GetString(), StringComparer.Ordinal), Wire.Json);
                        expectedOwned = JsonSerializer.SerializeToElement(value, Wire.Json);
                    }
                }
                diagnostic?.Invoke("selected_view_owned_metadata", expectedOwned, actualOwned);
                Need(expectedOwned.GetRawText() == actualOwned.GetRawText(), "Selected view foreign owned metadata changed.", "DRAWING_VIEW_UPDATE_MISMATCH");
            }
        }
        diagnostic?.Invoke("source_body_inventory_count", null, null);
        Need(before.Bodies.Length == after.Bodies.Length, "Native view lifecycle added/removed a source body.", "DRAWING_VIEW_UPDATE_MISMATCH");
        foreach (var old in before.Bodies)
        {
            cancel(); var current = after.Bodies.SingleOrDefault(body => body.Entity == old.Entity);
            diagnostic?.Invoke("retained_source_body_identity_geometry", old.Geometry, current?.Geometry);
            Need(current != null && current.Id == old.Id && current.Model == old.Model && ModelParameterSnapshot.Equivalent(old.Geometry, current.Geometry), "Source body identity/geometry changed.", "DRAWING_VIEW_UPDATE_MISMATCH");
            bool attached = child != null && child.Native.HostSolidID == old.Id;
            sourceHistory.Bodies.TryGetValue(old.Id, out var released);
            var wanted = (released?.Contents ?? old.Contents).ToHashSet(); if (attached) wanted.Add(child!.Id);
            var expectedLast = released?.Last ?? old.Last; var expectedNodes = released?.Nodes ?? old.Nodes;
            diagnostic?.Invoke("source_body_history_membership_terminal", expectedNodes, current!.Nodes);
            Need(wanted.SetEquals(current!.Contents) && (current.Last == expectedLast || attached && current.Last == child!.Id), "Source body history membership/terminal changed unexpectedly.", "DRAWING_VIEW_UPDATE_MISMATCH");
            var retained = current.Nodes.EnumerateArray().Where(item => !attached || item.GetProperty("id").GetString() != child!.Id.ToString()).ToArray();
            var retainedNodes = Wire.Element(retained); diagnostic?.Invoke("retained_source_feature_status_order", expectedNodes, retainedNodes);
            Need(ModelParameterSnapshot.Equivalent(expectedNodes, retainedNodes), "Retained source feature status/order changed.", "DRAWING_VIEW_UPDATE_MISMATCH");
            if (attached)
            {
                diagnostic?.Invoke("created_child_retained_predecessor", null, null);
                Need(current.Previous.TryGetValue(child!.Id, out var predecessor) && !predecessor.IsNull && old.Contents.Contains(predecessor), "New child history predecessor is not a retained item in its native host.", "DRAWING_VIEW_UPDATE_MISMATCH");
            }
        }
        diagnostic?.Invoke("complete_view_tree", null, null); Tree(after, cancel); return sourceHistory.Detached.ToArray();
    }
    private sealed record VerificationFingerprint(string path, string? expected_sha256, string? current_sha256);
    private static object VerificationDetails(string component, JsonElement? before, JsonElement? after, Action cancel)
    {
        // Completed managed snapshots only. No native getters or admission changes.
        const int detailBytes = 8192;
        try
        {
            var fields = new List<VerificationFingerprint>(); int different = 0, bytes = 1024;
            bool Equal(JsonElement? first, JsonElement? second) => first.HasValue == second.HasValue &&
                (!first.HasValue || first.Value.GetRawText() == second!.Value.GetRawText());
            string? Hash(JsonElement? value)
            {
                cancel(); if (!value.HasValue) return null;
                var raw = System.Text.Encoding.UTF8.GetBytes(value.Value.GetRawText()); cancel();
                var result = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(raw)); cancel(); return result;
            }
            void Add(string path, JsonElement? first, JsonElement? second)
            {
                cancel(); if (Equal(first, second)) return; different++;
                var field = new VerificationFingerprint(path, Hash(first), Hash(second));
                int size = JsonSerializer.SerializeToUtf8Bytes(field, Wire.Json).Length + 1;
                if ((long)bytes + size <= detailBytes) { fields.Add(field); bytes += size; }
            }
            void Walk(string path, JsonElement? first, JsonElement? second)
            {
                cancel(); if (Equal(first, second)) return;
                Add(path, first, second);
                if (!first.HasValue || !second.HasValue || first.Value.ValueKind != second.Value.ValueKind) return;
                if (first.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in first.Value.EnumerateObject())
                    { cancel(); Walk(path + "/" + property.Name, property.Value, second.Value.TryGetProperty(property.Name, out var current) ? current : (JsonElement?)null); }
                    foreach (var property in second.Value.EnumerateObject())
                    { cancel(); if (!first.Value.TryGetProperty(property.Name, out _)) Walk(path + "/" + property.Name, null, property.Value); }
                }
                else if (first.Value.ValueKind == JsonValueKind.Array)
                {
                    int count = Math.Max(first.Value.GetArrayLength(), second.Value.GetArrayLength());
                    for (int index = 0; index < count; index++)
                    { cancel(); Walk(path + "/" + index, index < first.Value.GetArrayLength() ? first.Value[index] : null, index < second.Value.GetArrayLength() ? second.Value[index] : null); }
                }
            }
            Walk("/snapshot", before, after);
            var result = new
            {
                diagnostic_scope = "completed_view_verification_component_fingerprints_only",
                component,
                comparison_basis = "Already computed expected/current snapshots; raw serialized differences are diagnostic and do not replace the existing numeric-equivalence predicate.",
                snapshots_available = before.HasValue && after.HasValue,
                differing_component_count = different,
                changed_component_fingerprints = fields.ToArray(),
                omitted_differing_component_count = different - fields.Count
            };
            if (JsonSerializer.SerializeToUtf8Bytes(result, Wire.Json).Length > detailBytes)
                return new { diagnostic_unavailable_reason = "Captured view verification diagnostics exceed their byte budget; no complete changed-field list is claimed." };
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (System.Exception error)
        { return new { diagnostic_unavailable_reason = "Captured view verification diagnostics unavailable: " + error.GetType().Name }; }
    }
    private static void Child(View parent, View child, AddDrawingViewChild345 edit, Snapshot after, Action cancel)
    {
        cancel(); Ordinary(child);
        Need(child.Layout == parent.Layout && child.Space == parent.Space && child.Native.ParentViewID == parent.Id && child.Native.RootViewID == parent.Id,
            "New native child has incorrect layout/root/parent ownership.", "DRAWING_VIEW_UPDATE_MISMATCH");
        Need(child.Native.HostSolidID == parent.Native.HostSolidID, "New child changed its parent's native history host.", "DRAWING_VIEW_UPDATE_MISMATCH");
        var dbParent = child.Native.DbEntity.ParentId;
        Need(dbParent.IsNull || dbParent == parent.Id || !child.Native.HostSolidID.IsNull && dbParent == child.Native.HostSolidID, "New child has an unrelated native storage parent.", "DRAWING_VIEW_UPDATE_MISMATCH");
        Need(child.Native.Name == edit.Name && child.Native.DirectionType == Direction(edit.Direction) && child.Native.DbEntity.DScale == edit.ModelScale && child.Native.DbEntity.Scale == 1 && !child.Native.AlignOnParent,
            "New child name/direction/scale/alignment differs from the request.", "DRAWING_VIEW_UPDATE_MISMATCH");
        Need(child.Native.ChildViews.Count == 0 && Ids(parent.Native.GetTop3dEntitiesInView(), "Parent sources").SequenceEqual(Ids(child.Native.GetTop3dEntitiesInView(), "Child sources")), "New child source set/descendants differ.", "DRAWING_VIEW_UPDATE_MISMATCH");
        var plane = child.Native.ViewPlane; var expected = new Plane3d(new Point3d(edit.Origin.X, edit.Origin.Y, parent.Native.ViewPlane.Origin.Z), parent.Native.ViewPlane.DirU, parent.Native.ViewPlane.DirV);
        Need(ModelParameterSnapshot.Equivalent(Wire.Element(Plane(plane)), Wire.Element(Plane(expected))), "New child placed view plane differs from request.", "DRAWING_VIEW_UPDATE_MISMATCH");
    }
    private static void Geometry(View view, Action cancel)
    {
        cancel(); var geometry = view.Native.GetViewGeometry(true, 0);
        var failures = new List<System.Exception>();
        try { Need(geometry != null && geometry.Count > 0, "Native selected projection is empty/unavailable.", "VIEW_GEOMETRY_UNAVAILABLE"); }
        catch (System.Exception error) { failures.Add(error); }
        if (geometry != null) foreach (var item in geometry.Distinct(ReferenceEqualityComparer.Instance))
            if (item is IDisposable resource) try { resource.Dispose(); } catch (System.Exception error) { failures.Add(error); }
        if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Native projection inspection/resource release failed.", failures);
        cancel();
    }
    public static Result Execute(StructureWriteArguments args, DocumentState state, WriteJob job, ReadContext context, JsonLog log)
    {
        McUndoPoint? tracker = null; bool started = false;
        void Close() { var current = tracker; tracker = null; if (current != null) try { current.Stop(); } finally { current.Dispose(); } }
        try
        {
            Prepared plan; using (var tx = state.Document.Database.TransactionManager.StartTransaction()) plan = Prepare(args, state, tx, job.CheckCancellation);
            job.CheckCancellation(); Context(state); tracker = new McUndoPoint(false); tracker.Start(); started = true;
            log.Write(job.Operation, job.OriginalRequestId, "drawing_view_lifecycle_started", operationId: job.OperationId);
            DrawingView? newChild = null; var edit = args.Operations[0];
            if (edit is RenameDrawingView345 rename)
            { plan.Selected[0].Native.TryModify((uint)McDbObject.ObjChangesType.Geom); plan.Selected[0].Native.Name = rename.Name; }
            else if (edit is SetDrawingViewSources345)
                foreach (var view in plan.Selected) { job.CheckCancellation(); view.Native.TryModify((uint)McDbObject.ObjChangesType.Geom); view.Native.Set3dEntsIds(plan.Sources, true); }
            else
            {
                var child = (AddDrawingViewChild345)edit; var parent = plan.Selected[0]; var plane = parent.Native.ViewPlane;
                parent.Native.TryModify((uint)McDbObject.ObjChangesType.Geom);
                newChild = parent.Native.CreateChildView(Direction(child.Direction), new Plane3d(new Point3d(child.Origin.X, child.Origin.Y, plane.Origin.Z), plane.DirU, plane.DirV));
                Need(newChild != null && !newChild.ID.IsNull && plan.Before.Views.All(view => view.Id != newChild.ID), "Native child factory returned an invalid/existing identity.", "OBJECT_IDENTITY_MISMATCH");
                newChild!.AlignOnParent = false; newChild.Name = child.Name; newChild.DbEntity.DScale = child.ModelScale; newChild.DbEntity.Scale = 1;
                Need(newChild.DbEntity.AddToCurrentDocument(), "Native child insertion refused.", "NATIVE_INSERT_FAILED");
            }
            McObjectManager.UpdateAll(); job.CheckCancellation();
            (StructureChange[] Changes, DrawingViewLifecycleChange345[] Lifecycle, McObjectId[] Detached) Readback()
            {
                using var tx = state.Document.Database.TransactionManager.StartTransaction(); var after = CadDrawingViewLifecycleState345.Capture(state, tx, job.CheckCancellation);
                View? childView = newChild == null ? null : after.Views.SingleOrDefault(view => view.Id == newChild.ID);
                Need(newChild == null || childView != null, "Created child has no unique native drawing representation.", "OBJECT_IDENTITY_MISMATCH");
                string component = "verification_entry"; JsonElement? expectedSnapshot = null, currentSnapshot = null; McObjectId[] detached;
                try { detached = Verify(plan, after, edit, childView, tx, job.CheckCancellation, (name, expected, actual) => { component = name; expectedSnapshot = expected; currentSnapshot = actual; }); }
                catch (DomainException error) when (error.Error.code is "DRAWING_VIEW_UPDATE_MISMATCH" or "DOCUMENT_CONTEXT_MISMATCH")
                {
                    throw new DomainException(error.Error.code, error.Error.message, error.Error.retryable,
                    VerificationDetails(component, expectedSnapshot, currentSnapshot, job.CheckCancellation));
                }
                if (childView != null) Child(plan.Selected[0], childView, (AddDrawingViewChild345)edit, after, job.CheckCancellation);
                var selected = after.Views.Where(view => plan.Selected.Any(old => old.Id == view.Id) || childView?.Id == view.Id).ToArray();
                var changes = new List<StructureChange>(); var lifecycle = new List<DrawingViewLifecycleChange345>();
                foreach (var view in selected)
                {
                    job.CheckCancellation(); if (edit is not RenameDrawingView345) Geometry(view, job.CheckCancellation);
                    changes.Add(new(0, edit.Operation, new(args.BridgeId, args.DocumentId, CadDrawingViewLifecycleState345.Handle(view.Entity)), "drawing_view", view.Native.Name, childView?.Id == view.Id));
                    lifecycle.Add(Change(view, after, tx, args, job.CheckCancellation));
                }
                Bytes(new ModifiedStructure(args.OperationId, "committed", false, changes.ToArray(), "not_guaranteed", drawing_view_lifecycle_changes: lifecycle.ToArray()), job.CheckCancellation, Wire.PayloadBudget - 8192);
                return (changes.ToArray(), lifecycle.ToArray(), detached);
            }
            Readback(); job.CheckCancellation(); job.BeginCommit(); Close(); var final = Readback();
            var warnings = new List<string> { Warning };
            if (final.Detached.Length > 0)
                warnings.Add("MULTI_SOURCE_VIEW_HISTORY: The selected projections retain the requested sources and view tree, and no longer occupy one part's native history. Remaining source history was verified; refresh view/source references before further edits.");
            if (edit is SetDrawingViewSources345 && plan.Selected.Any(view => view.Metadata.GetProperty("auto_projection_plane").GetBoolean()))
                warnings.Add("Native automatic projection may recenter its projection-plane origin after source changes; the placed view origin, scale and direction remain retained. Inspect the resulting view.");
            var result = Result.Ok(job.OriginalRequestId, new ModifiedStructure(args.OperationId, "committed", false, final.Changes, "not_guaranteed", drawing_view_lifecycle_changes: final.Lifecycle), context, warnings.ToArray());
            job.RecordCommit(result); return result;
        }
        catch (System.Exception error)
        {
            if (job.CommittedResult is { } committed) return committed; System.Exception? cleanup = null; try { Close(); } catch (System.Exception caught) { cleanup = caught; }
            string cause = error is DomainException domain ? domain.Error.code : error is OperationCanceledException ? "CANCELLED" : "MODEL_UPDATE_FAILED";
            return job.Error(started ? "WRITE_OUTCOME_UNKNOWN" : cause, started ? "Native view lifecycle started but its complete outcome was not verified. Inspect its exact source/tree/history before another mutation; replay only the identical request." : "Native view lifecycle request failed before changes started.",
                started ? "unknown" : "not_started", context, new
                {
                    cause,
                    error_type = error.GetType().Name,
                    cleanup_error = cleanup?.GetType().Name,
                    verification = error is DomainException diagnostic && diagnostic.Error.code is "DRAWING_VIEW_UPDATE_MISMATCH" or "DOCUMENT_CONTEXT_MISMATCH" ? diagnostic.Error.details : null
                });
        }
    }
}
