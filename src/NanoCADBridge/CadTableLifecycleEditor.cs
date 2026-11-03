using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using static NanoCADBridge.CadTableEditingReader;

namespace NanoCADBridge;

// Native Table lifecycle, never replacement line/text primitives.
internal static class CadTableLifecycleEditor
{
    internal static bool Handles(EditSpec edit)=>edit is TableEditingSpec;
    internal static void CheckSource(Table table,TableEditingSpec edit,Database db,Transaction tr,Action cancel)
    {
        var before=CaptureEdit(table,edit,cancel);RequireToken(table,before,edit,cancel);Validate(table,before,edit,db,tr,cancel);
        if(TableTopologyValidation354.Handles(edit.Operation))CadTableTopologyEditor354.CheckSource(table,edit,tr,cancel);
    }
    internal static Snapshot CaptureEdit(Table table,TableEditingSpec edit,Action cancel)
    {
        bool rich=edit.Operation=="set_table_cell_format"||TableTopologyValidation354.Handles(edit.Operation);
        return Capture(table,cancel,requireWorldXy:edit.Operation!="set_table_cell_format",allowFormattedText:rich,allowNumeric:rich,allowFormulas:rich);
    }
    private static void RequireToken(Table table,Snapshot before,TableEditingSpec edit,Action cancel)
    {
        CadTableToken349.Require(table,before,edit.TableToken,edit.Operation,cancel);
    }
    private static CellValue Cell(Snapshot before,int row,int column)
    {
        Need(row>=0&&column>=0&&row<before.Rows&&column<before.Columns,"Cell is outside current table dimensions.","CELL_OUT_OF_RANGE");
        return before.Cells[checked(row*before.Columns+column)];
    }
    private static bool Same(object? first,object? second)=>ModelParameterSnapshot.Equivalent(JsonSerializer.SerializeToElement(first,Wire.Json),JsonSerializer.SerializeToElement(second,Wire.Json));
    private static ObjectId ResolveStyle(string handle,Database db,Transaction tr)
    {
        var id=CadStructureWriter.Find(db,handle);
        Need(tr.GetObject(id,OpenMode.ForRead) is TextStyleTableRecord style&&style.GetType()==typeof(TextStyleTableRecord)&&style.OwnerId==db.TextStyleTableId&&!style.IsDependent&&!style.IsShapeFile&&!style.IsVertical,
            "Cell text style must be an existing ordinary local native text style.","STYLE_UNAVAILABLE");return id;
    }
    private static void Validate(Table table,Snapshot before,TableEditingSpec edit,Database db,Transaction tr,Action cancel,List<string>? nativeFormatWarnings=null)
    {
        using(var reactors=table.GetPersistentReactorIds())Need(reactors.Count==0,"Native table lifecycle cannot modify unclassified persistent dependencies.");
        bool insert=edit.Operation.StartsWith("insert_",StringComparison.Ordinal),delete=edit.Operation.StartsWith("delete_",StringComparison.Ordinal);
        bool rows=edit.Operation.EndsWith("_rows",StringComparison.Ordinal);
        if(insert||delete)
        {
            int index=edit.Index!.Value,count=edit.Count!.Value,total=rows?before.Rows:before.Columns;
            Need(index<=total&&(insert||index<total&&(long)index+count<=total),"Row/column range is outside current table dimensions.","CELL_OUT_OF_RANGE");
            Need(!delete||count<total,"Leave at least one row/column; erase the Table itself to remove it completely.","INVALID_ARGUMENT");
            Need(!insert||(long)total+count<=int.MaxValue,"Native row/column count exceeds the SDK integer representation.","INVALID_ARGUMENT");
            if(insert)
            {
                Need(rows?edit.Values!.Length==count&&edit.Values.All(r=>r.Length==before.Columns):edit.Values!.Length==before.Rows&&edit.Values.All(r=>r.Length==count),
                    "Inserted literal values must cover the actual existing table's opposite axis.","INVALID_ARGUMENT");
            }
            // Native range shifts are unambiguous when insertion does not cut a
            // merged span and deletion removes either none or its whole axis span.
            foreach(var range in before.Cells.Select(c=>c.merged_range).OfType<TableCellRangeSpec>().Distinct())
            {
                int first=rows?range.TopRow:range.LeftColumn,last=rows?range.BottomRow:range.RightColumn;
                bool crosses=insert?index>first&&index<=last:index<=last&&(long)index+count-1>=first&&!(index<=first&&(long)index+count-1>=last);
                Need(!crosses,"The selected row/column change cuts an existing merged range; unmerge that exact range first.","INVALID_ARGUMENT");
            }
            // Insertion may shift protected existing cells while retaining their
            // stored content/format. Only explicitly deleted cells need deletion permission.
            foreach(var cell in before.Cells.Where(c=>delete&&Anchor(c)&&(rows?c.row:c.column)>=index&&(long)(rows?c.row:c.column)<index+(long)count))
            {cancel();Need(cell.content_editable&&cell.format_editable,"A selected native cell has protected content or formatting.");}
        }
        else if(edit.Range is {} requested)
        {
            Cell(before,requested.TopRow,requested.LeftColumn);Cell(before,requested.BottomRow,requested.RightColumn);
            foreach(var cell in before.Cells.Where(c=>Contains(requested,c.row,c.column)))
            {
                cancel();
                if(edit.Operation=="merge_table_cells")
                {
                    Need(cell.merged_range==null,"Merge selects an unmerged rectangle; unmerge an existing range explicitly first.","INVALID_ARGUMENT");
                    Need(cell.content_editable&&cell.format_editable,"Selected cell has protected content or formatting.");
                    Need(cell.row==requested.TopRow&&cell.column==requested.LeftColumn||cell.text.Length==0,"Merge would hide non-anchor text; move/delete that text explicitly before merging.","TABLE_CONTENT_WOULD_BE_LOST");
                }
                else
                {
                    Need(cell.merged_range==requested,"Unmerge must name exactly one complete native merged range.","INVALID_ARGUMENT");
                    if(Anchor(cell))Need(cell.content_editable&&cell.format_editable,"Merged anchor has protected content or formatting.");
                    Need(Anchor(cell)||cell.text.Length==0,"Hidden secondary merged content needs an explicit content adapter before unmerge.");
                }
            }
        }
        else if(edit.TextCells!=null)
        {
            foreach(var editCell in edit.TextCells)
            {
                cancel();var cell=Cell(before,editCell.Row,editCell.Column);
                Need(Anchor(cell),"Edit a merged cell's text by its top-left native anchor only.","INVALID_ARGUMENT");
                Need(cell.content_editable,"Selected native cell has protected content.");
                if(cell.content_count==1)FontCoverage.Require(tr,table.GetTextStyleId(editCell.Row,editCell.Column,0),editCell.Text);
            }
        }
        else
        {
            foreach(var editCell in edit.Cells!)
            {
                cancel();var cell=Cell(before,editCell.Row,editCell.Column);
                Need(Anchor(cell),"Format a merged cell by its top-left native anchor only.","INVALID_ARGUMENT");
                Need(cell.format_editable,"Selected native cell has protected formatting.");
                if(editCell.Properties.TextStyleHandle is {} handle)
                {
                    var coverage=CadTableValueEditor336.CheckFontCoverage(table,cell,ResolveStyle(handle,db,tr),tr,cancel);
                    nativeFormatWarnings?.AddRange(coverage);
                }
                CadTableAppearanceEditor.Validate(editCell.Properties,db,tr,cancel);
            }
        }
    }
    internal static void Apply(Table table,TableEditingSpec edit,Database db,Transaction tr,Action cancel,bool regenerate,bool checkToken=true,List<string>? nativeFormatWarnings=null)
    {
        var before=CaptureEdit(table,edit,cancel);if(checkToken)RequireToken(table,before,edit,cancel);Validate(table,before,edit,db,tr,cancel,nativeFormatWarnings);
        bool insert=edit.Operation.StartsWith("insert_",StringComparison.Ordinal),delete=edit.Operation.StartsWith("delete_",StringComparison.Ordinal);
        bool rows=edit.Operation.EndsWith("_rows",StringComparison.Ordinal);
        cancel();
        if(insert)
        {
            if(rows)table.InsertRows(edit.Index!.Value,edit.Size!.Value,edit.Count!.Value);
            else table.InsertColumns(edit.Index!.Value,edit.Size!.Value,edit.Count!.Value);
            for(int row=0;row<edit.Values!.Length;row++)for(int column=0;column<edit.Values[row].Length;column++)
            {
                cancel();int targetRow=rows?edit.Index!.Value+row:row,targetColumn=rows?column:edit.Index!.Value+column;
                Need(table.IsContentEditable(targetRow,targetColumn)&&table.IsFormatEditable(targetRow,targetColumn),"Inserted native cell has protected content or formatting.");
                table.SetDataType(targetRow,targetColumn,DataType.String,UnitType.Unitless);
                table.SetTextString(targetRow,targetColumn,edit.Values[row][column]);
                FontCoverage.Require(tr,table.GetTextStyleId(targetRow,targetColumn,0),edit.Values[row][column]);
            }
        }
        else if(delete)
        {
            if(rows)table.DeleteRows(edit.Index!.Value,edit.Count!.Value);else table.DeleteColumns(edit.Index!.Value,edit.Count!.Value);
        }
        else if(edit.Range is {} range)
        {
            var native=CellRange.Create(table,range.TopRow,range.LeftColumn,range.BottomRow,range.RightColumn);
            if(edit.Operation=="merge_table_cells")table.MergeCells(native);else table.UnmergeCells(native);
        }
        else if(edit.TextCells!=null)
        {
            foreach(var cell in edit.TextCells)
            {
                cancel();
                if(table.Cells[cell.Row,cell.Column].Contents.Count==0)
                {table.SetDataType(cell.Row,cell.Column,DataType.String,UnitType.Unitless);table.SetTextString(cell.Row,cell.Column,"");}
                FontCoverage.Require(tr,table.GetTextStyleId(cell.Row,cell.Column,0),cell.Text);
                table.SetTextString(cell.Row,cell.Column,0,cell.Text);
            }
        }
        else
        {
            foreach(var cell in edit.Cells!)
            {
                cancel();var properties=cell.Properties;
                if(properties.Alignment is {} alignment)table.SetAlignment(cell.Row,cell.Column,Enum.Parse<CellAlignment>(alignment));
                bool empty=table.Cells[cell.Row,cell.Column].Contents.Count==0;
                if(properties.TextHeight is {} height)
                {
                    if(empty)table.SetTextHeight(cell.Row,cell.Column,height);else table.SetTextHeight(cell.Row,cell.Column,0,height);
                }
                if(properties.TextStyleHandle is {} handle)
                {
                    if(empty)table.SetTextStyle(cell.Row,cell.Column,ResolveStyle(handle,db,tr));else table.SetTextStyleId(cell.Row,cell.Column,0,ResolveStyle(handle,db,tr));
                }
                CadTableAppearanceEditor.Apply(table,cell.Row,cell.Column,properties,db,tr,cancel);
            }
        }
        if(regenerate){cancel();table.GenerateLayout();cancel();table.RecomputeTableBlock(true);}
        var after=CaptureEdit(table,edit,cancel);
        if(edit.Operation=="set_table_cell_format")CadTableValueEditor336.VerifyAppearanceContents(before,after,edit,cancel);
        Verify(before,after,edit,cancel,nativeFormatWarnings);
        if(edit.Operation=="set_table_cell_format"&&nativeFormatWarnings!=null)CadTableFormulaEditor343.ObserveEvaluation(before,after,cancel,nativeFormatWarnings);
    }
    private static void Verify(Snapshot before,Snapshot after,TableEditingSpec edit,Action cancel,List<string>? nativeFormatWarnings)
    {
        Need(before.Identity==after.Identity,"Native table entity identity or owner changed.","OBJECT_IDENTITY_MISMATCH");
        Need(Same(before.Common,after.Common),"Table lifecycle changed table style, placement, direction or stored display properties.","NATIVE_TABLE_MISMATCH");
        bool insert=edit.Operation.StartsWith("insert_",StringComparison.Ordinal),delete=edit.Operation.StartsWith("delete_",StringComparison.Ordinal);
        bool rows=edit.Operation.EndsWith("_rows",StringComparison.Ordinal);
        int index=edit.Index??0,count=edit.Count??0;
        int? Map(int value)=>delete&&value>=index&&(long)value<index+(long)count?null:insert&&value>=index?value+count:delete&&value>=index+count?value-count:value;
        int wantedRows=before.Rows+(rows?(insert?count:delete?-count:0):0),wantedColumns=before.Columns+(!rows?(insert?count:delete?-count:0):0);
        Need(after.Rows==wantedRows&&after.Columns==wantedColumns,"Native row/column operation did not retain the requested dimensions.","NATIVE_TABLE_MISMATCH");
        double[] ExpectedSizes(double[] source,bool edited)
        {
            if(!edited)return source;
            var sizes=source.ToList();if(insert)sizes.InsertRange(index,Enumerable.Repeat(edit.Size!.Value,count));else if(delete)sizes.RemoveRange(index,count);return sizes.ToArray();
        }
        bool sizesMatch=Same(ExpectedSizes(before.RowHeights,rows&&(insert||delete)),after.RowHeights)&&Same(ExpectedSizes(before.ColumnWidths,!rows&&(insert||delete)),after.ColumnWidths);
        bool formulaFormatting=edit.Operation=="set_table_cell_format"&&before.Values.Any(value=>value?.formula!=null);
        Need(sizesMatch||formulaFormatting,
            "Native table dimensions were clamped or unselected row/column sizes changed.","NATIVE_TABLE_MISMATCH");
        if(!sizesMatch)nativeFormatWarnings?.Add("TABLE_FORMULA_LAYOUT_REFLOWED: Native formula-aware cell formatting changed observed row/column sizes; inspect the actual table. Explicit requested formatting, independent raw inputs, topology and placement remain checked.");
        TableCellRangeSpec? MapRange(TableCellRangeSpec? range)
        {
            if(range==null||!insert&&!delete)return range;
            int? top=rows?Map(range.TopRow):range.TopRow,bottom=rows?Map(range.BottomRow):range.BottomRow;
            int? left=rows?range.LeftColumn:Map(range.LeftColumn),right=rows?range.RightColumn:Map(range.RightColumn);
            return top.HasValue&&bottom.HasValue&&left.HasValue&&right.HasValue?new(top.Value,left.Value,bottom.Value,right.Value):null;
        }
        foreach(var old in before.Cells)
        {
            cancel();int? row=rows&&(insert||delete)?Map(old.row):old.row,column=!rows&&(insert||delete)?Map(old.column):old.column;
            if(!row.HasValue||!column.HasValue)continue;var current=Cell(after,row.Value,column.Value);
            var expected=old with{row=row.Value,column=column.Value,merged_range=MapRange(old.merged_range)};
            bool rangeSelected=edit.Range is {} range&&Contains(range,old.row,old.column);
            if(rangeSelected)
            {
                Need(current.text==old.text,"Merging/unmerging changed literal text.","NATIVE_TABLE_MISMATCH");
                Need(current.merged_range==(edit.Operation=="merge_table_cells"?edit.Range:null),"Native merge state differs from the requested complete range.","NATIVE_TABLE_MISMATCH");
                // Native covered-cell format is not a separate visible cell. Preserve
                // the anchor format; secondary cells become native cells on unmerge.
                if(Anchor(old)&&old.row==edit.Range!.TopRow&&old.column==edit.Range.LeftColumn&&old.format!=null)
                    Need(current.format!=null&&Same(old.format with{grids=current.format!.grids},current.format),"Native merge/unmerge changed the retained anchor text/background formatting.","NATIVE_TABLE_MISMATCH");
                continue;
            }
            var textEdit=edit.TextCells?.SingleOrDefault(c=>c.Row==old.row&&c.Column==old.column);
            if(textEdit!=null)
            {
                var expectedText=expected with{text=textEdit.Text,content_count=1};
                if(old.content_count==0)expectedText=expectedText with{format=current.format};
                var literal=after.Values[checked(current.row*after.Columns+current.column)];
                Need(literal?.data_type=="String"&&literal.unit_type=="Unitless"&&literal.native_value.GetString()==textEdit.Text,"Native literal cell storage differs from the supplied text.","NATIVE_TABLE_MISMATCH");
                Need(Same(expectedText,current),"Native cell text differs from the supplied literal or changed formatting/merge state.","NATIVE_TABLE_MISMATCH");
                continue;
            }
            var formatEdit=edit.Cells?.SingleOrDefault(c=>c.Row==old.row&&c.Column==old.column);
            if(formatEdit!=null)
            {
                CadTableAppearanceEditor.VerifyCell(old,current,formatEdit.Properties,before,edit,cancel);
                continue;
            }
            if(edit.Operation=="set_table_cell_format"&&old.format!=null)
            {CadTableAppearanceEditor.VerifyCell(old,current,null,before,edit,cancel);continue;}
            Need(Same(expected,current),"Native table editing changed an unselected cell's content, formatting or merge state.","NATIVE_TABLE_MISMATCH");
        }
        if(insert)
        {
            for(int row=0;row<edit.Values!.Length;row++)for(int column=0;column<edit.Values[row].Length;column++)
            {
                cancel();var cell=Cell(after,rows?index+row:row,rows?column:index+column);
                Need(cell.text==edit.Values[row][column]&&cell.content_count==1&&cell.merged_range==null,"Inserted native cells differ from supplied literal values or are unexpectedly merged.","NATIVE_TABLE_MISMATCH");
            }
        }
    }
}
