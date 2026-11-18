using Multicad;
using Multicad.DatabaseServices;
using Multicad.DatabaseServices.Constraints;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db = Teigha.DatabaseServices;
using Teigha.Geometry;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCADBridge;

internal static class ModelThroughHole
{
    private static void Need(bool value, string message)
    {
        if (!value) throw new DomainException("HOLE_EXTENT_UNVERIFIED", message);
    }

    // Measure material remaining within the entire nominal bore on a detached
    // physical body. In the bore frame, the farthest positive Z of that
    // intersection is the depth required to reach its far end.
    internal static double? PhysicalDepth(Mc3dSolid body, HoleFeature hole, DocumentState state, Action cancel)
    {
        cancel();
        Need(hole.HostSolidID == body.ID && hole.HoleType == HoleFeatureType.Simple && hole.IsFullDepth &&
            !hole.IsSuppressed && double.IsFinite(hole.Diameter) && hole.Diameter > 0, "Select an active through Hole on its owning part.");
        using var placement = hole.GetPlacement();
        Need(placement?.DefType == IppDefinitionType.Fixed, "A fixed native hole frame is required for the physical depth check.");
        var frame = ModelManufacturingHistory.FixedPlacement(placement!);
        Need(frame.Center is { Length: 3 } && frame.Normal is { Length: 3 }, "The native hole entrance and direction are unavailable.");
        var c = frame.Center!;
        var n = frame.Normal!;
        var entrance = new Point3d(c[0], c[1], c[2]);
        var inward = new Vector3d(-n[0], -n[1], -n[2]).GetNormal();
        var database = state.Document.Database;
        using var tx = database.TransactionManager.StartTransaction();
        var id = ModelHistoryEditGuard.CurrentRepresentation(body, state, tx, cancel);
        using var material = (Db.Solid3d)((Db.Solid3d)tx.GetObject(id, Db.OpenMode.ForRead)).Clone();
        material.TransformBy(Matrix3d.Displacement(Point3d.Origin - entrance));
        material.TransformBy(Matrix3d.WorldToPlane(inward));
        double length = material.GeometricExtents.MaxPoint.Z;
        Need(double.IsFinite(length) && length > 0, "The native part has no positive extent along the drilling direction.");
        using var probe = new Db.Solid3d { RecordHistory = false };
        double radius = hole.Diameter / 2;
        probe.CreateFrustum(length, radius, radius, radius);
        probe.TransformBy(Matrix3d.Displacement(new Point3d(0, 0, length / 2) - probe.MassProperties.Centroid));
        cancel();
        if (!material.CheckInterference(probe)) return null;
        material.BooleanOperation(Db.BooleanOperationType.BoolIntersect, probe);
        cancel();
        if (material.IsNull) return null;
        double volume = material.MassProperties.Volume;
        Need(double.IsFinite(volume) && volume >= 0, "The bore intersection produced no finite physical volume.");
        if (volume == 0) return null;
        double depth = material.GeometricExtents.MaxPoint.Z;
        Need(double.IsFinite(depth) && depth > 0, "Residual bore material has no valid measured depth.");
        return depth;
    }

