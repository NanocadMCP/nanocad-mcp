namespace NanoCAD.Contracts;

// Incremental elimination in each connected component. Proven identities use a
// separate basis over observable geometric quantities. The numerical basis only
// certifies local freedom when the represented equations have full regular rank.
internal sealed class MobilityBasis
{
    private sealed record Pivot(int Column, Dictionary<int, double> Terms, double Residual, HashSet<string> Sources);
    internal sealed record Change(string Kind, string[] Sources);
    private readonly List<Pivot> pivots;
    private readonly bool identities;
    internal int Rank => pivots.Count;
    internal MobilityBasis(bool identities) { this.identities = identities; pivots = new(); }
    private MobilityBasis(MobilityBasis source) { identities = source.identities; pivots = new(source.pivots); }
    internal MobilityBasis Copy() => new(this);

    internal Change Insert(Dictionary<int, double> terms, double residual, string id, Action? cancel)
    {
        var row = new Dictionary<int, double>(terms);
        var sources = new HashSet<string>(StringComparer.Ordinal) { id };
        double scale = row.Count == 0 ? 1 : row.Values.Max(Math.Abs);
        if (scale > 0 && scale != 1)
        {
            foreach (var c in row.Keys.ToArray()) row[c] /= scale;
            residual /= scale;
        }
        double residualScale = Math.Max(1, Math.Abs(residual));
        foreach (var pivot in pivots)
        {
            cancel?.Invoke();
            if (!row.Remove(pivot.Column, out var factor) || factor == 0) continue;
            foreach (var (col, coeff) in pivot.Terms)
            {
                double v = row.GetValueOrDefault(col) - factor * coeff;
                if (v == 0) row.Remove(col); else row[col] = v;
            }
            residual -= factor * pivot.Residual;
            residualScale += Math.Abs(factor * pivot.Residual);
            sources.UnionWith(pivot.Sources);
        }
        var best = row.Count == 0 ? default : row.MaxBy(kv => Math.Abs(kv.Value));
        double largest = Math.Abs(best.Value);
        if (largest <= (identities ? 0 : 1e-12))
        {
            string outcome = Math.Abs(residual) <= 2e-12 * residualScale ? "implied" :
                Math.Abs(residual) <= 1e-7 * residualScale ? "uncertain" : "conflict";
            return new(outcome, sources.ToArray());
        }
        if (!double.IsFinite(largest) || !double.IsFinite(residual) || largest < 1e-8)
            return new("uncertain", sources.ToArray());
        row.Remove(best.Key);
        foreach (int col in row.Keys.ToArray()) row[col] /= best.Value;
        pivots.Add(new(best.Key, row, residual / best.Value, sources));
        return new("independent", sources.ToArray());
    }

    internal Dictionary<int, double> ParticularDisplacement()
    {
        var solution = new Dictionary<int, double>();
        for (int i = pivots.Count - 1; i >= 0; i--)
        {
            var p = pivots[i];
            solution[p.Column] = -p.Residual - p.Terms.Sum(term => term.Value * solution.GetValueOrDefault(term.Key));
        }
        return solution;
    }
}

internal sealed class SketchMobilitySystem
{
    private readonly Action? cancel;
    private readonly HashSet<string> members;
    private readonly HashSet<string> fixedMembers;
    private readonly MobilityGeometry geometry;
    private MobilityBasis physical = new(false), facts = new(true);
    private readonly Dictionary<SketchConstraintSpec, string> direct = new();
    private readonly Dictionary<SketchConstraintSpec, (double value, string id)> dimensions = new();
    private readonly Dictionary<(string, string, string, string), string> joints = new();
    private readonly HashSet<string> blockers = new(StringComparer.Ordinal);
    private bool allAffine = true, offPose;
    internal HashSet<string> Reasons { get; } = new(StringComparer.Ordinal);
    internal List<SketchMobilityExistingIssue> ExistingIssues { get; } = new();
    internal int? Freedom => Reasons.Count == 0 ? geometry.Variables - physical.Rank : null;

