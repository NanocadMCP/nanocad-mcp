namespace NanoCAD.Contracts;

public sealed record SketchMobilityRelation(string Id, SketchConstraintSpec? Spec, string[]? ObjectIds = null);

// A promised translation of the selected members in sketch coordinates.
// dx/dy describe one movement, so disconnected elements can move together.
public sealed record SketchMobilityMotion(string name, string[] object_ids, double dx, double dy);
public sealed record SketchMobilityDecision(int candidate_index, string classification, int? dof_before, int? dof_after,
    string reason, string[] witnesses);
public sealed record SketchMobilityMovement(string name, string status, string[] witnesses, bool requested);
public sealed record SketchMobilityExistingIssue(string constraint_id, string classification, string[] witnesses, string reason);
public sealed record SketchMobilityComponent(string[] members, int? remaining_dof, int? planned_dof, string[] reasons,
    SketchMobilityMovement[] movements_before, SketchMobilityMovement[] movements_after, SketchMobilityExistingIssue[] existing_issues);
public sealed record SketchMobilityReport(int? remaining_dof, int? planned_dof, string state, SketchMobilityDecision[] decisions,
    SketchMobilityComponent[] components, string source = "computed_from_geometry_and_constraint_equations")
{
    // Native commands also accept explicit operations outside the mathematical vocabulary.
    // Ignorance never establishes either a conflict or a removable geometric relation.
    public SketchConstraintSpec[] EffectiveCandidates(IReadOnlyList<SketchConstraintSpec> candidates, bool requireIndependentProof = false)
    {
        if (decisions.Length != candidates.Count)
            throw new ArgumentException("The plan and the requested group must come from the same preflight.");
        CheckRequestedMovements();
        var result = new List<SketchConstraintSpec>();
        foreach (var decision in decisions)
        {
            var candidate = candidates[decision.candidate_index];
            if (decision.classification == "implied" && candidate.Value is null) continue;
            if (decision.classification is "conflict" or "partly_implied" or "implied" or "no_independent_motion" or "removes_intended_motion" or "unverified_intended_motion")
                throw new DomainException("CONSTRAINT_PLAN_REJECTED",
                    $"Constraint {decision.candidate_index} ({candidate.Kind}) is {decision.classification}. {decision.reason}",
                    details: new { candidate, decision, mobility = this });
            if (requireIndependentProof && (decision.classification != "independent" ||
                !decision.dof_before.HasValue || !decision.dof_after.HasValue || decision.dof_before <= decision.dof_after))
                throw new DomainException("CONSTRAINT_PLAN_UNVERIFIED",
                    $"Cannot certify independence of constraint {decision.candidate_index} ({candidate.Kind}). {decision.reason}",
                    details: new { candidate, decision, mobility = this });
            result.Add(candidate);
        }
        return result.ToArray();
    }

    public void RequireCompatibleReplacement()
    {
        CheckRequestedMovements();
        var blocked = decisions.FirstOrDefault(d => d.classification is "conflict" or "removes_intended_motion" or "unverified_intended_motion");
        if (blocked != null)
            throw new DomainException("CONSTRAINT_PLAN_REJECTED", blocked.reason, details: new { decision = blocked, mobility = this });
    }

    private void CheckRequestedMovements()
    {
        var blocked = components.SelectMany(c => c.movements_after).FirstOrDefault(m => m.requested && m.status != "permitted");
        if (blocked != null)
            throw new DomainException("CONSTRAINT_PLAN_REJECTED", $"The requested movement '{blocked.name}' is {blocked.status}.",
                details: new { movement = blocked, mobility = this });
    }
}

