using System.Diagnostics;
using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CadTableTextEditor
{
    private static void Need(bool condition,string message,string code="UNSUPPORTED_TABLE_CONTENT")
    {if(!condition)throw new DomainException(code,message);}
    internal static JsonElement Capture(Table table,Action cancel)
    {
        void Budget()=>cancel();
        double Number(double n){Need(double.IsFinite(n)&&Math.Abs(n)<=1e9,"Non-finite or excessive table geometry.","INVALID_GEOMETRY");return n;}
        double[] Point(Point3d p)=>new[]{Number(p.X),Number(p.Y),Number(p.Z)};
        double[] Vector(Vector3d p)=>new[]{Number(p.X),Number(p.Y),Number(p.Z)};
        string Text(string? text){Need(text!=null&&text.Length<=4096,"Unavailable or excessive cell text/format.");return text!;}
        int rows=table.Rows.Count,columns=table.Columns.Count;
        Need(rows>=1 && columns>=1,"Native table requires nonempty row/column topology.","INVALID_GEOMETRY");
        Need(table.GetType()==typeof(Table)&&Math.Abs(table.Position.Z)<=1e-9&&table.Normal.IsEqualTo(Vector3d.ZAxis)&&Math.Abs(table.Direction.Z)<=1e-9,"Expected native Table in WCS XY at Z=0.","OUT_OF_SCOPE");
        using(var links=table.GetDataLink())Need(links.Count==0,"Data-linked tables are not editable.");
        var cells=new List<object>();var capacity=new SerializedCapacity357(cancel:cancel);
        for(int row=0;row<rows;row++)for(int col=0;col<columns;col++)
        {
            Budget();var cell=table.Cells[row,col];
            Need(cell.IsMerged==false&&!table.IsLinked(row,col)&&table.IsContentEditable(row,col),"Merged, linked, locked or unavailable cell.");
            Need(cell.Contents.Count==1&&table.GetContentTypes(row,col,0)==CellContentTypes.Value,"Every cell must contain one plain text value.");
            Need(table.GetFieldId(row,col,0).IsNull&&string.IsNullOrEmpty(table.GetFormula(row,col,0)),"Fields and formulas are not editable.");
            var dataType=table.GetDataType(row,col,0);
            Need(dataType.DataType==DataType.String&&dataType.UnitType==UnitType.Unitless,"Numeric, unit-bearing or unknown cell type is not editable.");
            string text=Text(table.GetTextString(row,col,0));
            Need(!text.Any(char.IsControl)&&text.IndexOfAny(new[]{'\\','{','}'})<0&&!text.Contains("%<")&&!text.Contains("%%"),"Formatted/field table text is not editable.");
            var value=new{row,column=col,text,cell_style=Text(table.GetCellStyle(row,col)),data_format=Text(table.GetDataFormat(row,col,0)),alignment=table.Alignment(row,col).ToString(),text_height=Number(table.GetTextHeight(row,col,0)),text_style=table.GetTextStyleId(row,col,0).Handle.ToString(),state=(int)table.GetCellState(row,col)};capacity.Add(value);cells.Add(value);
        }
        Budget();
        var result=Wire.Element(new{rows,columns,table_style=table.TableStyle.Handle.ToString(),position=Point(table.Position),direction=Vector(table.Direction),normal=Vector(table.Normal),width=Number(table.Width),height=Number(table.Height),row_heights=Enumerable.Range(0,rows).Select(i=>{Budget();return Number(table.RowHeight(i));}).ToArray(),column_widths=Enumerable.Range(0,columns).Select(i=>{Budget();return Number(table.ColumnWidth(i));}).ToArray(),cells});
        SerializedCapacity357.Check(result,cancel:cancel);return result;
    }
    internal static void Apply(Table table,ReplaceTableTextSpec edit,Action cancel,bool regenerate,List<string>? nativeFormatWarnings=null)
    {
        var before=Capture(table,cancel);int rows=before.GetProperty("rows").GetInt32(),columns=before.GetProperty("columns").GetInt32();
        foreach(var cell in edit.Cells)
        {
            cancel();Need(cell.Row<rows&&cell.Column<columns,"Cell index exceeds current table dimensions.","CELL_OUT_OF_RANGE");
            Need(before.GetProperty("cells")[cell.Row*columns+cell.Column].GetProperty("text").GetString()==cell.ExpectedText,"Cell text differs from expected_text.","TEXT_CONTENT_CHANGED");
        }
        foreach(var cell in edit.Cells){cancel();table.SetTextString(cell.Row,cell.Column,0,cell.Text);}
        if(regenerate){table.GenerateLayout();table.RecomputeTableBlock(true);}
        var after=Capture(table,cancel);TableTextVerification.Verify(before,after,edit.Cells);
        if(regenerate&&nativeFormatWarnings!=null&&TableTextVerification.Reflowed(before,after))
        {
            cancel();var rowsChanged=Enumerable.Range(0,rows).Where(row=>!TableSizeVerification.Equal(before.GetProperty("row_heights")[row].GetDouble(),after.GetProperty("row_heights")[row].GetDouble())).ToArray();
            var observed=new{width_before=before.GetProperty("width").GetDouble(),width_after=after.GetProperty("width").GetDouble(),
                height_before=before.GetProperty("height").GetDouble(),height_after=after.GetProperty("height").GetDouble(),changed_rows=rowsChanged};
            nativeFormatWarnings.Add("TABLE_TEXT_REFLOW: Native text layout changed table/row sizes: "+JsonSerializer.Serialize(observed,Wire.Json)+". Column widths, text formatting and placement are retained; review the rendered table.");
        }
    }
}
