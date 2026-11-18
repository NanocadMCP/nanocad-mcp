using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using static NanoCADBridge.CadTableEditingReader;

namespace NanoCADBridge;

internal static class CadTableFormulaEvaluation
{
    // A native text Field can wrap the typed expression Field. Only the actual
    // direct child and its complete local expression identify the numeric cache.
    internal static bool WithExpression(Field root, string formula, Action<Field> action, Action cancel)
    {
        bool Matches(Field field, bool allowBareExpression = false)
        {
            const string prefix = @"\AcExpr ";
            string code = field.GetFieldCode();
            if (code.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) code = code[prefix.Length..];
            else if (!allowBareExpression) return false;
            return NativeTableFormula343.SameMeaning(formula, code, cancel);
        }
        bool Leaf(Field field)
        {
            if (field.GetType() != typeof(Field) || !string.Equals(field.EvaluatorId, "AcExpr", StringComparison.OrdinalIgnoreCase) || !Matches(field, true)) return false;
            var nested = field.GetChildren();
            try { if (nested.Length != 0) return false; action(field); return true; }
            finally { foreach (var item in nested) item?.Dispose(); }
        }
        try
        {
            if (!root.IsTextField) return Leaf(root);
            if (root.EvaluatorId != "_text" || !Matches(root)) return false;
            var children = root.GetChildren();
            try { return children.Length == 1 && children[0] != null && Leaf(children[0]); }
            finally { foreach (var child in children) child?.Dispose(); }
        }
        catch (DomainException error) when (error.Error.code == "UNSUPPORTED_TABLE_FORMULA") { return false; }
    }

    // Input edits can affect a chain. Visit only reachable local formulas, in
    // dependency order, and only when all formula inputs are proved native
    // local expressions. Unrelated or opaque fields never receive Evaluate.
    internal static Snapshot AfterWrite(Table table, Transaction transaction, Snapshot layout,
        IEnumerable<(int Row, int Column)> changed, List<string> warnings, Action cancel)
    {
        if (!layout.Values.Any(value => value?.native_formula_field != null)) return layout;
        var affected = changed.Select(cell => new TableFormulaReference343(cell.Row, cell.Column)).ToHashSet();
        var formulas = new Dictionary<TableFormulaReference343, (Field Field, TableFormulaReference343[] References)>();
        var budget = new ModelInventoryBudget(cancel);
        for (int index = 0; index < layout.Values.Length; index++)
        {
            cancel(); var cell = layout.Cells[index]; var key = new TableFormulaReference343(cell.row, cell.column);
            var value = layout.Values[index];
            if (value?.formula is not { } formula) continue;
            if (value.native_formula_field == null) continue;
            try
            {
                var parsed = NativeTableFormula350.Expand(NativeTableFormula343.Inspect(formula, cancel), layout.Rows, layout.Columns, cancel);
                var id = table.GetFieldId(cell.row, cell.column, 0);
                if (id.IsNull || Validation.NormalizeHandle(id.Handle.ToString()) != value.native_formula_field.handle) continue;
                var field = (Field)transaction.GetObject(id, OpenMode.ForRead);
                if (!WithExpression(field, formula, _ => { }, cancel)) continue;
                budget.Add(new { key, parsed.References }); formulas.Add(key, (field, parsed.References));
            }
            catch (DomainException error) when (error.Error.code is "UNSUPPORTED_TABLE_FORMULA" or "CELL_OUT_OF_RANGE") { }
            catch (Teigha.Runtime.Exception error) when (error.ErrorStatus == Teigha.Runtime.ErrorStatus.NotImplementedYet) { }
        }
        bool attempted = false;
        // Each edge is processed once, including a range expanded by the
        // existing parser. Blocked dependencies and cycles stay unevaluated.
        var waiting = new Dictionary<TableFormulaReference343, List<TableFormulaReference343>>();
        var counts = new Dictionary<TableFormulaReference343, int>();
        var queue = new Queue<TableFormulaReference343>();
        foreach (var (key, item) in formulas)
        {
            cancel(); int count = 0;
            foreach (var input in item.References)
            {
                cancel(); if (layout.Values[input.Row * layout.Columns + input.Column]?.formula == null) continue; count++;
                if (!waiting.TryGetValue(input, out var consumers)) waiting.Add(input, consumers = []);
                consumers.Add(key);
            }
            counts.Add(key, count); if (count == 0) queue.Enqueue(key);
        }
        while (queue.TryDequeue(out var key))
        {
            cancel(); var (field, inputs) = formulas[key];
            if (affected.Contains(key) || inputs.Any(affected.Contains))
            {
                affected.Add(key); attempted = true;
                try
                {
                    var writable = (Field)transaction.GetObject(field.ObjectId, OpenMode.ForWrite);
                    writable.Evaluate((int)FieldEvaluationContext.Demand, table.Database); cancel();
                    var status = writable.EvaluationStatus;
                    if (status.Status != FieldEvaluationStatus.Success)
                    {
                        warnings.Add($"TABLE_FORMULA_EVALUATION_UNVERIFIED: {NativeTableFormula343.CellName(key)} native Field.Evaluate returned {status.Status} ({status.ErrorCode}).");
                        continue;
                    }
                }
                catch (Teigha.Runtime.Exception error) when (error.ErrorStatus == Teigha.Runtime.ErrorStatus.NotImplementedYet)
                {
                    warnings.Add($"TABLE_FORMULA_EVALUATION_UNVERIFIED: {NativeTableFormula343.CellName(key)} native Field.Evaluate returned {error.ErrorStatus}.");
                    continue;
                }
            }
            if (waiting.TryGetValue(key, out var dependents)) foreach (var dependent in dependents)
            { cancel(); if (--counts[dependent] == 0) queue.Enqueue(dependent); }
        }
        return attempted ? CadTableFormulaEditor343.Read(table, cancel) : layout;
    }
}
