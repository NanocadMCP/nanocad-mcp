using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class TableFormula343Tests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        void Need(bool value, string message) { if (!value) throw new Exception(message); }
        void Reject(string formula)
        { try { NativeTableFormula343.Inspect(formula); } catch (DomainException e) when (e.Error.code == "UNSUPPORTED_TABLE_FORMULA") { return; } throw new Exception("Unsupported formula syntax admitted."); }
        await test("block343 native reference syntax normalizes formatting but preserves arithmetic association", () =>
        {
            Need(NativeTableFormula343.SameMeaning("=$B$2 * (b3)", "$B$2*B3"), "Native whitespace/case/parenthesis normalization rejected.");
            Need(!NativeTableFormula343.SameMeaning("$A$1", "A1") && !NativeTableFormula343.SameMeaning("$A1", "A$1"), "Persistent reference anchor semantics lost.");
            Need(!NativeTableFormula343.SameMeaning("A1-(B2-C3)", "A1-B2-C3") && !NativeTableFormula343.SameMeaning("A1+(B2+C3)", "A1+B2+C3"), "Arithmetic association changed.");
            var refs = NativeTableFormula343.Inspect("AA12+1.25e-2+B2+B2").References;
            Need(refs.SequenceEqual(new[] { new TableFormulaReference343(1, 1), new TableFormulaReference343(11, 26) }) && NativeTableFormula343.CellName(new(11, 26)) == "AA12", "Actual A1 indexing or dedup lost.");
            var aggregate = NativeTableFormula343.Inspect("SUM(A1:A3)");
            Need(aggregate.Ranges!.Single() == new TableFormulaRangeReference350(new(0, 0), new(2, 0)), "Supported same-table aggregate lost its native rectangle.");
            Reject("MEDIAN(A1:A3)"); Reject("Other!A1+2"); Reject("A0+1"); Reject("B2*"); Reject("(A1+2"); Reject("A1(2)"); Reject("NaN"); Reject("1e999");
            return Task.CompletedTask;
        });
        await test("block343 native dependency planning is iterative and cancellable with local cycle checks", () =>
        {
            var graph = Enumerable.Range(0, 2048).ToDictionary(row => new TableFormulaReference343(row, 0), row => row == 2047 ? Array.Empty<TableFormulaReference343>() : new[] { new TableFormulaReference343(row + 1, 0) });
            NativeTableFormula343.RequireAcyclic(graph, new[] { new TableFormulaReference343(0, 0) });
            graph[new(2047, 0)] = new[] { new TableFormulaReference343(1, 0) };
            try { NativeTableFormula343.RequireAcyclic(graph, new[] { new TableFormulaReference343(0, 0) }); throw new Exception("Native local cycle accepted."); } catch (DomainException e) when (e.Error.code == "TABLE_FORMULA_CYCLE") { }
            int visits = 0; try { NativeTableFormula343.RequireAcyclic(graph, new[] { new TableFormulaReference343(0, 0) }, () => { if (++visits == 17) throw new OperationCanceledException(); }); throw new Exception("Cancellation ignored."); } catch (OperationCanceledException) { }
            return Task.CompletedTask;
        });
        await test("block343 sole token-bound formula request preserves sparse native format and has no cell ceiling", () =>
        {
            string token = new('A', 64);
            var cells = Enumerable.Range(0, 130).Select(row => new { row, column = 0, formula = "B2*B3", unit_type = "Distance" }).ToArray();
            var edit = TableFormulaValidation343.Parse(Wire.Element(new { operation = TableFormulaValidation343.Operation, handles = new[] { "A1" }, table_token = token, cells }), "$", new[] { "A1" });
            Need(edit.Cells.Length == 130 && edit.Cells.All(cell => cell.DataFormat == null), "Count ceiling or omitted data format changed.");
            TableFormulaValidation343.Batch(new EditSpec[] { edit });
            try { TableFormulaValidation343.Batch(new EditSpec[] { edit, edit }); throw new Exception("Mixed fresh-token consumption accepted."); } catch (DomainException e) when (e.Error.code == "INVALID_OPERATION_ORDER") { }
            var schema = ToolCatalog.Tools.Single(tool => tool.Name == "nanocad.modify_entities").InputSchema.GetProperty("properties").GetProperty("operations").GetProperty("items").GetProperty("oneOf")
                .EnumerateArray().Single(branch => branch.GetProperty("properties").GetProperty("operation").GetProperty("const").GetString() == TableFormulaValidation343.Operation);
            Need(!schema.GetProperty("properties").GetProperty("cells").TryGetProperty("maxItems", out _) && schema.GetProperty("properties").GetProperty("table_token").GetProperty("pattern").GetString() == "^[0-9A-F]{64}$", "Actual publication differs from parser.");
            return Task.CompletedTask;
        });
    }
}
