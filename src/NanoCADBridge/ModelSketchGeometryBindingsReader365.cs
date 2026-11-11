using System.Globalization;
using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.DatabaseServices.Constraints;
using Multicad.Geometry;
using Multicad.Mc3D;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Observe actual current native membership and marker/solver identity. Never
// enter the native editor, solve, create a manager's missing storage, infer an
// alias from coincident geometry, or require DBObject storage for abstract IDs.
internal static class ModelSketchGeometryBindingsReader365
{
    static string Key(McObjectId id) => id.ToString();
    static string? Optional(McObjectId id) => id.IsNull ? null : Key(id);
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool value, string message)
    { if (!value) throw new DomainException("SKETCH_BINDING_UNAVAILABLE", message); }
    static object? Shape(EntityGeometry? geometry)
    {
        if (geometry?.GeometryType == EntityGeomType.kLine)
        { using var line = geometry.LineSeg; return SketchGeometryReader.Line(line); }
        if (geometry?.GeometryType == EntityGeomType.kCircArc)
        { using var arc = geometry.CircArc; return SketchGeometryReader.Arc(arc); }
        return null;
    }
    static ModelSketchGeometryBindings365 Result(PlanarSketch sketch, McDocument owner, bool membersComplete,
        ModelSketchGeometryMember365[]? members, ModelSketchSolverScope365? scope, bool mappingComplete,
        ModelSketchSolverMember365[]? solver, string[]? missing, string? reason) => new(
        "current_native_sketch_geometry_membership_marker_and_solver_identity_only", Key(sketch.ID), Key(owner.ID),
        membersComplete, members, scope, mappingComplete, solver, missing, reason,
        "not_established_by_membership_marker_or_constraint_reference",
        "not_evaluated; native_edit_mode_not_entered",
        "observation_only; no_geometry_setter_or_driving_variable_storage_binding_proved");
    internal static ModelSketchGeometryBindings365 Read(PlanarSketch sketch, McDocument owner, JsonElement constraints, Action cancel,
        bool observeConstraintState = false)
    {
        Dictionary<McObjectId, EntityGeometry>? geometry = null; Constraints2D? manager = null;
        ModelSketchGeometryMember365[]? members = null; ModelSketchSolverScope365? scope = null;
        try
        {
            cancel(); Need(sketch.DbEntity.Document?.ID == owner.ID && !sketch.IsInEditMode,
                "The actual sketch owner must match and the sketch must be outside edit mode.");
            geometry = sketch.GetAllItemsGeometry(false);
            Need(geometry != null, "Current native geometry identity inventory returned null.");
            var objects = sketch.GetContainingObjects(true, false, false);
            var projections = sketch.GetContainingObjects(false, true, false);
            Need(objects != null && projections != null, "Current native nonprojection/projection membership returned null.");
            ModelInventoryBudget.Ids(objects, cancel); ModelInventoryBudget.Ids(projections, cancel);
            var budget = new ModelInventoryBudget(cancel); var observed = new List<ModelSketchGeometryMember365>();
            var markerCounts = geometry.Values.Where(value => value != null && value.GSMarker != -1)
                .GroupBy(value => value.GSMarker).ToDictionary(group => group.Key, group => group.Count());
            foreach (var pair in geometry.OrderBy(pair => Key(pair.Key), StringComparer.OrdinalIgnoreCase))
            {
                cancel(); Need(!pair.Key.IsNull && pair.Value != null, "Current native geometry contains an empty identity/wrapper.");
                var marker = pair.Value.GSMarker; string status = marker == -1 ? "native_marker_unavailable" :
                    markerCounts[marker] != 1 ? "native_marker_shared_by_multiple_geometry_items" : "native_marker_not_resolved";
                McObjectId mapped = McObjectId.Null; bool? equivalent = null;
                if (marker != -1 && markerCounts[marker] == 1)
                {
                    EntityGeometry? copy = null;
                    try
                    {
                        mapped = sketch.GetSketchEntityByGsMarker(marker, out copy, true); cancel();
                        status = mapped.IsNull ? "native_marker_not_resolved" : mapped == pair.Key ? "exact_current_element_roundtrip" : "native_marker_resolves_different_element";
                        var old = Shape(pair.Value); var current = Shape(copy);
                        if (old != null && current != null) equivalent = SketchGeometryComparison.Same(Wire.Element(old), Wire.Element(current));
                    }
                    finally { if (copy != null && !ReferenceEquals(copy, pair.Value)) copy.Dispose(); }
                }
                var member = new ModelSketchGeometryMember365(Key(pair.Key), pair.Value.GeometryType.ToString(), objects.Contains(pair.Key),
                    projections.Contains(pair.Key), marker == -1 ? null : marker.ToString(CultureInfo.InvariantCulture), Optional(mapped), status, equivalent);
                if (observeConstraintState)
                {
                    // MAPI documents this const query on a sketch geometry ID.
                    // The ID is the exact key from this sketch's current geometry.
                    try { member = member with { fully_constrained = sketch.IsGeomFullyConstrained(pair.Key) }; }
                    catch (Exception error) when (error is not OperationCanceledException)
                    { member = member with { fully_constrained_unavailable_reason = "Native query unavailable: " + error.GetType().Name }; }
                }
                budget.Add(member); observed.Add(member);
            }
            members = observed.ToArray(); cancel();
            using var plane = sketch.Plane; manager = owner.Get2dConstraintsManager(Constraint2dNetType.Auto, sketch.ID, plane);
            Need(manager != null, "Actual readonly constraint manager is unavailable.");
            scope = new(Key(manager.DocumentId), Optional(manager.PlanarSketchId), Optional(manager.ScopeId), manager.ConstraintsNetworkType.ToString());
            Need(manager.DocumentId == owner.ID && manager.PlanarSketchId == sketch.ID && !manager.ScopeId.IsNull,
                "Actual readonly constraint manager differs from the selected owner/sketch scope.");
            Need(constraints.GetProperty("document_id").GetString() == Key(owner.ID) && constraints.GetProperty("sketch_id").GetString() == Key(sketch.ID) &&
                constraints.GetProperty("scope_id").GetString() == Key(manager.ScopeId) && constraints.GetProperty("network").GetString() == scope.network && constraints.GetProperty("constraints_complete").GetBoolean(),
                "The retained native constraint inventory is unavailable or has a different actual scope.");
            var inventory = constraints.GetProperty("constraints").EnumerateArray().ToDictionary(value => value.GetProperty("id").GetString()!, StringComparer.OrdinalIgnoreCase);
            var map = manager.GetAllObjectsToConstraints(); cancel(); Need(map != null, "Native solver object-to-constraint map returned null; no empty-map claim.");
            var values = new List<ModelSketchSolverMember365>(); var mappedConstraints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var memberConstraints = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase); bool complete = true;
            foreach (var pair in map.OrderBy(pair => Key(pair.Key), StringComparer.OrdinalIgnoreCase))
            {
                cancel(); Need(!pair.Key.IsNull && pair.Value != null, "Native solver mapping contains a null object or unavailable constraint list.");
                var listed = pair.Value.Select(Key).ToArray();
                Need(pair.Value.All(id => !id.IsNull) && listed.Distinct(StringComparer.OrdinalIgnoreCase).Count() == listed.Length,
                    "Native solver member repeats or lacks a constraint identity.");
                memberConstraints.Add(Key(pair.Key), listed.ToHashSet(StringComparer.OrdinalIgnoreCase));
                foreach (string id in listed)
                {
                    cancel(); mappedConstraints.Add(id);
                    if (!inventory.TryGetValue(id, out var constraint)) { complete = false; continue; }
                    bool references = new[] { "first", "second", "third" }.Any(name => constraint.GetProperty(name) is var value &&
                        value.ValueKind == JsonValueKind.Object && value.GetProperty("object_id").GetString() == Key(pair.Key));
                    if (!references) complete = false;
                }
                var current = members.SingleOrDefault(member => member.element_id == Key(pair.Key));
                var item = new ModelSketchSolverMember365(Key(pair.Key), listed.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray(), current?.element_id,
                    current is { marker_status: "exact_current_element_roundtrip", marker_geometry_equivalent: true });
                budget.Add(item); values.Add(item);
            }
            foreach (var (id, constraint) in inventory)
            {
                cancel(); foreach (string name in new[] { "first", "second", "third" })
                {
                    var reference = constraint.GetProperty(name); if (reference.ValueKind == JsonValueKind.Null) continue;
                    Need(reference.ValueKind == JsonValueKind.Object, "A retained constraint has no actual native object reference.");
                    var objectId = reference.GetProperty("object_id");
                    Need(objectId.ValueKind == JsonValueKind.String, "A retained constraint has no actual native object reference identity.");
                    if (!memberConstraints.TryGetValue(objectId.GetString()!, out var bindings) || !bindings.Contains(id)) complete = false;
                }
            }
            var missing = inventory.Keys.Where(id => !mappedConstraints.Contains(id)).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray();
            complete &= missing.Length == 0;
            return Result(sketch, owner, true, members, scope, complete, values.ToArray(), missing,
                complete ? null : "native_solver_map_and_retained_constraint_reference_inventory_do_not_fully_agree");
        }
        catch (OperationCanceledException) { throw; }
        catch (DomainException error) { return Result(sketch, owner, members != null, members, scope, false, null, null, error.Error.code + ": " + error.Error.message); }
        catch (Exception error) { return Result(sketch, owner, members != null, members, scope, false, null, null, "Native binding observation unavailable: " + error.GetType().Name); }
        finally
        {
            // Disposal failures remain visible to the normal read/epoch guard.
            try { manager?.Dispose(); }
            finally { if (geometry != null) foreach (var item in geometry.Values.Distinct<EntityGeometry>(ReferenceEqualityComparer.Instance)) item?.Dispose(); }
        }
    }
}
