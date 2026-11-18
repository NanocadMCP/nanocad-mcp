using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using static NanoCADBridge.CadTableEditingReader;

namespace NanoCADBridge;

// Comparison of regenerated extension dictionaries on the same resident MText.
// The original inventories and the returned current DWG identities stay intact.
internal static class CadTableRetainedFieldStorage
{
    sealed record Link(string Id, string Owner, string Child, JsonElement Record);
    static bool Exact(JsonElement left, JsonElement right) => left.GetRawText() == right.GetRawText();
    static string Id(JsonElement record) => record.GetProperty("common").GetProperty("handle").GetString()!;
    static List<Link>? Chain(string carrier, string? extension, string field, Dictionary<string, JsonElement> records, Action cancel)
    {
        if (extension == null) return null;
        var chain = new List<Link>(); var seen = new HashSet<string>(); string owner = carrier, id = extension;
        while (id != field)
        {
            cancel(); if (!seen.Add(id) || !records.TryGetValue(id, out var record)) return null;
            var common = record.GetProperty("common");
            if (common.GetProperty("runtime_type").GetString() != "Teigha.DatabaseServices.DBDictionary" ||
                common.GetProperty("owner_handle").GetString() != owner || common.GetProperty("extension_handle").ValueKind != JsonValueKind.Null ||
                common.GetProperty("xdata").ValueKind != JsonValueKind.Null || record.GetProperty("dictionary_hard_owned").ValueKind != JsonValueKind.True) return null;
            var reactors = common.GetProperty("reactors");
            if (reactors.GetArrayLength() != 0 && (owner == carrier || reactors.GetArrayLength() != 1 || reactors[0].GetString() != owner)) return null;
            var entries = record.GetProperty("entries"); if (entries.GetArrayLength() != 1) return null;
            var edge = entries[0]; string child = edge.GetProperty("handle").GetString()!;
            if (!edge.GetProperty("owned").GetBoolean() || edge.GetProperty("owner_handle").GetString() != id ||
                !records.TryGetValue(child, out var next) || next.GetProperty("common").GetProperty("owner_handle").GetString() != id ||
                !Exact(edge.GetProperty("runtime_type"), next.GetProperty("common").GetProperty("runtime_type"))) return null;
            chain.Add(new(id, owner, child, record)); owner = id; id = child;
        }
        return chain.Count > 0 && records[field].TryGetProperty("table_formula_field", out _) ? chain : null;
    }

    static JsonObject Copy(JsonElement item) => JsonNode.Parse(item.GetRawText())!.AsObject();
    static void MoveOwner(JsonObject record, string actual, string prior)
    {
        var common = record["common"]!.AsObject(); common["owner_handle"] = prior;
        var reactors = common["reactors"]!.AsArray();
        // Only the reciprocal link to the proved hard owner may change.
        // Preserve and compare every other persistent reactor as an exact ID.
        if (actual == prior) return;
        for (int i = 0; i < reactors.Count; i++) if (reactors[i]!.GetValue<string>() == actual) reactors[i] = prior;
        var sorted = reactors.Select(value => value!.GetValue<string>()).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        common["reactors"] = JsonSerializer.SerializeToNode(sorted, Wire.Json);
    }

