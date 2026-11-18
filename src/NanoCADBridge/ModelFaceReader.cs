using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Geometry;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// These are MultiCAD feature-face identities, not Teigha B-rep native_id values.
// Read planar support data so an opening face can be selected by its datum
// plane; no cross-kernel ID conversion is guessed.
internal static class ModelFaceReader
{
    private sealed record Plane(double[] origin, double[] normal);
    private sealed record Face(string marker, string model_face_id, string? surface_type,
        Plane? plane, string? detail_unavailable_reason, CylinderGeometry? cylinder = null);
    private sealed record CylinderGeometry(double[] axis_origin, double[] axis_direction, double radius, double length);
    private static void Need(bool ok, string code, string message)
    { if (!ok) throw new DomainException(code, message); }
    internal static object Capture(ObjectId representation, DocumentState state, string instance, CancellationToken cancel, Action? checkCancel = null, ModelFaceQuery? query = null)
    {
        void Budget() { checkCancel?.Invoke(); cancel.ThrowIfCancellationRequested(); }
        Budget(); var inventoryBudget = new ModelInventoryBudget(Budget);
        var active = McDocument.ActiveDocument;
        Need(state.Document.IsNamedDrawing && active != null && !string.IsNullOrWhiteSpace(active.Path) &&
            string.Equals(Path.GetFullPath(active.Path), Path.GetFullPath(state.Document.Name), StringComparison.OrdinalIgnoreCase),
            "DOCUMENT_CONTEXT_MISMATCH", "Native model faces require the exact active named DWG.");
        var bodyId = McObjectId.FromHandle(representation.Handle.Value, active!.ID);
        NativeTopologyReadTrace362.Observe("face_body_id");
        var body = bodyId.GetObjectOfType<Mc3dSolid>();
        NativeTopologyReadTrace362.Observe("face_body_lookup");
        Need(body != null && body.ID == bodyId && !body.LastPartItemID.IsNull,
            "UNSUPPORTED_ENTITY_TYPE", "Selected Solid3d has no native model history.");
        Budget(); var solidBody = body!.GetSolidBody();
        NativeTopologyReadTrace362.Observe("face_solid_body");
        Need(solidBody != null, "HISTORY_UNAVAILABLE", "Native model body geometry is unavailable.");
        var markers = solidBody!.GetFaces(); Budget();
        NativeTopologyReadTrace362.Observe("face_markers");
        Need(markers != null && markers.Length > 0,
            "SUBENTITY_IDENTITY_UNAVAILABLE", "Native model contains no readable faces.");
        var source = new EntityRef(instance, state.Id, Validation.NormalizeHandle(representation.Handle.ToString()));
        string last = body.LastPartItemID.ToString();
        var faces = new List<Face>(); var seen = new HashSet<long>();
        var candidates = new List<object>();
        var members = new Lazy<HashSet<McObjectId>?>(() => body.GetPartContents(false)?.ToHashSet());
        HashSet<McObjectId>? neighbors = null;
        if (query?.AdjacentTo != null)
        {
            neighbors = new();
            ModelFaceIncidence.Edges(bodyId, markers!, query.AdjacentTo, Budget, neighbors);
        }
        int matches = 0, unavailable = 0, pageBytes = 0;
        bool pageFull = false;
        using var hash = SHA256.Create();
        using var stream = new CryptoStream(Stream.Null, hash, CryptoStreamMode.Write);
        using var json = new Utf8JsonWriter(stream);
        json.WriteStartObject();
        json.WritePropertyName("source");
        JsonSerializer.Serialize(json, source, Wire.Json);
        json.WriteString("body_mc_id", bodyId.ToString());
        json.WriteString("last_item_id", last);
        json.WriteStartArray("faces");
        foreach (long marker in markers!.OrderBy(value => value))
        {
            Budget();
            Need(marker > 0 && seen.Add(marker), "SUBENTITY_IDENTITY_UNAVAILABLE", "Duplicate or invalid native model face marker.");
            var faceId = McObjectId.FromGsMarker(bodyId, marker);
            NativeTopologyReadTrace362.Observe("face_native_id", marker);
            Need(!faceId.IsNull && faceId.IsFace && faceId.ParentId == bodyId && faceId.GsMarker == marker,
                "SUBENTITY_IDENTITY_UNAVAILABLE", "Marker did not resolve to a face owned by this model.");
            // Exact current native ID, WCS geometry, model-only lookup and no cache update.
            using var geometry = Service.GetFEVGeometry(faceId, FEVGeometryQueryMode.IdMatchExact | FEVGeometryQueryMode.CacheIgnore | FEVGeometryQueryMode.EnvelopeExact | FEVGeometryQueryMode.CacheLock);
            NativeTopologyReadTrace362.Observe("face_subentity_geometry", marker);
            // Read typed borrowed descriptors. AsSurface constructs owning curved
            // surface wrappers in this SDK; do not materialize them for inspection.
            using var planar = geometry?.Plane;
            using var cylinder = geometry?.GeometryType == EntityGeomType.kCylinderSegment ? geometry.CylinderSurf : null;
            Plane? plane = null;
            if (planar != null)
            {
                var origin = planar.Origin; var normal = planar.Normal;
                var values = new[] { origin.X, origin.Y, origin.Z, normal.X, normal.Y, normal.Z };
                if (values.All(double.IsFinite) && normal.X * normal.X + normal.Y * normal.Y + normal.Z * normal.Z > 1e-18)
                    plane = new([origin.X, origin.Y, origin.Z], [normal.X, normal.Y, normal.Z]);
            }
            CylinderGeometry? cylinderGeometry = null;
            if (cylinder is { } support)
            {
                double[] axisOrigin = [support.BasePnt.X, support.BasePnt.Y, support.BasePnt.Z];
                double[] axisDirection = [support.Axis.X, support.Axis.Y, support.Axis.Z];
                if (axisOrigin.All(double.IsFinite) && axisDirection.All(double.IsFinite) && axisDirection.Any(x => x != 0) &&
                    double.IsFinite(support.Radius) && support.Radius > 0 && double.IsFinite(support.Length))
                    cylinderGeometry = new(axisOrigin, axisDirection, support.Radius, support.Length);
            }
            var observed = new Face(marker.ToString(CultureInfo.InvariantCulture), faceId.ToString(),
                planar?.GetType().Name ?? geometry?.GeometryType.ToString(), plane,
                plane == null ? geometry == null ? "native_surface_unavailable" : cylinderGeometry != null ? null :
                    cylinder != null ? "native_cylindrical_support_unavailable" : "not_a_finite_planar_support" : null, cylinderGeometry);
            JsonSerializer.Serialize(json, observed, Wire.Json);
            if (query == null)
            {
                inventoryBudget.Add(observed);
                faces.Add(observed);
            }
            else if (neighbors == null || neighbors.Contains(faceId))
            {
                var origin = query.Surface == "plane" ? observed.plane?.origin : observed.cylinder?.axis_origin;
                var direction = query.Surface == "plane" ? observed.plane?.normal : observed.cylinder?.axis_direction;
                if (origin == null || direction == null || !origin.All(double.IsFinite) || !direction.All(double.IsFinite) ||
                    direction.All(value => value == 0) || (query.Surface == "cylinder" && (observed.cylinder == null ||
                    !double.IsFinite(observed.cylinder.radius) || observed.cylinder.radius <= 0)))
                {
                    var expected = query.Surface == "plane" ? EntityGeomType.kPlaneSegment : EntityGeomType.kCylinderSegment;
                    if (geometry == null || geometry.GeometryType == expected || observed.surface_type == null) unavailable++;
                }
                else if (query.Accept(origin, direction, observed.cylinder?.radius, out var distance))
                {
                    if (matches >= query.StartIndex && !pageFull)
                    {
                        ProjectedFeatureSource.Feature? feature = null;
                        try
                        {
                            var creator = Service.GetFeatureObjectIdForFEV(faceId);
                            if (!creator.IsNull && members.Value?.Contains(creator) == true)
                                feature = ProjectedFeatureSource.Read(creator, bodyId);
                        }
                        catch (Exception error) when (error is NotImplementedException or NotSupportedException or Teigha.Runtime.Exception) { }
                        var item = new
                        {
                            face = observed,
                            datum_distance = distance,
                            feature,
                            feature_unavailable_reason = feature == null ? "native_creator_not_verified_in_selected_part" : null
                        };
                        int bytes = JsonSerializer.SerializeToUtf8Bytes(item, Wire.Json).Length + 1;
                        pageFull = candidates.Count >= query.PageSize || pageBytes + bytes > Wire.PayloadBudget - 8192;
                        if (!pageFull) { candidates.Add(item); pageBytes += bytes; }
                    }
                    matches++;
                }
            }
            NativeTopologyReadTrace362.Observe("face_descriptor", marker);
        }
        json.WriteEndArray(); json.WriteEndObject(); json.Flush();
        stream.FlushFinalBlock();
        if (query == null && json.BytesCommitted > 8L * 1024 * 1024)
            throw new DomainException("RESPONSE_LIMIT_EXCEEDED", "Native model face snapshot exceeds the actual8MiB serialized byte budget.");
        string token = Convert.ToHexString(hash.Hash!);
        Budget();
        if (query != null)
        {
            if (query.StartIndex > matches) throw new DomainException("INVALID_ARGUMENT", "start_index exceeds the number of matching model faces.");
            int next = query.StartIndex + candidates.Count;
            return new
            {
                source_ref = source,
                body_mc_id = bodyId.ToString(),
                face_token = token,
                selection_scope = "native_model_supports_in_WCS",
                surface = query.Surface,
                distance_reference = query.Surface == "plane" ? "supporting_plane" : "infinite_axis_line",
                scanned_faces = markers!.Length,
                unavailable_supports = unavailable,
                inventory_complete = unavailable == 0,
                total_matches = matches,
                start_index = query.StartIndex,
                next_index = next < matches ? (int?)next : null,
                complete = next == matches,
                candidates
            };
        }
        return new
        {
            source_ref = source,
            body_mc_id = bodyId.ToString(),
            last_item_id = last,
            face_token = token,
            identity_scope = "current_bridge_document_revision_model_snapshot",
            complete = true,
            faces
        };
    }
}