    private static JsonElement Controls(Mc3dSolid body, HoleFeature hole, Action cancel)
    {
        using var manager = body.DbEntity.Document.GetDocumentVariablesManager(Constraint2dNetType.MCS);
        var variables = manager?.GetAllVariables();
        Need(manager != null && manager.DocumentId == body.DbEntity.Document.ID && manager.ConstraintsNetworkType == Constraint2dNetType.MCS &&
            variables != null, "The Hole variable inventory could not be checked before recalculating its depth.");
        ModelInventoryBudget.Ids(variables!, cancel);
        var budget = new ModelInventoryBudget(cancel);
        var owned = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in variables!)
        {
            cancel();
            if (manager!.GetVariableHostObjectId(id) != hole.ID) continue;
            string? expression = manager.GetVariableInfo(id)?.Expression;
            Need(expression != null, "The Hole has an unreadable native driver.");
            budget.Add(new { id = id.ToString(), expression });
            owned.Add(id.ToString(), expression!);
        }
        return Wire.Element(owned);
    }

    internal static void EnsureClear(Mc3dSolid body, HoleFeature hole, DocumentState state, Action cancel)
    {
        if (PhysicalDepth(body, hole, state, cancel) is not { } depth) return;
        var controls = Controls(body, hole, cancel);
        var setter = NativeModelHistory421.LoadMeasuredThroughDepth();
        int status = setter(body.DbEntity.Document.ID.ToString(), body.ID.ToString(), hole.ID.ToString(), depth);
        Need(status == 0, $"The native Hole did not retain its measured depth in the through-all extent (HRESULT 0x{unchecked((uint)status):X8}).");
        hole.DbEntity.Update();
        McObjectManager.UpdateAll();
        Need(body.RebuildPart(), "The native part did not rebuild after recalculating its through hole.");
        McObjectManager.UpdateAll();
        Need(hole.IsFullDepth && ModelParameterSnapshot.Near(hole.Depth, depth) &&
            ModelParameterSnapshot.Equivalent(controls, Controls(body, hole, cancel)), "The through Hole lost its measured depth, mode or a native driver.");
        Need(PhysicalDepth(body, hole, state, cancel) == null, "The nominal through bore still contains native material after the depth correction.");
    }

    internal sealed record Support(McObjectId Feature, McObjectId Face, string? Key, Point3d? PlaneOrigin,
        Vector3d Normal, Point3d Entrance, double Depth, double ExtrusionChange, JsonElement PartReferences);

    private static Support? ReadSupport(Mc3dSolid body, HoleFeature hole, double extrusionChange)
    {
        using var placement = hole.GetPlacement();
        if (placement?.DefType != IppDefinitionType.Fixed) return null;
        var face = placement.TargetPlaneId;
        if (face.IsNull) return null;
        var frame = ModelManufacturingHistory.FixedPlacement(placement);
        if (frame.Center is not { Length: 3 } c || frame.Normal is not { Length: 3 } n) return null;
        var entrance = new Point3d(c[0], c[1], c[2]);
        var normal = new Vector3d(n[0], n[1], n[2]).GetNormal();
        var parts = placement.PartsIDs;
        var selected = placement.GetUserPartIds();
        if (parts?.Any(id => !id.IsNull && id != body.ID) == true || selected?.Any(id => !id.IsNull && id != body.ID) == true)
            return null;
        var references = Wire.Element(new
        {
            first = placement.FirstPartID.ToString(),
            limit = placement.LimitPlaneId.ToString(),
            parts = parts?.Select(id => id.ToString()).ToArray(),
            selected = selected?.Select(id => id.ToString()).ToArray()
        });
        Point3d? planeOrigin = null;
        string? faceKey = null;
        if (face.IsFace && !face.IsInexactFEV && face.ParentId == body.ID)
        {
            using var key = face.RefKey;
            if (key != null && !key.IsNull && McObjectId.FromRefKey(body.ID, key) == face)
            {
                faceKey = key.ToString();
                using var geometry = Service.GetFEVGeometry(face, FEVGeometryQueryMode.IdMatchExact | FEVGeometryQueryMode.CacheIgnore |
                    FEVGeometryQueryMode.EnvelopeExact | FEVGeometryQueryMode.CacheLock);
                using var plane = geometry?.Plane;
                if (plane != null)
                {
                    var origin = new Point3d(plane.Origin.X, plane.Origin.Y, plane.Origin.Z);
                    var axis = new Vector3d(plane.Normal.X, plane.Normal.Y, plane.Normal.Z).GetNormal();
                    if (Math.Abs((entrance - origin).DotProduct(normal)) > 1e-7 || Math.Abs(normal.DotProduct(axis)) < 1 - 1e-9)
                        return null;
                    planeOrigin = origin;
                }
            }
        }
        return new(hole.ID, face, faceKey, planeOrigin, normal, entrance, hole.Depth, extrusionChange, references);
    }

    internal static Support? Observe(ModelParameterBody part, DocumentState state, double extrusionChange, Action cancel)
    {
        var hole = part.Views.LastModelItem.GetObjectOfType<HoleFeature>();
        if (hole == null || hole.HostSolidID != part.Body.ID || !hole.IsFullDepth || hole.IsSuppressed ||
            hole.HoleType != HoleFeatureType.Simple || hole.Status.ToString() != "Ok") return null;
        var before = ReadSupport(part.Body, hole, extrusionChange);
        return before != null && PhysicalDepth(part.Body, hole, state, cancel) == null ? before : null;
    }

    internal static Support RetainedSupport(ModelParameterBody part, Support before)
    {
        var hole = before.Feature.GetObjectOfType<HoleFeature>();
        Need(hole != null && hole.HostSolidID == part.Body.ID && hole.IsFullDepth && !hole.IsSuppressed,
            "The through Hole or its retained owner changed during the upstream edit.");
        var current = ReadSupport(part.Body, hole!, before.ExtrusionChange);
        Need(current != null && current.Face == before.Face && current.Key == before.Key &&
            ModelParameterSnapshot.Equivalent(before.PartReferences, current.PartReferences) &&
            (current.Normal - before.Normal).Length <= 1e-7, "The through Hole lost its original support or direction.");
        var shift = current!.Entrance - before.Entrance;
        double axialShift = shift.DotProduct(before.Normal);
        // SDK 3DCore.h GetPosition places its origin on the stored target plane
        // with Z along that plane's normal. A historical target need not be a
        // face of the finished solid. Keep that same target and require the
        // measured axial travel to agree with the one changed stock distance.
        if (before.PlaneOrigin is { } oldPlane && current.PlaneOrigin is { } newPlane)
            axialShift = (newPlane - oldPlane).DotProduct(before.Normal);
        else Need(before.PlaneOrigin.HasValue == current.PlaneOrigin.HasValue &&
            (Math.Abs(axialShift) <= 1e-7 || ModelParameterSnapshot.Near(Math.Abs(axialShift), Math.Abs(before.ExtrusionChange))),
            "The historical support motion differs from the controlling extrusion change.");
        var expectedShift = before.Normal * axialShift;
        Need((shift - expectedShift).Length <= 1e-7, "The native Hole center did not retain its in-plane location.");
        return current;
    }

    internal static JsonElement Expected(JsonElement snapshot, Support before, Support verified)
    {
        var result = JsonNode.Parse(snapshot.GetRawText())!.AsObject();
        var node = result["nodes"]!.AsArray().Single(n => n!["mc_id"]!.GetValue<string>() == before.Feature.ToString());
        var parameters = node!["parameters"]!;
        parameters["placement"]!["center"] = JsonSerializer.SerializeToNode(new[] { verified.Entrance.X, verified.Entrance.Y, verified.Entrance.Z });
        parameters["depth"] = verified.Depth;
        return Wire.Element(result);
    }
}
