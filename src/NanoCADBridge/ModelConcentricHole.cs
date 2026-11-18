using Multicad;
using Multicad.DatabaseServices;
using Multicad.Geometry;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using System.Text.Json.Nodes;

namespace NanoCADBridge;

internal static class ModelConcentricHole
{
    internal sealed record Source(string EdgeKey, string FaceKey, string Creator, double[] Center, double[] Axis, double Radius, string BoundaryFace);
    internal sealed record Observation(string placement_type, string support_face_id, string? concentric_edge_id,
        string? source_ref_key, string? support_ref_key, string? source_creator_id, double[]? center, double[]? support_normal_wcs,
        double? source_radius, string? position_unavailable_reason, int? nonempty_reference_count,
        double[]? source_circle_center, string? current_boundary_face_id);

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

    internal static Observation Read(IppDefinition placement, McObjectId body)
    {
        string support = placement.TargetPlaneId.ToString();
        int? referenceCount = null;
        Source? source = null;
        string? edgeId = null;
        ModelManufacturingHistory.FixedFrame? frame = null;
        try
        {
            Need(placement.DefType == IppDefinitionType.Concentric, "The native placement is not concentric.");
            var refs = placement.GetAssocParams()?.Where(r => r != null && !r.IsNull).ToArray();
            referenceCount = refs?.Length;
            Need(refs is { Length: 1 }, "The native concentric placement must retain exactly one nonempty geometry parameter.");
            var edge = refs![0].ID;
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
    }
}
