using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.Contracts;

public sealed record TableSizeEdit(int Index,double ExpectedSize,double Size);
public sealed record ResizeTableSpec(string[] Targets,TableSizeEdit[] Rows,TableSizeEdit[] Columns):EditSpec("resize_table",Targets);

public static partial class ModifyValidation
{
    private static ResizeTableSpec ParseTableSize(JsonElement item,string path,string[] handles)
    {
        Shape(item,path,"operation","handles","rows","columns");
        if(handles.Length!=1)throw Invalid(path+".handles","Exactly one native Table handle required.");
        TableSizeEdit[] Sizes(string field)
        {
            var values=Required(item,field,path);string label=path+"."+field;
            if(values.ValueKind!=JsonValueKind.Array)throw Invalid(label,"Expected a row/column size array.");
            var result=new List<TableSizeEdit>();var seen=new HashSet<int>();
            foreach(var value in values.EnumerateArray())
            {
                string p=label+"["+result.Count+"]";Shape(value,p,"index","expected_size","size");
                var index=Required(value,"index",p);
                if(index.ValueKind!=JsonValueKind.Number || !index.TryGetInt32(out int n) || n<0 || !seen.Add(n))throw Invalid(p+".index","Expected unique zero-based index within supported bounds.");
                result.Add(new(n,Number(Required(value,"expected_size",p),p+".expected_size",0.001,1e6),Number(Required(value,"size",p),p+".size",0.001,1e6)));
            }
            return result.ToArray();
        }
        var rows=Sizes("rows");var columns=Sizes("columns");
        if(rows.Length+columns.Length==0)throw Invalid(path,"At least one row height or column width required.");
        return new(handles,rows,columns);
    }
}

public static class TableSizeVerification
{
    public static bool Equal(double a,double b)=>double.IsFinite(a)&&double.IsFinite(b)&&Math.Abs(a-b)<=Math.Max(1e-7,1e-8*Math.Max(Math.Abs(a),Math.Abs(b)));
    public static JsonElement Expected(JsonElement before,ResizeTableSpec edit)
    {
        var expected=JsonNode.Parse(before.GetRawText())!;
        void Apply(string field,TableSizeEdit[] sizes,string total)
        {
            var values=expected[field]!.AsArray();
            foreach(var size in sizes)
            {
                if(size.Index>=values.Count)throw new DomainException("CELL_OUT_OF_RANGE","Row/column index exceeds current table dimensions.");
                if(!Equal(values[size.Index]!.GetValue<double>(),size.ExpectedSize))throw new DomainException("TABLE_SIZE_CHANGED","Row height or column width differs from expected_size after preceding edits.");
                values[size.Index]=size.Size;
            }
            expected[total]=values.Sum(v=>v!.GetValue<double>());
        }
        Apply("row_heights",edit.Rows,"height");Apply("column_widths",edit.Columns,"width");
        return Wire.Element(expected);
    }
    public static void Verify(JsonElement expected,JsonElement actual)
    {
        if(!TableTextVerification.Equivalent(expected,actual))throw new DomainException("NATIVE_TABLE_MISMATCH","Native table sizes were clamped or observed content, formatting or placement changed.");
    }
}
