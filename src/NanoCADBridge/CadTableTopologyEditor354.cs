using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using static NanoCADBridge.CadTableEditingReader;

namespace NanoCADBridge;

// Retained native cells and formula meaning, not a replacement Table or a
// spreadsheet evaluator. All source/request work completes before Insert/Delete.
internal static class CadTableTopologyEditor354
{
    sealed record Formula(CellValue Cell, ContentValue Value, int Row, int Column, string Planned, string Action);
    sealed record Plan(Snapshot Before, JsonElement Metadata, bool Rows, bool Insert, int Index, int Count, int FinalRows, int FinalColumns, Formula[] Formulas);
    const string ReferencePolicy = "retained_native_cell_identity; structural_A1_mapping_moves_relative_and_dollar_anchors; inserted_ranges_expand_and_partial_deleted_ranges_contract; removed_references_require_explicit_final_coordinate_replacements";
    const string Evaluation = "native_Table_only; caches_display_text_and_auto_scale_observed_after_native_layout; no_external_value_substitution_or_evaluation_claim";
    static bool Exact(object? a, object? b) => CadTableFormulaEditor343.Exact(a, b);
    static int? Map(int value, bool insert, int index, int count) => !insert && value >= index && (long)value < index + (long)count ? null :
        insert && value >= index ? checked(value + count) : !insert && (long)value >= index + (long)count ? value - count : value;
    static (int? Row, int? Column) MapCell(CellValue cell, Plan plan) => (plan.Rows ? Map(cell.row, plan.Insert, plan.Index, plan.Count) : cell.row,
        plan.Rows ? cell.column : Map(cell.column, plan.Insert, plan.Index, plan.Count));
    static TableCellRangeSpec? MapRange(TableCellRangeSpec? range, Plan plan)
    {
        if (range == null) return null;
        int? top = plan.Rows ? Map(range.TopRow, plan.Insert, plan.Index, plan.Count) : range.TopRow, bottom = plan.Rows ? Map(range.BottomRow, plan.Insert, plan.Index, plan.Count) : range.BottomRow;
        int? left = plan.Rows ? range.LeftColumn : Map(range.LeftColumn, plan.Insert, plan.Index, plan.Count), right = plan.Rows ? range.RightColumn : Map(range.RightColumn, plan.Insert, plan.Index, plan.Count);
        return top.HasValue && bottom.HasValue && left.HasValue && right.HasValue ? new(top.Value, left.Value, bottom.Value, right.Value) : null;
    }
    static Plan Build(Table table, TableEditingSpec edit, Transaction tx, Action cancel)
    {
        var before = CadTableLifecycleEditor.CaptureEdit(table, edit, cancel); CadTableToken349.Require(table, before, edit.TableToken, edit.Operation, cancel);
        var metadata = CadTableValueEditor336.Metadata(table, tx, cancel);
        bool rows = edit.Operation.EndsWith("_rows", StringComparison.Ordinal), insert = edit.Operation.StartsWith("insert_", StringComparison.Ordinal);
        int index = edit.Index!.Value, count = edit.Count!.Value;
        var plan = new Plan(before, metadata, rows, insert, index, count, before.Rows + (rows ? (insert ? count : -count) : 0), before.Columns + (!rows ? (insert ? count : -count) : 0), []);
        var replacements = (edit.FormulaReplacements ?? []).ToDictionary(item => (item.Row, item.Column)); var formulas = new List<Formula>(); var budget = new ModelInventoryBudget(cancel); budget.Add(new { before, metadata });
        var cells = new Dictionary<TableFormulaReference343, (CellValue? Cell, ContentValue? Value)>();
        foreach (var cell in before.Cells)
        {
            cancel(); var mapped = MapCell(cell, plan); if (!mapped.Row.HasValue || !mapped.Column.HasValue) continue;
            var value = before.Values[checked(cell.row * before.Columns + cell.column)]; cells.Add(new(mapped.Row.Value, mapped.Column.Value), (cell, value));
            if (value?.formula == null) continue;
            Need(value.data_type is "Long" or "Double", "Structural formula editing requires ordinary numeric formula storage; external/field/block content needs its owning adapter.", "UNSUPPORTED_TABLE_FORMULA");
            bool selected = replacements.Remove((cell.row, cell.column), out var replacement);
            if (selected) Need(Anchor(cell) && cell.content_editable && cell.format_editable, "An explicit retained formula replacement needs an editable visible anchor.");
            string wanted = selected ? replacement!.Formula : NativeTableFormula350.RemapStructural(value.formula, rows, insert, index, count, cancel);
            var item = new Formula(cell, value, mapped.Row.Value, mapped.Column.Value, wanted, selected ? "replaced" : wanted == value.formula ? "preserved" : "remapped"); budget.Add(item); formulas.Add(item);
        }
        Need(replacements.Count == 0, "formula_replacements must select existing retained formula anchors, not deleted, empty or literal cells.", "INVALID_ARGUMENT");
        if (insert)
            for (int row = 0; row < edit.Values!.Length; row++) for (int column = 0; column < edit.Values[row].Length; column++)
            {
                cancel(); var key = new TableFormulaReference343(rows ? index + row : row, rows ? column : index + column);
                cells.Add(key, (null, new ContentValue("String", "Unitless", Wire.Element(edit.Values[row][column]), null, 0, 0, 1, false)));
                budget.Add(key);
            }
        var planned = formulas.ToDictionary(item => new TableFormulaReference343(item.Row, item.Column));
        var graph = new Dictionary<TableFormulaReference343, TableFormulaReference343[]>(); var leaves = new Dictionary<TableFormulaReference343, ContentValue?>();
        var strict = new HashSet<TableFormulaReference343>(); var pending = new Stack<TableFormulaReference343>(planned.Keys);
        while (pending.Count > 0)
        {
            cancel(); var key = pending.Pop(); if (graph.ContainsKey(key)) continue;
            Need(cells.TryGetValue(key, out var actual), "A planned native formula reference is outside the final Table.", "CELL_OUT_OF_RANGE");
            Need(actual.Cell == null || Anchor(actual.Cell), "Formula references must address visible native merged anchors, not covered cells.", "INVALID_ARGUMENT");
            if (!planned.TryGetValue(key, out var formula)) { graph.Add(key, []); leaves.Add(key, actual.Value); budget.Add(key); continue; }
            var syntax = NativeTableFormula350.Expand(NativeTableFormula350.Inspect(formula.Planned, cancel), plan.FinalRows, plan.FinalColumns, cancel);
            var aggregates = (syntax.AggregateOnlyReferences ?? []).ToHashSet(); foreach (var reference in syntax.References) { cancel(); if (!aggregates.Contains(reference)) strict.Add(reference); pending.Push(reference); }
            graph.Add(key, syntax.References); budget.Add(new { key, syntax.References });
        }
        foreach (var (key, value) in leaves)
        {
            cancel(); if (strict.Contains(key)) Need(value != null && value.data_type is "Long" or "Double", "Arithmetic structural references need retained numeric native cells; aggregate-only references may include ordinary empty/String cells.", "UNSUPPORTED_TABLE_FORMULA");
            else Need(value == null || value.data_type is "Long" or "Double" or "String", "Native aggregate references need ordinary scalar cells.", "UNSUPPORTED_TABLE_FORMULA");
        }
        NativeTableFormula343.RequireAcyclic(graph, planned.Keys, cancel); plan = plan with { Formulas = formulas.ToArray() };
        // Reserve actual formula text and fixed native numeric observations before
        // writing; no cell/formula count ceiling or response truncation is used.
        var estimate = formulas.Select(item => new TableFormulaTopologyChange354(item.Cell.row, item.Cell.column, item.Row, item.Column, item.Action,
            item.Value.formula!, item.Planned, item.Planned, item.Value.data_type, item.Value.unit_type, Wire.Element(double.MinValue), new string('X', 192))).ToArray();
        Need(JsonSerializer.SerializeToUtf8Bytes(new { formulas = estimate, reference_policy = ReferencePolicy, evaluated_values = Evaluation }, Wire.Json).Length <= Wire.PayloadBudget - 32768,
            "The complete formula-remapping receipt exceeds actual transport bytes; no native structural write occurred.", "RESPONSE_LIMIT_EXCEEDED");
        return plan;
    }
    internal static void CheckSource(Table table, TableEditingSpec edit, Transaction tx, Action cancel) => Build(table, edit, tx, cancel);
    static bool Meaning(string wanted, string? actual, Action cancel)
    { if (actual == null) return false; try { return NativeTableFormula343.SameMeaning(wanted, actual, cancel); } catch (DomainException error) when (error.Error.code == "UNSUPPORTED_TABLE_FORMULA") { return false; } }
    static CellValue Cell(Snapshot snapshot, int row, int column) => snapshot.Cells[checked(row * snapshot.Columns + column)];
    static bool SetInsertedLiteral(Table table, int row, int column, CellValue sourceCell, string text, Database db, Transaction tx, Action cancel)
    {
        cancel(); string phase = "create_inserted_literal", fontSource = "indexed_content";
        ObjectId indexed = ObjectId.Null, cell = ObjectId.Null, inherited = ObjectId.Null; string? name = null; RowType role = default;
        string? Id(ObjectId id) => id.IsNull || !id.IsValid ? null : id.Handle.ToString();
        DomainException Failure(string reason) => new("TABLE_INSERTED_CELL_STYLE_UNAVAILABLE", reason, false,
            new
            {
                table_handle = table.Handle.ToString(),
                row,
                column,
                content_index = 0,
                phase,
                font_source = fontSource,
                table_style_handle = Id(table.TableStyle),
                assigned_cell_style = name,
                row_role = role.ToString(),
                indexed_style_handle = Id(indexed),
                cell_style_handle = Id(cell),
                inherited_style_handle = Id(inherited),
                source_row = sourceCell.row,
                source_column = sourceCell.column,
                source_text_style_handle = sourceCell.format?.text_style_handle
            });
        ObjectId LocalFont(ObjectId id)
        {
            if (id.IsNull || !id.IsValid || id.IsErased || id.IsEffectivelyErased || id.Database?.UnmanagedObject != db.UnmanagedObject ||
                tx.GetObject(id, OpenMode.ForRead) is not TextStyleTableRecord font || font.OwnerId != db.TextStyleTableId)
                throw Failure("The inserted cell's native text style is unavailable or outside its drawing's text-style table.");
            return id;
        }
        try
        {
            var destination = table.Cells[row, column]; int contents = destination.Contents.Count;
            var kind = contents == 1 ? table.GetContentTypes(row, column, 0) : default;
            bool ordinary = kind == CellContentTypes.Value || ((int)kind == 0 && table.IsEmpty(row, column));
            if (destination.IsMerged != false || table.IsLinked(row, column) || contents > 1 ||
                contents == 1 && (!ordinary || destination.Contents[0].HasFormula || !table.GetFieldId(row, column, 0).IsNull ||
                    !string.IsNullOrEmpty(table.GetFormula(row, column, 0))))
                throw new DomainException("NATIVE_TABLE_INSERT_INHERITED_CONTENT",
                    "Native format inheritance produced a merged, linked, multi-content, field, formula or non-scalar destination.", false,
                    new
                    {
                        table_handle = table.Handle.ToString(),
                        row,
                        column,
                        source_row = sourceCell.row,
                        source_column = sourceCell.column,
                        content_count = contents,
                        content_type = contents == 1 ? kind.ToString() : null,
                        merged = destination.IsMerged,
                        linked = table.IsLinked(row, column),
                        phase = "before_inserted_literal"
                    });
            Need(table.IsContentEditable(row, column) && table.IsFormatEditable(row, column), "Inserted native cells have protected content or formatting.");
            table.SetDataType(row, column, DataType.String, UnitType.Unitless); table.SetTextString(row, column, text); cancel();
            if (table.Cells[row, column].Contents.Count == 0)
            {
                Need(table.CreateContent(row, column, 0) == 0, "Native Table did not create the inserted literal's first content.", "NATIVE_TABLE_MISMATCH");
                table.SetValue(row, column, 0, text); table.SetDataType(row, column, 0, new DataTypeParameter(DataType.String, UnitType.Unitless)); cancel();
            }
            Need(table.Cells[row, column].Contents.Count == 1, "A newly inserted literal needs exactly one native content.", "NATIVE_TABLE_MISMATCH");
            phase = "read_inserted_content_font"; indexed = table.GetTextStyleId(row, column, 0);
            if (!indexed.IsNull) { FontCoverage.Require(tx, LocalFont(indexed), text); return false; }
            phase = "resolve_inserted_effective_font"; name = table.GetCellStyle(row, column); role = table.RowType(row);
            cell = table.TextStyle(row, column);
            if (!cell.IsNull) { fontSource = "effective_cell"; inherited = LocalFont(cell); }
            else
            {
                // GetTextStyleId and the new cell's own TextStyle can both
                // be null even when the selected source has an ordinary font.
                // Use the same native formatting source as Insert*AndInherit.
                fontSource = "original_inheritance_source_cell";
                if (sourceCell.format?.text_style_handle is not { } sourceFont)
                    throw Failure("The source cell of the native format inheritance has no observed text style.");
                try { inherited = LocalFont(CadStructureWriter.Find(db, sourceFont)); }
                catch (DomainException error) when (error.Error.code is "OBJECT_NOT_FOUND" or "OBJECT_ERASED")
                { throw Failure("The observed font of the original source cell no longer resolves in its drawing: " + error.Error.code + "."); }
            }
            phase = "store_inserted_content_font"; FontCoverage.Require(tx, inherited, text);
            table.SetTextStyleId(row, column, 0, inherited); cancel(); indexed = table.GetTextStyleId(row, column, 0);
            if (indexed != inherited) throw Failure("The inserted content did not retain its resolved native text style.");
            return true;
        }
        catch (Teigha.Runtime.Exception error) { throw Failure("Native Table inserted-cell style operation returned " + error.ErrorStatus + "."); }
    }
    internal static TableTopologyChange354 Apply(Table table, TableEditingSpec edit, Database db, Transaction tx, Action cancel, int operationIndex, EntityRef source, List<string>? warnings)
    {
        CadTableLifecycleEditor.CheckSource(table, edit, db, tx, cancel); var plan = Build(table, edit, tx, cancel); var before = plan.Before;
        var sourceExpressions = CadTableFormulaStructuralBindings.LocalExpressions(table, tx, before, cancel);
        int inheritFrom = Math.Min(plan.Index, (plan.Rows ? before.Rows : before.Columns) - 1);
        cancel(); if (plan.Insert)
        {
            if (plan.Rows) table.InsertRowsAndInherit(plan.Index, inheritFrom, plan.Count);
            else table.InsertColumnsAndInherit(plan.Index, inheritFrom, plan.Count);
            for (int offset = 0; offset < plan.Count; offset++)
            {
                cancel(); int index = plan.Index + offset;
                if (plan.Rows) table.SetRowHeight(index, edit.Size!.Value); else table.SetColumnWidth(index, edit.Size!.Value);
            }
        }
        else { if (plan.Rows) table.DeleteRows(plan.Index, plan.Count); else table.DeleteColumns(plan.Index, plan.Count); }
        cancel();
        int materializedFonts = 0;
        if (plan.Insert)
            for (int row = 0; row < edit.Values!.Length; row++) for (int column = 0; column < edit.Values[row].Length; column++)
            {
                cancel(); int r = plan.Rows ? plan.Index + row : row, c = plan.Rows ? column : plan.Index + column;
                var sample = Cell(before, plan.Rows ? inheritFrom : r, plan.Rows ? c : inheritFrom);
                if (SetInsertedLiteral(table, r, c, sample, edit.Values[row][column], db, tx, cancel)) materializedFonts++;
            }
        if (plan.Insert) warnings?.Add($"TABLE_INSERT_FORMAT_INHERITANCE: Inserted {(plan.Rows ? "rows" : "columns")} used the pre-edit {(plan.Rows ? "row" : "column")} at index {inheritFrom} as their native formatting source. The requested physical size was applied separately. {materializedFonts} new contents required an explicit verified font from the cell or its original formatting source.");
        foreach (var item in plan.Formulas)
        {
            cancel(); string actual = table.GetFormula(item.Row, item.Column, 0); if (Meaning(item.Planned, actual, cancel)) continue;
            Need(table.IsContentEditable(item.Row, item.Column) && table.IsFormatEditable(item.Row, item.Column), "The native structural change did not remap a protected formula; its intended stored references remain unverified.", "NATIVE_TABLE_MISMATCH");
            table.SetFormula(item.Row, item.Column, 0, NativeTableFormula343.NativeExpression(item.Planned, cancel));
        }
        cancel(); table.GenerateLayout(); cancel(); table.RecomputeTableBlock(true); cancel();
        var after = CadTableLifecycleEditor.CaptureEdit(table, edit, cancel);
        if (after.Rows != plan.FinalRows || after.Columns != plan.FinalColumns) throw new DomainException("NATIVE_TABLE_MISMATCH", "Native structural table dimensions differ from the request.", false,
            new { expected_rows = plan.FinalRows, expected_columns = plan.FinalColumns, actual_rows = after.Rows, actual_columns = after.Columns, plan.Index, plan.Count });
        var reevaluate = plan.Formulas.Where(item => item.Action != "preserved" || item.Row != item.Cell.row || item.Column != item.Cell.column)
            .Select(item => (item.Row, item.Column)).ToArray();
        if (reevaluate.Length != 0) after = CadTableFormulaEvaluation.AfterWrite(table, tx, after, reevaluate, warnings ?? [], cancel);
        var oldFormulaCells = before.Cells.Where(cell => before.Values[checked(cell.row * before.Columns + cell.column)]?.formula != null).Select(cell => (cell.row, cell.column)).ToArray();
        var newFormulaCells = plan.Formulas.Select(item => (item.Row, item.Column)).ToArray();
        var targetExpressions = CadTableFormulaStructuralBindings.LocalExpressions(table, tx, after, cancel);
        var fieldPlan = plan.Formulas.Select(item =>
        {
            var value = after.Values[checked(item.Row * after.Columns + item.Column)]; string? from = item.Value.native_formula_field?.handle, to = value?.native_formula_field?.handle;
            return new CadTableFormulaStructuralBindings.Slot(item.Cell.row, item.Cell.column, item.Row, item.Column, from, to, item.Value.formula!,
                item.Planned, value?.formula, from != null && to != null && sourceExpressions.Contains(from) && targetExpressions.Contains(to));
        }).ToArray();
        CadTableFormulaField358.Verify(plan.Metadata, CadTableValueEditor336.Metadata(table, tx, cancel), before, after, oldFormulaCells, newFormulaCells, cancel,
            warnings ?? [], fieldPlan, handle => CadTableFormulaStructuralBindings.ReactorTarget(db, tx, handle, cancel));
        if (before.Values.Any(value => value?.native_formula_field != null) || after.Values.Any(value => value?.native_formula_field != null)) warnings?.Add(CadTableFormulaField358.Warning);
        if (before.Identity != after.Identity || !Exact(before.Common, after.Common)) throw CadTableFormulaStructuralBindings.Mismatch(
            "Native structural editing changed retained Table identity, owner, placement, style or defaults.", new { before.Identity, before.Common }, new { after.Identity, after.Common });
        double[] Sizes(double[] old, bool edited)
        { if (!edited) return old; var values = old.ToList(); if (plan.Insert) values.InsertRange(plan.Index, Enumerable.Repeat(edit.Size!.Value, plan.Count)); else values.RemoveRange(plan.Index, plan.Count); return values.ToArray(); }
        bool Near(double[] wanted, double[] actual) => wanted.Length == actual.Length && wanted.Zip(actual).All(pair => TableSizeVerification.Equal(pair.First, pair.Second));
        var requestedHeights = Sizes(before.RowHeights, plan.Rows); var requestedWidths = Sizes(before.ColumnWidths, !plan.Rows);
        if (!Near(requestedHeights, after.RowHeights) || !Near(requestedWidths, after.ColumnWidths)) throw CadTableFormulaStructuralBindings.Mismatch(
            "Native structural row/column sizes were clamped or another stored size changed.",
            new { row_heights = requestedHeights, column_widths = requestedWidths }, new { row_heights = after.RowHeights, column_widths = after.ColumnWidths });
        var reports = new List<TableFormulaTopologyChange354>(); var expectedFormulas = plan.Formulas.ToDictionary(item => (item.Cell.row, item.Cell.column));
        foreach (var old in before.Cells)
        {
            cancel(); var mapped = MapCell(old, plan); if (!mapped.Row.HasValue || !mapped.Column.HasValue) continue;
            int r = mapped.Row.Value, c = mapped.Column.Value, oldIndex = checked(old.row * before.Columns + old.column), currentIndex = checked(r * after.Columns + c);
            var current = Cell(after, r, c); var oldValue = before.Values[oldIndex]; var actual = after.Values[currentIndex];
            var expected = old with { row = r, column = c, merged_range = MapRange(old.merged_range, plan), text = oldValue?.formula != null ? current.text : old.text };
            if (!Exact(expected, current) || !Exact(before.CellOverrides[oldIndex], after.CellOverrides[currentIndex])) throw CadTableFormulaStructuralBindings.Mismatch(
                "Native structural editing changed a retained cell's literal text, merge/protection or stored appearance/override flags.",
                new { cell = expected, overrides = before.CellOverrides[oldIndex] }, new { cell = current, overrides = after.CellOverrides[currentIndex] }, old.row, old.column, r, c);
            if (oldValue?.formula == null)
            { Need(Exact(oldValue, actual), "Native structural editing changed an independent retained raw scalar/type/unit/content-layout flag.", "NATIVE_TABLE_MISMATCH"); continue; }
            var item = expectedFormulas[(old.row, old.column)]; var actualValue = actual ?? throw new DomainException("NATIVE_TABLE_MISMATCH", "A retained native formula lost its content.");
            Need(actualValue.formula != null && Meaning(item.Planned, actualValue.formula, cancel), "Native formula meaning differs from its structural plan or explicit replacement.", "NATIVE_TABLE_MISMATCH");
            var expectedValue = oldValue with
            {
                formula = actualValue.formula,
                native_value = actualValue.native_value,
                native_value_source = actualValue.native_value_source,
                double_bits = actualValue.double_bits,
                evaluation_unavailable_reason = actualValue.evaluation_unavailable_reason,
                scale = oldValue.auto_scale && actualValue.auto_scale ? actualValue.scale : oldValue.scale,
                native_content_type = actualValue.native_content_type,
                native_formula_field = actualValue.native_formula_field
            };
            if (!Exact(expectedValue, actualValue)) throw CadTableFormulaStructuralBindings.Mismatch(
                "Native structural editing changed an unrequested formula datatype/unit/rotation/override/layout flag.", expectedValue, actualValue, old.row, old.column, r, c);
            reports.Add(new(old.row, old.column, r, c, item.Action, oldValue.formula, item.Planned, actualValue.formula!, actualValue.data_type, actualValue.unit_type, actualValue.native_value, actualValue.evaluation_unavailable_reason));
            if (item.Planned != actualValue.formula) warnings?.Add("TABLE_FORMULA_NORMALIZED: Native structural formula text normalized while arithmetic/reference and dollar-anchor meaning remained checked.");
            if (actualValue.evaluation_unavailable_reason != null) warnings?.Add("TABLE_FORMULA_EVALUATION_UNVERIFIED: A retained structural formula has no observed finite native value. Read its actual expression/cache and inspect the Table; no external value was substituted.");
        }
        if (plan.Insert)
            for (int row = 0; row < edit.Values!.Length; row++) for (int column = 0; column < edit.Values[row].Length; column++)
            {
                cancel(); int r = plan.Rows ? plan.Index + row : row, c = plan.Rows ? column : plan.Index + column; var cell = Cell(after, r, c); var value = after.Values[checked(r * after.Columns + c)];
                Need(cell.content_count == 1 && cell.merged_range == null && cell.text == edit.Values[row][column] && value is { data_type: "String", unit_type: "Unitless", formula: null } && value.native_value.GetString() == edit.Values[row][column],
                    "Inserted native cells differ from their explicit literal matrix/type or unexpectedly acquired a merge/formula.", "NATIVE_TABLE_MISMATCH");
            }
        warnings?.Add("NATIVE_TABLE_TOPOLOGY_CHANGED: Existing numeric inputs, explicit formats/layout/grids and stored formula meaning were retained through native row/column edits. Inspect actual formula caches and the readable engineering schedule. Legacy row-role datatype/unit defaults are unavailable; actual per-cell values/types/units stay observed. Recovery/Undo are not guaranteed.");
        var result = new TableTopologyChange354(operationIndex, source, edit.Operation, plan.Index, plan.Count, before.Rows, before.Columns, after.Rows, after.Columns, after.Token, reports.ToArray(), ReferencePolicy, Evaluation);
        Need(JsonSerializer.SerializeToUtf8Bytes(result, Wire.Json).Length <= Wire.PayloadBudget - 16384, "Actual native formula-remapping receipt exceeds transport bytes; retain the truthful owning write outcome.", "RESPONSE_LIMIT_EXCEEDED");
        return result;
    }
}
