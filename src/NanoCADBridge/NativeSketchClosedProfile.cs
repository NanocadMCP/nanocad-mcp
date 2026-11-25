using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;

namespace NanoCADBridge;

internal static class NativeSketchClosedProfile
{
    internal sealed record Comparison(IReadOnlyDictionary<(string Id, bool Selected), int> Own,
        IReadOnlyDictionary<(string Id, bool Selected), int> Other);

    internal static (Comparison Before, Comparison After) Pair(NativeSketchConstraintState385.Snapshot before,
        NativeSketchConstraintState385.Snapshot after, Action cancel)
    {
        var a = Witnesses(before, cancel);
        var b = Witnesses(after, cancel);
        return (new(a, b), new(b, a));
    }

    static Dictionary<(string, bool), int> Witnesses(NativeSketchConstraintState385.Snapshot snapshot, Action cancel)
    {
        var result = new Dictionary<(string, bool), int>();
        var inputs = snapshot.Inputs;
        if (!inputs.Members.All(m => m.Owned && !m.ReferenceOnly && m.Segment == null && m.Curve.Kind is "line" or "arc"))
            return result;
        var members = inputs.Members.Select(m => m.Curve).ToArray();
        string sketch = snapshot.Sketch.Sketch.ID.ToString();
        void Read(string id, bool selected, JsonElement raw)
        {
            cancel();
            if (raw.ValueKind == JsonValueKind.Array && raw.GetArrayLength() == 1 && raw[0].GetProperty("holes").GetArrayLength() == 0)
                raw = raw[0].GetProperty("outer");
            if (raw.ValueKind != JsonValueKind.Object || !raw.TryGetProperty("identity_scope", out var scope) ||
                scope.GetString() is not ("boundary_order_only_not_native_element_identity" or "boundary_order_only_not_native_subentity_identity") ||
                !raw.TryGetProperty("elements", out var rows) || rows.GetArrayLength() < 3 || rows.GetArrayLength() != members.Length) return;
            int ordinal = 0;
            foreach (var row in rows.EnumerateArray())
            {
                cancel();
                if (row.EnumerateObject().Count() != 2 || row.GetProperty("element_id").GetString() != ordinal.ToString(CultureInfo.InvariantCulture) ||
                    !row.TryGetProperty("geometry", out var g) || g.ValueKind != JsonValueKind.Object ||
                    !g.TryGetProperty("kind", out var kind) || kind.GetString() is not ("line" or "circular_arc")) return;
                ordinal++;
            }
            var loop = NativeSketchGeometry385.Curves(raw, inputs.Plane, cancel);
            if (loop.Any(c => c.Kind is not ("line" or "arc")) || !ModelProfileElementGeometry358.SameBoundary(loop, members, cancel)) return;
            int winding = ClosedProfileTraversal.Winding(loop, cancel);
            if (winding != 0) result.Add((id.ToUpperInvariant(), selected), winding);
        }
        foreach (var row in snapshot.Part.Preserved.GetProperty("profiles").EnumerateArray())
            if (NativeSketchConstraintState385.Same(row.GetProperty("parent_sketch_id").GetString(), sketch) && row.TryGetProperty("profile_geometry", out var physical))
                Read(row.GetProperty("profile_id").GetString()!, false, physical);
        foreach (var row in snapshot.Sketch.Profiles.EnumerateArray())
            Read(row.GetProperty("profile_id").GetString()!, true, row.GetProperty("state").GetProperty("selected_profile_geometry"));
        return result;
    }

    // Positions are synthetic ordinals in a closed traversal. A missing witness on
    // either side leaves both original arrays untouched for their exact comparison.
    internal static void Normalize(JsonNode? masked, Comparison? pair, string profileId, bool selected, Action cancel)
    {
        if (pair == null || masked == null) return;
        if (masked is JsonArray { Count: 1 } areas) masked = areas[0]?["outer"];
        if (masked is not JsonObject boundary) return;
        var key = (profileId.ToUpperInvariant(), selected);
        int winding = pair.Own.GetValueOrDefault(key), otherWinding = pair.Other.GetValueOrDefault(key);
        ClosedProfileTraversal.AlignForComparison(boundary, winding, otherWinding, cancel);
    }
}