    internal SketchMobilitySystem(string[] ids, IReadOnlyDictionary<string, SketchConstraintCurve> snapshot,
        HashSet<string> fixedGeometry, bool incomplete, Action? cancel)
    {
        this.cancel = cancel;
        members = ids.ToHashSet(StringComparer.Ordinal);
        fixedMembers = fixedGeometry;
        geometry = new(ids, snapshot, fixedGeometry, cancel);
        if (!geometry.Complete) Reasons.Add("unsupported_or_missing_geometry");
        if (incomplete) Reasons.Add("incomplete_constraint_inventory");
    }

    private void Unknown(string reason, string id)
    {
        Reasons.Add(reason);
        blockers.Add(id);
    }
    private bool Joined(string a, string ap, string b, string bp) => joints.ContainsKey((a, ap, b, bp));
    private static SketchConstraintSpec Canonical(SketchConstraintSpec s)
    {
        if (s.Second == null || s.Kind is not ("coincident" or "parallel" or "perpendicular" or "equal" or "concentric" or "collinear" or "symmetric"
            or "tangent" or "distance" or "distance_x" or "distance_y" or "angle")) return s;
        bool swap = string.CompareOrdinal(s.First, s.Second) > 0 ||
            s.First == s.Second && (Nullable.Compare(s.FirstSegmentIndex, s.SecondSegmentIndex) > 0 ||
                s.FirstSegmentIndex == s.SecondSegmentIndex && string.CompareOrdinal(s.FirstPoint, s.SecondPoint) > 0);
        return swap ? s with
        {
            First = s.Second,
            Second = s.First,
            FirstPoint = s.SecondPoint,
            SecondPoint = s.FirstPoint,
            FirstSegmentIndex = s.SecondSegmentIndex,
            SecondSegmentIndex = s.FirstSegmentIndex
        } : s;
    }
    private void Remember(SketchConstraintSpec s, string id)
    {
        var key = Canonical(s);
        direct.TryAdd(key, id);
        if (s.Value.HasValue) dimensions.TryAdd(key with { Value = null }, (s.Value.Value, id));
        if (s.Kind == "coincident" && s.Second != null && s.FirstPoint != null && s.SecondPoint != null &&
            s.FirstSegmentIndex == null && s.SecondSegmentIndex == null)
        {
            joints.TryAdd((s.First, s.FirstPoint, s.Second, s.SecondPoint), id);
            joints.TryAdd((s.Second, s.SecondPoint, s.First, s.FirstPoint), id);
        }
    }

    internal void Retain(SketchMobilityRelation relation)
    {
        cancel?.Invoke();
        if (relation.Spec == null) { Unknown("unresolved_constraint_operands_or_type", relation.Id); return; }
        var result = Analyze(relation.Spec, relation.Id, -1, null, false);
        if (result.classification is "implied" or "partly_implied" or "conflict")
            ExistingIssues.Add(new(relation.Id, result.classification, result.witnesses, result.reason));
    }

    internal SketchMobilityDecision Add(SketchConstraintSpec spec, int index, IReadOnlyList<SketchMobilityMotion>? preserve, bool replacing)
        => Analyze(spec, "candidate[" + index + "]", index, preserve, replacing);

    internal SketchMobilityMovement[] TestMovements(IReadOnlyList<SketchMobilityMotion>? requested)
    {
        var movable = members.Except(fixedMembers).ToArray();
        var result = new List<SketchMobilityMovement>();
        if (movable.Length != 0)
        {
            result.Add(Movement(new("translate_component_x", movable, 1, 0), false));
            result.Add(Movement(new("translate_component_y", movable, 0, 1), false));
        }
        foreach (var motion in requested ?? Array.Empty<SketchMobilityMotion>())
            if (motion.object_ids.Any(members.Contains)) result.Add(Movement(motion, true));
        return result.ToArray();
    }

