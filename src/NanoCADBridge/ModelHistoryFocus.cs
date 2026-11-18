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
    internal bool Include(string id) => selected.Contains(id) || upstream.Contains(id) || consumers.Contains(id);

    internal ModelHistoryFocus(List<McObjectId> contents, string bodyId, string request, Action cancel)
    {
        var graph = new Dictionary<string, string[]>(Identity);
        var activeIssues = new HashSet<string>(Identity);
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
        if (activeIssues.Count > 0)
        {
            var dependents = new Dictionary<string, List<string>>(Identity);
            foreach (var (id, deps) in graph)
                foreach (var dependency in deps.Where(d => !Identity.Equals(d, id)))
                {
                    if (!dependents.TryGetValue(dependency, out var children)) dependents[dependency] = children = new();
                    children.Add(id);
                }
            // A reachable active issue can be a downstream symptom. Cycles have no first node.
            var downstream = new HashSet<string>(Identity);
            var search = new Queue<string>(activeIssues);
            while (search.TryDequeue(out var id))
            {
                cancel();
                if (dependents.TryGetValue(id, out var children))
                    foreach (string child in children) if (downstream.Add(child)) search.Enqueue(child);
            }
            upstreamIssueCandidates.UnionWith(activeIssues.Except(downstream, Identity));
        }
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
            external_reference_ids = external.OrderBy(id => id, Identity),
            unknown_status_ids = unknownStatus.OrderBy(id => id, Identity),
            parameter_variables = drivers,
            upstream_issue_candidate_ids = upstreamIssueCandidates.OrderBy(id => id, Identity),
            next_reads = (request == "issues" ? upstreamIssueCandidates : selected).OrderBy(id => id, Identity)
                .Select(id => new { operation = "get_model_variables", handle, feature_id = id }),
            evidence = "GetDependsOn(false,false) within current part contents. External references are leaves. Native statuses and variable bindings do not establish a failure cause or permission to edit."
        };
    }
}
