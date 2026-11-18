using System.Security.Cryptography;
using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using static NanoCADBridge.CadTableEditingReader;

namespace NanoCADBridge;

internal static class CadTableFormulaStructuralBindings
{
    internal sealed record Slot(int BeforeRow, int BeforeColumn, int Row, int Column, string? BeforeField, string? AfterField,
        string BeforeFormula, string Planned, string? Actual, bool LocalExpressions);
    internal static HashSet<string> LocalExpressions(Table table, Transaction tx, Snapshot snapshot, Action cancel)
    {
        var proved = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cell in snapshot.Cells)
        {
            cancel(); var value = snapshot.Values[cell.row * snapshot.Columns + cell.column];
            if (value?.formula is not { } formula || value.native_formula_field is not { } definition || proved.Contains(definition.handle)) continue;
            var id = table.GetFieldId(cell.row, cell.column, 0);
            if (id.IsNull || Validation.NormalizeHandle(id.Handle.ToString()) != definition.handle) continue;
            if (tx.GetObject(id, OpenMode.ForRead) is not Field field || field.GetType() != typeof(Field) || field.Database?.UnmanagedObject != table.Database?.UnmanagedObject) continue;
            try { if (CadTableFormulaEvaluation.WithExpression(field, formula, _ => { }, cancel)) proved.Add(definition.handle); }
            catch (Teigha.Runtime.Exception error) when (error.ErrorStatus is Teigha.Runtime.ErrorStatus.NotImplementedYet or Teigha.Runtime.ErrorStatus.NotApplicable) { }
        }
        return proved;
    }
    internal static bool Matches(JsonElement oldRole, JsonElement actualRole, IReadOnlyList<Slot> slots, Action cancel)
    {
        if (oldRole.GetProperty("table_handle").GetString() != actualRole.GetProperty("table_handle").GetString() ||
            oldRole.GetProperty("block_handle").GetString() != actualRole.GetProperty("block_handle").GetString() ||
            oldRole.GetProperty("scope").GetString() != actualRole.GetProperty("scope").GetString()) return false;
        static HashSet<(int, int, string?)> Cells(JsonElement role) => role.GetProperty("cell_bindings").EnumerateArray()
            .Select(cell => (cell.GetProperty("row").GetInt32(), cell.GetProperty("column").GetInt32(), cell.GetProperty("field_handle").GetString())).ToHashSet();
        if (slots.Count == 0 || slots.Any(slot => slot.Actual == null || !NativeTableFormula343.SameMeaning(slot.Planned, slot.Actual, cancel))) return false;
        return Cells(oldRole).SetEquals(slots.Select(slot => (slot.BeforeRow, slot.BeforeColumn, slot.BeforeField))) &&
            Cells(actualRole).SetEquals(slots.Select(slot => (slot.Row, slot.Column, slot.AfterField)));
    }

    static string[] Differences(JsonElement old, JsonElement actual)
    {
        var result = new List<string>();
        void Visit(JsonElement left, JsonElement right, string at)
        {
            if (left.GetRawText() == right.GetRawText()) return;
            if (left.ValueKind != JsonValueKind.Object || right.ValueKind != JsonValueKind.Object) { result.Add(at); return; }
            foreach (string name in left.EnumerateObject().Select(p => p.Name).Union(right.EnumerateObject().Select(p => p.Name)))
            {
                var path = at.Length == 0 ? name : at + "." + name;
                if (!left.TryGetProperty(name, out var a) || !right.TryGetProperty(name, out var b)) result.Add(path); else Visit(a, b, path);
            }
        }
        Visit(old, actual, ""); return result.ToArray();
    }
    internal static DomainException Mismatch(string message, object expected, object actual, int? sourceRow = null, int? sourceColumn = null, int? finalRow = null, int? finalColumn = null)
    {
        var left = Wire.Element(expected); var right = Wire.Element(actual);
        object Sample(JsonElement value)
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, Wire.Json);
            if (bytes.Length <= 8192) return value;
            return new { complete = false, serialized_bytes = bytes.Length, sha256 = Convert.ToHexString(SHA256.HashData(bytes)) };
        }
        return new("NATIVE_TABLE_MISMATCH", message, false, new
        {
            source_row = sourceRow,
            source_column = sourceColumn,
            row = finalRow,
            column = finalColumn,
            differences = Differences(left, right),
            expected = Sample(left),
            actual = Sample(right)
        });
    }
    internal static object ReactorTarget(Database db, Transaction tx, string handle, Action cancel)
    {
        try
        {
            cancel(); var id = CadStructureWriter.Find(db, handle); var item = tx.GetObject(id, OpenMode.ForRead);
            static string? Id(ObjectId value) => value.IsNull ? null : value.Handle.ToString();
            if (item is not DBDictionary dictionary) return new { handle, runtime_type = item.GetType().FullName, owner = Id(item.OwnerId) };
            var members = new List<object>(); int omitted = 0, bytes = 0;
            foreach (DBDictionaryEntry edge in dictionary)
            {
                cancel(); var child = tx.GetObject(edge.Value, OpenMode.ForRead);
                var member = new { name = edge.Key, handle = Id(child.ObjectId), owner = Id(child.OwnerId), runtime_type = child.GetType().FullName, owned = child.OwnerId == id };
                int size = JsonSerializer.SerializeToUtf8Bytes(member, Wire.Json).Length;
                if (bytes + size <= 2048) { members.Add(member); bytes += size; } else omitted++;
            }
            return new { handle, runtime_type = item.GetType().FullName, owner = Id(item.OwnerId), dictionary_hard_owned = dictionary.TreatElementsAsHard, entries = members, omitted };
        }
        catch (DomainException error) { return new { handle, read_error = error.Error.code }; }
        catch (Teigha.Runtime.Exception error) { return new { handle, read_error = error.ErrorStatus.ToString() }; }
    }
    internal static object Diagnostics(JsonElement before, JsonElement after, IReadOnlyList<Slot> plan, DomainException error, Action cancel, Func<string, object>? readReactor)
    {
        var old = before.GetProperty("owned").EnumerateArray().ToDictionary(item => item.GetProperty("common").GetProperty("handle").GetString()!);
        var current = after.GetProperty("owned").EnumerateArray().ToDictionary(item => item.GetProperty("common").GetProperty("handle").GetString()!);
        static JsonElement Chain(Dictionary<string, JsonElement> records, string? id)
        {
            var chain = new List<JsonElement>(); var seen = new HashSet<string>();
            while (id != null && seen.Add(id) && records.TryGetValue(id, out var record))
            {
                chain.Add(record);
                if (record.TryGetProperty("table_cell_mtext_owner", out _) || record.TryGetProperty("table_content_owner", out _)) break;
                id = record.GetProperty("common").GetProperty("owner_handle").GetString();
            }
            return Wire.Element(chain);
        }
        var sample = new List<object>(); int bytes = 0, omitted = 0; var seenFields = new HashSet<(string?, string?)>();
        var focus = error.Error.details == null ? default : Wire.Element(error.Error.details);
        string? focusId = focus.ValueKind == JsonValueKind.Object && focus.TryGetProperty("field_handle", out var handle) ? handle.GetString() : null;
        foreach (var slot in plan.OrderByDescending(slot => focusId != null && (slot.BeforeField == focusId || slot.AfterField == focusId)))
        {
            cancel(); if (!seenFields.Add((slot.BeforeField, slot.AfterField))) continue;
            var a = Chain(old, slot.BeforeField); var b = Chain(current, slot.AfterField);
            if (a.GetRawText() == b.GetRawText() && slot.BeforeField != focusId) continue;
            string[] Reactors(JsonElement chain) => chain.GetArrayLength() == 0 ? [] : chain[0].GetProperty("common").GetProperty("reactors").EnumerateArray().Select(id => id.GetString()!).ToArray();
            var newReactors = Reactors(b).Except(Reactors(a)).Where(id => b.GetArrayLength() == 0 || id != b[0].GetProperty("common").GetProperty("owner_handle").GetString());
            var full = Wire.Element(new
            {
                slot,
                before_owner_chain = a,
                after_owner_chain = b,
                field_differences = a.GetArrayLength() > 0 && b.GetArrayLength() > 0 ? Differences(a[0], b[0]) : ["field_presence"],
                newly_observed_nonowner_reactors = readReactor == null ? null : newReactors.Select(readReactor).ToArray()
            });
            byte[] data = JsonSerializer.SerializeToUtf8Bytes(full, Wire.Json);
            if (bytes + data.Length <= 32768) { sample.Add(full); bytes += data.Length; }
            else
            {
                omitted++; if (sample.Count == 0)
                {
                    sample.Add(new
                    {
                        slot.BeforeField,
                        slot.AfterField,
                        slot.BeforeRow,
                        slot.BeforeColumn,
                        slot.Row,
                        slot.Column,
                        complete = false,
                        bytes = data.Length,
                        sha256 = Convert.ToHexString(SHA256.HashData(data)),
                        field_differences = full.GetProperty("field_differences")
                    }); bytes = 1024;
                }
            }
        }
        return new
        {
            comparison = error.Error.message,
            comparison_details = error.Error.details,
            formula_owner_comparisons = sample,
            oversized_or_omitted_count = omitted,
            scope = "actual_captured_owner_chains_from_bound_Field_to_layout_carrier; original_handles_and_definitions"
        };
    }
}
