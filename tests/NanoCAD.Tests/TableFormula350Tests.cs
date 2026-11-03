using NanoCAD.Contracts;

internal static class TableFormula350Tests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        void Need(bool value, string message) { if (!value) throw new Exception(message); }
        await test("block350 aggregate dependencies retain strict arithmetic operands", () =>
        {
            var syntax = NativeTableFormula350.Expand(
                NativeTableFormula350.Inspect("SUM(A1:B2)+B2+COUNT(A1,A1)"), 4, 4, () => { });
            Need(syntax.References.SequenceEqual(new[] { new TableFormulaReference343(0, 0),
                new TableFormulaReference343(0, 1), new TableFormulaReference343(1, 0),
                new TableFormulaReference343(1, 1) }), "Aggregate range lost or duplicated dependencies.");
            Need(syntax.AggregateOnlyReferences!.SequenceEqual(new[] { new TableFormulaReference343(0, 0),
                new TableFormulaReference343(0, 1), new TableFormulaReference343(1, 0) }),
                "B2 must remain a required numeric operand even when an aggregate also includes it.");
            return Task.CompletedTask;
        });
        await test("block350 formula fill preserves mixed anchors and numeric exponents", () =>
        {
            string source = "=SUM($A1:B$3)+$C$2*1.25e-2";
            Need(NativeTableFormula350.Shift(source, 2, 1) == "=SUM($A3:C$3)+$C$2*1.25e-2",
                "Relative fill changed a fixed coordinate, range endpoint or numeric literal.");
            Need(NativeTableFormula350.Shift("AVERAGE(Z1:AA2)+COUNT($B$2)", 1, 1)
                == "AVERAGE(AA2:AB3)+COUNT($B$2)", "Column rollover or native function changed.");
            try { NativeTableFormula350.Shift("A1+$B$2", -1, 0); throw new Exception("Negative relative row admitted."); }
            catch (DomainException e) when (e.Error.code == "UNSUPPORTED_TABLE_FORMULA") { }
            return Task.CompletedTask;
        });
        await test("block350 expanded aggregate range participates in cycle and boundary checks", () =>
        {
            var syntax = NativeTableFormula350.Expand(NativeTableFormula350.Inspect("SUM(A1:B2)"), 2, 2, () => { });
            var graph = syntax.References.ToDictionary(cell => cell, _ => Array.Empty<TableFormulaReference343>());
            graph[new(1, 1)] = syntax.References;
            try { NativeTableFormula343.RequireAcyclic(graph, new[] { new TableFormulaReference343(1, 1) });
                throw new Exception("Aggregate self-reference cycle admitted."); }
            catch (DomainException e) when (e.Error.code == "TABLE_FORMULA_CYCLE") { }
            try { NativeTableFormula350.Expand(NativeTableFormula350.Inspect("SUM(A1:C2)"), 2, 2, () => { });
                throw new Exception("Range outside the actual table admitted."); }
            catch (DomainException e) when (e.Error.code == "CELL_OUT_OF_RANGE") { }
            return Task.CompletedTask;
        });
    }
}
