using Multicad;
using Multicad.DatabaseServices;
using Multicad.Geometry;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace NanoCADBridge;

internal static class ModelConcentricHole
{
    internal sealed record Source(string EdgeKey, string FaceKey, string Creator, double[] Center, double[] Axis, double Radius, string BoundaryFace);
    internal sealed record Observation(string placement_type, string support_face_id, string? concentric_edge_id,
        string? source_ref_key, string? support_ref_key, string? source_creator_id, double[]? center, double[]? support_normal_wcs,
        double? source_radius, string? position_unavailable_reason, int? nonempty_reference_count,
        double[]? source_circle_center, string? current_boundary_face_id)
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? reference_geometry_scope { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? current_bore_face_id { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? source_geometry_unavailable_reason { get; init; }
    }

    private static void Need(bool condition, string reason)
    {
        if (!condition) throw new DomainException("CONCENTRIC_PLACEMENT_UNAVAILABLE", reason);
    }

    private static bool Near(double x, double y) => double.IsFinite(x) && double.IsFinite(y) && Math.Abs(x - y) <= 1e-6;
    private static bool Point(double[]? a, double[] b) => a?.Length == 3 && a.Zip(b, Near).All(value => value);
    private static bool Axis(double[]? a, double[] b) => a?.Length == 3 &&
        Near(Math.Abs(a.Zip(b, (x, y) => x * y).Sum()), 1) && Near(a.Sum(v => v * v), 1) && Near(b.Sum(v => v * v), 1);

    private static string Key(McObjectId id, McObjectId body)
    {
        Need(!id.IsNull && !id.IsInexactFEV && id.ParentId == body, "The selected hole reference is not exact in the local body.");
        using var key = id.RefKey;
        Need(key != null && !key.IsNull && McObjectId.FromRefKey(body, key) == id, "The hole reference key did not round-trip on its source solid.");
        return key!.ToString();
    }

    internal sealed record CreatorLink(string creator_id, string edge_id, string hole_id);

    internal static CreatorLink? ReadCreatorLink(HoleFeature hole, IEnumerable<McObjectId> dependencies)
    {
        try
        {
            using var placement = hole.GetPlacement();
            if (placement?.DefType != IppDefinitionType.Concentric) return null;
            var refs = placement.GetAssocParams()?.Where(item => item != null && !item.IsNull).ToArray();
            if (refs is not { Length: 1 }) return null;
            var edge = refs[0].ID;
            var body = hole.HostSolidID;
            if (body.IsNull || edge.IsNull || !edge.IsEdge || !dependencies.Contains(edge)) return null;
            Key(edge, body);
            var creator = Service.GetFeatureObjectIdForFEV(edge);
            if (creator.IsNull || creator == hole.ID || creator.GetObjectOfType<HistoryItem>() is not { } source ||
                source.ID != creator || source.HostSolidID != body) return null;
            return new(creator.ToString(), edge.ToString(), hole.ID.ToString());
        }
        catch (Exception ex) when (ex is DomainException or NotSupportedException or NotImplementedException or Teigha.Runtime.Exception)
        {
            // A hole can outlive its original edge. Keep the native history inventory
            // available when this additional local provenance cannot be established.
            return null;
        }
    }

    private static (double[] Center, double[] Axis, double Radius) Circle(EntityGeometry? geometry)
    {
        Need(geometry != null && geometry.GeometryType == EntityGeomType.kCircArc, "The native placement parameter does not evaluate to a circle.");
        using var arc = geometry!.CircArc;
        Need(arc != null && double.IsFinite(arc.Radius) && arc.Radius > 0 &&
            Near(arc.LengthOfCurve / arc.Radius, 2 * Math.PI), "The native placement requires a complete circular boundary.");
        return ([arc!.Center.X, arc.Center.Y, arc.Center.Z], [arc.Normal.X, arc.Normal.Y, arc.Normal.Z], arc.Radius);
    }

    private const FEVGeometryQueryMode ExactModel = FEVGeometryQueryMode.IdMatchExact | FEVGeometryQueryMode.CacheIgnore |
        FEVGeometryQueryMode.EnvelopeExact | FEVGeometryQueryMode.CacheLock;

    internal static Source Capture(McObjectId body, McObjectId face, McObjectId edge, bool retained = false)
    {
        Need(face.IsFace && edge.IsEdge, "The hole must use a planar support and an actual boundary edge.");
        string faceKey = Key(face, body), edgeKey = Key(edge, body);
        using var g = Service.GetFEVGeometry(edge, ExactModel);
        var circle = Circle(g);
        var creator = Service.GetFeatureObjectIdForFEV(edge);
        Need(!creator.IsNull && creator.GetObjectOfType<HistoryItem>()?.HostSolidID == body,
            "The circular edge has no verified creating feature in the selected part.");

        var neighbors = Service.GetLinkedFEVsToObject(edge, EntityGeomType.kSurfaceEntities, false);
        bool BoundedPlanarFace(McObjectId candidate)
        {
            if (candidate.IsNull || candidate.IsInexactFEV || !candidate.IsFace || candidate.ParentId != body) return false;
            using var geometry = Service.GetFEVGeometry(candidate, ExactModel);
            using var plane = geometry?.Plane;
            if (plane == null || !Axis([plane.Normal.X, plane.Normal.Y, plane.Normal.Z], circle.Axis)) return false;
            var distance = (circle.Center[0] - plane.Origin.X) * plane.Normal.X +
                (circle.Center[1] - plane.Origin.Y) * plane.Normal.Y + (circle.Center[2] - plane.Origin.Z) * plane.Normal.Z;
            return Near(distance, 0) &&
                Service.GetLinkedFEVsToObject(candidate, EntityGeomType.kCurveEntities, false)?.Contains(edge) == true;
        }
        Need(neighbors != null, "The selected circle has no native incident surface inventory.");
        McObjectId boundary = face;
        if (!neighbors!.Contains(face) || !BoundedPlanarFace(face))
        {
            // A Hole trims its input face. TargetPlaneId and its reversible key still
            // identify the original support, while the intact exact circular edge
            // identifies its present boundary by reciprocal topology on the same body.
            Need(retained, "The selected circle has no reciprocal planar boundary incidence with the selected face.");
            var faces = neighbors.Distinct().Where(BoundedPlanarFace).ToArray();
            Need(faces.Length == 1, "The retained circle must bound one current planar face in the evaluated placement plane.");
            boundary = faces[0];
        }
        return new(edgeKey, faceKey, creator.ToString(), circle.Center, circle.Axis, circle.Radius, boundary.ToString());
    }

    internal static IppDefinition Prepare(McObjectId body, McObjectId face, McObjectId edge, Source source)
    {
        var placement = IppDefinition.Create(IppDefinitionType.Concentric);
        Need(placement != null, "The native host did not create a detached concentric placement.");
        try
        {
            placement!.TargetPlaneId = face;
            var reference = new McGeomParam { ID = edge };
            using var geometry = reference.GetGeometry();
            var circle = Circle(geometry);
            Need(Point(circle.Center, source.Center) && Near(circle.Radius, source.Radius) && Axis(circle.Axis, source.Axis),
                "The native geometry parameter does not describe the selected complete circle.");
            placement.SetAssocParams(reference, new McGeomParam());
            Verify(placement, body, source);
            return placement;
        }
        catch { placement!.Dispose(); throw; }
    }

    // A stored Concentric input can be consumed by a later native finish.
    // Prove actual feature ordering and the exact edge consumers in this
    // part. A final-model rim of a similar size has no role in this lookup.
    private static bool HasFinishingConsumer(Mc3dSolid part, HoleFeature hole, McObjectId edge, McObjectId creator, Action cancel)
    {
        bool foundFinish = false, passedHole = false;
        var seen = new HashSet<McObjectId>();
        for (var id = part.LastPartItemID; !id.IsNull && seen.Add(id);)
        {
            cancel();
            var item = id.GetObjectOfType<HistoryItem>();
            if (item == null || item.HostSolidID != part.ID) break;
            if (id == creator) return foundFinish && passedHole;
            if (id == hole.ID) passedHole = true;
            if (!passedHole && !foundFinish && !item.IsSuppressed && item.Status.ToString() == "Ok")
            {
                var edges = id.GetObjectOfType<FilletFeature>()?.GetEdges() ?? id.GetObjectOfType<ChamferFeature>()?.GetEdges();
                foundFinish = edges != null && edges.Contains(edge);
            }
            id = item.OrderObjBefore;
        }
        return false;
    }

    private static (string Face, double[] Center) CurrentBore(Mc3dSolid part, HoleFeature hole, double[] planePoint, double[] axis, Action cancel)
    {
        var markers = part.GetSolidBody()?.GetFaces();
        Need(markers != null, "The current body has no readable native face inventory.");
        (string Face, double[] Center)? matched = null;
        foreach (var marker in markers!)
        {
            cancel();
            var face = McObjectId.FromGsMarker(part.ID, marker);
            if (face.IsNull || !face.IsFace || face.IsInexactFEV || face.ParentId != part.ID || face.GsMarker != marker ||
                Service.GetFeatureObjectIdForFEV(face) != hole.ID) continue;
            using var geometry = Service.GetFEVGeometry(face, ExactModel);
            using var cylinder = geometry?.GeometryType == EntityGeomType.kCylinderSegment ? geometry.CylinderSurf : null;
            if (cylinder == null || !Near(2 * cylinder.Radius, hole.Diameter) || !Axis([cylinder.Axis.X, cylinder.Axis.Y, cylinder.Axis.Z], axis)) continue;
            var rims = Service.GetLinkedFEVsToObject(face, EntityGeomType.kCurveEntities, false);
            foreach (var rim in rims ?? [])
            {
                cancel();
                if (rim.IsNull || !rim.IsEdge || rim.IsInexactFEV || rim.ParentId != part.ID) continue;
                using var ring = Service.GetFEVGeometry(rim, ExactModel);
                using var arc = ring?.GeometryType == EntityGeomType.kCircArc ? ring.CircArc : null;
                if (arc == null || !Near(arc.Radius, cylinder.Radius) || !Near(arc.LengthOfCurve / arc.Radius, 2 * Math.PI) ||
                    !Axis([arc.Normal.X, arc.Normal.Y, arc.Normal.Z], axis)) continue;
                double[] center = [arc.Center.X, arc.Center.Y, arc.Center.Z];
                if (!Near(center.Zip(planePoint, (a, b) => a - b).Zip(axis, (a, b) => a * b).Sum(), 0)) continue;
                Need(matched == null, "The stored support plane has multiple present bore rims created by this Hole.");
                matched = (face.ToString(), center);
            }
        }
        Need(matched != null, "The current hole has no uniquely verified cylindrical bore rim on its support plane.");
        return matched!.Value;
    }

    private static Observation Historical(McObjectId body, HoleFeature hole, IppDefinition placement, McObjectId edge, Action cancel)
    {
        var support = placement.TargetPlaneId;
        string edgeKey = Key(edge, body), supportKey = Key(support, body);
        Need(edge.IsEdge && support.IsFace && hole.HostSolidID == body && !hole.IsSuppressed && hole.Status.ToString() == "Ok" &&
            hole.GetDependsOn(false, false)?.Contains(edge) == true, "Stored source is not an exact dependency of the current Hole.");
        var identified = new Observation("Concentric", support.ToString(), edge.ToString(), edgeKey, supportKey, null, null, null,
            null, null, 1, null, null)
        { reference_geometry_scope = "stored_exact_references_only" };
        var part = body.GetObjectOfType<Mc3dSolid>();
        McObjectId creator;
        try
        {
            creator = Service.GetFeatureObjectIdForFEV(edge);
            Need(part != null && !creator.IsNull && HasFinishingConsumer(part, hole, edge, creator, cancel),
                "The current part does not prove an earlier source and a later finishing feature on the same edge.");
        }
        catch (Exception ex) when (ex is DomainException or NotSupportedException or NotImplementedException or Teigha.Runtime.Exception)
        { return identified with { position_unavailable_reason = ex.Message }; }
        // The SDK recommends exact ID with model-then-cache for stored FEVs.
        // CacheLock prevents this read from mutating the model-owned cache.
        const FEVGeometryQueryMode stored = FEVGeometryQueryMode.ExactGeometry | FEVGeometryQueryMode.CacheLock;
        (double[] Center, double[] Axis, double Radius)? circle = null;
        string? sourceReason = null;
        try
        {
            using var geometry = Service.GetFEVGeometry(edge, stored);
            circle = Circle(geometry);
        }
        catch (Exception ex) when (ex is DomainException or NotSupportedException or NotImplementedException or Teigha.Runtime.Exception)
        { sourceReason = ex.Message; }
        var partial = new Observation("Concentric", support.ToString(), edge.ToString(), edgeKey, supportKey, creator.ToString(), null, null,
            circle?.Radius, null, 1, circle?.Center, null)
        {
            reference_geometry_scope = "stored_exact_edge_before_finishing",
            source_geometry_unavailable_reason = sourceReason
        };
        try
        {
            using var face = Service.GetFEVGeometry(support, stored);
            using var plane = face?.Plane;
            var frame = ModelManufacturingHistory.FixedPlacement(placement);
            double[]? point = plane == null ? frame.Center : [plane.Origin.X, plane.Origin.Y, plane.Origin.Z];
            double[]? axis = plane == null ? frame.Normal : [plane.Normal.X, plane.Normal.Y, plane.Normal.Z];
            Need(point != null && axis != null && Axis(axis, axis), "The stored support and native placement frame have no verified plane.");
            if (circle is { } historical)
                Need(Axis(axis, historical.Axis) && Near(historical.Center.Zip(point!, (a, b) => a - b).Zip(axis!, (a, b) => a * b).Sum(), 0),
                    "The exact stored circle disagrees with its support plane.");
            var bore = CurrentBore(part!, hole, point!, axis!, cancel);
            if (frame.Unavailable == null)
                Need(Point(frame.Center, bore.Center) && Axis(frame.Normal, axis!), "The current bore rim and the native placement frame disagree.");
            return partial with
            {
                center = bore.Center,
                support_normal_wcs = axis,
                current_bore_face_id = bore.Face,
                reference_geometry_scope = "stored_exact_edge_before_finishing; current_center_measured_on_hole_owned_bore_rim"
            };
        }
        catch (Exception ex) when (ex is DomainException or NotSupportedException or NotImplementedException or Teigha.Runtime.Exception)
        { return partial with { position_unavailable_reason = ex.Message }; }
    }

    internal static Observation Read(IppDefinition placement, McObjectId body, HoleFeature? historyHole = null, Action? cancel = null)
    {
        string support = placement.TargetPlaneId.ToString();
        int? referenceCount = null;
        Source? source = null;
        string? edgeId = null;
        var input = McObjectId.Null;
        ModelManufacturingHistory.FixedFrame? frame = null;
        try
        {
            Need(placement.DefType == IppDefinitionType.Concentric, "The native placement is not concentric.");
            var refs = placement.GetAssocParams()?.Where(r => r != null && !r.IsNull).ToArray();
            referenceCount = refs?.Length;
            Need(refs is { Length: 1 }, "The native concentric placement must retain exactly one nonempty geometry parameter.");
            var edge = refs![0].ID;
            input = edge;
            edgeId = edge.ToString();
            source = Capture(body, placement.TargetPlaneId, edge, retained: true);
            using var geometry = refs[0].GetGeometry();
            var circle = Circle(geometry);
            frame = ModelManufacturingHistory.FixedPlacement(placement);
            Need(Point(source.Center, circle.Center) && Axis(source.Axis, circle.Axis) && Near(source.Radius, circle.Radius),
                "The retained placement parameter does not evaluate to its verified source circle.");
            Need(frame.Unavailable == null, "The native concentric placement frame is unavailable: " + frame.Unavailable);
            Need(Point(frame.Center, circle.Center) && Axis(frame.Normal, circle.Axis),
                "The native concentric placement frame differs from the center or normal of its source circle.");
            return new("Concentric", support, edgeId, source.EdgeKey, source.FaceKey, source.Creator, frame.Center, frame.Normal,
                source.Radius, null, referenceCount, source.Center, source.BoundaryFace);
        }
        catch (Exception ex) when (ex is DomainException or NotSupportedException or NotImplementedException or Teigha.Runtime.Exception)
        {
            if (historyHole != null && referenceCount == 1 && !input.IsNull)
            {
                try { return Historical(body, historyHole, placement, input, cancel ?? (() => { })); }
                catch (Exception unverified) when (unverified is DomainException or NotSupportedException or NotImplementedException or Teigha.Runtime.Exception)
                {
                    return new("Concentric", support, source == null ? null : edgeId, source?.EdgeKey, source?.FaceKey, source?.Creator,
                        frame?.Center, frame?.Normal, source?.Radius, ex.Message + " " + unverified.Message, referenceCount, source?.Center, source?.BoundaryFace);
                }
            }
            return new("Concentric", support, source == null ? null : edgeId, source?.EdgeKey, source?.FaceKey, source?.Creator,
                frame?.Center, frame?.Normal, source?.Radius, ex.Message, referenceCount, source?.Center, source?.BoundaryFace);
        }
    }

    internal static void Verify(IppDefinition placement, McObjectId body, Source expected)
    {
        var actual = Read(placement, body);
        if (actual.position_unavailable_reason != null || actual.source_ref_key != expected.EdgeKey ||
            actual.support_ref_key != expected.FaceKey || actual.source_creator_id != expected.Creator ||
            !Point(actual.center, expected.Center) || !Axis(actual.support_normal_wcs, expected.Axis) ||
            actual.source_radius is not { } radius || !Near(radius, expected.Radius))
            throw new DomainException("CONCENTRIC_PLACEMENT_UNAVAILABLE",
                "The host did not retain the selected concentric reference and its evaluated placement.", false, new { actual });
    }

    // Called on both complete snapshots. Read has measured the frame against the
    // live circle and proven the local face/edge incidence and both reversible keys.
    internal static void NormalizeAffectedCenter(JsonObject parameters, IReadOnlySet<string> affected)
    {
        if (parameters["placement"] is not JsonObject p || p["placement_type"]?.GetValue<string>() != "Concentric" ||
            p["source_creator_id"]?.GetValue<string>() is not { } creator || !affected.Contains(creator) ||
            p["source_ref_key"] is null || p["support_ref_key"] is null || p["position_unavailable_reason"] != null ||
            p["center"] is not JsonArray { Count: 3 }) return;
        p.Remove("center");
        p.Remove("source_circle_center");
        p.Remove("concentric_edge_id");
        p.Remove("support_face_id");
        p.Remove("current_boundary_face_id");
        p.Remove("current_bore_face_id");
    }
}
