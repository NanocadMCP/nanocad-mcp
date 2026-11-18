using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using EntityGeomType = Multicad.Geometry.EntityGeomType;

namespace NanoCADBridge;

// MultiCAD feature edges have their own identity. Teigha B-rep subentity IDs
// from inspect_solid_topology must never be passed to AddFilletFeature.
internal static class ModelEdgeReader
{
    internal sealed record CircleGeometry(double[] center, double[] normal, double radius);
    internal sealed record Edge(string marker, string model_edge_id, string curve_type, double[] start, double[] end, double length, CircleGeometry? circle = null);
    internal sealed record Snapshot(EntityRef source_ref, string body_mc_id, string last_item_id, string edge_token, Edge[] edges);
    private sealed class Page(ModelEdgeQuery query)
    {
        internal ModelEdgeQuery Query => query;
        internal int Matches, BoundaryCount, Unclassified, Bytes;
        internal bool Full;
    }
    private static void Need(bool valid, string code, string message) { if (!valid) throw new DomainException(code, message); }
    public static object Read(Arguments args, DocumentState state, Transaction tx, ObjectId model, string instance, CancellationToken cancel)
    {
        var db = state.Document.Database;
        long bits = unchecked((long)ulong.Parse(args.Handles![0], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        Need(db.TryGetObjectId(new Handle(bits), out var id) && !id.IsNull && id.IsValid && !id.IsErased && !id.IsEffectivelyErased, "OBJECT_NOT_FOUND", "Model body handle is unavailable.");
        Need(tx.GetObject(id, OpenMode.ForRead) is Solid3d solid && solid.GetType() == typeof(Solid3d) && solid.OwnerId == model, "OUT_OF_SCOPE", "Require one top-level native model representation.");
        return Capture(id, state, instance, cancel);
    }
    internal static Snapshot Capture(ObjectId representation, DocumentState state, string instance, CancellationToken cancel, Action? checkCancel = null)
        => Scan(representation, state, instance, cancel, checkCancel, null);

    internal static McObjectId[] ResolveFeatureEdges(ObjectId representation, McObjectId body, DocumentState state,
        string instance, string expectedLast, string token, string[] ids, Action? check)
    {
        var wanted = ids.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var current = Scan(representation, state, instance, CancellationToken.None, check, null, wanted);
        Need(current.body_mc_id == body.ToString() && current.last_item_id.Equals(expectedLast, StringComparison.OrdinalIgnoreCase)
            && current.edge_token.Equals(token, StringComparison.OrdinalIgnoreCase), "STALE_TOPOLOGY", "Read the current edge token and history anchor on this body.");
        var byId = current.edges.ToDictionary(row => row.model_edge_id, StringComparer.OrdinalIgnoreCase);
        var result = new McObjectId[ids.Length];
        for (int i = 0; i < ids.Length; i++)
        {
            check?.Invoke();
            if (!byId.TryGetValue(ids[i], out var edge))
                throw new DomainException("SUBENTITY_NOT_FOUND", "The selected native model edge does not belong to this body.");
            var actual = McObjectId.FromGsMarker(body, long.Parse(edge.marker, CultureInfo.InvariantCulture));
            Need(!actual.IsNull && actual.IsEdge && !actual.IsInexactFEV && actual.ParentId == body && actual.ToString().Equals(ids[i], StringComparison.OrdinalIgnoreCase),
                "SUBENTITY_IDENTITY_UNAVAILABLE", "The selected edge changed before feature creation.");
            result[i] = actual;
        }
        return result;
    }

    internal static object BoundaryPage(ObjectId representation, DocumentState state, string instance, CancellationToken cancel, ModelEdgeQuery query)
    {
        var page = new Page(query);
        var result = Scan(representation, state, instance, cancel, null, page);
        if (query.StartIndex > page.Matches) throw new DomainException("INVALID_ARGUMENT", "start_index exceeds the number of matching model edges.");
        int next = query.StartIndex + result.edges.Length;
        return new
        {
            result.source_ref,
            result.body_mc_id,
            result.last_item_id,
            source_revision = state.Revision,
            result.edge_token,
            boundary_of_face_id = query.BoundaryOfFaceId,
            curve = query.Curve,
            boundary_edges = page.BoundaryCount,
            unavailable_curve_classifications = page.Unclassified,
            inventory_complete = page.Unclassified == 0,
            total_matches = page.Matches,
            start_index = query.StartIndex,
            next_index = next < page.Matches ? (int?)next : null,
            complete = next == page.Matches,
            edges = result.edges
        };
    }

    private static Snapshot Scan(ObjectId representation, DocumentState state, string instance, CancellationToken cancel, Action? checkCancel, Page? page, HashSet<string>? wanted = null)
    {
        void Budget() { checkCancel?.Invoke(); cancel.ThrowIfCancellationRequested(); }
        Budget(); var inventoryBudget = new ModelInventoryBudget(Budget);
        var active = McDocument.ActiveDocument;
        Need(state.Document.IsNamedDrawing && active != null && !string.IsNullOrWhiteSpace(active.Path) &&
             string.Equals(Path.GetFullPath(active.Path), Path.GetFullPath(state.Document.Name), StringComparison.OrdinalIgnoreCase),
             "DOCUMENT_CONTEXT_MISMATCH", "The active MultiCAD document differs from the requested named drawing.");
        var bodyId = McObjectId.FromHandle(representation.Handle.Value, active!.ID);
        NativeTopologyReadTrace362.Observe("edge_body_id");
        var body = bodyId.GetObjectOfType<Mc3dSolid>();
        NativeTopologyReadTrace362.Observe("edge_body_lookup");
        Need(body != null && body.ID == bodyId && !body.LastPartItemID.IsNull, "UNSUPPORTED_ENTITY_TYPE", "Selected Solid3d has no available native model history.");
        Budget(); var solidBody = body!.GetSolidBody();
        NativeTopologyReadTrace362.Observe("edge_solid_body");
        Need(solidBody != null, "HISTORY_UNAVAILABLE", "Native body geometry is unavailable.");
        var markers = solidBody!.GetEdges(); Budget();
        NativeTopologyReadTrace362.Observe("edge_markers");
        Need(markers != null && markers.Length > 0, "SUBENTITY_GEOMETRY_UNAVAILABLE", "Native model has no readable edges.");
        HashSet<McObjectId>? boundary = null;
        if (page != null)
        {
            var faces = solidBody.GetFaces() ?? throw new DomainException("SUBENTITY_IDENTITY_UNAVAILABLE", "Current body face identities are unavailable.");
            boundary = ModelFaceIncidence.Edges(bodyId, faces, page.Query.BoundaryOfFaceId, Budget);
            page.BoundaryCount = boundary.Count;
        }
        var edges = new List<Edge>(); var seen = new HashSet<long>();
        var source = new EntityRef(instance, state.Id, Validation.NormalizeHandle(representation.Handle.ToString()));
        string last = body.LastPartItemID.ToString();
        using var hash = SHA256.Create();
        using var stream = new CryptoStream(Stream.Null, hash, CryptoStreamMode.Write);
        using var json = new Utf8JsonWriter(stream);
        json.WriteStartObject(); json.WritePropertyName("source"); JsonSerializer.Serialize(json, source, Wire.Json);
        json.WriteString("body_mc_id", bodyId.ToString()); json.WriteString("last_item_id", last); json.WriteStartArray("edges");
        foreach (long marker in markers!.OrderBy(x => x))
        {
            Budget(); Need(marker > 0 && seen.Add(marker), "SUBENTITY_IDENTITY_UNAVAILABLE", "Duplicate or invalid native model edge marker.");
            var edgeId = McObjectId.FromGsMarker(bodyId, marker);
            NativeTopologyReadTrace362.Observe("edge_native_id", marker);
            Need(!edgeId.IsNull && edgeId.IsEdge && edgeId.ParentId == bodyId && edgeId.GsMarker == marker,
                "SUBENTITY_IDENTITY_UNAVAILABLE", "Marker did not resolve to an edge owned by this model.");
            // Exact current native ID, WCS geometry, model-only lookup and no cache update.
            using var geometry = Service.GetFEVGeometry(edgeId, FEVGeometryQueryMode.IdMatchExact | FEVGeometryQueryMode.CacheIgnore | FEVGeometryQueryMode.EnvelopeExact | FEVGeometryQueryMode.CacheLock);
            NativeTopologyReadTrace362.Observe("edge_subentity_geometry", marker);
            var curve = geometry?.AsCurve();
            Need(curve != null, "SUBENTITY_GEOMETRY_UNAVAILABLE", "Native edge curve is unavailable; no partial inventory returned.");
            var start = curve!.StartPoint; var end = curve.EndPoint; double length = curve.LengthOfCurve;
            var coordinates = new[] { start.X, start.Y, start.Z, end.X, end.Y, end.Z, length };
            Need(coordinates.All(double.IsFinite) && length > 1e-9, "SUBENTITY_GEOMETRY_UNAVAILABLE", "Native edge has invalid endpoints or length.");
            CircleGeometry? circle = null;
            bool circular = geometry!.GeometryType == EntityGeomType.kCircArc;
            bool knownCircularExtent = false;
            if (circular)
            {
                using var arc = geometry.CircArc;
                knownCircularExtent = arc != null && double.IsFinite(arc.Radius) && arc.Radius > 0;
                if (arc != null && knownCircularExtent &&
                   Math.Abs(length / arc.Radius - 2 * Math.PI) <= 1e-7 * 2 * Math.PI)
                    circle = new([arc.Center.X, arc.Center.Y, arc.Center.Z], [arc.Normal.X, arc.Normal.Y, arc.Normal.Z], arc.Radius);
            }
            var observed = new Edge(marker.ToString(CultureInfo.InvariantCulture), edgeId.ToString(), curve.GetType().Name,
                [start.X, start.Y, start.Z], [end.X, end.Y, end.Z], length, circle);
            JsonSerializer.Serialize(json, observed, Wire.Json);
            if (page == null)
            {
                if (wanted == null || wanted.Contains(observed.model_edge_id))
                {
                    if (wanted == null) inventoryBudget.Add(observed);
                    edges.Add(observed);
                }
            }
            else if (boundary!.Remove(edgeId))
            {
                bool matches = page.Query.Curve switch
                {
                    "line" => geometry.GeometryType == EntityGeomType.kLine,
                    "circle" => circle != null,
                    "circular_arc" => circular && knownCircularExtent && circle == null,
                    _ => true
                };
                if (circular && !knownCircularExtent && page.Query.Curve is "circle" or "circular_arc") page.Unclassified++;
                if (matches && page.Matches++ >= page.Query.StartIndex && !page.Full)
                {
                    int bytes = JsonSerializer.SerializeToUtf8Bytes(observed, Wire.Json).Length + 1;
                    if (bytes > Wire.PayloadBudget - 8192) throw new DomainException("RESPONSE_LIMIT_EXCEEDED", "An individual boundary edge exceeds the response budget.");
                    page.Full = edges.Count >= page.Query.PageSize || page.Bytes + bytes > Wire.PayloadBudget - 8192;
                    if (!page.Full) { edges.Add(observed); page.Bytes += bytes; }
                }
            }
            Budget();
            NativeTopologyReadTrace362.Observe("edge_descriptor", marker);
        }
        if (boundary?.Count > 0) throw new DomainException("SUBENTITY_IDENTITY_UNAVAILABLE", "A linked boundary edge was absent from the current native edge inventory.");
        json.WriteEndArray(); json.WriteEndObject(); json.Flush(); stream.FlushFinalBlock();
        if (page == null && wanted == null && json.BytesCommitted > 8L * 1024 * 1024) throw new DomainException("RESPONSE_LIMIT_EXCEEDED", "Native model edge snapshot exceeds the actual8MiB serialized byte budget.");
        var token = Convert.ToHexString(hash.Hash!); Budget();
        return new(source, bodyId.ToString(), last, token, edges.ToArray());
    }
}