public static class SketchMobility
{
    // Existing and candidate angles use degrees. All IDs must already be in the same
    // exact namespace (native identity or local creation key). No coordinate aliasing.
    public static SketchMobilityReport Plan(IReadOnlyList<SketchConstraintCurve> geometry,
        IReadOnlyList<SketchMobilityRelation> existing, IReadOnlyList<SketchConstraintSpec>? candidates = null,
        bool inventoryComplete = true, IReadOnlyCollection<string>? fixedGeometry = null,
        Action? cancel = null, IReadOnlyList<SketchMobilityMotion>? preserve = null, bool replacingDrivers = false)
    {
        candidates ??= Array.Empty<SketchConstraintSpec>();
        var all = new Dictionary<string, SketchConstraintCurve>(StringComparer.Ordinal);
        var groups = new Components();
        bool unlocated = !inventoryComplete;
        foreach (var curve in geometry)
        {
            cancel?.Invoke();
            if (!all.TryAdd(curve.Id, curve)) throw new ArgumentException("Repeated geometry identity: " + curve.Id);
            groups.Add(curve.Id);
        }
        string[] Members(SketchMobilityRelation r) => r.Spec == null ? r.ObjectIds ?? Array.Empty<string>()
            : Ids(r.Spec).Concat(r.ObjectIds ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var r in existing)
        {
            cancel?.Invoke();
            var ids = Members(r);
            if (ids.Length == 0) unlocated = true;
            else groups.Join(ids);
        }
        foreach (var m in preserve ?? Array.Empty<SketchMobilityMotion>())
            if (m.object_ids == null || m.object_ids.Length == 0 || m.object_ids.Any(id => !all.ContainsKey(id)) ||
                !double.IsFinite(m.dx) || !double.IsFinite(m.dy) || m.dx == 0 && m.dy == 0)
                throw new DomainException("INVALID_ARGUMENT", "A protected translation needs known geometry identities and a finite nonzero direction in the sketch plane.");
        foreach (var c in candidates) groups.Join(Ids(c));
        var fixedIds = new HashSet<string>(fixedGeometry ?? Array.Empty<string>(), StringComparer.Ordinal);
        var retained = existing.Where(r => Members(r).Length > 0).ToLookup(r => groups.Root(Members(r)[0]));
        var requests = candidates.Select((spec, index) => (spec, index)).ToLookup(c => groups.Root(c.spec.First));
        var results = new SketchMobilityDecision[candidates.Count];
        var components = new List<SketchMobilityComponent>();
        bool Rejected(SketchMobilityDecision d) => d.classification is "conflict" or "removes_intended_motion" or "unverified_intended_motion" ||
            !replacingDrivers && (d.classification is "partly_implied" or "no_independent_motion" ||
                d.classification == "implied" && candidates[d.candidate_index].Value.HasValue);
        foreach (var component in groups.Keys.GroupBy(groups.Root))
        {
            cancel?.Invoke();
            var ids = component.OrderBy(id => id, StringComparer.Ordinal).ToArray();
            var analysis = new SketchMobilitySystem(ids, all, fixedIds, unlocated, cancel);
            foreach (var r in retained[component.Key].OrderBy(r => Priority(r.Spec?.Kind)))
                analysis.Retain(r);
            int? before = analysis.Freedom;
            var movements = analysis.TestMovements(preserve);
            // Intrinsic topology and directions precede discretionary relationships.
            // Stable ties preserve the engineer's first declared equivalent driver.
            foreach (var (spec, index) in requests[component.Key].OrderBy(c => Priority(c.spec.Kind)))
                results[index] = analysis.Add(spec, index, preserve, replacingDrivers);
            var plannedMovements = analysis.TestMovements(preserve);
            bool rejectedHere = requests[component.Key].Any(c => Rejected(results[c.index])) ||
                plannedMovements.Any(m => m.requested && m.status != "permitted");
            components.Add(new(ids, before, rejectedHere ? null : analysis.Freedom, analysis.Reasons.ToArray(), movements, plannedMovements,
                analysis.ExistingIssues.ToArray()));
        }
        bool rejected = results.Any(Rejected)
            || components.SelectMany(c => c.movements_after).Any(m => m.requested && m.status != "permitted");
        bool inconsistent = components.Any(c => c.reasons.Contains("existing_conflict"));
        int? total = !unlocated && components.All(c => c.remaining_dof.HasValue) ? components.Sum(c => c.remaining_dof!.Value) : null;
        int? after = !rejected && !unlocated && components.All(c => c.planned_dof.HasValue) ? components.Sum(c => c.planned_dof!.Value) : null;
        return new(total, after, inconsistent ? "existing_conflict" : rejected ? "rejected" : after.HasValue ? "regular" : "unknown",
            results, components.ToArray());
    }

    internal static string[] Ids(SketchConstraintSpec s) =>
        new[] { s.First, s.Second, s.Third }.OfType<string>().Distinct(StringComparer.Ordinal).ToArray();

    private static int Priority(string? kind) => kind switch
    {
        "coincident" => 0,
        "horizontal" or "vertical" => 1,
        "parallel" or "perpendicular" => 2,
        "fix" => 5,
        _ => 3
    };

    private sealed class Components
    {
        private readonly Dictionary<string, string> parents = new(StringComparer.Ordinal);
        public string[] Keys => parents.Keys.ToArray();
        public void Add(string id) => parents.TryAdd(id, id);
        public string Root(string id)
        {
            while (parents[id] != id) { parents[id] = parents[parents[id]]; id = parents[id]; }
            return id;
        }
        public void Join(string[] ids)
        {
            foreach (string id in ids) Add(id);
            string root = Root(ids[0]);
            for (int i = 1; i < ids.Length; i++) parents[Root(ids[i])] = root;
        }
    }
}
