using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.DatabaseServices.Constraints;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db = Teigha.DatabaseServices;

namespace NanoCADBridge;

// A stationary sketch plane can contain a translated projection of an edited
// solid edge. Compare the projected endpoints and any proven X/Y-located
// centers in that edge's frame. Shape, orientation and signed offsets remain.
internal static class ModelSketchProjectedDatum
{
    internal sealed record Datum(string Sketch, string Projection, string Source, double[] SourceStart, string[] Centers);
    static double[] Point(JsonElement geometry, string field) =>
        geometry.GetProperty(field).EnumerateArray().Select(v => v.GetDouble()).ToArray();
    static bool SamePoint(double[] a, double[] b) => a.Length == 3 && b.Length == 3 &&
        ModelParameterSnapshot.Near(Math.Sqrt(a.Zip(b).Sum(p => Math.Pow(p.First - p.Second, 2))), 0);

    internal static Datum[] Capture(NativeSketchConstraintState385.Snapshot selected, DocumentState state, Db.Transaction tx, Action cancel)
    {
        var result = new List<Datum>();
        var body = selected.Part.Body;
        var reachable = ModelDependencyGraph410.Closure(selected.Part.Preserved, selected.GraphRoots.Select(root => root.ToString()), cancel);
        reachable.UnionWith(selected.Affected);
        var stored = selected.Part.Preserved.GetProperty("sketches").EnumerateArray()
            .ToDictionary(row => row.GetProperty("id").GetString()!, StringComparer.OrdinalIgnoreCase);
        foreach (var sketchId in selected.Part.Contents)
        {
            cancel();
            string key = sketchId.ToString();
            if (sketchId == selected.Sketch.Sketch.ID || !reachable.Contains(key) ||
                !stored.TryGetValue(key, out var row) || !selected.Part.Constraints.TryGetValue(key, out var network) ||
                !ModelSketchConstraintObservation353.Complete(network)) continue;
            var retained = row.GetProperty("retained");
            if (retained.ValueKind != JsonValueKind.Object) continue;
            var bindings = retained.GetProperty("projection_bindings").EnumerateArray()
                .Where(b => b.GetProperty("source_kind").GetString() == "native_part_edge" &&
                    b.GetProperty("source_geometry").GetProperty("kind").GetString() == "line").ToArray();
            if (bindings.Length == 0) continue;
            var sketch = sketchId.GetObjectOfType<PlanarSketch>();
            if (sketch == null || sketch.ID != sketchId || sketch.HostSolidID != body.ID || sketch.DbEntity.Document.ID != body.DbEntity.Document.ID) continue;
            try
            {
                using var manager = NativeSketchMembers385.Manager(sketch);
                var ids = manager.GetAllConstraints();
                if (ids == null) continue;
                var byId = ids.GroupBy(v => v.ToString(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() == 1)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                var inputs = NativeSketchMembers385.Capture(sketch, state, tx, cancel, manager, ids);
                var relations = new Dictionary<string, List<JsonElement>>(StringComparer.OrdinalIgnoreCase);
                foreach (var relation in network.GetProperty("constraints").EnumerateArray())
                {
                    cancel();
                    foreach (var objectId in new[] { "first", "second", "third" }.Select(field => relation.GetProperty(field))
                        .Where(reference => reference.ValueKind == JsonValueKind.Object)
                        .Select(reference => reference.GetProperty("object_id").GetString()!).Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        if (!relations.TryGetValue(objectId, out var existing)) relations[objectId] = existing = [];
                        existing.Add(relation);
                    }
                }
                foreach (var binding in bindings)
                {
                    cancel();
                    string id = binding.GetProperty("id").GetString()!;
                    var member = inputs.Members.SingleOrDefault(m => m.Id.ToString() == id && m.ReferenceOnly && m.Curve.Kind == "line");
                    var projection = member?.Id.GetObjectOfType<ProjectionSketchEnt>();
                    if (member == null || projection == null || projection.ID != member.Id || projection.HostSketchID != sketch.ID ||
                        projection.HostSolidID != body.ID || projection.DbEntity.Document.ID != body.DbEntity.Document.ID ||
                        !projection.SrcObjectID.IsEdge || projection.SrcObjectID.ParentId != body.ID ||
                        projection.SrcObjectID.ToString() != binding.GetProperty("source_object_id").GetString()) continue;
                    var source = binding.GetProperty("source_geometry");
                    var start = Point(source, "start"); var end = Point(source, "end");
                    var a = Point(member.Geometry, "start"); var b = Point(member.Geometry, "end");
                    // Only a coplanar exact source segment. Endpoint order in
                    // the projection may be reversed relative to the FEV.
                    if (!(SamePoint(a, start) && SamePoint(b, end) || SamePoint(a, end) && SamePoint(b, start))) continue;
                    if (relations.TryGetValue(id, out var onProjection) && onProjection.Any(r => r.GetProperty("type").GetString() == "GeomFix")) continue;
                    var centers = new List<string>();
                    foreach (var circle in inputs.Members.Where(m => m.Owned && m.Curve.Kind is "circle" or "arc"))
                    {
                        cancel(); string circleId = circle.Id.ToString();
                        if (relations.TryGetValue(circleId, out var onCircle) && CenterFollows(circle, member, onCircle, manager, byId, inputs, cancel))
                            centers.Add(circleId);
                    }
                    result.Add(new(key, id, projection.SrcObjectID.ToString(), start, centers.ToArray()));
                }
            }
            catch (DomainException)
            {
                // Unavailable ownership, point roles or geometry proof leaves
                // the existing absolute-coordinate preservation in force.
            }
        }
        return result.ToArray();
    }

    static bool References(JsonElement relation, string objectId) => new[] { "first", "second", "third" }.Any(field =>
        relation.GetProperty(field) is { ValueKind: JsonValueKind.Object } value && value.GetProperty("object_id").GetString() == objectId);

    static bool CenterFollows(NativeSketchMembers385.Member circle, NativeSketchMembers385.Member projection,
        List<JsonElement> rows, Constraints2D manager, IReadOnlyDictionary<string, McObjectId> ids, NativeSketchMembers385.Snapshot inputs, Action cancel)
    {
        string center = circle.Id.ToString(), datum = projection.Id.ToString();
        var axes = new HashSet<string>(); string? endpoint = null;
        foreach (var row in rows)
        {
            cancel();
            string? type = row.GetProperty("type").GetString();
            if (type is "DimRadial" or "DimDiametrical") continue;
            if (type is not ("DimHorizontal" or "DimVertical") || !References(row, datum) || !axes.Add(type)) return false;
            if (!ids.TryGetValue(row.GetProperty("id").GetString()!, out var id)) return false;
            using var info = manager.GetConstraintInfo(id);
            if (info == null || !info.IsDimensional || info.VarId.IsNull || !double.IsFinite(info.Param)) return false;
            var binding = NativeSketchDimensionBinding411.Prove(manager, id, info, inputs, cancel);
            var spec = NativeSketchDimensionBinding411.Spec(info, binding.Kind, inputs, info.Param);
            bool centerFirst = spec.First == center;
            if ((centerFirst ? spec.Second : spec.First) != datum || (centerFirst ? spec.FirstPoint : spec.SecondPoint) != "center") return false;
            string? role = centerFirst ? spec.SecondPoint : spec.FirstPoint;
            if (role is not ("start" or "end") || endpoint != null && endpoint != role) return false;
            endpoint = role;
            NativeSketchMembers385.ValidateSolved([spec], inputs);
        }
        return axes.Count == 2;
    }

    internal static void CompareRelativeToSource(JsonNode sketch, JsonObject retained, Datum[] datums, string id,
        IReadOnlySet<string> movingSources, Action cancel)
    {
        var active = datums.Where(d => d.Sketch == id && movingSources.Contains(d.Source)).ToArray();
        if (active.Length == 0) return;
        foreach (var snapshot in new[] { sketch["geometry"], retained["geometry"] })
            foreach (var row in snapshot!["elements"]!.AsArray())
            {
                cancel(); string member = row!["element_id"]!.GetValue<string>();
                var matches = active.Where(d => d.Projection == member || d.Centers.Contains(member)).ToArray();
                if (matches.Length != 1) continue;
                var datum = matches[0]; var geometry = row["geometry"]!;
                foreach (string field in datum.Projection == member ? new[] { "start", "end" } : new[] { "center" })
                {
                    var point = geometry[field]!.AsArray();
                    for (int axis = 0; axis < 3; axis++) point[axis] = point[axis]!.GetValue<double>() - datum.SourceStart[axis];
                }
                row["relative_to_source_edge_start"] = datum.Source;
                row["relative_to_projection"] = datum.Projection;
            }
    }
}
