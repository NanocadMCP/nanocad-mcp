using System.Text.Json;
using System.Text.Json.Serialization;

namespace NanoCAD.Contracts;

public sealed record TableFormulaReplacement354(int Row,int Column,string Formula);
public sealed record TableFormulaTopologyChange354(int before_row,int before_column,int row,int column,
    string action,string before_formula,string planned_formula,string formula,string data_type,string unit_type,
    JsonElement native_value,string? evaluation_unavailable_reason);
public sealed record TableTopologyChange354(int operation_index,EntityRef source,string operation,int index,int count,
    int before_rows,int before_columns,int rows,int columns,string table_token,TableFormulaTopologyChange354[] formulas,
    string reference_policy,string evaluated_values);
public sealed record TableTopologyWorkflow354(string[] operations,string structural_references,string inserted_ranges,
    string removed_references,string formula_replacements,string inserted_values,string row_default_type_observation);

public static class TableTopologyValidation354
{
    public static bool Handles(string operation)=>operation is "insert_table_rows" or "insert_table_columns" or "delete_table_rows" or "delete_table_columns";
    public static TableTopologyWorkflow354 Describe()=>new(["insert_table_rows","insert_table_columns","delete_table_rows","delete_table_columns"],
        "relative_and_dollar_anchored_A1_references_follow_retained_native_cells; dollar_flags_retained; this_is_not_fill_shifting",
        "insertion_at_or_before_first_shifts_range; strictly_after_first_and_at_or_before_last_expands_range",
        "partial_aggregate_ranges_contract; removed_single_reference_or_entire_range_requires_explicit_replacement_before_write",
        "formula_replacements_select_before_operation_retained_formula_anchors; expressions_use_final_table_coordinates; types_units_and_unrequested_formats_retained",
        "values_is_the_explicit_plain_String_Unitless_matrix_for_new_cells; existing_numeric_and_formula_cells_retained; dedicated_scalar_and_formula_edits_can_fill_new_cells",
        "legacy_Table_RowType_datatype_unit_getters_unavailable; actual_per_cell_types_units_values_formulas_and_other_native_defaults_remain_observed");
    public static TableFormulaReplacement354[] Parse(JsonElement value,string path)
    {
        DomainException Invalid(string message)=>new("INVALID_ARGUMENT",message,false,new{path});
        if(value.ValueKind!=JsonValueKind.Array||value.GetArrayLength()==0)throw Invalid("Select at least one retained before-operation formula anchor.");
        var result=new List<TableFormulaReplacement354>();var seen=new HashSet<(int,int)>();
        foreach(var item in value.EnumerateArray())
        {
            if(item.ValueKind!=JsonValueKind.Object)throw Invalid("Expected formula replacement object.");
            var names=new HashSet<string>(StringComparer.Ordinal);
            foreach(var field in item.EnumerateObject())if(!new[]{"row","column","formula"}.Contains(field.Name)||!names.Add(field.Name))throw Invalid("Unknown or duplicate formula replacement field.");
            int Index(string key)=>item.TryGetProperty(key,out var field)&&field.ValueKind==JsonValueKind.Number&&field.TryGetInt32(out int index)&&index>=0?index:throw Invalid("Use nonnegative before-operation cell indices.");
            int row=Index("row"),column=Index("column");
            if(!seen.Add((row,column)))throw Invalid("Formula replacement anchors must be distinct.");
            if(!item.TryGetProperty("formula",out var formula)||formula.ValueKind!=JsonValueKind.String)throw Invalid("A final-coordinate native formula is required.");
            string expression=formula.GetString()!;NativeTableFormula350.Inspect(expression);
            result.Add(new(row,column,expression));
        }
        return result.ToArray();
    }
}
