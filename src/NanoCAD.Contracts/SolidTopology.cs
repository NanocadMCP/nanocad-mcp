using System.Text.Json;

namespace NanoCAD.Contracts;

public static class SolidTopologyValidation
{
    public static Arguments Parse(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object) throw new DomainException("INVALID_ARGUMENT", "Expected object.");
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        ModelFaceQuery? faceQuery = null;
        ModelEdgeQuery? edgeQuery = null;
        bool includeNativeFaces = false, seenNativeFaces = false, includeNativeEdges = false, seenNativeEdges = false;
        foreach (var p in input.EnumerateObject())
        {
            if (p.Name == "edge_query")
            {
                if (edgeQuery != null) throw new DomainException("INVALID_ARGUMENT", "Duplicate edge_query.");
                edgeQuery = ModelEdgeQuery.Parse(p.Value);
                continue;
            }
            if (p.Name == "face_query")
            {
                if (faceQuery != null) throw new DomainException("INVALID_ARGUMENT", "Duplicate face_query.");
                faceQuery = ModelFaceQuery.Parse(p.Value);
                continue;
            }
            if (p.Name == "include_native_model_edges")
            {
                if (p.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False) || seenNativeEdges)
                    throw new DomainException("INVALID_ARGUMENT", "include_native_model_edges must be boolean and unique.");
                includeNativeEdges = p.Value.GetBoolean(); seenNativeEdges = true; continue;
            }
            if (p.Name == "include_native_model_faces")
            {
                if (p.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False) || seenNativeFaces)
                    throw new DomainException("INVALID_ARGUMENT", "include_native_model_faces must be boolean and unique.");
                includeNativeFaces = p.Value.GetBoolean(); seenNativeFaces = true;
                continue;
            }
            if (p.Name is not ("bridge_instance_id" or "document_id" or "expected_revision" or "handle") || !values.TryAdd(p.Name, p.Value))
                throw new DomainException("INVALID_ARGUMENT", "Unknown or duplicate topology argument: " + p.Name);
        }
        if (!values.Remove("handle", out var handle) || handle.ValueKind != JsonValueKind.String)
            throw new DomainException("INVALID_ARGUMENT", "A native Solid3d handle is required.");
        values.Add("handles", Wire.Element(new[] { handle.GetString() }));
        var args = Validation.Parse("inspect_objects", Wire.Element(values));
        if (args.BridgeId == null || args.DocumentId == null || args.ExpectedRevision == null)
            throw new DomainException("INVALID_ARGUMENT", "Topology inspection requires explicit bridge/document/revision.");
        if ((faceQuery != null || edgeQuery != null) && (includeNativeFaces || includeNativeEdges)
            || faceQuery != null && edgeQuery != null)
            throw new DomainException("INVALID_ARGUMENT", "Choose one focused topology query or a full native inventory.");
        return args with { IncludeNativeModelFaces = includeNativeFaces, IncludeNativeModelEdges = includeNativeEdges, FaceQuery = faceQuery, EdgeQuery = edgeQuery };
    }
}

public sealed record ModelEdgeQuery(string BoundaryOfFaceId, string? Curve, int StartIndex, int PageSize)
{
    public static ModelEdgeQuery Parse(JsonElement value)
    {
        static DomainException Invalid(string message) => new("INVALID_ARGUMENT", "edge_query: " + message);
        if (value.ValueKind != JsonValueKind.Object) throw Invalid("Expected an object.");
        string? face = null, curve = null;
        int start = 0, size = 30;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            var item = property.Value;
            if (!seen.Add(property.Name)) throw Invalid("Duplicate " + property.Name + ".");
            switch (property.Name)
            {
                case "boundary_of_face_id" when item.ValueKind == JsonValueKind.String && Guid.TryParse(item.GetString(), out var id) && id != Guid.Empty:
                    face = id.ToString("B").ToUpperInvariant();
                    break;
                case "curve" when item.ValueKind == JsonValueKind.String && item.GetString() is "line" or "circle" or "circular_arc":
                    curve = item.GetString();
                    break;
                case "start_index" when item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out start) && start >= 0:
                case "page_size" when item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out size) && size > 0:
                    break;
                default:
                    throw Invalid("Unknown property or invalid value for " + property.Name + ".");
            }
        }
        return new(face ?? throw Invalid("A current model_face_id is required."), curve, start, size);
    }
}
