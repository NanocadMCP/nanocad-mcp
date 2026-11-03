using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using static NanoCADBridge.CadTableEditingReader;

namespace NanoCADBridge;

// Resident native scalar/style editing. No formatted-text parsing or replacement table.
internal static class CadTableValueEditor336
{
    private static bool Exact(object? first,object? second)=>JsonSerializer.Serialize(first,Wire.Json)==JsonSerializer.Serialize(second,Wire.Json);
    private static Snapshot Read(Table table,Action cancel)=>Capture(table,cancel,requireWorldXy:false,allowFormattedText:true,allowNumeric:true,allowFormulas:true);
    private static CellValue Cell(Snapshot snapshot,int row,int column)
    {
        Need(row>=0&&column>=0&&row<snapshot.Rows&&column<snapshot.Columns,"Selected cell is outside the current native table.","CELL_OUT_OF_RANGE");
        return snapshot.Cells[checked(row*snapshot.Columns+column)];
    }
    private static ContentValue? Data(Snapshot snapshot,CellValue cell)=>snapshot.Values[checked(cell.row*snapshot.Columns+cell.column)];
    private static int[] Overrides(Snapshot snapshot,CellValue cell)=>snapshot.CellOverrides[checked(cell.row*snapshot.Columns+cell.column)];
    private static ObjectId Style(string handle,Database db,Transaction tr,Action cancel)
    {
        cancel();var id=CadStructureWriter.Find(db,handle);var dictionary=(DBDictionary)tr.GetObject(db.TableStyleDictionaryId,OpenMode.ForRead);
        Need(tr.GetObject(id,OpenMode.ForRead) is TableStyle style&&style.GetType()==typeof(TableStyle)&&style.OwnerId==db.TableStyleDictionaryId,
            "Select an existing local native TableStyle from get_structure table_styles.","STYLE_UNAVAILABLE");
        bool local=false;foreach(DBDictionaryEntry entry in dictionary)
        {cancel();if(entry.Value==id){Need(!entry.Key.Contains('|'),"External TableStyle references need their owning adapter.","STYLE_UNAVAILABLE");local=true;}}
        Need(local,"TableStyle is not indexed by this drawing's native style dictionary.","STYLE_UNAVAILABLE");return id;
    }
    internal static JsonElement Metadata(Table table,Transaction tr,Action cancel)
    {
        using var xdata=table.XData;
        var values=new List<object>();long bytes=0;
        void Charge(object value){cancel();bytes+=JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json).Length;Need(bytes<=8*1024*1024,"Table metadata exceeds the actual8MiB snapshot budget.","RESPONSE_LIMIT_EXCEEDED");}
        var data=xdata==null?null:NativeOwnedMetadata.Buffer(xdata,cancel,Charge);
        var bindings=CadTableFormulaField358.Bindings(table,tr,cancel);
        var owned=CadTableFormulaField358.Owned(table,tr,cancel,bindings);
        var result=Wire.Element(new{handle=table.Handle.ToString(),owner=table.OwnerId.Handle.ToString(),extension=table.ExtensionDictionary.IsNull?null:table.ExtensionDictionary.Handle.ToString(),xdata=data,owned});
        Need(JsonSerializer.SerializeToUtf8Bytes(result,Wire.Json).Length<=8*1024*1024,"Table metadata exceeds the actual8MiB snapshot budget.","RESPONSE_LIMIT_EXCEEDED");return result;
    }
    private static bool InlineFont(string contents,Action cancel)
    {
        for(int i=0;i<contents.Length-1;i++)
        {
            cancel();if(contents[i]!='\\')continue;
            char code=contents[++i];if(code=='\\'||code is not ('f' or 'F'))continue;
            int start=i+1;
            for(int j=start;j<contents.Length;j++)
            {cancel();if(contents[j]==';')return j>start;if(contents[j] is '{' or '}' or '\\')break;}
        }
        return false;
    }
    private static void Coverage(Table table,CellValue cell,ObjectId style,Transaction tr,Action cancel,List<string> warnings)
    {
        cancel();bool inline=InlineFont(cell.text,cancel);
        string plain=table.GetTextString(cell.row,cell.column,0,FormatOption.IgnoreMtextFormat);
        new ModelInventoryBudget(cancel).Add(plain);
        if(style.IsNull)
        {warnings.Add("TABLE_FONT_COVERAGE_UNVERIFIED: Native inherited text style is unresolved; review actual rendered text.");return;}
        var record=(TextStyleTableRecord)tr.GetObject(style,OpenMode.ForRead);
        if(record.IsShapeFile||!string.IsNullOrEmpty(record.BigFontFileName)||string.IsNullOrWhiteSpace(record.Font.TypeFace))
        {warnings.Add("TABLE_FONT_COVERAGE_UNVERIFIED: Retained SHX/big-font or unresolved font needs its coverage adapter; review actual rendered text.");return;}
        // Native visible text excludes formatting syntax and font-name controls.
        // Inline runs can retain a different font from the reassigned base font.
        try{FontCoverage.Require(tr,style,string.Empty);cancel();FontCoverage.Require(tr,style,plain);}
        catch(DomainException error) when(error.Error.code=="FONT_CHECK_UNAVAILABLE"||inline&&error.Error.code=="FONT_GLYPHS_MISSING")
        {warnings.Add(error.Error.code+": Table base-style glyph coverage is not fully verified; review actual rendered text.");}
        if(inline)warnings.Add("TABLE_INLINE_FONT_COVERAGE_UNVERIFIED: Native inline font runs remain retained; the visible-text base-font check does not prove coverage of each run. Review actual rendered text.");
    }
    internal static string[] CheckFontCoverage(Table table,CellValue cell,ObjectId style,Transaction tr,Action cancel)
    {
        if(cell.content_count==0){FontCoverage.Require(tr,style,string.Empty);return Array.Empty<string>();}
        if(!style.IsNull&&style.Handle.ToString()==cell.format?.text_style_handle)return Array.Empty<string>();
        var warnings=new List<string>();Coverage(table,cell,style,tr,cancel,warnings);
        return warnings.Distinct(StringComparer.Ordinal).ToArray();
    }
    private static string[] Validate(Table table,Snapshot before,TableValueEditingSpec edit,Database db,Transaction tr,Action cancel)
    {
        var warnings=new List<string>();
        Need(!table.ObjectId.IsNull&&table.Database==db,"Value/style editing requires the exact resident native table.","OUT_OF_SCOPE");
        CadTableToken349.Require(table,before,edit.TableToken,edit.Operation,cancel);
        using(var reactors=table.GetPersistentReactorIds())Need(reactors.Count==0,"Persistent table dependencies need their owning adapter.");
        if(edit.TableStyleHandle is {} handle)
        {
            var style=(TableStyle)tr.GetObject(Style(handle,db,tr,cancel),OpenMode.ForRead);
            foreach(var cell in before.Cells)
            {
                cancel();if(!Anchor(cell))continue;
                Need(cell.format_editable,"TableStyle assignment touches protected native formatting.");
                if(cell.content_count==0||cell.format==null)continue;
                int fontOverride=table.RowType(cell.row) switch{RowType.TitleRow=>18,RowType.HeaderRow=>19,RowType.DataRow=>20,_=>0};
                bool ownFont=((Data(before,cell)?.overrides??0)&64)!=0||Overrides(before,cell).Contains(134)||
                    before.Common.GetProperty("table_overrides").EnumerateArray().Any(value=>value.GetInt32()==fontOverride);
                if(ownFont)continue;
                string name=cell.format.cell_style;
                Need(style.CellStyles.Contains(name),"The selected TableStyle lacks an inherited native cell-style name used by this table; choose a compatible style.","STYLE_UNAVAILABLE");
                var textStyle=style.TextStyle(name);
                // Check the actual required existing glyphs against the new
                // inherited native font. Explicit per-cell/content fonts stay.
                if(textStyle.IsNull||textStyle.Handle.ToString()!=cell.format.text_style_handle)
                    Coverage(table,cell,textStyle,tr,cancel,warnings);
            }
        }
        else foreach(var selected in edit.Cells!)
        {
            cancel();var cell=Cell(before,selected.Row,selected.Column);
            Need(Anchor(cell),"Select a merged scalar cell by its top-left native anchor.","INVALID_ARGUMENT");
            Need(cell.content_editable&&cell.format_editable,"Selected scalar cell has protected native content or format.");
            if(selected.DataType=="String")FontCoverage.Require(tr,cell.content_count==0?table.TextStyle(selected.Row,selected.Column):table.GetTextStyleId(selected.Row,selected.Column,0),selected.Value.GetString()!);
        }
        return warnings.Distinct(StringComparer.Ordinal).ToArray();
    }
    internal static void CheckSource(Table table,TableValueEditingSpec edit,Database db,Transaction tr,Action cancel)
    {var before=Read(table,cancel);Validate(table,before,edit,db,tr,cancel);Metadata(table,tr,cancel);}
    internal static void VerifyAppearanceContents(Snapshot before,Snapshot after,TableEditingSpec edit,Action cancel)
    {
        Need(before.Values.Length==after.Values.Length,"Native appearance changed cell-content dimensions.","NATIVE_TABLE_MISMATCH");
        foreach(var cell in before.Cells)
        {
            cancel();var prior=Data(before,cell);var actual=Data(after,Cell(after,cell.row,cell.column));
            var requested=edit.Cells?.SingleOrDefault(value=>value.Row==cell.row&&value.Column==cell.column)?.Properties;
            int mask=requested==null?0:(requested.Alignment!=null?16:0)|(requested.ColorAci!=null||requested.ColorRgb!=null?32:0)|
                (requested.TextStyleHandle!=null?64:0)|(requested.TextHeight!=null?128:0)|(requested.Background!=null?512:0);
            Need(prior==null?actual==null:actual!=null&&Exact(CadTableFormulaEditor343.NormalizeEvaluation(prior,actual) with{overrides=actual.overrides},actual)&&(prior.overrides&~mask)==(actual.overrides&~mask),
                "Native appearance changed raw scalar values/types/units, rotation/scale or unrelated content overrides.","NATIVE_TABLE_MISMATCH");
        }
    }
    internal static string[] Apply(Table table,TableValueEditingSpec edit,Database db,Transaction tr,Action cancel)
    {
        var before=Read(table,cancel);var warnings=Validate(table,before,edit,db,tr,cancel).ToList();var metadata=Metadata(table,tr,cancel);
        var id=table.ObjectId;var owner=table.OwnerId;
        if(edit.TableStyleHandle is {} handle)
        {cancel();table.TableStyle=Style(handle,db,tr,cancel);}
        else foreach(var selected in edit.Cells!)
        {
            cancel();object value=selected.DataType=="String"?(object)selected.Value.GetString()!:selected.DataType=="Long"?(object)selected.Value.GetInt32():selected.Value.GetDouble();
            if(Data(before,Cell(before,selected.Row,selected.Column))?.formula!=null)
            {table.SetFormula(selected.Row,selected.Column,0,string.Empty);cancel();}
            // The indexless setter also creates exactly one scalar in a selected
            // empty anchor. Existing single contents retain their native identity.
            if(table.Cells[selected.Row,selected.Column].Contents.Count==0)table.SetValue(selected.Row,selected.Column,value);
            else table.SetValue(selected.Row,selected.Column,0,value);
            cancel();table.SetDataType(selected.Row,selected.Column,0,new DataTypeParameter(Enum.Parse<DataType>(selected.DataType),Enum.Parse<UnitType>(selected.UnitType)));
            cancel();table.SetDataFormat(selected.Row,selected.Column,0,selected.DataFormat);
        }
        cancel();table.GenerateLayout();cancel();table.RecomputeTableBlock(true);cancel();
        var after=Read(table,cancel);
        try
        {
            if(edit.TableStyleHandle!=null)foreach(var cell in after.Cells)
            {
                cancel();if(!Anchor(cell)||cell.content_count!=1||cell.format==null)continue;
                if(Cell(before,cell.row,cell.column).format?.text_style_handle!=cell.format.text_style_handle)
                    Coverage(table,cell,CadStructureWriter.Find(db,cell.format.text_style_handle),tr,cancel,warnings);
            }
            var selected=edit.TableStyleHandle==null?edit.Cells!.Select(cell=>(cell.Row,cell.Column)).ToArray():Array.Empty<(int Row,int Column)>();
            CadTableFormulaField358.Verify(metadata,Metadata(table,tr,cancel),before,after,selected,selected,cancel);
            if(before.Values.Any(value=>value?.native_formula_field!=null)||after.Values.Any(value=>value?.native_formula_field!=null))warnings.Add(CadTableFormulaField358.Warning);
            Need(table.ObjectId==id&&table.OwnerId==owner,
                "Native scalar/style editing changed table identity, owning space or retained owned metadata.","NATIVE_TABLE_MISMATCH");
            bool reflow=CadTableNativeLayout356.VerifyTopology(before,after,cancel);
            if(reflow)warnings.Add(CadTableNativeLayout356.Warning);
            if(edit.TableStyleHandle is {} requestedStyle)VerifyStyle(before,after,requestedStyle,cancel);
            else VerifyValues(before,after,edit.Cells!,cancel,warnings);
            CadTableFormulaEditor343.ObserveEvaluation(before,after,cancel,warnings);
            return before.Values.Any(value=>value?.formula!=null)||after.Values.Any(value=>value?.formula!=null)?
                CadTableFormulaEditor343.BoundedWarnings(warnings,cancel):warnings.Distinct(StringComparer.Ordinal).ToArray();
        }
        catch(DomainException error)when(error.Error.code=="NATIVE_TABLE_MISMATCH")
        {throw CadTableNativeLayout356.WithDiagnostics(error,before,after,cancel);}

    }
    private static void VerifyValues(Snapshot before,Snapshot after,TableNumericCellEdit[] selected,Action cancel,List<string> warnings)
    {
        Need(before.Common.GetRawText()==after.Common.GetRawText(),"Native scalar edit changed table placement, root display, style or unrelated defaults.","NATIVE_TABLE_MISMATCH");
        var edits=selected.ToDictionary(cell=>(cell.Row,cell.Column));
        foreach(var prior in before.Cells)
        {
            cancel();var actual=Cell(after,prior.row,prior.column);
            if(!edits.TryGetValue((prior.row,prior.column),out var request))
            {
                var oldData=Data(before,prior);var currentData=Data(after,actual);
                Need(Exact(prior with{text=oldData?.formula!=null?actual.text:prior.text},actual)&&
                    (oldData==null?currentData==null:currentData!=null&&Exact(CadTableFormulaEditor343.NormalizeEvaluation(oldData,currentData),currentData))&&Exact(Overrides(before,prior),Overrides(after,actual)),
                    "Native scalar edit changed an unselected stored expression, independent raw input or stored formatting.","NATIVE_TABLE_MISMATCH");continue;
            }
            var data=Data(after,actual);
            Need(data!=null&&data.formula==null&&data.data_type==request.DataType&&data.unit_type==request.UnitType&&actual.content_count==1&&actual.format!=null&&
                (request.DataFormat.Length==0||actual.format.data_format==request.DataFormat),
                "Native scalar datatype, unit or format differs from the explicit request.","NATIVE_TABLE_MISMATCH");
            if(request.DataFormat.Length==0&&actual.format!.data_format.Length!=0)
                warnings.Add("TABLE_DEFAULT_FORMAT_NORMALIZED: Native empty/default format resolved to an observed stored format at cell "+request.Row+","+request.Column+"; inspect its data_format and rendered value. This is not a raw-value mismatch.");
            bool sameValue=request.DataType=="String"?data!.native_value.GetString()==request.Value.GetString():request.DataType=="Long"?data!.native_value.GetInt32()==request.Value.GetInt32():
                data!.double_bits==BitConverter.DoubleToInt64Bits(request.Value.GetDouble()).ToString("X16");
            Need(sameValue,"Native scalar raw value differs from the supplied value.","NATIVE_TABLE_MISMATCH");
            if(Data(before,prior) is {} content)
                Need(Exact(content with{data_type=data!.data_type,unit_type=data.unit_type,native_value=data.native_value,double_bits=data.double_bits,overrides=data.overrides,formula=null,evaluation_unavailable_reason=null,native_content_type="Value",native_formula_field=null,
                    scale=content.formula!=null&&content.auto_scale&&data.auto_scale?data.scale:content.scale},data)&&
                    (content.overrides&~3)==(data!.overrides&~3),"Numeric replacement changed unrequested native content formatting/override bits.","NATIVE_TABLE_MISMATCH");
            if(Data(before,prior) is {formula:not null,auto_scale:true} previous&&data!.auto_scale&&previous.scale!=data.scale)
                warnings.Add("TABLE_AUTOMATIC_SCALE_REFLOWED: Native automatic scale changed after intentional formula-to-scalar replacement; the requested raw value, stored format and unrelated overrides remain checked.");
            var oldOverrides=Overrides(before,prior).Where(value=>value!=148).ToArray();
            var newOverrides=Overrides(after,actual).Where(value=>value!=148).ToArray();
            Need(Exact(oldOverrides,newOverrides)&&Exact(prior with{text=actual.text,content_count=1,
                format=prior.format is {} format?format with{data_format=actual.format!.data_format}:null},actual),
                "Numeric replacement changed merge state, protection or unrequested cell/grid appearance.","NATIVE_TABLE_MISMATCH");
        }
    }
    private static void Explicit(bool selected,object? old,object? current,string message)
    {if(selected)Need(Exact(old,current),message,"NATIVE_TABLE_MISMATCH");}
    internal static void VerifyStyle(Snapshot before,Snapshot after,string style,Action cancel)
    {
        var common=before.Common.EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.Clone(),StringComparer.Ordinal);
        common["table_style_handle"]=Wire.Element(style);
        var global=before.Common.GetProperty("table_overrides").EnumerateArray().Select(value=>value.GetInt32()).ToHashSet();
        foreach(var pair in new[]{("IsTitleSuppressed",1),("IsHeaderSuppressed",2),("flow_direction",3),("horizontal_margin",4),("vertical_margin",5)})
            if(!global.Contains(pair.Item2))common[pair.Item1]=after.Common.GetProperty(pair.Item1).Clone();
        // Table-wide row defaults also have explicit legacy override identities.
        // Read all three roles, including currently suppressed/dormant rows.
        var rowDefaults=new List<JsonElement>();int role=0;
        foreach(var oldRow in before.Common.GetProperty("row_defaults").EnumerateArray())
        {
            cancel();var currentRow=after.Common.GetProperty("row_defaults")[role];
            var expectedRow=oldRow.EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.Clone(),StringComparer.Ordinal);
            foreach(var entry in new[]{("content_color",6),("background_none",9),("background_color",12),("alignment",15),("text_style_handle",18),("text_height",21),("data_type",24),("unit_type",24)})
                if(!global.Contains(entry.Item2+role))expectedRow[entry.Item1]=currentRow.GetProperty(entry.Item1).Clone();
            var expectedGrids=new List<JsonElement>();int line=0;
            foreach(var grid in oldRow.GetProperty("grids").EnumerateArray())
            {
                cancel();var actualGrid=currentRow.GetProperty("grids")[line];
                var fields=grid.EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.Clone(),StringComparer.Ordinal);
                if(!global.Contains(40+role*6+line))fields["color"]=actualGrid.GetProperty("color").Clone();
                bool weight=global.Contains(70+role*6+line)||role==0&&line==4&&global.Contains(73);
                if(!weight)fields["lineweight"]=actualGrid.GetProperty("lineweight").Clone();
                if(!global.Contains(100+role*6+line))fields["visible"]=actualGrid.GetProperty("visible").Clone();
                expectedGrids.Add(Wire.Element(fields));line++;
            }
            expectedRow["grids"]=Wire.Element(expectedGrids);rowDefaults.Add(Wire.Element(expectedRow));role++;
        }
        common["row_defaults"]=Wire.Element(rowDefaults);
        Need(Exact(Wire.Element(common),after.Common),"TableStyle assignment changed placement/root appearance or explicit native table overrides.","NATIVE_TABLE_MISMATCH");
        foreach(var prior in before.Cells)
        {
            cancel();var actual=Cell(after,prior.row,prior.column);
            // Numeric displayed strings can legitimately follow inherited data
            // format. Exact raw storage remains the content preservation oracle.
            var oldData=Data(before,prior);var newData=Data(after,actual);
            var expected=prior with{text=oldData!=null?actual.text:prior.text,format=actual.format};
            Need(Exact(expected,actual)&&Exact(Overrides(before,prior),Overrides(after,actual)),"TableStyle assignment changed raw content/types/units, merge state, protection or explicit override inventory.","NATIVE_TABLE_MISMATCH");
            if(oldData!=null&&newData!=null)
            {
                var contentExpected=CadTableFormulaEditor343.NormalizeEvaluation(oldData,newData) with{rotation=(oldData.overrides&4)!=0?oldData.rotation:newData.rotation,
                    scale=(oldData.overrides&8)!=0?oldData.scale:newData.scale,auto_scale=(oldData.overrides&256)!=0?oldData.auto_scale:newData.auto_scale};
                Need(Exact(contentExpected,newData),"TableStyle assignment changed raw native content or explicit content overrides.","NATIVE_TABLE_MISMATCH");
            }
            else Need(oldData==null&&newData==null,"Native style assignment created or removed cell content.","NATIVE_TABLE_MISMATCH");
            if(prior.format is not {} old||actual.format is not {} current)continue;
            var flags=Overrides(before,prior).ToHashSet();int bits=oldData?.overrides??0;
            Explicit((bits&2)!=0,old.data_format,current.data_format,"TableStyle assignment changed an explicit scalar data format.");
            Explicit((bits&16)!=0||flags.Contains(130),old.alignment,current.alignment,"Explicit cell alignment changed.");
            Explicit((bits&32)!=0||flags.Contains(133),old.content_color,current.content_color,"Explicit content color changed.");
            Explicit((bits&64)!=0||flags.Contains(134),old.text_style_handle,current.text_style_handle,"Explicit text style changed.");
            Explicit((bits&128)!=0||flags.Contains(135),old.text_height,current.text_height,"Explicit text height changed.");
            Explicit((bits&512)!=0||flags.Contains(131),old.background_none,current.background_none,"Explicit fill state changed.");
            Explicit((bits&512)!=0||flags.Contains(132),old.background_color,current.background_color,"Explicit fill color changed.");
            for(int i=0;i<4;i++)Explicit((bits&(1<<(i switch{0=>11,1=>10,2=>13,_=>12})))!=0,old.margins[i],current.margins[i],"Explicit cell margin changed.");
            foreach(var grid in old.grids)
            {
                cancel();var changed=current.grids.Single(value=>value.line==grid.line);
                Need(grid.overrides==changed.overrides,"Native style assignment removed explicit grid overrides.","NATIVE_TABLE_MISMATCH");
                Explicit((grid.overrides&1)!=0,grid.line_style,changed.line_style,"Explicit grid line style changed.");
                Explicit((grid.overrides&2)!=0,grid.lineweight,changed.lineweight,"Explicit grid weight changed.");
                Explicit((grid.overrides&4)!=0,grid.linetype_handle,changed.linetype_handle,"Explicit grid linetype changed.");
                Explicit((grid.overrides&8)!=0,grid.color,changed.color,"Explicit grid color changed.");
                Explicit((grid.overrides&16)!=0,grid.visibility,changed.visibility,"Explicit grid visibility changed.");
                Explicit((grid.overrides&32)!=0,grid.double_spacing,changed.double_spacing,"Explicit grid spacing changed.");
            }
        }
    }
}
