using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// Read stored native values only. Never GenerateLayout/RecomputeTableBlock from a read.
internal static class CadTableEditingReader
{
    internal sealed record ColorValue(string method,int? aci,int[]? rgb);
    internal sealed record GridValue(string line,string visibility,int lineweight,string? linetype_handle,string line_style,double double_spacing,int overrides,ColorValue color);
    internal sealed record FormatValue(string cell_style,string data_format,string alignment,double text_height,string text_style_handle,
        ColorValue content_color,bool background_none,ColorValue background_color,double[] margins,GridValue[] grids);
    internal sealed record CellValue(int row,int column,string text,int content_count,int state,bool content_editable,bool format_editable,
        TableCellRangeSpec? merged_range,FormatValue? format,[property:JsonIgnore]string? text_unavailable_reason=null);
    internal sealed record ContentValue(string data_type,string unit_type,JsonElement native_value,string? double_bits,
        int overrides,double rotation,double scale,bool auto_scale,
        [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]string? formula=null,
        [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]string? evaluation_unavailable_reason=null,
        string native_content_type="Value",
        [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]NativeTableFormulaFieldRead358? native_formula_field=null,
        [property:JsonIgnore]string? native_value_source=null);
    internal sealed record Snapshot(JsonElement Common,int Rows,int Columns,double[] RowHeights,double[] ColumnWidths,CellValue[] Cells,string Identity,string Token,
        ContentValue?[] Values,int[][] CellOverrides);
    internal sealed record ReadFormat(string alignment,double text_height,string text_style_handle,ColorValue content_color,int appearance_id);
    internal sealed record AppearanceValue(string cell_style,string data_format,bool background_none,ColorValue background_color,double[] margins,GridValue[] grids);
    internal sealed record ReadAppearance(int id,string cell_style,string data_format,bool background_none,ColorValue background_color,double[] margins,GridValue[] grids);
    internal sealed record ReadCell(int row,int column,string text,int content_count,int state,bool content_editable,bool format_editable,
        TableCellRangeSpec? merged_range,ReadFormat? format,string? data_type,string? unit_type,JsonElement? native_value,ReadContentLayout? content_layout,
        [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]string? formula=null,
        [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]string? evaluation_unavailable_reason=null,
        string native_content_type="Value",
        [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]NativeTableFormulaFieldRead358? native_formula_field=null,
        string? native_value_source=null,string? text_unavailable_reason=null);
    internal sealed record ReadContentLayout(double rotation,double scale,bool auto_scale);
    internal sealed record ReadValue(string table_token,string table_style_handle,ReadCell[] cells,ReadAppearance[] appearance_formats,TableFormulaWorkflowRead350? formula_workflows=null,TableTopologyWorkflow354? topology_workflows=null,object? layout_workflow=null);
    private const int SnapshotByteBudget=8*1024*1024;
    internal static void Need(bool value,string message,string code="UNSUPPORTED_TABLE_CONTENT")
    {if(!value)throw new DomainException(code,message);}
    private static double Number(double value)
    {Need(double.IsFinite(value)&&Math.Abs(value)<=1e9,"Native table contains nonfinite/excessive geometry.","INVALID_GEOMETRY");return value;}
    private static double[] Point(Point3d point)=>new[]{Number(point.X),Number(point.Y),Number(point.Z)};
    private static double[] Vector(Vector3d vector)=>new[]{Number(vector.X),Number(vector.Y),Number(vector.Z)};
    private static ColorValue Color(Teigha.Colors.Color color)=>new(color.IsByLayer?"by_layer":color.IsByBlock?"by_block":color.IsByAci?"aci":color.IsByColor?"true_color":color.ColorMethod.ToString(),
        color.IsByAci?(int?)color.ColorIndex:null,color.IsByColor?new[]{(int)color.Red,color.Green,color.Blue}:null);
    private static string Handle(ObjectId id)
    {Need(!id.IsNull&&id.IsValid&&!id.IsErased&&!id.IsEffectivelyErased,"Table style/text style identity is unavailable.");return Validation.NormalizeHandle(id.Handle.ToString());}
    private static string Literal(string text)
    {
        Need(text.Length<=4096&&!text.Any(char.IsControl)&&text.IndexOfAny(new[]{'\\','{','}'})<0&&!text.Contains("%<")&&!text.Contains("%%"),"Lifecycle editing supports plain literal text without formatting/field codes.");return text;
    }
    private static object[] RowDefaults(Table table,Action cancel)
    {
        var rows=new List<object>();
        foreach(var type in new[]{RowType.TitleRow,RowType.HeaderRow,RowType.DataRow})
        {
            cancel();using var color=table.ContentColor(type);using var background=table.BackgroundColor(type);var grids=new List<object>();
            foreach(var line in new[]{GridLineType.HorizontalTop,GridLineType.HorizontalInside,GridLineType.HorizontalBottom,GridLineType.VerticalLeft,GridLineType.VerticalInside,GridLineType.VerticalRight})
            {
                cancel();using var gridColor=table.GridColor(line,type);
                grids.Add(new{line=line.ToString(),color=Color(gridColor),lineweight=(int)table.GridLineWeight(line,type),visible=table.GridVisibility(line,type)});
            }
            rows.Add(new{row_type=type.ToString(),content_color=Color(color),background_none=table.IsBackgroundColorNone(type),background_color=Color(background),
                alignment=table.Alignment(type).ToString(),text_style_handle=table.TextStyle(type).IsNull?null:Handle(table.TextStyle(type)),text_height=Number(table.TextHeight(type)),
                // Actual350 receipts prove these legacy row-role type getters return unstable non-enum outputs.
                // Installed native void binding supplies no availability status. Do not invent inherited values.
                data_type=(string?)null,unit_type=(string?)null,type_observation="legacy_row_role_type_getters_unavailable; actual_per_cell_GetDataType_remains_observed",grids=grids.ToArray()});
        }
        return rows.ToArray();
    }
    internal static bool Contains(TableCellRangeSpec range,int row,int column)=>row>=range.TopRow&&row<=range.BottomRow&&column>=range.LeftColumn&&column<=range.RightColumn;
    internal static bool Anchor(CellValue cell)=>cell.merged_range is not {} range||range.TopRow==cell.row&&range.LeftColumn==cell.column;
    internal static TableCellRangeSpec? MergedRange(Table table,int row,int column,Action cancel)
    {
        cancel();var cell=table.Cells[row,column];var isMerged=cell.IsMerged;
        if(isMerged==false)return null;
        Need(isMerged==true,"Native cell merge state is unavailable.","OBJECT_READ_FAILED");
        // Installed hostdbmgd metadata exposes a normal zero-argument Cell
        // getter. Table.IsMergedCell has a required custom modifier on its
        // range argument and is not callable through the C# managed binding.
        var native=cell.GetMergeRange();
        if(native==null)throw new DomainException("OBJECT_READ_FAILED","Native merged range is unavailable.");
        var range=new TableCellRangeSpec(native.TopRow,native.LeftColumn,native.BottomRow,native.RightColumn);
        Need(range.TopRow>=0&&range.LeftColumn>=0&&range.BottomRow>=range.TopRow&&range.RightColumn>=range.LeftColumn&&
            range.BottomRow<table.Rows.Count&&range.RightColumn<table.Columns.Count&&Contains(range,row,column)&&
            (range.BottomRow>range.TopRow||range.RightColumn>range.LeftColumn),"Native merged range is inconsistent.","OBJECT_READ_FAILED");
        return range;
    }
    internal static Snapshot Capture(Table table,Action cancel,bool requireWorldXy=true,bool allowFormattedText=false,bool allowNumeric=false,bool allowFormulas=false)
    {
        cancel();Need(table.GetType()==typeof(Table),"Expected an exact native Table.","UNSUPPORTED_ENTITY_TYPE");
        Need(table.Rows.Count>0&&table.Columns.Count>0,"Native table has no usable rows or columns.");
        // Observe the same native getters in the same order for public reads
        // and token admission. Scope flags only validate the observed state.
        var position=table.Position;var normal=table.Normal;var direction=table.Direction;
        if(requireWorldXy)Need(Math.Abs(position.Z)<=1e-9&&normal.IsEqualTo(Vector3d.ZAxis)&&Math.Abs(direction.Z)<=1e-9,"Row/column, merge and literal text lifecycle currently supports native WCS XY/Z0 tables; cell formatting retains any native table plane.","OUT_OF_SCOPE");
        using var formulaFields=new CadTableFormulaField358.Reader(table,cancel);
        using(var links=table.GetDataLink())Need(links.Count==0,"Data-linked native tables require their owning adapter.");
        int rows=table.Rows.Count,columns=table.Columns.Count;
        // Resolve native frame/default getters before the override inventories.
        // Width/height were formerly read by TableGeometry after its token was
        // captured. Neither this observation nor the reader regenerates layout.
        _=Number(table.Width);_=Number(table.Height);
        var rowDefaults=RowDefaults(table,cancel);var rootProperties=EntityAdapter.StoredProperties(table);
        var heights=new double[rows];var widths=new double[columns];
        for(int i=0;i<rows;i++){cancel();heights[i]=Number(table.RowHeight(i));}
        for(int i=0;i<columns;i++){cancel();widths[i]=Number(table.ColumnWidth(i));}
#pragma warning disable CS0618
        bool titleSuppressed=table.IsTitleSuppressed,headerSuppressed=table.IsHeaderSuppressed;
#pragma warning restore CS0618
        string flowDirection=table.FlowDirection.ToString();
        double horizontalMargin=Number(table.HorizontalCellMargin),verticalMargin=Number(table.VerticalCellMargin);
        var cells=new List<CellValue>();var values=new List<ContentValue?>();var cellOverrides=new List<int[]>();long bytes=2048;
        foreach(int row in Enumerable.Range(0,rows))foreach(int column in Enumerable.Range(0,columns))
        {
            cancel();Need(!table.IsLinked(row,column),"Linked cells are outside plain table lifecycle editing.");
            var merged=MergedRange(table,row,column,cancel);
            int count=table.Cells[row,column].Contents.Count;
            bool anchor=merged==null||merged.TopRow==row&&merged.LeftColumn==column;
            Need(count is 0 or 1,"Multiple native cell contents require their content adapter.");
            string text="";string? textReason=null;FormatValue? format=null;ContentValue? value=null;
            if(count==1)
            {
                var contentType=table.GetContentTypes(row,column,0);var fieldId=table.GetFieldId(row,column,0);
                var content=table.Cells[row,column].Contents[0];string storedFormula=table.GetFormula(row,column,0);
                bool nativeFormula=content.HasFormula;bool hasFormula=allowFormulas&&nativeFormula;
                var nativeField=formulaFields.Read(fieldId,hasFormula,storedFormula,contentType,row,column);
                Need(hasFormula||string.IsNullOrEmpty(storedFormula),"Formula cell content is outside plain table lifecycle editing.");
                Need(!hasFormula||!string.IsNullOrWhiteSpace(storedFormula),"Native formula flag has no observable stored expression.","OBJECT_READ_FAILED");
                var type=table.GetDataType(row,column,0);
                bool literal=type.DataType==DataType.String&&type.UnitType==UnitType.Unitless;
                Need(hasFormula||literal||allowNumeric&&(type.DataType is DataType.Long or DataType.Double)&&TableValueEditingValidation336.UnitTypes.Contains(type.UnitType.ToString(),StringComparer.Ordinal),"Numeric or unsupported unit-bearing cell content is outside this table adapter.");
                cancel();object? raw=null;string? evaluationReason=null;var cache=fieldId.IsNull?null:formulaFields.Cache(fieldId);
                try
                {
                    raw=cache==null?table.GetValue(row,column,0):cache.Value;evaluationReason=cache?.ValueReason;
                    bool scalar=type.DataType==DataType.String&&raw is string||type.DataType==DataType.Long&&raw is int||type.DataType==DataType.Double&&raw is double number&&double.IsFinite(number);
                    if(!scalar&&hasFormula){raw=null;evaluationReason="Native formula has no finite scalar value matching its observed datatype.";}
                    else Need(scalar,"Native scalar value has an unavailable or inconsistent storage type.","OBJECT_READ_FAILED");
                }
                catch(OperationCanceledException){throw;}
                catch(System.Exception error) when(hasFormula&&error is not DomainException)
                {raw=null;evaluationReason="Native formula value getter unavailable: "+error.GetType().Name;}
                value=new(type.DataType.ToString(),type.UnitType.ToString(),JsonSerializer.SerializeToElement(raw,Wire.Json),raw is double real?BitConverter.DoubleToInt64Bits(real).ToString("X16"):null,
                    0,Number(content.Rotation),Number(content.Scale),content.IsAutoScale,hasFormula?storedFormula:null,evaluationReason,contentType.ToString(),nativeField,cache==null?null:CadTableFieldCache365.Source);
                var stored=cache==null?table.GetTextString(row,column,0):cache.Text;textReason=cache?.TextReason;
                // Native appearance setters do not replace text. Retain its
                // exact stored string, including existing CAD format codes;
                // literal lifecycle still validates its original narrower scope.
                text=allowFormattedText||!literal?stored:Literal(stored);
            }
            // Covered cells can have no content or formatting of their own.
            // Read their range and hidden literal contents; do not substitute an anchor's format.
            if(anchor)
            {
                // Empty native cells have real inherited cell formatting. The
                // indexless API reads it without inventing content index zero.
                using var contentColor=count==0?table.ContentColor(row,column):table.GetContentColor(row,column,0);using var background=table.BackgroundColor(row,column);
                var grids=new List<GridValue>();
                foreach(var line in new[]{GridLineType.HorizontalTop,GridLineType.HorizontalInside,GridLineType.HorizontalBottom,GridLineType.VerticalLeft,GridLineType.VerticalInside,GridLineType.VerticalRight})
                {
                    cancel();using var color=table.GetGridColor(row,column,line);var linetype=table.GetGridLinetype(row,column,line);
                    grids.Add(new(line.ToString(),table.GetGridVisibility(row,column,line).ToString(),(int)table.GetGridLineWeight(row,column,line),
                        linetype.IsNull?null:Handle(linetype),table.GetGridLineStyle(row,column,line).ToString(),Number(table.GetGridDoubleLineSpacing(row,column,line)),(int)table.GetOverrides(row,column,line),Color(color)));
                }
                format=new(table.GetCellStyle(row,column),count==0?table.GetDataFormat(row,column):table.GetDataFormat(row,column,0),table.Alignment(row,column).ToString(),Number(count==0?table.TextHeight(row,column):table.GetTextHeight(row,column,0)),Handle(count==0?table.TextStyle(row,column):table.GetTextStyleId(row,column,0)),
                    Color(contentColor),table.IsBackgroundColorNone(row,column),Color(background),
                    new[]{CellMargins.Top,CellMargins.Left,CellMargins.Bottom,CellMargins.Right}.Select(m=>Number(table.GetMargin(row,column,m))).ToArray(),grids.ToArray());
            }
            cancel();var overrides=Array.Empty<int>();
            var cell=new CellValue(row,column,text,count,(int)table.GetCellState(row,column),table.IsContentEditable(row,column),table.IsFormatEditable(row,column),merged,format,textReason);
            bytes+=JsonSerializer.SerializeToUtf8Bytes(new{cell,value,overrides},Wire.Json).Length;
            Need(bytes<=SnapshotByteBudget,"Stored table snapshot exceeds the actual8MiB memory budget; no cell-count truncation occurred.","RESPONSE_LIMIT_EXCEEDED");
            cells.Add(cell);values.Add(value);cellOverrides.Add(overrides);
        }
        formulaFields.ValidateRoot();
        // Capture complete native inheritance inventories only after all
        // content/format/protection getters above. No flag is masked or removed.
        for(int index=0;index<cells.Count;index++)
        {
            cancel();var cell=cells[index];
            if(values[index] is {} value)values[index]=value with{overrides=(int)table.GetOverrides(cell.row,cell.column,0)};
            cellOverrides[index]=table.CellStyleOverrides(cell.row,cell.column).Select(item=>(int)item).OrderBy(item=>item).ToArray();
        }
        // Installed getter IL is native-backed despite CS0618; retain these
        // exact suppression flags (basic-337-table-style.md compatibility note).
#pragma warning disable CS0618
        var common=Wire.Element(new{table_style_handle=Handle(table.TableStyle),position=Point(position),direction=Vector(direction),normal=Vector(normal),
            properties=rootProperties,table_overrides=table.TableStyleOverrides().Select(item=>(int)item).OrderBy(item=>item).ToArray(),
            IsTitleSuppressed=titleSuppressed,IsHeaderSuppressed=headerSuppressed,flow_direction=flowDirection,horizontal_margin=horizontalMargin,vertical_margin=verticalMargin,row_defaults=rowDefaults});
#pragma warning restore CS0618
        var state=Wire.Element(new{common,rows,columns,row_heights=heights,column_widths=widths,cells=cells.ToArray(),native_contents=values.ToArray(),cell_overrides=cellOverrides.ToArray()});
        var serialized=JsonSerializer.SerializeToUtf8Bytes(state,Wire.Json);
        Need(serialized.Length<=SnapshotByteBudget,"Stored table snapshot exceeds the actual8MiB memory budget.","RESPONSE_LIMIT_EXCEEDED");
        // Resident identity is included in the externally published token. Preview
        // clones compare their stored state but never claim the resident token.
        string identity=table.ObjectId.IsNull?"detached":table.Handle.ToString()+":"+table.OwnerId.Handle.ToString();
        string token=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{identity,state},Wire.Json)));
        return new(common,rows,columns,heights,widths,cells.ToArray(),identity,token,values.ToArray(),cellOverrides.ToArray());
    }
    internal static (object? Editing,string? Reason) TryRead(Table table,Action cancel)
    {
        try
        {
            var snapshot=Capture(table,cancel,requireWorldXy:false,allowFormattedText:true,allowNumeric:true,allowFormulas:true);
            // Repeated native style/grid values must not multiply a normal
            // engineering table beyond the actual transport-byte budget.
            // Keep the existing four format fields inline and expose lossless
            // shared appearance records, referenced by their response-local ID.
            var appearances=new List<ReadAppearance>();var ids=new Dictionary<string,int>(StringComparer.Ordinal);var cells=new List<ReadCell>();
            foreach(var cell in snapshot.Cells)
            {
                cancel();ReadFormat? read=null;
                if(cell.format is {} format)
                {
                    var appearance=new AppearanceValue(format.cell_style,format.data_format,format.background_none,format.background_color,format.margins,format.grids);
                    var key=JsonSerializer.Serialize(appearance,Wire.Json);
                    if(!ids.TryGetValue(key,out int id))
                    {
                        id=appearances.Count;ids.Add(key,id);
                        appearances.Add(new(id,appearance.cell_style,appearance.data_format,appearance.background_none,appearance.background_color,appearance.margins,appearance.grids));
                    }
                    read=new(format.alignment,format.text_height,format.text_style_handle,format.content_color,id);
                }
                var data=snapshot.Values[checked(cell.row*snapshot.Columns+cell.column)];
                cells.Add(new(cell.row,cell.column,cell.text,cell.content_count,cell.state,cell.content_editable,cell.format_editable,cell.merged_range,read,data?.data_type,data?.unit_type,data?.native_value,
                    data==null?null:new ReadContentLayout(data.rotation,data.scale,data.auto_scale),data?.formula,data?.evaluation_unavailable_reason,data?.native_content_type??"none",data?.native_formula_field,data?.native_value_source,cell.text_unavailable_reason));
            }
            var value=new ReadValue(snapshot.Token,snapshot.Common.GetProperty("table_style_handle").GetString()!,cells.ToArray(),appearances.ToArray(),TableFormulaRangeValidation350.Describe(),TableTopologyValidation354.Describe(),CadTableNativeLayout356.Describe());
            Need(JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json).Length<=Wire.PayloadBudget-16384,"Compact table editing metadata exceeds the actual response-byte budget.","RESPONSE_LIMIT_EXCEEDED");
            CadTableSnapshotDiagnostics346.Observe(table,snapshot,cancel);
            return(value,null);
        }
        catch(OperationCanceledException){throw;}
        catch(DomainException error){return(null,error.Error.code+": "+error.Error.message);}
        catch(System.Exception error) when(error is not CadTableFormulaField358.CleanupFailure){return(null,"Stored native table editing metadata unavailable: "+error.GetType().Name);}
    }
}
