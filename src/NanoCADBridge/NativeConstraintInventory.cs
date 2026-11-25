using System.Runtime.InteropServices;
using Multicad;
using Multicad.DatabaseServices.Constraints;

namespace NanoCADBridge;

// All enumeration routes use the caller's already validated manager, plane and owner.
internal static class NativeConstraintInventory
{
    internal sealed record Result(List<McObjectId> Ids, bool Complete, string Source, string? Reason)
    {
        public IReadOnlyDictionary<string,string> Routes { get; init; } = new Dictionary<string,string>();
    }

    internal static Result Read(Constraints2D manager, Func<IEnumerable<McObjectId>?>? members, Action cancel)
    {
        var seen = new HashSet<McObjectId>();
        var routes = new Dictionary<string,string>();
        var budget = new ModelInventoryBudget(cancel);
        static string Count(int? count) => count?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null";
        void RecordFailure(string route,Exception error)
        {
            string outcome=$"{error.GetType().Name} (0x{error.HResult:X8})";
            routes[route]=routes.TryGetValue(route,out var previous) ? $"{previous}, {outcome}" : outcome;
        }
        static bool OptionalReadFailure(Exception error) =>
            error is InvalidOperationException or NotSupportedException or NotImplementedException or COMException;
        bool Collect(IEnumerable<McObjectId> ids)
        {
            bool valid = true;
            var actual = new HashSet<McObjectId>();
            foreach (var id in ids)
            {
                cancel();
                if (id.IsNull || !actual.Add(id)) { valid = false; continue; }
                if (seen.Add(id)) budget.Add(id.ToString());
            }
            return valid;
        }
        bool CheckMap(Dictionary<McObjectId,List<McObjectId>> map)
        {
            var reverse = new Dictionary<McObjectId,HashSet<McObjectId>>();
            bool valid = true;
            foreach (var (member, ids) in map)
            {
                cancel();
                if (ids == null) { valid = false; continue; }
                valid &= !member.IsNull;
                valid &= Collect(ids);
                foreach (var id in ids.Where(id => !id.IsNull))
                {
                    if (!reverse.TryGetValue(id, out var operands)) reverse.Add(id, operands = new());
                    operands.Add(member);
                }
            }
            if (!reverse.Keys.ToHashSet().SetEquals(seen)) valid = false;
            if (!valid) return false;
            foreach (var (id, operands) in reverse)
            {
                cancel();
                using var info = manager.GetConstraintInfo(id);
                if (info == null) return false;
                var actual = new[] { info.FirstObject, info.SecondObject, info.ThirdObject }
                    .Where(reference => reference != null && !reference.ObjectId.IsNull)
                    .Select(reference => reference!.ObjectId);
                if (!operands.SetEquals(actual)) return false;
            }
            return true;
        }
        Result Finish(bool complete, string source, string? reason = null) =>
            new(seen.OrderBy(id => id.ToString(), StringComparer.Ordinal).ToList(), complete, source, reason) { Routes=routes };

        cancel();
        try
        {
            var direct = manager.GetAllConstraints();
            routes["scope_list"]="ids="+Count(direct?.Count);
            if (direct != null && Collect(direct)) return Finish(true, "native_scope_list");
        }
        catch (Exception error) when (OptionalReadFailure(error)) { RecordFailure("scope_list",error); }

        // In the installed 26.0 mapimgd, these full-scope overloads request
        // different outputs from one native enumeration. They can all fail
        // together. The final member + external query uses another function.
        bool disagreement = false;
        try
        {
            var listed = manager.GetAllConstraints(out var mapped);
            routes["scope_list_and_map"]="ids="+Count(listed?.Count)+", mapped_objects="+Count(mapped?.Count);
            var prior = seen.ToHashSet();
            bool valid = listed != null && Collect(listed);
            if (mapped != null)
            {
                bool mappedAll = CheckMap(mapped);
                if (mappedAll && (listed == null || valid && seen.SetEquals(listed))) return Finish(true, "native_scope_list_and_map");
                disagreement = true;
            }
            else if (valid && prior.IsSubsetOf(listed!)) return Finish(true, "native_scope_list_with_map_overload");
        }
        catch (Exception error) when (OptionalReadFailure(error)) { RecordFailure("scope_list_and_map",error); }
        try
        {
            var mapped = manager.GetAllObjectsToConstraints();
            routes["scope_object_map"]="mapped_objects="+Count(mapped?.Count);
            if (mapped != null)
            {
                if (CheckMap(mapped) && !disagreement) return Finish(true, "native_verified_scope_object_map");
                disagreement = true;
            }
        }
        catch (Exception error) when (OptionalReadFailure(error)) { RecordFailure("scope_object_map",error); }
        if (members != null)
        {
            try
            {
                var selected = members()?.Where(id => !id.IsNull).Distinct().ToList();
                routes["member_and_external"]="selected="+Count(selected?.Count);
                if (selected is { Count: > 0 })
                {
                    ModelInventoryBudget.Ids(selected, cancel);
                    var attached = manager.GetConstraints(selected, out var external);
                    routes["member_and_external"]+=", attached="+Count(attached?.Count)+", external="+Count(external?.Count);
                    if (attached != null) Collect(attached);
                    if (external != null) Collect(external);
                }
            }
            catch (Exception error) when (OptionalReadFailure(error)) { RecordFailure("member_and_external",error); }
        }
        return Finish(false, "partial_native_observation", disagreement ? "native_inventory_routes_disagree" : "complete_native_inventory_unavailable");
    }
}
