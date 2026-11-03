using System.Text.Json;
using System.Text;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using static NanoCADBridge.CadTableEditingReader;

namespace NanoCADBridge;

// Resident grid setters. The existing complete table snapshot/token stays the
// single source of observed state; no detached context-dependent preview.
internal static class CadTableGridLayoutEditor341
{
    private sealed record Selected(CellValue Cell,TableGridLineLayout341 Grid);
    private static Snapshot Read(Table table,Action cancel)=>Capture(table,cancel,requireWorldXy:false,allowFormattedText:true,allowNumeric:true,allowFormulas:true);
    private static bool Exact(object? a,object? b)=>JsonSerializer.Serialize(a,Wire.Json)==JsonSerializer.Serialize(b,Wire.Json);
    private static TableCellRangeSpec Bounds(CellValue cell)=>cell.merged_range??new(cell.row,cell.column,cell.row,cell.column);
    private static int Mask(TableGridLineLayout341 grid)=>(grid.LineStyle!=null?1:0)|(grid.DoubleSpacing!=null?32:0);
    private static JsonElement Defaults(Database db)=>Wire.Element(new{layer=db.Clayer.Handle.ToString(),text_style=db.Textstyle.Handle.ToString(),
        dimension_style=db.Dimstyle.Handle.ToString(),table_style=db.Tablestyle.Handle.ToString(),mleader_style=db.MLeaderstyle.Handle.ToString(),
        db.TileMode,space=db.CurrentSpaceId.Handle.ToString()});
    private static Selected[] Validate(Table table,Snapshot before,TableGridLayoutEdit341 edit,Database db,Action cancel)
    {
        Need(!table.ObjectId.IsNull&&table.Database==db,"Grid layout requires the exact resident native Table.","OUT_OF_SCOPE");
        CadTableToken349.Require(table,before,edit.TableToken,edit.Operation,cancel);
        using(var reactors=table.GetPersistentReactorIds())Need(reactors.Count==0,"Persistent table dependencies require their owning adapter.");
        var selected=new List<Selected>();
        foreach(var requested in edit.Cells)
        {
            cancel();Need(requested.Row>=0&&requested.Column>=0&&requested.Row<before.Rows&&requested.Column<before.Columns,"Selected cell is outside the current table.","CELL_OUT_OF_RANGE");
            var cell=before.Cells[checked(requested.Row*before.Columns+requested.Column)];
            Need(Anchor(cell),"Select merged grid layout by its top-left native anchor.","INVALID_ARGUMENT");
            Need(cell.format_editable&&cell.format!=null,"Selected cell has protected/unavailable native formatting.");
            foreach(var grid in requested.Grids){cancel();selected.Add(new(cell,grid));}
        }
        for(int i=0;i<selected.Count;i++)for(int j=i+1;j<selected.Count;j++)
        {
            cancel();var a=selected[i];var b=selected[j];
            Need(!TableGridLayoutValidation341.SharedBorder(Bounds(a.Cell),a.Grid.Line,Bounds(b.Cell),b.Grid.Line)||!TableGridLayoutValidation341.Conflicting(a.Grid,b.Grid),
                "Conflicting selected grid settings address the same physical shared border; use compatible settings on both sides.","INVALID_ARGUMENT");
        }
        return selected.ToArray();
    }
    internal static void CheckSource(Table table,TableGridLayoutEdit341 edit,Database db,Transaction tr,Action cancel)
    {
        var before=Read(table,cancel);Validate(table,before,edit,db,cancel);CadTableValueEditor336.Metadata(table,tr,cancel);
    }
    internal static string[] Apply(Table table,TableGridLayoutEdit341 edit,Database db,Transaction tr,Action cancel)
    {
        var before=Read(table,cancel);var selected=Validate(table,before,edit,db,cancel);
        var metadata=CadTableValueEditor336.Metadata(table,tr,cancel);var id=table.ObjectId;var owner=table.OwnerId;var defaults=Defaults(db);
        foreach(var requested in selected)
        {
            cancel();var line=Enum.Parse<GridLineType>(requested.Grid.Line);int row=requested.Cell.row,column=requested.Cell.column;
            if(requested.Grid.LineStyle is {} style){table.SetGridLineStyle(row,column,line,Enum.Parse<GridLineStyle>(style));cancel();}
            if(requested.Grid.DoubleSpacing is {} spacing){table.SetGridDoubleLineSpacing(row,column,line,spacing);cancel();}
        }
        cancel();table.GenerateLayout();cancel();table.RecomputeTableBlock(true);cancel();var after=Read(table,cancel);
        Need(table.ObjectId==id&&table.OwnerId==owner&&defaults.GetRawText()==Defaults(db).GetRawText()&&metadata.GetRawText()==CadTableValueEditor336.Metadata(table,tr,cancel).GetRawText(),
            "Native grid layout changed table identity/owner, current defaults/context or retained owned metadata.","NATIVE_TABLE_MISMATCH");
        var warnings=new List<string>();Verify(before,after,selected,cancel,warnings);CadTableFormulaEditor343.ObserveEvaluation(before,after,cancel,warnings);
        return before.Values.Any(value=>value?.formula!=null)?CadTableFormulaEditor343.BoundedWarnings(warnings,cancel):warnings.Distinct(StringComparer.Ordinal).ToArray();
    }
    private static void Verify(Snapshot before,Snapshot after,Selected[] selected,Action cancel,List<string> warnings)
    {
        Need(before.Rows==after.Rows&&before.Columns==after.Columns&&before.Common.GetRawText()==after.Common.GetRawText(),
            "Native grid layout changed table topology/placement/root display or table-wide defaults.","NATIVE_TABLE_MISMATCH");
        var direct=selected.ToDictionary(item=>(item.Cell.row,item.Cell.column,item.Grid.Line));
        var legacyDeltas=new List<object>();int omittedLegacyDeltas=0;long legacyBytes=0;
        for(int index=0;index<before.Cells.Length;index++)
        {
            cancel();var prior=before.Cells[index];var actual=after.Cells[index];var format=prior.format;
            bool involved=false;
            if(format!=null)
            {
                var current=actual.format??throw new DomainException("NATIVE_TABLE_MISMATCH","Native cell formatting became unavailable.");
                var grids=(GridValue[])format.grids.Clone();
                for(int g=0;g<grids.Length;g++)
                {
                    cancel();var old=grids[g];var value=current.grids.Single(item=>item.line==old.line);direct.TryGetValue((prior.row,prior.column,old.line),out var own);
                    var neighbors=new List<TableGridLineLayout341>();int mask=own==null?0:Mask(own.Grid);
                    foreach(var other in selected)
                    {
                        cancel();if(TableGridLayoutValidation341.SharedBorder(Bounds(prior),old.line,Bounds(other.Cell),other.Grid.Line))
                        {neighbors.Add(other.Grid);mask|=Mask(other.Grid);}
                    }
                    involved|=mask!=0;
                    Need((old.overrides&~mask)==(value.overrides&~mask),"Native grid layout changed unrelated grid override bits.","NATIVE_TABLE_MISMATCH");
                    bool styleAllowed=own?.Grid.LineStyle is {} style?value.line_style==style:
                        value.line_style==old.line_style||neighbors.Any(item=>item.LineStyle==value.line_style);
                    bool spacingAllowed=own?.Grid.DoubleSpacing is {} spacing?TableSizeVerification.Equal(value.double_spacing,spacing):
                        TableSizeVerification.Equal(value.double_spacing,old.double_spacing)||neighbors.Any(item=>item.DoubleSpacing is {} expected&&TableSizeVerification.Equal(value.double_spacing,expected));
                    Need(styleAllowed&&spacingAllowed,"Native grid line style/spacing differs from requested or retained shared-border state.","NATIVE_TABLE_MISMATCH");
                    var expected=old with{line_style=value.line_style,double_spacing=value.double_spacing,overrides=value.overrides};
                    Need(Exact(expected,value),"Native grid layout changed unrequested visibility/color/lineweight/linetype or grid identity.","NATIVE_TABLE_MISMATCH");
                    if(neighbors.Count>0&&!Exact(old,value))warnings.Add("TABLE_SHARED_BORDER_NORMALIZED: Native requested grid settings also normalized a corresponding adjacent border; raw content and unrelated fields remain checked.");
                    grids[g]=expected;
                }
                format=format with{grids=grids};
            }
            if(!Exact(before.CellOverrides[index],after.CellOverrides[index]))
            {
                Need(involved,"Native grid layout changed legacy override inventory on an unrelated cell.","NATIVE_TABLE_MISMATCH");
                // This older enum has only Color/LineWeight/Visibility grid
                // entries, no LineStyle/DoubleLineSpacing counterpart. Exact
                // modern masks/fields below remain authoritative. Disclose
                // the redundant observation rather than inventing a mapping.
                var delta=new{row=prior.row,column=prior.column,before=before.CellOverrides[index],after=after.CellOverrides[index]};
                string diagnostic=JsonSerializer.Serialize(delta,Wire.Json);cancel();int bytes=Encoding.UTF8.GetByteCount(diagnostic);
                if(legacyBytes+bytes<=16384){legacyDeltas.Add(delta);legacyBytes+=bytes;}else omittedLegacyDeltas++;
            }
            Need(Exact(prior with{format=format,text=before.Values[index]?.formula!=null?actual.text:prior.text},actual),"Native grid layout changed content/merge/protection or unrequested cell formatting.","NATIVE_TABLE_MISMATCH");
            var oldContent=before.Values[index];var content=after.Values[index];
            if(oldContent!=null&&content!=null)oldContent=CadTableFormulaEditor343.NormalizeEvaluation(oldContent,content);
            if(oldContent!=null&&content!=null&&content.auto_scale&&oldContent.scale!=content.scale)
            {
                oldContent=oldContent with{scale=content.scale};warnings.Add("TABLE_AUTOMATIC_SCALE_REFLOWED: Native automatic content scale changed after grid layout; exact raw scalar values/types/units remain checked.");
            }
            Need(Exact(oldContent,content),"Native grid layout changed raw values/types/units or unrequested content formatting/masks.","NATIVE_TABLE_MISMATCH");
        }
        if(legacyDeltas.Count>0||omittedLegacyDeltas>0)
            warnings.Add("TABLE_LEGACY_GRID_OVERRIDE_PROVENANCE_UNCERTAIN: Only requested/reciprocal anchors changed the older redundant CellStyleOverrides inventory; native grid/content fields and unrelated masks remain checked. Observations="+
                JsonSerializer.Serialize(new{cells=legacyDeltas,omitted_diagnostic_cells=omittedLegacyDeltas,
                    omission_reason=omittedLegacyDeltas>0?"Detailed diagnostic text exceeded16KiB; no model data was truncated.":null},Wire.Json));
        if(!Exact(before.RowHeights,after.RowHeights)||!Exact(before.ColumnWidths,after.ColumnWidths))
            warnings.Add("TABLE_MINIMUM_SIZE_REFLOWED: Native layout changed observed row/column sizes after grid settings; inspect actual table readability. Root placement/topology/raw values remain checked.");
    }
}