    internal static (JsonElement After, Snapshot PriorCells) Compare(JsonElement before, JsonElement after, Snapshot original, List<string> warnings, Action cancel,
        IReadOnlyList<CadTableFormulaStructuralBindings.Slot>? fieldPlan = null)
    {
        var old = before.GetProperty("owned").EnumerateArray().ToDictionary(Id);
        var current = after.GetProperty("owned").EnumerateArray().ToDictionary(Id);
        var replacements = new Dictionary<string, JsonElement>();
        var owners = new Dictionary<string, (string Old, string New)>();
        var planned = fieldPlan?.Where(slot => slot.BeforeField != null).ToLookup(slot => slot.BeforeField!, StringComparer.Ordinal);
        var actualCarriers = planned == null ? null : current.Values.Where(record => record.TryGetProperty("table_cell_mtext_owner", out var role) &&
            role.GetProperty("field_handle").ValueKind == JsonValueKind.String).ToLookup(record => record.GetProperty("table_cell_mtext_owner").GetProperty("field_handle").GetString()!, StringComparer.Ordinal);
        var provedReplacements = new HashSet<(string, string)>();
        foreach (var (carrier, a) in old)
        {
            cancel(); if (!a.TryGetProperty("table_cell_mtext_owner", out var role)) continue;
            string? field = role.GetProperty("field_handle").GetString();
            if (field == null || role.GetProperty("cell_bindings").GetArrayLength() == 0) continue;
            string finalField = field; JsonElement b; List<Link>? right = null;
            var slots = planned?[field].ToArray();
            if (slots != null)
            {
                if (slots.Length == 0 || slots[0].AfterField is not { } destination || slots.Any(slot => slot.AfterField != destination)) continue;
                finalField = destination; var candidates = actualCarriers![finalField].Where(record =>
                    CadTableFormulaStructuralBindings.Matches(role, record.GetProperty("table_cell_mtext_owner"), slots, cancel)).ToArray();
                JsonElement chosen = default;
                foreach (var candidate in candidates)
                {
                    var candidateChain = Chain(Id(candidate), candidate.GetProperty("common").GetProperty("extension_handle").GetString(), finalField, current, cancel);
                    if (candidateChain == null) continue;
                    if (right != null) { right = null; break; }
                    right = candidateChain; chosen = candidate;
                }
                if (right == null) continue; b = chosen;
            }
            else if (!current.TryGetValue(carrier, out b) || !b.TryGetProperty("table_cell_mtext_owner", out var actualRole) || !Exact(role, actualRole)) continue;
            string? oldRoot = a.GetProperty("common").GetProperty("extension_handle").GetString();
            string? newRoot = b.GetProperty("common").GetProperty("extension_handle").GetString();
            if (oldRoot == newRoot && (slots == null || field == finalField && Exact(old[field], current[field]))) continue;
            var left = Chain(carrier, oldRoot, field, old, cancel); right ??= Chain(Id(b), newRoot, finalField, current, cancel);
            if (left == null || right == null || left.Count != right.Count) continue;
            var staged = new Dictionary<string, JsonElement>(); bool equivalent = true;
            foreach (var (prior, actual) in left.Zip(right))
            {
                cancel(); if (prior.Id != actual.Id && (old.ContainsKey(actual.Id) || current.ContainsKey(prior.Id))) { equivalent = false; break; }
                var record = Copy(actual.Record); MoveOwner(record, actual.Owner, prior.Owner);
                record["common"]!["handle"] = prior.Id;
                var edge = record["entries"]![0]!; edge["handle"] = prior.Child; edge["owner_handle"] = prior.Id;
                if (!Exact(Wire.Element(record), prior.Record)) { equivalent = false; break; }
                staged.Add(actual.Id, prior.Record);
            }
            if (!equivalent) continue;
            string from = left[^1].Id, to = right[^1].Id;
            var oldField = old[field]; var newField = current[finalField];
            var definition = newField.GetProperty("table_formula_field");
            if (definition.GetProperty("handle").GetString() != finalField || definition.GetProperty("owner_handle").GetString() != to ||
                oldField.GetProperty("table_formula_field").GetProperty("owner_handle").GetString() != from) continue;
            var normalized = Copy(newField);
            var oldDefinition = oldField.GetProperty("table_formula_field");
            if (slots is { Length: > 0 } && slots.All(slot => slot.LocalExpressions))
            {
                normalized["table_formula_field"]!["field_code"] = oldDefinition.GetProperty("field_code").GetString();
                normalized["table_formula_field"]!["object_reference_code"] = oldDefinition.GetProperty("object_reference_code").GetString();
            }
            if (field != finalField)
            {
                // A new resident Field can correspond only to the entire planned
                // binding group. Content attached to a different identity cannot
                // be transferred by matching expression, role or coordinates.
                if (old.ContainsKey(finalField) || current.ContainsKey(field) || slots == null || !slots.All(slot => slot.LocalExpressions)) continue;
                bool HasIndependentData(JsonElement record, string owner)
                {
                    var common = record.GetProperty("common"); var reactors = common.GetProperty("reactors");
                    return common.GetProperty("xdata").ValueKind != JsonValueKind.Null || common.GetProperty("extension_handle").ValueKind != JsonValueKind.Null ||
                        reactors.GetArrayLength() > 1 || reactors.GetArrayLength() == 1 && reactors[0].GetString() != owner;
                }
                if (HasIndependentData(oldField, from) || HasIndependentData(newField, to)) continue;
                normalized["common"]!["handle"] = field; normalized["table_formula_field"]!["handle"] = field;
            }
            var priorReactors = oldField.GetProperty("common").GetProperty("reactors").EnumerateArray().Select(item => item.GetString()).ToHashSet(StringComparer.Ordinal);
            var actualReactors = normalized["common"]!["reactors"]!.AsArray();
            // nanoCAD can retain the original owner's existing reactor and
            // append a reciprocal reactor to this same Field's new hard owner.
            // Remove exactly that extra current-owner edge for comparison. The
            // old edge and every other ID must still compare byte-for-byte.
            bool retainedOwner = from != to && priorReactors.Contains(from) && !priorReactors.Contains(to) &&
                actualReactors.Count(node => node!.GetValue<string>() == to) == 1 && actualReactors.Any(node => node!.GetValue<string>() == from);
            if (retainedOwner)
            {
                actualReactors.Remove(actualReactors.Single(node => node!.GetValue<string>() == to));
                normalized["common"]!["owner_handle"] = from;
            }
            else MoveOwner(normalized, to, from);
            normalized["table_formula_field"]!["owner_handle"] = from;
            if (!Exact(Wire.Element(normalized), oldField)) continue;
            if (field != finalField) { provedReplacements.Add((field, finalField)); continue; }
            var carrierRecord = Copy(b); carrierRecord["common"]!["extension_handle"] = oldRoot;
            foreach (var item in staged) replacements.Add(item.Key, item.Value);
            if (slots == null) replacements.Add(carrier, Wire.Element(carrierRecord));
            replacements.Add(field, oldField); owners.Add(field, (from, to));
            if (retainedOwner) warnings.Add($"TABLE_FORMULA_STORAGE_REGENERATED: Field {field} kept its reactor to previous owner {from} and acquired a reactor to current hard owner {to}. Its current MText is {Id(b)}.");
        }
        if (fieldPlan != null) foreach (var slot in fieldPlan)
        {
            cancel(); if (slot.BeforeField != null && slot.AfterField == null)
                throw new DomainException("NATIVE_TABLE_MISMATCH", "A retained formula lost its actual resident Field binding.", false,
                    new { field_handle = slot.BeforeField, slot.BeforeRow, slot.BeforeColumn, slot.Row, slot.Column });
            if (slot.BeforeField is { } source && slot.AfterField is { } target && source != target && !provedReplacements.Contains((source, target)))
                throw new DomainException("NATIVE_TABLE_MISMATCH", "A retained formula acquired a different native Field without an exclusive mapped owner chain and matching local definition.", false,
                    new { field_handle = source, actual_field_handle = target, slot.BeforeRow, slot.BeforeColumn, slot.Row, slot.Column });
            if (slot.BeforeField is { } same && slot.AfterField == same && !slot.LocalExpressions)
            {
                var left = old[same].GetProperty("table_formula_field"); var right = current[same].GetProperty("table_formula_field");
                if (!Exact(left.GetProperty("field_code"), right.GetProperty("field_code")) || !Exact(left.GetProperty("object_reference_code"), right.GetProperty("object_reference_code")))
                    throw new DomainException("NATIVE_TABLE_MISMATCH", "A retained Field changed its code without matching native local expressions.", false, new { field_handle = same });
            }
        }
        if (owners.Count == 0) return (after, original);
        var comparable = Copy(after); var entries = comparable["owned"]!.AsArray();
        for (int i = 0; i < entries.Count; i++)
        {
            cancel(); var entry = entries[i]!; string id = entry["common"]!["handle"]!.GetValue<string>();
            if (replacements.TryGetValue(id, out var replacement)) { entries[i] = Copy(replacement); continue; }
            // Comparable() has already validated these actual TableContent
            // slots against every resident Table cell and its native FieldId.
            if (entry["common"]!["runtime_type"]!.GetValue<string>() != "Teigha.DatabaseServices.TableContent" || entry["entries"] is not JsonArray slots) continue;
            foreach (var slot in slots)
            {
                cancel(); if (slot!["owned"]!.GetValue<bool>()) continue;
                if (owners.TryGetValue(slot["handle"]!.GetValue<string>(), out var owner) && slot["owner_handle"]!.GetValue<string>() == owner.New)
                    slot["owner_handle"] = owner.Old;
            }
        }
        var values = original.Values.ToArray();
        for (int i = 0; i < values.Length; i++)
        {
            cancel(); if (values[i] is not { } value || value.native_formula_field is not { } field ||
                !owners.TryGetValue(field.handle, out var owner) || field.owner_handle != owner.Old) continue;
            values[i] = value with { native_formula_field = field with { owner_handle = owner.New } };
        }
        return (Wire.Element(comparable), original with { Values = values });
    }
}