    private SketchMobilityMovement Movement(SketchMobilityMotion motion, bool requested, SketchConstraintSpec? proposal = null)
    {
        var selected = motion.object_ids.ToHashSet(StringComparer.Ordinal);
        if (selected.Any(id => members.Contains(id) && fixedMembers.Contains(id)))
            return new(motion.name, "blocked", selected.Where(fixedMembers.Contains).ToArray(), requested);
        var blocked = new List<string>();
        bool unknown = false;
        foreach (var (spec, relationId) in direct)
        {
            cancel?.Invoke();
            string state = geometry.Translation(spec, selected, motion);
            if (state == "blocked") blocked.Add(relationId);
            unknown |= state == "unknown";
        }
        if (proposal != null)
        {
            string state = geometry.Translation(proposal, selected, motion);
            if (state == "blocked") blocked.Add("proposed_relation");
            unknown |= state == "unknown";
        }
        if (blocked.Count != 0) return new(motion.name, "blocked", blocked.ToArray(), requested);
        return unknown || Freedom == null || offPose && !allAffine
            ? new(motion.name, "unknown", blockers.ToArray(), requested) : new(motion.name, "permitted", Array.Empty<string>(), requested);
    }

    private SketchMobilityDecision Analyze(SketchConstraintSpec spec, string id, int index,
        IReadOnlyList<SketchMobilityMotion>? preserve, bool replacing)
    {
        cancel?.Invoke();
        bool existing = index < 0;
        int? before = Freedom;
        var sources = new HashSet<string>(StringComparer.Ordinal);
        var motions = preserve?.Where(m => m.object_ids.Any(members.Contains)).ToArray() ?? Array.Empty<SketchMobilityMotion>();
        bool protectedRequest = motions.Length != 0;
        SketchMobilityDecision Result(string kind, string message, int? after = null)
            => new(index, kind, before, after, message, sources.Where(s => s != id).OrderBy(s => s, StringComparer.Ordinal).ToArray());
        SketchMobilityDecision Inconclusive(string reason, string message)
        {
            sources.UnionWith(blockers);
            Unknown(reason, id);
            return Result(protectedRequest ? "unverified_intended_motion" : "unknown", message);
        }
        SketchMobilityDecision Conflict(string message)
        {
            if (existing) { Unknown("existing_conflict", id); blockers.UnionWith(sources); }
            return Result("conflict", message);
        }

        if (!existing && Reasons.Contains("existing_conflict"))
        {
            sources.UnionWith(blockers);
            return Conflict("The retained constraints already contain an incompatible requirement.");
        }
        var canonical = Canonical(spec);
        if (spec.Value is double target && dimensions.TryGetValue(canonical with { Value = null }, out var dimension) &&
            target != dimension.value)
        {
            sources.Add(dimension.id);
            return Conflict("Two driving values were assigned to the same dimension and exact operands.");
        }
        if (direct.TryGetValue(canonical, out string? duplicate) && !protectedRequest)
        {
            sources.Add(duplicate);
            return Result("implied", "The same native relation and operands are already present.", before);
        }

        if (spec.Second == null && spec.Kind is "coincident" or "parallel" or "perpendicular" or "concentric" or "equal"
            or "angle" or "tangent" or "collinear" or "distance" or "distance_x" or "distance_y")
            return Inconclusive("unresolved_constraint_operands_or_type", "A required operand is unavailable.");
        MobilityGeometry.Equations expression;
        try { expression = geometry.Build(spec, Joined); }
        catch (MobilityGeometry.Unsupported)
        {
            if (!existing && !replacing && before == 0)
                return Result("no_independent_motion", "The current regular component has no remaining motion for an additional driving relation.");
            Remember(spec, id);
            return Inconclusive("unsupported_equation_or_branch",
                "The observed operands do not establish the native equation and its geometric branch.");
        }

        var trialFacts = facts.Copy();
        int proofImplied = 0;
        bool uncertainProof = false;
        foreach (var row in expression.Rows.Concat(expression.AdditionalFacts))
        {
            if (row.Identity == null) continue;
            var change = trialFacts.Insert(row.Identity, row.Value, id, cancel);
            if (change.Kind == "conflict")
            {
                sources.UnionWith(change.Sources);
                return Conflict("The requested value or geometric relation contradicts retained equations on this branch.");
            }
            if (change.Kind == "implied" && expression.Rows.Contains(row))
            {
                sources.UnionWith(change.Sources);
                proofImplied++;
            }
            uncertainProof |= change.Kind == "uncertain";
        }
        bool allImplied = proofImplied == expression.Rows.Length;

        // Validate the promised motion against both retained equations and this proposal.
        foreach (var motion in motions)
        {
            var movement = Movement(motion, true, spec);
            sources.UnionWith(movement.witnesses);
            if (movement.status == "blocked")
            {
                return Result("removes_intended_motion", $"The required movement '{motion.name}' is blocked by the retained or proposed equations.");
            }
            if (movement.status == "unknown" || uncertainProof)
                return Inconclusive("unverified_intended_motion", $"The movement '{motion.name}' cannot be certified in this component.");
        }
        if (allImplied)
        {
            facts = trialFacts;
            Remember(spec, id);
            return Result("implied", "The relation follows from the existing equations.", before);
        }
        if (!existing && !replacing && before == 0)
            return Result("no_independent_motion", "The current regular component has no remaining motion. Adding another driving relation requires replacing an existing driver.");

        var trialPhysical = physical.Copy();
        int gain = 0;
        bool ambiguous = uncertainProof;
        bool affine = allAffine && expression.Rows.All(r => r.Affine);
        foreach (var row in expression.Rows)
        {
            var change = trialPhysical.Insert(row.Gradient, row.Value, id, cancel);
            if (change.Kind == "independent") gain++;
            else if (change.Kind == "conflict" && affine && !uncertainProof)
            {
                // Numerically dependent derivatives are never a proof of an exact conflict.
                ambiguous = true;
            }
            else if (change.Kind != "implied") ambiguous = true;
        }
        bool differs = expression.Rows.Any(row => Math.Abs(row.Value) > 1e-7);
        bool previousOffPose = offPose;
        bool feasible = !(differs || offPose) || affine && geometry.Admissible(trialPhysical.ParticularDisplacement());
        bool partial = proofImplied > 0 && proofImplied < expression.Rows.Length;

        if (!existing && !replacing && partial)
            return Result("partly_implied", "Some of the requested equations are already implied. Use independent operands or explicitly replace the old driver.",
                before.HasValue && !ambiguous && feasible && gain == expression.Rows.Length - proofImplied ? before.Value - gain : null);

        facts = trialFacts;
        physical = trialPhysical;
        allAffine = affine;
        offPose |= differs;
        Remember(spec, id);
        if (!feasible || existing && differs)
            return Inconclusive("native_pose_or_solution_branch_unverified", "The observed geometry does not establish a feasible solution on the required branch.");
        if (ambiguous || gain != expression.Rows.Length - proofImplied)
            return Inconclusive("singular_or_unproved_dependence", "The local rank is inconclusive. No equation was identified as redundant from its derivative alone.");
        if (protectedRequest && (differs || previousOffPose) && !affine)
            return Inconclusive("unverified_intended_motion", "The required movement could not be checked at the proposed geometry.");
        if (before == null)
            return Inconclusive("incomplete_component", "Existing geometry or constraints prevent a complete independence test.");
        if (partial)
            return Result("partly_implied", "Part of the retained relation is implied by earlier equations.", Freedom);
        return Result("independent", "The equations remove independent freedoms in this component.", Freedom);
    }
}
