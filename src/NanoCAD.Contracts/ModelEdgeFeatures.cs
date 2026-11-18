using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NanoCAD.Contracts;

public sealed record ModelEdgeFeatureSpec(string Kind, string[] Targets, string ExpectedLastItemId, string? TopologyToken, string[] EdgeIds, double Size)
    : EditSpec(Kind, Targets)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ModelEdgeToken { get; init; }
}
public sealed record ModelEdgeFeatureChange(EntityRef source, EntityRef body_ref, string body_mc_id, string feature_id, string variable_id,
    string kind, string[] edge_ids, double size, double volume_before, double volume_after, bool source_representation_replaced);

public static partial class ModifyValidation
{
    private static ModelEdgeFeatureSpec ParseModelEdgeFeature(JsonElement item, string path, string[] handles, string kind)
    {
        string sizeField = kind == "add_model_fillet" ? "radius" : "distance";
        Shape(item, path, "operation", "handles", "expected_last_item_id", "topology_token", "edges", "edge_token", "model_edge_ids", sizeField);
        if (handles.Length != 1) throw Invalid(path + ".handles", "Append one native edge feature to one model body.");
        string last = Text(item, "expected_last_item_id", path, 38);
        if (!Guid.TryParseExact(last, "B", out var id) || id == Guid.Empty) throw Invalid(path + ".expected_last_item_id", "Use the current last_item_id from inspect_model_history.");
        bool native = item.TryGetProperty("edge_token", out _) || item.TryGetProperty("model_edge_ids", out _);
        if (native && (item.TryGetProperty("edges", out _) || item.TryGetProperty("topology_token", out _)))
            throw Invalid(path, "Choose model_edge_ids with edge_token, or B-rep edges with topology_token.");
        string tokenField = native ? "edge_token" : "topology_token";
        string idsField = native ? "model_edge_ids" : "edges";
        string token = Text(item, tokenField, path, 64);
        if (token.Length != 64 || token.Any(c => !Uri.IsHexDigit(c))) throw Invalid(path + "." + tokenField, "Use the current matching token from inspect_solid_topology.");
        var input = Required(item, idsField, path);
        if (input.ValueKind != JsonValueKind.Array || input.GetArrayLength() < 1) throw Invalid(path + "." + idsField, "Select a nonempty set of current edges.");
        double size = Number(Required(item, sizeField, path), path + "." + sizeField, .001, 1e6);
        if (native)
        {
            var nativeIds = new List<string>(); var uniqueIds = new HashSet<Guid>();
            foreach (var row in input.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.String || !Guid.TryParseExact(row.GetString(), "B", out var edge) || edge == Guid.Empty || !uniqueIds.Add(edge))
                    throw Invalid(path + ".model_edge_ids", "Select unique native model_edge_id values from the current body.");
                nativeIds.Add(edge.ToString("B").ToUpperInvariant());
            }
            return new(kind, handles, id.ToString("B").ToUpperInvariant(), null, nativeIds.ToArray(), size) { ModelEdgeToken = token.ToUpperInvariant() };
        }
        var markers = new List<string>(); var unique = new HashSet<long>();
        foreach (var marker in input.EnumerateArray())
        {
            if (marker.ValueKind != JsonValueKind.String || !long.TryParse(marker.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out long value) || value <= 0 || !unique.Add(value))
                throw Invalid(path + ".edges", "Edge IDs must be unique positive decimal strings from inspect_solid_topology.");
            markers.Add(value.ToString(CultureInfo.InvariantCulture));
        }
        return new(kind, handles, id.ToString("B").ToUpperInvariant(), token, markers.ToArray(), size);
    }
}
