using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Brep = Teigha.BoundaryRepresentation.Brep;
using Face = Teigha.BoundaryRepresentation.Face;

namespace NanoCADBridge;

internal static partial class CadSolidMeasurement409
{
    private sealed record DirectFaceGeometry(AnalyticSupport Support, double? DatumDistance, CylinderBand? Band);

    internal static object ReadDirectFacePage(Solid3d body, EntityRef source, string token, ModelFaceQuery query,
        string[] ids, Dictionary<string, HashSet<string>> boundaries, Action check)
    {
        if (query.StartIndex > ids.Length)
            throw new DomainException("INVALID_ARGUMENT", "start_index exceeds the number of eligible B-rep faces.");
        var page = ids.Skip(query.StartIndex).Take(query.PageSize).ToArray();
        var pending = page.ToHashSet(StringComparer.Ordinal);
        var geometry = new Dictionary<string, DirectFaceGeometry>(StringComparer.Ordinal);
        var unresolved = new List<object>();
        void Unknown(string id, DomainException e) => unresolved.Add(new { native_id = id, reason = e.Error });
        using (var brep = new Brep(body))
        {
            Need(!brep.IsNull, "Native B-rep was lost during the query.", "OBJECT_READ_FAILED");
            foreach (Face face in brep.Faces)
                using (face)
                {
                    check();
                    var id = Identity(face, body, SubentityType.Face);
                    if (!pending.Remove(id)) continue;
                    try
                    {
                        using var surface = face.Surface;
                        AnalyticSupport support;
                        if (query.Surface == "plane")
                        {
                            var (origin, normal) = ReadPlane(surface, id, check);
                            support = new(origin, normal, null);
                        }
                        else support = ReadCylinder(surface, id, check);
                        if (!query.Accept(Point(support.Origin), Point(support.Direction), support.Diameter / 2, out var distance)) continue;
                        var band = query.CylinderRole == null ? null : ReadCylinderBand(body, face, support, check);
                        geometry.Add(id, new(support, distance, band));
                    }
                    catch (DomainException e) when (e.Error.code is "NATIVE_FACE_PLANE_UNAVAILABLE" or
                        "UNSUPPORTED_MEASUREMENT_GEOMETRY" or "UNSUPPORTED_CYLINDER_TRIM")
                    {
                        Unknown(id, e);
                    }
                }
        }
        Need(pending.Count == 0, "B-rep faces no longer match the inspected snapshot.", "SUBENTITY_IDENTITY_UNAVAILABLE");
        var candidates = new List<object>();
        foreach (var id in page)
        {
            check();
            if (!geometry.TryGetValue(id, out var g)) continue;
            string? role = null;
            if (g.Band != null)
            {
                try { role = MaterialInside(body, g.Band, new(source.handle, id), check) ? "external" : "bore"; }
                catch (DomainException e) when (e.Error.code is "CYLINDER_ROLE_UNAVAILABLE" or "SECTION_UNAVAILABLE" or "SECTION_BOUNDARY_UNAVAILABLE")
                {
                    Unknown(id, e);
                    continue;
                }
                if (role != query.CylinderRole) continue;
            }
            candidates.Add(new
            {
                native_id = id,
                measurement_face = new { handle = source.handle, native_id = id },
                support_origin = Point(g.Support.Origin),
                support_direction = Point(g.Support.Direction),
                radius = g.Support.Diameter / 2,
                datum_distance = g.DatumDistance,
                cylinder_role = role,
                boundary_edge_ids = boundaries[id].OrderBy(edge => edge, StringComparer.Ordinal).ToArray()
            });
        }
        int next = query.StartIndex + page.Length;
        return new
        {
            source_ref = source,
            identity_space = "brep",
            snapshot_token = token,
            surface = query.Surface,
            coordinate_system = "WCS",
            candidate_faces_before_geometry_filters = ids.Length,
            start_index = query.StartIndex,
            next_index = next < ids.Length ? (int?)next : null,
            complete = next == ids.Length,
            page_filters_resolved = unresolved.Count == 0,
            candidates,
            unresolved,
            result_scope = "Recognized analytic supporting surfaces. Boundary IDs, selections and snapshot_token belong to this body and revision. Use native_id for direct edits and measurement_face for analyze_geometry. Check unresolved faces and all pages before selecting."
        };
    }
}
