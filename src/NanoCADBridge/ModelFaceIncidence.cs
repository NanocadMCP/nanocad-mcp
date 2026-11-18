using Multicad;
using Multicad.Geometry;
using Multicad.Mc3D;
using NanoCAD.Contracts;

namespace NanoCADBridge;

internal static class ModelFaceIncidence
{
    internal static HashSet<McObjectId> Edges(McObjectId body, IEnumerable<long> faceMarkers, string faceId,
        Action check, HashSet<McObjectId>? neighbors = null)
    {
        foreach (long marker in faceMarkers)
        {
            check();
            var face = McObjectId.FromGsMarker(body, marker);
            if (face.IsNull || !face.IsFace || face.ParentId != body || face.GsMarker != marker ||
                !string.Equals(face.ToString(), faceId, StringComparison.OrdinalIgnoreCase)) continue;
            var edges = Service.GetLinkedFEVsToObject(face, EntityGeomType.kCurveEntities, false)
                ?? throw new DomainException("SUBENTITY_IDENTITY_UNAVAILABLE", "Native boundary incidence is unavailable for the selected face.");
            var verified = new HashSet<McObjectId>();
            foreach (var edge in edges)
            {
                check();
                if (edge.IsNull || !edge.IsEdge || edge.ParentId != body)
                    throw new DomainException("SUBENTITY_IDENTITY_UNAVAILABLE", "A boundary edge has no verified owner in the selected body.");
                if (!verified.Add(edge)) continue;
                var faces = Service.GetLinkedFEVsToObject(edge, EntityGeomType.kSurfaceEntities, false);
                if (faces == null || !faces.Contains(face))
                    throw new DomainException("SUBENTITY_IDENTITY_UNAVAILABLE", "The native edge lacks a reciprocal link to the selected face.");
                if (neighbors != null)
                    foreach (var other in faces)
                        if (other.IsFace && other.ParentId == body && other != face) neighbors.Add(other);
            }
            return verified;
        }
        throw new DomainException("OBJECT_NOT_FOUND", "The selected model face is absent from the current body.");
    }
}
