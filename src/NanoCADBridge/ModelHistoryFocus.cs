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
    private readonly Dictionary<string, HistoryIssueDiagnosis.Node>? issueNodes;
    private readonly Dictionary<string, string[]>? observedDependencies;
    internal Dictionary<string, HistoryIssueDiagnosis.Support>? SketchSupports { get; }
    internal Dictionary<string, HistoryIssueDiagnosis.Evidence>? IssueEvidence { get; }
    internal string[]? ObservedDependencies(string id) => observedDependencies?.GetValueOrDefault(id);
    internal string? DependencyProblem(string id) => issueNodes?.GetValueOrDefault(id)?.InventoryProblem;
    private readonly HashSet<string>? downstream;
    private readonly HashSet<string>? externalDownstream;
    private readonly List<ModelConcentricHole.CreatorLink> placementLinks = new();
    internal bool Include(string id) => selected.Contains(id) || upstream.Contains(id) || consumers.Contains(id) || downstream?.Contains(id) == true;

    internal ModelHistoryFocus(List<McObjectId> contents, string bodyId, string request, bool includeDownstream, Action cancel)
    {
        if (request == "issues")
        {
            issueNodes = new(Identity);
            observedDependencies = new(Identity);
            SketchSupports = new(Identity);
            IssueEvidence = new(Identity);
        }
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
                IEnumerable<McObjectId>? native;
                string? problem = null;
                try { native = node.GetDependsOn(false, false); }
                catch (Exception e) when (issueNodes != null && e is not (OperationCanceledException or DomainException or OutOfMemoryException))
                {
                    native = null;
                    problem = "native_dependency_read_failed:" + e.GetType().Name;
                }
                if (native == null)
                {
                    if (issueNodes == null) throw new DomainException("HISTORY_UNAVAILABLE", "A native history item returned no dependency inventory.");
                    problem ??= "native_dependency_list_unavailable";
                }
                var nativeIds = native?.ToArray() ?? [];
                if (issueNodes != null && nativeIds.Any(d => d.IsNull)) problem ??= "null_native_dependency_id";
                var direct = nativeIds.Where(d => !d.IsNull).Select(d => d.ToString()).ToArray();
                dependencies = direct.Where(d => !Identity.Equals(d, bodyId)).Distinct(Identity).ToArray();
                if (issueNodes != null)
                {
                    observedDependencies![key] = direct;
                    issueNodes[key] = node.ID == id && Identity.Equals(node.HostSolidID.ToString(), bodyId)
                        ? new(key, node.Name, node.Status.ToString(), !node.IsSuppressed && !node.IsBeyondEndOfPart, dependencies, problem)
                        : new(key, null, null, true, [], "part_ownership_unverified");
                }
                if (includeDownstream && id.GetObjectOfType<HoleFeature>() is { } hole && hole.ID == id &&
                    Identity.Equals(hole.HostSolidID.ToString(), bodyId) && ModelConcentricHole.ReadCreatorLink(hole, native!) is { } link)
                    placementLinks.Add(link);
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
            else if (issueNodes != null)
                issueNodes[key] = new(key, null, null, true, [], "not_a_history_item");
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
        var dependents = new Dictionary<string, List<string>>(Identity);
        void Link(string parent, string child)
        {
            if (Identity.Equals(parent, child)) return;
            if (!dependents.TryGetValue(parent, out var children)) dependents[parent] = children = new();
            children.Add(child);
        }
        if (includeDownstream || activeIssues.Count > 0)
            foreach (var (id, deps) in graph)
                foreach (var dependency in deps) Link(dependency, id);
        // Issue diagnosis and direct_consumer_ids retain their GetDependsOn-only meaning.
        if (activeIssues.Count > 0)
            upstreamIssueCandidates.UnionWith(activeIssues.Except(Reachable(activeIssues, dependents, cancel), Identity));
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
            placementLinks.RemoveAll(link => !graph.ContainsKey(link.creator_id));
            foreach (var link in placementLinks) Link(link.creator_id, link.hole_id);
            downstream.UnionWith(Reachable(selected.Concat(downstream), dependents, cancel));
            downstream.ExceptWith(selected);
            placementLinks.RemoveAll(link => !(selected.Contains(link.creator_id) || downstream.Contains(link.creator_id)));
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

    internal object Describe(string request, string handle, string partId, IEnumerable<object> nodes, Action cancel, Action<object> charge)
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
            issue_diagnosis = issueNodes == null ? null : HistoryIssueDiagnosis.Explain(partId, issueNodes, IssueEvidence!, cancel, charge),
            selected_ids = selected.OrderBy(id => id, Identity),
            upstream_ids = upstream.OrderBy(id => id, Identity),
            direct_consumer_ids = consumers.OrderBy(id => id, Identity),
            downstream_ids = downstream?.OrderBy(id => id, Identity),
            external_downstream_ids = externalDownstream?.OrderBy(id => id, Identity),
            downstream_concentric_edge_sources = downstream == null ? null : placementLinks,
            external_reference_ids = external.OrderBy(id => id, Identity),
            unknown_status_ids = unknownStatus.OrderBy(id => id, Identity),
            parameter_variables = drivers,
            upstream_issue_candidate_ids = upstreamIssueCandidates.OrderBy(id => id, Identity),
            next_reads = (request == "issues" ? upstreamIssueCandidates : selected).OrderBy(id => id, Identity)
                .Select(id => new { operation = "get_model_variables", handle, feature_id = id }),
            evidence = (downstream == null ? "" : "Downstream IDs combine GetAllDependent(false) and reachable same-part references. downstream_concentric_edge_sources identifies each additional verified creator-to-hole link through its exact stored edge and GetFeatureObjectIdForFEV. Other placement links can be unavailable. External consumers have IDs only. ") + "Direct consumers use GetDependsOn(false,false) within current part contents. External references are leaves. Native statuses and variable bindings do not establish a failure cause or permission to edit."
        };
    }
}
