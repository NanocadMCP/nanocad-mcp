using System.Security.Cryptography;
using System.Text.Json;
using NanoCAD.Contracts;
using static NanoCADBridge.CadTableEditingReader;

namespace NanoCADBridge;

// Native layout is observed separately from topology and stored content. This
// does not change resize_table's explicit requested-size verification.
internal static class CadTableNativeLayout356
{
    internal const string Warning="TABLE_NATIVE_LAYOUT_REFLOWED: Native scalar/style layout changed observed row heights or column widths. Read current geometry.row_heights/column_widths and inspect readability/spacing; topology, stored raw values/types/units/formulas, merge/protection, explicit appearance, identity/owner and requested style remain checked. Explicit resize requests retain their own size verification.";
    internal static object Describe()=>new{scope="native_scalar_formula_style_layout_observed_separately_from_stored_content",
        operations=new[]{"set_table_cell_value","set_table_cell_formula","set_table_style"},
        native_size_reflow="observed_with_warning; no promised_fixed_row_column_sizes_for_content_or_style_edit",
        size_observation="geometry.row_heights and geometry.column_widths",explicit_resize="requested_physical_sizes_verified_by_resize_table"};
    internal static bool VerifyTopology(Snapshot before,Snapshot after,Action cancel)
    {
        cancel();
        if(before.Rows!=after.Rows||before.Columns!=after.Columns||before.RowHeights.Length!=after.RowHeights.Length||before.ColumnWidths.Length!=after.ColumnWidths.Length)
            throw new DomainException("NATIVE_TABLE_MISMATCH","Native scalar/style editing changed row/column topology.",false,Diagnostics(before,after,cancel));
        return before.RowHeights.Zip(after.RowHeights).Any(pair=>!TableSizeVerification.Equal(pair.First,pair.Second))||
               before.ColumnWidths.Zip(after.ColumnWidths).Any(pair=>!TableSizeVerification.Equal(pair.First,pair.Second));
    }
    internal static object Diagnostics(Snapshot before,Snapshot after,Action cancel)
    {
        cancel();var full=new{scope="completed_managed_native_table_layout_snapshots_only",
            before=new{rows=before.Rows,columns=before.Columns,row_heights=before.RowHeights,column_widths=before.ColumnWidths},
            after=new{rows=after.Rows,columns=after.Columns,row_heights=after.RowHeights,column_widths=after.ColumnWidths}};
        var bytes=JsonSerializer.SerializeToUtf8Bytes(full,Wire.Json);cancel();
        if(bytes.Length<=8192)return full;
        string Hash(double[] values){cancel();var data=JsonSerializer.SerializeToUtf8Bytes(values,Wire.Json);cancel();return Convert.ToHexString(SHA256.HashData(data));}
        // Full-array fingerprints bound failure details by actual bytes; they
        // neither truncate CAD topology nor introduce an entity-count limit.
        return new{scope="completed_managed_layout_array_fingerprints; full_arrays_exceed_failure_detail_byte_budget",
            before=new{rows=before.Rows,columns=before.Columns,row_heights_sha256=Hash(before.RowHeights),column_widths_sha256=Hash(before.ColumnWidths)},
            after=new{rows=after.Rows,columns=after.Columns,row_heights_sha256=Hash(after.RowHeights),column_widths_sha256=Hash(after.ColumnWidths)}};
    }
    internal static DomainException WithDiagnostics(DomainException error,Snapshot before,Snapshot after,Action cancel)
        =>error.Error.details!=null?error:new DomainException(error.Error.code,error.Error.message,error.Error.retryable,Diagnostics(before,after,cancel));
}
