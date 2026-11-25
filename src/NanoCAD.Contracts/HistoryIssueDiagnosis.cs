namespace NanoCAD.Contracts;

/// <summary>Explains the ordering of observed errors along actual direct dependencies of one part.</summary>
public static class HistoryIssueDiagnosis
{
    public sealed record Node(string Id, string? Name, string? Status, bool Active, string[] Dependencies, string? InventoryProblem);
    public sealed record Notice(string severity, string? description);
    public sealed record Support(string? id, string? kind, string state, object? detail);
    public sealed record Evidence(IReadOnlyList<Notice> notices, string notice_state, Support? sketch_support);
    public sealed record Issue(string id, string? name, string status, string role, Evidence? native_evidence);
    public sealed record Link(string upstream_error_id, string downstream_error_id, string[] dependency_path);
    public sealed record Boundary(string consumer_id, string? dependency_id, string reason);
    public sealed record Report(string part_id, string state, List<Issue> issues, List<Link> error_links,
        List<Boundary> unresolved_edges, string[] unknown_status_ids);

    public static Report Explain(string partId, IReadOnlyDictionary<string, Node> nodes,
        IReadOnlyDictionary<string, Evidence> evidence, Action check, Action<object> charge)
    {
        var identity = StringComparer.OrdinalIgnoreCase;
        var errors = nodes.Values.Where(n => n.Active && n.Status == "Error").ToDictionary(n => n.Id, identity);
        var parents = errors.Keys.ToDictionary(id => id, _ => new HashSet<string>(identity), identity);
        var incomplete = new HashSet<string>(identity);
        var links = new List<Link>();
        var boundaries = new HashSet<Boundary>();
        void BoundaryAt(string consumer, string? dependency, string reason)
        {
            var boundary = new Boundary(consumer, dependency, reason);
            if (boundaries.Add(boundary)) charge(boundary);
        }
        foreach (var error in errors.Values)
        {
            // Stop each path at its nearest other error. A healthy intermediate remains in
            // the witness path. Breadth-first traversal gives one shortest witness per pair.
            var via = new Dictionary<string, string?>(identity) { [error.Id] = null };
            var pending = new Queue<Node>();
            pending.Enqueue(error);
            while (pending.TryDequeue(out var consumer))
            {
                check();
                if (consumer.InventoryProblem != null)
                {
                    BoundaryAt(consumer.Id, null, consumer.InventoryProblem);
                    if (consumer.InventoryProblem != "not_a_history_item") incomplete.Add(error.Id);
                }
                foreach (string id in consumer.Dependencies)
                {
                    check();
                    if (!nodes.TryGetValue(id, out var dependency))
                    {
                        BoundaryAt(consumer.Id, id, "outside_part_history");
                        continue;
                    }
                    if (errors.ContainsKey(id))
                    {
                        if (!parents[error.Id].Add(id)) continue;
                        var path = new List<string> { id };
                        for (string? step = consumer.Id; step != null; step = via[step]) path.Add(step);
                        var link = new Link(id, error.Id, path.ToArray());
                        charge(link);
                        links.Add(link);
                    }
                    else if (dependency.Active && via.TryAdd(id, consumer.Id))
                    {
                        if (dependency.Status == "Unknown")
                        {
                            BoundaryAt(consumer.Id, id, "native_status_unknown");
                            incomplete.Add(error.Id);
                        }
                        pending.Enqueue(dependency);
                    }
                }
            }
        }

        // Remove all nodes whose ancestors are acyclic first. On the usual feature chain
        // this avoids walking all earlier errors again for each descendant.
        var children = errors.Keys.ToDictionary(id => id, _ => new List<string>(), identity);
        foreach (var pair in parents)
            foreach (string parent in pair.Value) children[parent].Add(pair.Key);
        var counts = parents.ToDictionary(pair => pair.Key, pair => pair.Value.Count, identity);
        var resolved = new Queue<string>(counts.Where(pair => pair.Value == 0).Select(pair => pair.Key));
        while (resolved.TryDequeue(out var root))
        {
            check();
            foreach (var child in children[root]) if (--counts[child] == 0) resolved.Enqueue(child);
        }

        bool InErrorCycle(string id)
        {
            if (counts[id] == 0) return false;
            var visited = new HashSet<string>(identity);
            var pending = new Stack<string>(parents[id]);
            while (pending.TryPop(out var parent))
            {
                check();
                if (identity.Equals(parent, id)) return true;
                if (counts[parent] > 0 && visited.Add(parent))
                    foreach (string next in parents[parent]) pending.Push(next);
            }
            return false;
        }

        var issues = new List<Issue>();
        foreach (var n in nodes.Values.Where(n => n.Active && n.Status is ("Error" or "Warn" or "NeedUpdate")))
        {
            check();
            string role = n.Status switch
            {
                "Warn" => "warning",
                "NeedUpdate" => "update_requested",
                _ when parents[n.Id].Count == 0 => incomplete.Contains(n.Id) ? "unresolved_error" : "first_observed_error",
                _ => InErrorCycle(n.Id) ? "cyclic_error" : "downstream_error"
            };
            evidence.TryGetValue(n.Id, out var observed);
            var issue = new Issue(n.Id, n.Name, n.Status!, role, observed);
            charge(issue);
            issues.Add(issue);
        }
        var unknown = nodes.Values.Where(n => n.Active && (n.Status == "Unknown" ||
            n.Status == null && n.InventoryProblem != "not_a_history_item")).Select(n => n.Id).ToArray();
        return new(partId, errors.Count > 0 ? "observed_errors" : unknown.Length > 0 ? "status_incomplete" : "no_observed_errors",
            issues, links, boundaries.ToList(), unknown);
    }
}
