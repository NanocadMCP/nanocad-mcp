using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using static NanoCADBridge.CadTableEditingReader;

namespace NanoCADBridge;

// Resident native formatting; never replace table/content or clear overrides.
internal static class CadTableCellLayoutEditor340
{
    private static readonly CellMargins[] Sides={CellMargins.Top,CellMargins.Left,CellMargins.Bottom,CellMargins.Right};
    private static readonly int[] MarginBits={2048,1024,8192,4096};
    private static Snapshot Read(Table table,Action cancel)=>Capture(table,cancel,requireWorldXy:false,allowFormattedText:true,allowNumeric:true,allowFormulas:true);
    private static bool Exact(object? first,object? second)=>JsonSerializer.Serialize(first,Wire.Json)==JsonSerializer.Serialize(second,Wire.Json);
    private static CellValue Cell(Snapshot state,int row,int column)
    {
        Need(row>=0&&column>=0&&row<state.Rows&&column<state.Columns,"Selected cell is outside the current native table.","CELL_OUT_OF_RANGE");
        return state.Cells[checked(row*state.Columns+column)];
    }
    private static int Offset(Snapshot state,CellValue cell)=>checked(cell.row*state.Columns+cell.column);
    private static double?[] Margins(TableCellMargins340? margins)=>new[]{margins?.Top,margins?.Left,margins?.Bottom,margins?.Right};
    private static JsonElement Defaults(Database db)=>Wire.Element(new{layer=db.Clayer.Handle.ToString(),text_style=db.Textstyle.Handle.ToString(),
        dimension_style=db.Dimstyle.Handle.ToString(),table_style=db.Tablestyle.Handle.ToString(),mleader_style=db.MLeaderstyle.Handle.ToString(),
        db.TileMode,space=db.CurrentSpaceId.Handle.ToString()});
    private static int Mask(TableCellLayoutProperties340 p)
    {
        int result=(p.DataFormat!=null?2:0)|(p.Rotation!=null?4:0)|(p.Scale!=null?8:0)|(p.AutoScale!=null?256:0);
        var margins=Margins(p.Margins);for(int i=0;i<4;i++)if(margins[i]!=null)result|=MarginBits[i];return result;
    }
    private static TableStyle Style(Table table,Database db,Transaction tr,Action cancel)
    {
        cancel();var obj=tr.GetObject(table.TableStyle,OpenMode.ForRead);
        Need(obj is TableStyle&&obj.GetType()==typeof(TableStyle)&&obj.OwnerId==db.TableStyleDictionaryId,
            "Cell-style defaults require the current exact local TableStyle.","STYLE_UNAVAILABLE");
        bool found=false;
        foreach(DBDictionaryEntry entry in (DBDictionary)tr.GetObject(db.TableStyleDictionaryId,OpenMode.ForRead))
        {cancel();if(entry.Value==table.TableStyle){Need(!entry.Key.Contains('|'),"External TableStyle requires its owning route.","STYLE_UNAVAILABLE");found=true;}}
        Need(found,"Current TableStyle is not indexed by this drawing's native dictionary.","STYLE_UNAVAILABLE");return (TableStyle)obj;
    }
    private static string[] Validate(Table table,Snapshot before,TableCellLayoutEdit340 edit,Database db,Transaction tr,Action cancel)
    {
        Need(!table.ObjectId.IsNull&&table.Database==db,"Cell layout requires the exact resident native Table.","OUT_OF_SCOPE");
        CadTableToken349.Require(table,before,edit.TableToken,edit.Operation,cancel);
        using(var reactors=table.GetPersistentReactorIds())Need(reactors.Count==0,"Persistent table dependencies require their owning adapter.");
        TableStyle? style=null;var warnings=new List<string>();
        foreach(var selected in edit.Cells)
        {
            cancel();var cell=Cell(before,selected.Row,selected.Column);var p=selected.Properties;
            Need(Anchor(cell),"Select merged cell layout by its top-left native anchor.","INVALID_ARGUMENT");
            Need(cell.format_editable&&cell.format!=null,"Selected cell has protected/unavailable native formatting.");
            if(p.Rotation!=null||p.Scale!=null||p.AutoScale!=null)Need(cell.content_count==1,"Rotation/scale/auto_scale requires one existing native scalar; do not invent content in an empty cell.","INVALID_ARGUMENT");
            if(p.CellStyle is {} name)
            {
                style??=Style(table,db,tr,cancel);
                Need(style.CellStyles.Contains(name),"Select an exact existing cell-style name from the current TableStyle.","STYLE_UNAVAILABLE");
                int offset=Offset(before,cell);int fontOverride=table.RowType(cell.row) switch{RowType.TitleRow=>18,RowType.HeaderRow=>19,RowType.DataRow=>20,_=>0};
                bool ownFont=((before.Values[offset]?.overrides??0)&64)!=0||before.CellOverrides[offset].Contains(134)||
                    before.Common.GetProperty("table_overrides").EnumerateArray().Any(value=>value.GetInt32()==fontOverride);
                if(!ownFont&&cell.content_count==1)warnings.AddRange(CadTableValueEditor336.CheckFontCoverage(table,cell,style.TextStyle(name),tr,cancel));
            }
        }
        return warnings.Distinct(StringComparer.Ordinal).ToArray();
    }
    internal static void CheckSource(Table table,TableCellLayoutEdit340 edit,Database db,Transaction tr,Action cancel)
    {
        var before=Read(table,cancel);Validate(table,before,edit,db,tr,cancel);CadTableValueEditor336.Metadata(table,tr,cancel);
        if(edit.Cells.Any(cell=>cell.Properties.CellStyle!=null))CadTableStyleWriter337.Capture(Style(table,db,tr,cancel),tr,cancel);
    }
    internal static string[] Apply(Table table,TableCellLayoutEdit340 edit,Database db,Transaction tr,Action cancel)
    {
        var before=Read(table,cancel);var warnings=Validate(table,before,edit,db,tr,cancel).ToList();
        var metadata=CadTableValueEditor336.Metadata(table,tr,cancel);var id=table.ObjectId;var owner=table.OwnerId;var defaults=Defaults(db);
        var styleBefore=edit.Cells.Any(cell=>cell.Properties.CellStyle!=null)?CadTableStyleWriter337.Capture(Style(table,db,tr,cancel),tr,cancel):null;
        foreach(var selected in edit.Cells)
        {
            cancel();int row=selected.Row,column=selected.Column;var p=selected.Properties;
            if(p.CellStyle is {} name){table.SetCellStyle(row,column,name);cancel();}
            var sides=Margins(p.Margins);
            for(int side=0;side<4;side++)if(sides[side] is {} margin){cancel();table.SetMargin(row,column,Sides[side],margin);}
            if(p.DataFormat is {} format)
            {cancel();if(table.Cells[row,column].Contents.Count==0)table.SetDataFormat(row,column,format);else table.SetDataFormat(row,column,0,format);}
            if(p.Rotation is {} rotation){cancel();table.SetRotation(row,column,0,Math.IEEERemainder(rotation,2*Math.PI));}
            if(p.AutoScale is {} auto){cancel();table.Cells[row,column].Contents[0].IsAutoScale=auto;}
            if(p.Scale is {} scale){cancel();table.Cells[row,column].Contents[0].Scale=scale;}
        }
        cancel();table.GenerateLayout();cancel();table.RecomputeTableBlock(true);cancel();var after=Read(table,cancel);
        Need(table.ObjectId==id&&table.OwnerId==owner&&defaults.GetRawText()==Defaults(db).GetRawText()&&metadata.GetRawText()==CadTableValueEditor336.Metadata(table,tr,cancel).GetRawText(),
            "Native cell layout changed table identity/owner, document current defaults/context or retained owned metadata.","NATIVE_TABLE_MISMATCH");
        if(styleBefore!=null)Need(styleBefore.Token==CadTableStyleWriter337.Capture(Style(table,db,tr,cancel),tr,cancel).Token,"Native cell-style assignment altered captured stored properties/metadata of its reusable TableStyle.","NATIVE_TABLE_MISMATCH");
        Verify(before,after,edit,cancel,warnings);
        foreach(var selected in edit.Cells)
        {
            cancel();var prior=Cell(before,selected.Row,selected.Column);var actual=Cell(after,selected.Row,selected.Column);
            if(actual.content_count==1&&prior.format?.text_style_handle!=actual.format?.text_style_handle)
                warnings.AddRange(CadTableValueEditor336.CheckFontCoverage(table,actual with{format=prior.format},CadStructureWriter.Find(db,actual.format!.text_style_handle),tr,cancel));
        }
        CadTableFormulaEditor343.ObserveEvaluation(before,after,cancel,warnings);
        return before.Values.Any(value=>value?.formula!=null)?CadTableFormulaEditor343.BoundedWarnings(warnings,cancel):warnings.Distinct(StringComparer.Ordinal).ToArray();
    }
    private static void Verify(Snapshot before,Snapshot after,TableCellLayoutEdit340 edit,Action cancel,List<string> warnings)
    {
        Need(before.Rows==after.Rows&&before.Columns==after.Columns&&before.Common.GetRawText()==after.Common.GetRawText(),
            "Native cell layout changed table topology/placement/root display or table-wide defaults.","NATIVE_TABLE_MISMATCH");
        var selected=edit.Cells.ToDictionary(cell=>(cell.Row,cell.Column));
        // Only selected cell styles may follow inherited effective defaults.
        // For those anchors reuse the native explicit-override preservation
        // rules; every other anchor is checked strictly below.
        var expectedCells=(CellValue[])before.Cells.Clone();var expectedData=(ContentValue?[])before.Values.Clone();var expectedOverrides=(int[][])before.CellOverrides.Clone();
        foreach(var prior in before.Cells)
        {
            cancel();int index=Offset(before,prior);var actual=Cell(after,prior.row,prior.column);var old=before.Values[index];var value=after.Values[index];
            selected.TryGetValue((prior.row,prior.column),out var request);var p=request?.Properties;int mask=p==null?0:Mask(p);
            Need(Exact(before.CellOverrides[index],after.CellOverrides[index]),"Native cell layout changed unrelated legacy cell override inventory.","NATIVE_TABLE_MISMATCH");
            var format=prior.format;
            if(p!=null)
            {
                Need(format!=null&&actual.format!=null,"Native cell formatting became unavailable.","NATIVE_TABLE_MISMATCH");
                var currentFormat=actual.format!;var margins=(double[])format!.margins.Clone();var requestedMargins=Margins(p.Margins);
                for(int side=0;side<4;side++)if(requestedMargins[side] is {} margin)
                {Need(TableSizeVerification.Equal(margin,currentFormat.margins[side]),"Native cell margin differs from the request.","NATIVE_TABLE_MISMATCH");margins[side]=currentFormat.margins[side];}
                string dataFormat=format.data_format;
                if(p.DataFormat is {} explicitFormat)
                {
                    Need(explicitFormat.Length==0||explicitFormat==currentFormat.data_format,"Native scalar data format differs from the explicit request.","NATIVE_TABLE_MISMATCH");
                    dataFormat=currentFormat.data_format;
                    if(explicitFormat.Length==0&&dataFormat.Length!=0)warnings.Add("TABLE_DEFAULT_FORMAT_NORMALIZED: Empty/default format resolved to native stored format at cell "+prior.row+","+prior.column+"; inspect rendered value.");
                }
                if(p.CellStyle is {} name)Need(currentFormat.cell_style==name,"Native cell-style name differs from the selected current style.","NATIVE_TABLE_MISMATCH");
                format=format with{margins=margins,data_format=dataFormat,cell_style=p.CellStyle??format.cell_style};
            }
            Need(old==null?value==null:value!=null,"Native layout created/removed scalar content.","NATIVE_TABLE_MISMATCH");
            if(old!=null&&value!=null)
            {
                Need((old.overrides&~mask)==(value.overrides&~mask),"Native cell layout changed unrelated content override bits.","NATIVE_TABLE_MISMATCH");
                var expected=CadTableFormulaEditor343.NormalizeEvaluation(old,value) with{overrides=value.overrides};
                if(p?.Rotation is {} rotation)
                {Need(TableSizeVerification.Equal(Math.Sin(rotation),Math.Sin(value.rotation))&&TableSizeVerification.Equal(Math.Cos(rotation),Math.Cos(value.rotation)),"Native rotation differs from requested radians.","NATIVE_TABLE_MISMATCH");expected=expected with{rotation=value.rotation};}
                if(p?.AutoScale is {} auto){Need(value.auto_scale==auto,"Native automatic scale flag differs from the request.","NATIVE_TABLE_MISMATCH");expected=expected with{auto_scale=auto};}
                if(p?.Scale is {} scale){Need(TableSizeVerification.Equal(scale,value.scale),"Native manual content scale differs from the request.","NATIVE_TABLE_MISMATCH");expected=expected with{scale=value.scale};}
                else if(value.auto_scale&&old.scale!=value.scale)
                {expected=expected with{scale=value.scale};warnings.Add("TABLE_AUTOMATIC_SCALE_REFLOWED: Native automatic content scale changed at cell "+prior.row+","+prior.column+"; raw scalar content remains checked.");}
                expectedData[index]=expected;
                if(p?.CellStyle==null)Need(Exact(expected,value),"Native layout changed raw scalar values/types/units or unrequested scalar formatting.","NATIVE_TABLE_MISMATCH");
            }
            var expectedCell=prior with{format=format,text=old?.formula!=null||p?.DataFormat!=null||p?.CellStyle!=null?actual.text:prior.text};
            expectedCells[index]=expectedCell;
            if(p?.CellStyle==null)Need(Exact(expectedCell,actual),"Native layout changed unselected content/merge/protection or unrequested cell/grid formatting.","NATIVE_TABLE_MISMATCH");
        }
        var expectedState=before with{Cells=expectedCells,Values=expectedData,CellOverrides=expectedOverrides};
        CadTableValueEditor336.VerifyStyle(expectedState,after,before.Common.GetProperty("table_style_handle").GetString()!,cancel);
        if(!Exact(before.RowHeights,after.RowHeights)||!Exact(before.ColumnWidths,after.ColumnWidths))
            warnings.Add("TABLE_MINIMUM_SIZE_REFLOWED: Native layout changed observed row/column sizes after requested cell layout; inspect actual table readability. Root placement/topology/raw values remain checked.");
    }
}
