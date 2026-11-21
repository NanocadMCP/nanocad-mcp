using System.Text.Json;
using Multicad;
using Multicad.Mc3D;
using NanoCAD.Contracts;

namespace NanoCADBridge;

internal sealed class ModelHistoryFocus
{
    private static readonly StringComparer Identity = StringComparer.OrdinalIgnoreCase;
    private readonly HashSet<string> selected = new(Identity);
    private readonly HashSet<string> upstream = new(Identity);
    private readonly HashSet<string> consumers = new(Identity);
    private readonly HashSet<string> external = new(Identity);
    private readonly HashSet<string> unknownStatus = new(Identity);
    private readonly HashSet<string> upstreamIssueCandidates = new(Identity);
    private readonly HashSet<string>? downstream;
    private readonly HashSet<string>? externalDownstream;
    internal bool Include(string id) => selected.Contains(id) || upstream.Contains(id) || consumers.Contains(id) || downstream?.Contains(id) == true;

    internal ModelHistoryFocus(List<McObjectId> contents, string bodyId, string request, bool includeDownstream, Action cancel)
    {
        var graph = new Dictionary<string, string[]>(Identity);
        var activeIssues = new HashSet<string>(Identity);
        List<McObjectId>? nativeDependents = null;
        foreach (var id in contents)
        {
            cancel();
            string key = id.ToString();
            var node = id.GetObjectOfType<HistoryItem>();
            string[] dependencies = [];
            if (node != null)
            {
                var native = node.GetDependsOn(false, false) ?? throw new DomainException("HISTORY_UNAVAILABLE", "A native history item returned no dependency inventory.");
                dependencies = native.Where(d => !d.IsNull).Select(d => d.ToString())
                    .Where(d => !Identity.Equals(d, bodyId)).Distinct(Identity).ToArray();
                if (includeDownstream && Identity.Equals(key, request))
                {
                    if (node.ID != id || !Identity.Equals(node.HostSolidID.ToString(), bodyId))
                        throw new DomainException("DEPENDENCY_OWNERSHIP_MISMATCH", "The selected history item does not belong to this part.");
                    var all = node.GetAllDependent(false) ?? throw new DomainException("DEPENDENCY_INVENTORY_UNAVAILABLE", "The native downstream inventory is unavailable.");
                    ModelInventoryBudget.Ids(all, cancel);
                    nativeDependents = all.ToList();
                }
                string status = node.Status.ToString();
                if (status == "Unknown") unknownStatus.Add(key);
                if (!node.IsSuppressed && status is ("Error" or "Warn" or "NeedUpdate")) activeIssues.Add(key);
            }
            graph[key] = dependencies;
        }
        if (request == "issues") selected.UnionWith(activeIssues);
        else
        {
            if (!graph.ContainsKey(request)) throw new DomainException("OBJECT_NOT_FOUND", "The selected native identity is absent from this part's current contents.");
            selected.Add(request);
        }
        var pending = new Queue<string>(selected);
        while (pending.TryDequeue(out var current))
        {
            cancel();
            foreach (string dependency in graph[current])
            {
                if (!graph.ContainsKey(dependency)) external.Add(dependency);
                else if (!selected.Contains(dependency) && upstream.Add(dependency)) pending.Enqueue(dependency);
            }
        }
        foreach (var (id, deps) in graph)
            if (!selected.Contains(id) && deps.Any(selected.Contains)) consumers.Add(id);
        activeIssues.IntersectWith(selected.Concat(upstream));
        if (includeDownstream)
        {
            if (nativeDependents == null) throw new DomainException("DEPENDENCY_INVENTORY_UNAVAILABLE", "Select a native history item with an available downstream inventory.");
            downstream = new HashSet<string>(consumers, Identity);
            externalDownstream = new HashSet<string>(Identity);
            foreach (var id in nativeDependents)
            {
                cancel();
                string key = id.ToString();
                if (id.IsNull || selected.Contains(key) || Identity.Equals(key, bodyId)) continue;
                if (graph.ContainsKey(key)) downstream.Add(key);
                else externalDownstream.Add(key);
            }
        }
        if (activeIssues.Count > 0)
        {
            var dependents = new Dictionary<string, List<string>>(Identity);
            foreach (var (id, deps) in graph)
                foreach (var dependency in deps.Where(d => !Identity.Equals(d, id)))
                {
                    if (!dependents.TryGetValue(dependency, out var children)) dependents[dependency] = children = new();
                    children.Add(id);
                }
            // An active issue reachable from another active issue can be a downstream symptom.
            upstreamIssueCandidates.UnionWith(activeIssues.Except(Reachable(activeIssues, dependents, cancel), Identity));
        }
    }

    private static HashSet<string> Reachable(IEnumerable<string> roots, Dictionary<string, List<string>> dependents, Action cancel)
    {
        var result = new HashSet<string>(Identity);
        var pending = new Queue<string>(roots);
        while (pending.TryDequeue(out var id))
        {
            cancel();
            if (dependents.TryGetValue(id, out var children))
                foreach (var child in children) if (result.Add(child)) pending.Enqueue(child);
        }
        return result;
    }

    internal object Describe(string request, string handle, IEnumerable<object> nodes)
    {
        var drivers = new List<object>();
        foreach (var value in nodes)
        {
            var node = Wire.Element(value);
            if (!node.TryGetProperty("parameters", out var parameters) || parameters.ValueKind != JsonValueKind.Object) continue;
            foreach (var field in parameters.EnumerateObject())
            {
                const string suffix = "_variable_id";
                if (!field.Name.EndsWith(suffix, StringComparison.Ordinal) || field.Value.ValueKind != JsonValueKind.String ||
                    !Guid.TryParse(field.Value.GetString(), out var id) || id == Guid.Empty) continue;
                drivers.Add(new
                {
                    feature_id = node.GetProperty("mc_id").GetString(),
                    parameter = field.Name[..^suffix.Length],
                    variable_id = field.Value.GetString()
                });
            }
        }
        return new
        {
            request,
            selected_ids = selected.OrderBy(id => id, Identity),
            upstream_ids = upstream.OrderBy(id => id, Identity),
            direct_consumer_ids = consumers.OrderBy(id => id, Identity),
            downstream_ids = downstream?.OrderBy(id => id, Identity),
            external_downstream_ids = externalDownstream?.OrderBy(id => id, Identity),
            external_reference_ids = external.OrderBy(id => id, Identity),
            unknown_status_ids = unknownStatus.OrderBy(id => id, Identity),
            parameter_variables = drivers,
            upstream_issue_candidate_ids = upstreamIssueCandidates.OrderBy(id => id, Identity),
            next_reads = (request == "issues" ? upstreamIssueCandidates : selected).OrderBy(id => id, Identity)
                .Select(id => new { operation = "get_model_variables", handle, feature_id = id }),
            evidence = (downstream == null ? "" : "GetAllDependent(false) supplies unordered direct and indirect consumers. External consumers have IDs only. ") + "GetDependsOn(false,false) within current part contents. External references are leaves. Native statuses and variable bindings do not establish a failure cause or permission to edit."
        };
    }
}
