using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Brep = Teigha.BoundaryRepresentation.Brep;
using BrepEntity = Teigha.BoundaryRepresentation.BrepEntity;
using Face = Teigha.BoundaryRepresentation.Face;
using Edge = Teigha.BoundaryRepresentation.Edge;
using Vertex = Teigha.BoundaryRepresentation.Vertex;

namespace NanoCADBridge;

internal static class SolidTopologyReader
{
    private sealed record FaceBounds(double[] min, double[] max);
    private sealed record FaceItem(string native_id, string surface_runtime_type, double? area, string? area_unavailable_reason, FaceBounds bounds, bool forward, string bounds_kind,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? analytic_surface);
    private sealed record EdgeItem(string native_id, string curve_runtime_type, double[]? start_vertex, double[]? end_vertex, bool forward, ModelEdgeReader.CircleGeometry? circle);
    public static object Read(Arguments args, DocumentState state, Transaction tx, ObjectId model, string instance, CancellationToken cancellation)
    {
        var db = state.Document.Database;
        long bits = unchecked((long)ulong.Parse(args.Handles![0], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        if (!db.TryGetObjectId(new Handle(bits), out var id) || id.IsNull || !id.IsValid) throw new DomainException("OBJECT_NOT_FOUND", "Solid not found.");
        if (id.IsErased || id.IsEffectivelyErased) throw new DomainException("OBJECT_ERASED", "Solid erased.");
        if (tx.GetObject(id, OpenMode.ForRead) is not Solid3d solid || solid.GetType() != typeof(Solid3d) || solid.OwnerId != model)
            throw new DomainException("OUT_OF_SCOPE", "Topology requires a top-level native Model Space Solid3d.");
        if (solid.IsNull) throw new DomainException("DEGENERATE_GEOMETRY", "Solid is empty.");
        return Capture(solid, new EntityRef(instance, state.Id, solid.Handle.ToString()), cancellation, identifySurfaces: true);
    }
    internal static object AttachNativeFaces(object topology, ObjectId id, DocumentState state, string instance, CancellationToken cancellation, bool faces, bool edges)
    {
        // MultiCAD face access can touch the host's model cache. Keep it outside
        // the Teigha ForRead transaction so abort/disposal cannot invalidate it.
        var merged = JsonNode.Parse(JsonSerializer.Serialize(topology, Wire.Json))!.AsObject();
        if (faces) merged["native_model_faces"] = JsonSerializer.SerializeToNode(ModelFaceReader.Capture(id, state, instance, cancellation), Wire.Json);
        if (edges) merged["native_model_edges"] = JsonSerializer.SerializeToNode(ModelEdgeReader.Capture(id, state, instance, cancellation), Wire.Json);
        return merged;
    }
    internal static object Capture(Solid3d solid, EntityRef source, CancellationToken cancellation, Action? checkCancel = null, bool identifySurfaces = false)
    {
        void Budget() { checkCancel?.Invoke(); cancellation.ThrowIfCancellationRequested(); }
        Budget();
        var inventoryBudget = new ModelInventoryBudget(Budget);
        var id = solid.ObjectId;
        SubentityId Identity(BrepEntity entity, SubentityType kind)
        {
            var path = entity.SubentityPath; var parents = path.GetObjectIds();
            // Brep(Solid3d) on this host returns one null parent, even for valid
            // subentities. Bind to the explicitly opened source and require a
            // the same live B-rep and its source throughout inspection.
            // A non-null foreign parent remains an error, as does an invalid ID.
            if (path.IsNull || parents.Length != 1 || (!parents[0].IsNull && parents[0] != id) || path.SubentId.Type != kind || path.SubentId.IndexPtr.ToInt64() <= 0)
                throw new DomainException("SUBENTITY_IDENTITY_UNAVAILABLE", "Native topology path does not identify this body and expected subentity kind.", false,
                    new { path_is_null = path.IsNull, parent_count = parents.Length, parent_matches = parents.Length == 1 && parents[0] == id, expected_kind = kind.ToString(), actual_kind = path.SubentId.Type.ToString() });
            return path.SubentId;
        }
        double[]? Point(Vertex? vertex)
        {
            using (vertex)
            {
                if (vertex == null || vertex.IsNull) return null;
                var p = vertex.Point; return new[] { Finite(p.X), Finite(p.Y), Finite(p.Z) };
            }
        }
        var faces = new List<FaceItem>(); var edges = new List<EdgeItem>();
        var edgeBounds = new Dictionary<string, FaceBounds>(StringComparer.Ordinal);
        var whole = solid.GeometricExtents;
        var wholeBounds = new FaceBounds(new[] { Finite(whole.MinPoint.X), Finite(whole.MinPoint.Y), Finite(whole.MinPoint.Z) }, new[] { Finite(whole.MaxPoint.X), Finite(whole.MaxPoint.Y), Finite(whole.MaxPoint.Z) });
        var faceIds = new HashSet<string>(StringComparer.Ordinal); var edgeIds = new HashSet<string>(StringComparer.Ordinal);
        using (var brep = new Brep(solid))
        {
            if (brep.IsNull) throw new DomainException("OBJECT_READ_FAILED", "B-rep is unavailable.");
            foreach (Edge edge in brep.Edges)
                using (edge)
                {
                    Budget();
                    var subent = Identity(edge, SubentityType.Edge);
                    string key = subent.IndexPtr.ToInt64().ToString(CultureInfo.InvariantCulture);
                    if (!edgeIds.Add(key)) throw new DomainException("SUBENTITY_IDENTITY_UNAVAILABLE", "Duplicate native edge identity.");
                    using var copy = solid.CopyEdge(subent);
                    if (copy == null || !copy.ObjectId.IsNull || copy is not Curve)
                        throw new DomainException("SUBENTITY_IDENTITY_UNAVAILABLE", "Source body did not resolve the edge to an owned transient curve.");
                    var box = copy.GeometricExtents; edgeBounds[key] = new(new[] { Finite(box.MinPoint.X), Finite(box.MinPoint.Y), Finite(box.MinPoint.Z) }, new[] { Finite(box.MaxPoint.X), Finite(box.MaxPoint.Y), Finite(box.MaxPoint.Z) });
                    using var curve = edge.Curve;
                    var circle = copy is Circle c ? new ModelEdgeReader.CircleGeometry(
                        [c.Center.X, c.Center.Y, c.Center.Z], [c.Normal.X, c.Normal.Y, c.Normal.Z], c.Radius) : null;
                    var observed = new EdgeItem(key, curve.GetType().FullName!, Point(edge.Vertex1), Point(edge.Vertex2), edge.IsOrientToCurve, circle);
                    inventoryBudget.Add(observed); edges.Add(observed); Budget();
                }
            foreach (Face face in brep.Faces)
                using (face)
                {
                    Budget();
                    var subent = Identity(face, SubentityType.Face);
                    string key = subent.IndexPtr.ToInt64().ToString(CultureInfo.InvariantCulture);
                    if (!faceIds.Add(key)) throw new DomainException("SUBENTITY_IDENTITY_UNAVAILABLE", "Duplicate native face identity.");
                    using var surface = face.Surface;
                    bool planar = surface is Plane || surface is ExternalBoundedSurface external && external.IsPlane;
                    var boundary = new List<FaceBounds>();
                    var boundaryBudget = new ModelInventoryBudget(Budget);
                    if (planar) foreach (Teigha.BoundaryRepresentation.BoundaryLoop loop in face.Loops)
                        using (loop)
                        {
                            Budget(); foreach (Edge edge in loop.Edges) using (edge)
                            {
                                Budget();
                                var edgeKey = Identity(edge, SubentityType.Edge).IndexPtr.ToInt64().ToString(CultureInfo.InvariantCulture);
                                if (!edgeBounds.TryGetValue(edgeKey, out var box)) throw new DomainException("SUBENTITY_IDENTITY_UNAVAILABLE", "Face boundary edge does not belong to this B-rep.");
                                boundaryBudget.Add(box); boundary.Add(box);
                            }
                        }
                    // Planar extrema lie on the complete boundary. For a curved face,
                    // boundary extrema need not enclose interior bulges: report the
                    // conservative source-body box explicitly, never guessed extrema.
                    var bounds = boundary.Count > 0 ? new FaceBounds(Enumerable.Range(0, 3).Select(i => boundary.Min(x => { Budget(); return x.min[i]; })).ToArray(), Enumerable.Range(0, 3).Select(i => boundary.Max(x => { Budget(); return x.max[i]; })).ToArray()) : wholeBounds;
                    string boundsKind = boundary.Count > 0 ? "planar_boundary" : "body_conservative";
                    // Native face area and BrepEntity.BoundBlock are not implemented.
                    // Never create/dispose CopyFace BODY/SURFACE temporaries to read.
                    double? area = null;
                    string? analyticSurface = identifySurfaces ? IdentifySurface(surface, planar) : null;
                    var observed = new FaceItem(key, surface.GetType().FullName!, area, area == null ? "Native face area is unavailable; topology inspection does not copy database faces." : null, bounds, face.IsOrientToSurface, boundsKind, analyticSurface);
                    inventoryBudget.Add(observed); faces.Add(observed); Budget();
                }

        }
        Budget();
        var orderedFaces = faces.OrderBy(x => x.native_id, StringComparer.Ordinal).ToArray();
        var orderedEdges = edges.OrderBy(x => x.native_id, StringComparer.Ordinal).ToArray();
        bool record = solid.RecordHistory, show = solid.ShowHistory;
        // Surface hints help choose measurement operands. The token also serves
        // write adapters, which hash the original topology observations only.
        var tokenFaces = identifySurfaces ? orderedFaces.Select(face => face with { analytic_surface = null }).ToArray() : orderedFaces;
        Budget(); var serialized = JsonSerializer.SerializeToUtf8Bytes(new { source, faces = tokenFaces, edges = orderedEdges, record, show }, Wire.Json);
        if (serialized.LongLength > 8L * 1024 * 1024) throw new DomainException("RESPONSE_LIMIT_EXCEEDED", "Native solid topology snapshot exceeds the actual8MiB serialized byte budget.");
        var token = Convert.ToHexString(SHA256.HashData(serialized)); Budget();
        return new
        {
            source_ref = source,
            coordinate_system = "WCS",
            reference_scope = "bridge/document/revision/snapshot only; reacquire after edits or regeneration",
            snapshot_token = token,
            complete = true,
            record_history = record,
            show_history = show,
            faces = orderedFaces,
            edges = orderedEdges
        };
    }

    private static string? IdentifySurface(Teigha.Geometry.Surface surface, bool planar)
    {
        if (planar) return "plane";
        try
        {
            return surface is Cylinder || surface is ExternalBoundedSurface bounded && bounded.IsCylinder ? "circular_cylinder" : null;
        }
        catch (Teigha.Runtime.Exception) { return null; }
    }
    private static double Finite(double value) => double.IsFinite(value) ? value : throw new DomainException("OBJECT_READ_FAILED", "Non-finite native topology value.");
}
