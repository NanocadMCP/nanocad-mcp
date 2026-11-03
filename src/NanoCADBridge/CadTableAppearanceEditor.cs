using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Color=Teigha.Colors.Color;
using static NanoCADBridge.CadTableEditingReader;

namespace NanoCADBridge;

// Native cell/background/grid properties, without replacement geometry or content.
internal static class CadTableAppearanceEditor
{
    internal static bool Same(object? first,object? second)=>ModelParameterSnapshot.Equivalent(
        JsonSerializer.SerializeToElement(first,Wire.Json),JsonSerializer.SerializeToElement(second,Wire.Json));
    private static ObjectId Linetype(string handle,Database db,Transaction tr)
    {
        var id=CadStructureWriter.Find(db,handle);
        Need(tr.GetObject(id,OpenMode.ForRead) is LinetypeTableRecord record&&record.GetType()==typeof(LinetypeTableRecord)&&
            record.OwnerId==db.LinetypeTableId&&!record.IsDependent,"Grid linetype must be an existing ordinary local native linetype.","LINETYPE_UNAVAILABLE");
        return id;
    }
    internal static void Validate(TableCellFormatChanges properties,Database db,Transaction tr,Action cancel)
    {
        foreach(var grid in properties.Grids??Array.Empty<TableGridFormatChanges>())
        {cancel();if(grid.LinetypeHandle is {} handle)Linetype(handle,db,tr);}
    }
    private static Color NativeColor(int? aci,RgbColor? rgb)=>rgb is {} channels?Color.FromRgb(channels.Red,channels.Green,channels.Blue):
        Color.FromColorIndex(aci==0?Teigha.Colors.ColorMethod.ByBlock:aci==256?Teigha.Colors.ColorMethod.ByLayer:Teigha.Colors.ColorMethod.ByAci,(short)aci!.Value);
    private static ColorValue ExpectedColor(int? aci,RgbColor? rgb)=>rgb is {} channels?new("true_color",null,channels.Channels()):
        new(aci==0?"by_block":aci==256?"by_layer":"aci",aci is 0 or 256?null:aci,null);
    internal static void Apply(Table table,int row,int column,TableCellFormatChanges properties,Database db,Transaction tr,Action cancel)
    {
        if(properties.ColorAci!=null||properties.ColorRgb!=null)
        {
            using var color=NativeColor(properties.ColorAci,properties.ColorRgb);
            if(table.Cells[row,column].Contents.Count==0)table.SetContentColor(row,column,color);
            else table.SetContentColor(row,column,0,color);
        }
        if(properties.Background is {} background)
        {
            cancel();
            if(!background.None){using var color=NativeColor(background.ColorAci,background.ColorRgb);table.SetBackgroundColor(row,column,color);}
            table.SetBackgroundColorNone(row,column,background.None);
        }
        foreach(var grid in properties.Grids??Array.Empty<TableGridFormatChanges>())
        {
            cancel();var line=Enum.Parse<GridLineType>(grid.Line);
            if(grid.ColorAci!=null||grid.ColorRgb!=null){using var color=NativeColor(grid.ColorAci,grid.ColorRgb);table.SetGridColor(row,column,line,color);}
            if(grid.Visible is {} visible)table.SetGridVisibility(row,column,line,visible?Visibility.Visible:Visibility.Invisible);
            if(grid.Lineweight is {} weight)table.SetGridLineWeight(row,column,line,(LineWeight)weight);
            if(grid.LinetypeHandle is {} handle)table.SetGridLinetype(row,column,line,Linetype(handle,db,tr));
        }
    }
    // GetOverrides(row,column,GridLineType) is mislabeled CellProperties in the
    // managed SDK, but the stored native mask is GridProperties: weight2,
    // linetype4, color8, visibility16. Only those requested bits may adapt.
    private static int GridMask(TableGridFormatChanges edit)=>(edit.Lineweight!=null?2:0)|(edit.LinetypeHandle!=null?4:0)|
        (edit.ColorAci!=null||edit.ColorRgb!=null?8:0)|(edit.Visible!=null?16:0);
    private static GridValue Requested(GridValue old,TableGridFormatChanges edit)=>old with{
        visibility=edit.Visible is {} visible?visible?"Visible":"Invisible":old.visibility,
        lineweight=edit.Lineweight??old.lineweight,linetype_handle=edit.LinetypeHandle??old.linetype_handle,
        color=edit.ColorAci!=null||edit.ColorRgb!=null?ExpectedColor(edit.ColorAci,edit.ColorRgb):old.color};
    private static TableCellRangeSpec Bounds(CellValue cell)=>cell.merged_range??new(cell.row,cell.column,cell.row,cell.column);
    private static bool SharedBorder(CellValue selected,string selectedLine,CellValue other,string otherLine)
    {
        var a=Bounds(selected);var b=Bounds(other);
        // A merged cell can share its side with several smaller neighboring
        // cells. Only an opposite side on that same touching boundary may
        // reflect native normalization; inside slots grant no neighbor exception.
        return (selectedLine,otherLine) switch{
            ("HorizontalTop","HorizontalBottom")=>b.BottomRow+1==a.TopRow&&b.LeftColumn<=a.RightColumn&&a.LeftColumn<=b.RightColumn,
            ("HorizontalBottom","HorizontalTop")=>a.BottomRow+1==b.TopRow&&b.LeftColumn<=a.RightColumn&&a.LeftColumn<=b.RightColumn,
            ("VerticalLeft","VerticalRight")=>b.RightColumn+1==a.LeftColumn&&b.TopRow<=a.BottomRow&&a.TopRow<=b.BottomRow,
            ("VerticalRight","VerticalLeft")=>a.RightColumn+1==b.LeftColumn&&b.TopRow<=a.BottomRow&&a.TopRow<=b.BottomRow,
            _=>false};
    }
    private static bool GridMatches(GridValue old,GridValue current,TableGridFormatChanges edit,bool optional)
    {
        int mask=GridMask(edit);
        if((old.overrides&~mask)!=(current.overrides&~mask))return false;
        var requested=Requested(old,edit) with{overrides=current.overrides};
        return Same(requested,current)||optional&&Same(old with{overrides=current.overrides},current);
    }
    internal static void VerifyCell(CellValue old,CellValue current,TableCellFormatChanges? properties,
        Snapshot before,TableEditingSpec edit,Action cancel)
    {
        Need(old.format!=null&&current.format!=null,"Cell appearance has no stored native format readback.","NATIVE_TABLE_MISMATCH");
        var prior=old.format!;var actual=current.format!;var expected=prior;
        if(properties is {} p)
        {
            expected=expected with{alignment=p.Alignment??prior.alignment,text_height=p.TextHeight??prior.text_height,
                text_style_handle=p.TextStyleHandle??prior.text_style_handle,
                content_color=p.ColorAci!=null||p.ColorRgb!=null?ExpectedColor(p.ColorAci,p.ColorRgb):prior.content_color};
            if(p.Background is {} background)expected=expected with{background_none=background.None,
                // An explicitly disabled fill has no visible color requirement;
                // the native dormant background value may normalize.
                background_color=background.None?actual.background_color:ExpectedColor(background.ColorAci,background.ColorRgb)};
        }
        Need(Same(expected with{grids=actual.grids},actual)&&
            (properties?.TextHeight is not {} height||TableSizeVerification.Equal(height,actual.text_height)),
            "Native cell format differs from requested fields or changed unrequested text/background/margins.","NATIVE_TABLE_MISMATCH");
        foreach(var oldGrid in prior.grids)
        {
            cancel();var grid=actual.grids.Single(g=>g.line==oldGrid.line);
            var direct=properties?.Grids?.SingleOrDefault(g=>g.Line==oldGrid.line);
            if(direct!=null)
            {Need(GridMatches(oldGrid,grid,direct,false),"Selected native grid line differs from requested appearance or changed unrequested grid fields.","NATIVE_TABLE_MISMATCH");continue;}
            if(Same(oldGrid,grid))continue;
            // Native tables can expose a shared physical border through both
            // adjacent cells. Permit only an unchanged or exactly requested
            // opposite-border result, preserving every other grid property.
            bool shared=false;
            foreach(var selected in edit.Cells??Array.Empty<TableCellFormatEdit>())
            {
                cancel();var cell=before.Cells[checked(selected.Row*before.Columns+selected.Column)];
                foreach(var change in selected.Properties.Grids??Array.Empty<TableGridFormatChanges>())
                    if(SharedBorder(cell,change.Line,old,oldGrid.line)&&GridMatches(oldGrid,grid,change,true)){shared=true;break;}
                if(shared)break;
            }
            Need(shared,"Native cell appearance changed an unrelated grid line/property.","NATIVE_TABLE_MISMATCH");
        }
        Need(Same(old with{format=current.format,text=CadTableFormulaEditor343.FormulaCell(before,old)?current.text:old.text},current),"Native formatting changed stored content, state, editability or merged range.","NATIVE_TABLE_MISMATCH");
    }
}
