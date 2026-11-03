using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// Read stored native values only. Never GenerateLayout/RecomputeTableBlock from a read.
internal static class CadTableContentsReader364
{
    internal sealed record ColorValue(string method,int? aci,int[]? rgb);
    internal sealed record GridValue(string line,string visibility,int lineweight,string? linetype_handle,string line_style,double double_spacing,int overrides,ColorValue color);
    internal sealed record FormatValue(string cell_style,string data_format,string alignment,double text_height,string text_style_handle,
        ColorValue content_color,bool background_none,ColorValue background_color,double[] margins,GridValue[] grids);
    internal sealed record CellValue(int row,int column,string text,int content_count,int state,bool content_editable,bool format_editable,
        TableCellRangeSpec? merged_range,FormatValue? format,string content_layout);
    internal sealed record ContentValue(string data_type,string unit_type,JsonElement native_value,string? double_bits,
        int overrides,double rotation,double scale,bool auto_scale,
        [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]string? formula=null,
        [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]string? evaluation_unavailable_reason=null,
        string native_content_type="Value",
        [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]NativeTableFormulaFieldRead358? native_formula_field=null);
    internal sealed record Snapshot(JsonElement Common,int Rows,int Columns,double[] RowHeights,double[] ColumnWidths,CellValue[] Cells,string Identity,string Token,
        Slot[][] Contents,int[][] CellOverrides,JsonElement Metadata);
    internal sealed record ReadFormat(string alignment,double text_height,string text_style_handle,ColorValue content_color,int appearance_id);
    internal sealed record AppearanceValue(string cell_style,string data_format,bool background_none,ColorValue background_color,double[] margins,GridValue[] grids);
    internal sealed record ReadAppearance(int id,string cell_style,string data_format,bool background_none,ColorValue background_color,double[] margins,GridValue[] grids);
    internal sealed record ReadCell(int row,int column,string text,int content_count,int state,bool content_editable,bool format_editable,
        TableCellRangeSpec? merged_range,ReadFormat? format,string? data_type,string? unit_type,JsonElement? native_value,ReadContentLayout? content_layout,
        [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]string? formula=null,
        [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]string? evaluation_unavailable_reason=null,
        string native_content_type="Value",
        [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]NativeTableFormulaFieldRead358? native_formula_field=null);
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
    internal sealed record Slot(int content_index,string native_content_type,string stored_text,
        string data_type,string unit_type,JsonElement native_value,string? double_bits,
        string data_format,double rotation,double scale,bool auto_scale,int overrides,
        double text_height,string text_style_handle,ColorValue content_color,
        bool has_formula,string formula,string? field_handle,string? block_handle,
        JsonElement? field_definition,JsonElement? block_attributes,string? evaluation_unavailable_reason,
        string? native_value_source=null,[property:JsonIgnore]string? stored_text_unavailable_reason=null);
    static string? Reference(ObjectId id)=>id.IsNull?null:Handle(id);
    static bool Exact(object? a,object? b)=>JsonSerializer.Serialize(a,Wire.Json)==JsonSerializer.Serialize(b,Wire.Json);
    internal static bool Ordinary(Slot slot)=>slot.native_content_type=="Value"&&slot.field_handle==null&&slot.block_handle==null&&!slot.has_formula&&string.IsNullOrEmpty(slot.formula)&&
        TableValueEditingValidation336.DataTypes.Contains(slot.data_type,StringComparer.Ordinal)&&TableValueEditingValidation336.UnitTypes.Contains(slot.unit_type,StringComparer.Ordinal);
    internal static JsonElement Definition(Field field)=>Wire.Element(new{handle=Handle(field.ObjectId),owner_handle=Handle(field.OwnerId),runtime_type=field.GetType().FullName,
        field_code=field.GetFieldCode(),object_reference_code=field.GetFieldCode(FieldCodeFlags.FieldCode|FieldCodeFlags.ObjectReference|FieldCodeFlags.PreserveFields),
        evaluator_id=field.EvaluatorId,format=field.Format,evaluation_options=field.EvaluationOption.ToString(),filing_options=field.FilingOption.ToString(),
        is_text_field=field.IsTextField,has_fields=field.HasFields,private_state_complete=false,
        scope="retained_actual_indexed_native_Field_binding_and_readable_definition; private_children_and_evaluator_data_unobserved; no_Field_write_or_evaluation"});
    static JsonElement Attributes(Table table,int row,int column,int index,ObjectId block,Transaction tr,Action cancel)
    {
        var value=tr.GetObject(block,OpenMode.ForRead);
        Need(value.GetType()==typeof(BlockTableRecord)&&value.Database?.UnmanagedObject==table.Database?.UnmanagedObject&&value.OwnerId==table.Database!.BlockTableId,
            "A retained block content binding needs an actual local block-table member.","OUT_OF_SCOPE");
        var record=(BlockTableRecord)value;
        Need(!record.IsFromExternalReference&&!record.IsLayout,"External or layout block cell contents need their owning adapter.","OUT_OF_SCOPE");
        var values=new List<object>();var budget=new ModelInventoryBudget(cancel);
        foreach(ObjectId id in record)
        {
            cancel();var member=tr.GetObject(id,OpenMode.ForRead);
            Need(member.Database?.UnmanagedObject==table.Database?.UnmanagedObject&&member.OwnerId==block,"Block definition member ownership is inconsistent.","OBJECT_READ_FAILED");
            if(member is not AttributeDefinition attribute)continue;
            Need(attribute.GetType()==typeof(AttributeDefinition),"Derived block attribute definition needs its owning adapter.","OUT_OF_SCOPE");
            var item=new{definition_handle=Handle(id),owner_handle=Handle(block),tag=attribute.Tag,constant=attribute.Constant,
                value=attribute.Constant?attribute.TextString:table.Cells[row,column].Contents[index].GetBlockAttributeValue(id)};
            budget.Add(item);values.Add(item);
        }
        return Wire.Element(values);
    }
    internal static Snapshot Capture(Table table,Transaction tr,Action cancel)
    {
        cancel();Need(table.GetType()==typeof(Table),"Expected exact native Table.","UNSUPPORTED_ENTITY_TYPE");
        int rows=table.Rows.Count,columns=table.Columns.Count;
        Need(rows>0&&columns>0,"Native Table has no usable topology.");
        using(var links=table.GetDataLink())Need(links.Count==0,"Data-linked native Tables require their owning adapter.","OUT_OF_SCOPE");
        var position=table.Position;var normal=table.Normal;var direction=table.Direction;
        _=Number(table.Width);_=Number(table.Height);var defaults=RowDefaults(table,cancel);var properties=EntityAdapter.StoredProperties(table);
        var heights=new double[rows];var widths=new double[columns];
        for(int i=0;i<rows;i++){cancel();heights[i]=Number(table.RowHeight(i));}
        for(int i=0;i<columns;i++){cancel();widths[i]=Number(table.ColumnWidth(i));}
#pragma warning disable CS0618
        bool title=table.IsTitleSuppressed,header=table.IsHeaderSuppressed;
#pragma warning restore CS0618
        string flow=table.FlowDirection.ToString();double horizontal=Number(table.HorizontalCellMargin),vertical=Number(table.VerticalCellMargin);
        var cells=new List<CellValue>();var contents=new List<Slot[]>();var overrides=new List<int[]>();
        var fields=new Dictionary<ObjectId,JsonElement>();var budget=new ModelInventoryBudget(cancel);
        for(int row=0;row<rows;row++)for(int column=0;column<columns;column++)
        {
            cancel();Need(!table.IsLinked(row,column),"Linked cell contents require their owning adapter.","OUT_OF_SCOPE");
            var merged=MergedRange(table,row,column,cancel);bool anchor=merged==null||merged.TopRow==row&&merged.LeftColumn==column;
            int count=table.Cells[row,column].Contents.Count;Need(count>=0,"Native content count is negative.","OBJECT_READ_FAILED");
            var slots=new List<Slot>();
            for(int index=0;index<count;index++)
            {
                cancel();var native=table.Cells[row,column].Contents[index];var kind=table.GetContentTypes(row,column,index);
                Need(kind is CellContentTypes.Value or CellContentTypes.Field or CellContentTypes.Block,"Combined/unknown native cell content needs its adapter.","OUT_OF_SCOPE");
                // The native content discriminator chooses the applicable
                // binding API; a Block is not observed as a scalar/Field.
                var field=kind==CellContentTypes.Block?ObjectId.Null:table.GetFieldId(row,column,index);
                var block=kind==CellContentTypes.Block?table.GetBlockTableRecordId(row,column,index):ObjectId.Null;
                Need(kind!=CellContentTypes.Field||!field.IsNull,"Native Field slot has no actual FieldId.","OBJECT_READ_FAILED");
                Need(kind!=CellContentTypes.Block||!block.IsNull,"Native block slot has no actual block identity.","OBJECT_READ_FAILED");
                Need(kind==CellContentTypes.Block?field.IsNull: block.IsNull,"Native content type disagrees with its Field/block bindings.","OBJECT_READ_FAILED");
                JsonElement? definition=null,attributes=null;
                if(!field.IsNull)
                {
                    if(!fields.TryGetValue(field,out var found))
                    {
                        var value=tr.GetObject(field,OpenMode.ForRead);
                        Need(value.GetType()==typeof(Field)&&value.Database?.UnmanagedObject==table.Database?.UnmanagedObject&&!value.OwnerId.IsNull,
                            "Retained Field is not an actual local exact Field.","OUT_OF_SCOPE");
                        found=Definition((Field)value);budget.Add(found);fields.Add(field,found);
                    }
                    definition=found;
                }
                if(!block.IsNull)attributes=Attributes(table,row,column,index,block,tr,cancel);
                var type=table.GetDataType(row,column,index);bool formula=kind!=CellContentTypes.Block&&native.HasFormula;string expression=kind==CellContentTypes.Block?"":table.GetFormula(row,column,index);
                Need(formula? !string.IsNullOrWhiteSpace(expression):string.IsNullOrEmpty(expression),"Native formula flag and stored expression disagree.","OBJECT_READ_FAILED");
                object? raw=null;string? unavailable=null;var cache=field.IsNull?null:CadTableFieldCache365.Read(table,field,tr,cancel);
                if(kind==CellContentTypes.Block)unavailable="Block content has retained actual binding and attribute values; no scalar value is substituted.";
                else
                {
                    try{raw=cache==null?table.GetValue(row,column,index):cache.Value;unavailable=cache?.ValueReason;NativeOwnedMetadata.Value(raw);}
                    catch(OperationCanceledException){throw;}
                    catch(Exception error)when((formula||!field.IsNull)&&error is not DomainException)
                    {raw=null;unavailable="Retained Field/formula scalar cache unavailable: "+error.GetType().Name;}
                }
                bool ordinary=kind==CellContentTypes.Value&&field.IsNull&&!formula;
                if(ordinary)Need(type.DataType==DataType.String&&type.UnitType==UnitType.Unitless&&raw is string||type.DataType==DataType.Long&&raw is int||type.DataType==DataType.Double&&raw is double real&&double.IsFinite(real),
                    "Ordinary indexed values currently support native String/Unitless, Long/Double scalars.","OUT_OF_SCOPE");
                using var color=table.GetContentColor(row,column,index);
                var observed=raw is string or int or bool||raw is double finite&&double.IsFinite(finite)?raw:NativeOwnedMetadata.Value(raw);
                var slot=new Slot(index,kind.ToString(),cache==null?table.GetTextString(row,column,index):cache.Text,type.DataType.ToString(),type.UnitType.ToString(),Wire.Element(observed!),
                    raw is double scalar?BitConverter.DoubleToInt64Bits(scalar).ToString("X16"):null,table.GetDataFormat(row,column,index),Number(native.Rotation),Number(native.Scale),native.IsAutoScale,0,
                    Number(table.GetTextHeight(row,column,index)),Handle(table.GetTextStyleId(row,column,index)),Color(color),formula,expression,Reference(field),Reference(block),definition,attributes,unavailable,cache==null?null:CadTableFieldCache365.Source,cache?.TextReason);
                budget.Add(slot);slots.Add(slot);
            }
            FormatValue? format=null;
            if(anchor)
            {
                using var foreground=table.ContentColor(row,column);using var background=table.BackgroundColor(row,column);
                var grids=new List<GridValue>();
                foreach(var line in new[]{GridLineType.HorizontalTop,GridLineType.HorizontalInside,GridLineType.HorizontalBottom,GridLineType.VerticalLeft,GridLineType.VerticalInside,GridLineType.VerticalRight})
                {
                    cancel();using var color=table.GetGridColor(row,column,line);var lineType=table.GetGridLinetype(row,column,line);
                    grids.Add(new(line.ToString(),table.GetGridVisibility(row,column,line).ToString(),(int)table.GetGridLineWeight(row,column,line),Reference(lineType),table.GetGridLineStyle(row,column,line).ToString(),Number(table.GetGridDoubleLineSpacing(row,column,line)),(int)table.GetOverrides(row,column,line),Color(color)));
                }
                // Per-content format is in slots. This record retains the real cell defaults,
                // including empty cells, rather than substituting the first content's format.
                format=new(table.GetCellStyle(row,column),table.GetDataFormat(row,column),table.Alignment(row,column).ToString(),Number(table.TextHeight(row,column)),Handle(table.TextStyle(row,column)),Color(foreground),
                    table.IsBackgroundColorNone(row,column),Color(background),new[]{CellMargins.Top,CellMargins.Left,CellMargins.Bottom,CellMargins.Right}.Select(m=>Number(table.GetMargin(row,column,m))).ToArray(),grids.ToArray());
            }
            var cell=new CellValue(row,column,"",count,(int)table.GetCellState(row,column),table.IsContentEditable(row,column),table.IsFormatEditable(row,column),merged,format,table.GetContentLayout(row,column).ToString());
            budget.Add(cell);cells.Add(cell);contents.Add(slots.ToArray());overrides.Add(Array.Empty<int>());
        }
        // Read complete override inventories after effective content/format getters.
        for(int i=0;i<cells.Count;i++)
        {
            cancel();var cell=cells[i];
            for(int index=0;index<contents[i].Length;index++)contents[i][index]=contents[i][index] with{overrides=(int)table.GetOverrides(cell.row,cell.column,index)};
            overrides[i]=table.CellStyleOverrides(cell.row,cell.column).Select(value=>(int)value).OrderBy(value=>value).ToArray();
        }
        CadTableFieldDeletion379.ObserveUnbound(table,tr,fields,cancel);
        Need(fields.Count>0||!table.HasFields,"Field-bearing Table without actual indexed cell bindings requires its owning adapter.","OUT_OF_SCOPE");
        var common=Wire.Element(new{table_style_handle=Handle(table.TableStyle),position=Point(position),direction=Vector(direction),normal=Vector(normal),properties,
            table_overrides=table.TableStyleOverrides().Select(value=>(int)value).OrderBy(value=>value).ToArray(),IsTitleSuppressed=title,IsHeaderSuppressed=header,
            flow_direction=flow,horizontal_margin=horizontal,vertical_margin=vertical,row_defaults=defaults});
        using var xdata=table.XData;using var reactors=table.GetPersistentReactorIds();
        var metadata=Wire.Element(new{handle=Handle(table.ObjectId),owner=Handle(table.OwnerId),extension=Reference(table.ExtensionDictionary),
            xdata=xdata==null?null:NativeOwnedMetadata.Buffer(xdata,cancel,value=>budget.Add(value)),reactors=reactors.Cast<ObjectId>().Select(Reference).OrderBy(value=>value,StringComparer.Ordinal).ToArray(),
            owned=CadTableContentsOwnership364.Capture(table,tr,cancel,fields)});
        var state=Wire.Element(new{common,rows,columns,row_heights=heights,column_widths=widths,cells,contents,cell_overrides=overrides,metadata});
        var bytes=JsonSerializer.SerializeToUtf8Bytes(state,Wire.Json);Need(bytes.Length<=SnapshotByteBudget,"Complete indexed Table snapshot exceeds actual8MiB serialized memory budget; no content-count truncation occurred.","RESPONSE_LIMIT_EXCEEDED");
        string identity=Handle(table.ObjectId)+":"+Handle(table.OwnerId),token=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{identity,state},Wire.Json)));
        return new(common,rows,columns,heights,widths,cells.ToArray(),identity,token,contents.ToArray(),overrides.ToArray(),metadata);
    }
    internal static (object? Editing,string? Reason) TryRead(Table table,Action cancel)
    {
        Transaction? tr=null;
        try
        {
            tr=table.Database.TransactionManager.StartTransaction();var value=Capture(table,tr,cancel);
            var roles=new string[value.Rows];
            for(int row=0;row<roles.Length;row++){cancel();roles[row]=table.RowType(row).ToString();}
            var result=new{contents_token=value.Token,table_style_handle=value.Common.GetProperty("table_style_handle").GetString(),row_roles=roles,
                scope="actual_indexed_native_Table_contents; actual_Field_binding_and_owned_storage; selected_Field_delete_and_static_block_content_availability; no_link_refresh",
                cells=value.Cells.Select((cell,i)=>new{cell.row,cell.column,cell.content_count,cell.content_layout,cell.content_editable,cell.format_editable,cell.merged_range,cell.format,
                    contents=value.Contents[i].Select(slot=>new{slot.content_index,slot.native_content_type,slot.stored_text,slot.data_type,slot.unit_type,slot.native_value,slot.double_bits,
                        slot.data_format,slot.rotation,slot.scale,slot.auto_scale,slot.overrides,slot.text_height,slot.text_style_handle,slot.content_color,slot.has_formula,slot.formula,slot.field_handle,slot.block_handle,
                        slot.field_definition,slot.block_attributes,slot.evaluation_unavailable_reason,slot.native_value_source,slot.stored_text_unavailable_reason,can_edit_scalar=Ordinary(slot),
                        field_delete=CadTableFieldDeletion379.Read(table,tr,value,cell,slot,cancel),
                        block_editing=CadTableBlockContents384.Read(table,value,cell,slot,tr,cancel)}).ToArray()}).ToArray(),
                actions=new[]{"set_value","set_format","set_layout","set_cell_format","insert_value","insert_block","set_block_attribute","move","delete"},indices="zero_based; actions_in_order; destination_is_final_index_in_same_cell; layout_and_cell_format_are_cell_scoped",
                preservation="all_retained_slots; exact_raw_storage_and_bindings; native_display_cache_may_recalculate; private_Field_state_not_claimed_complete"};
            Need(JsonSerializer.SerializeToUtf8Bytes(result,Wire.Json).Length<=Wire.PayloadBudget-16384,"Indexed contents editing data exceeds actual response-byte budget; no partial editing token is published.","RESPONSE_LIMIT_EXCEEDED");
            return(result,null);
        }
        catch(OperationCanceledException){throw;}
        catch(DomainException error){return(null,error.Error.code+": "+error.Error.message);}
        catch(Exception error){return(null,"Native indexed contents editing data unavailable: "+error.GetType().Name);}
        // A cleanup failure occurs outside observation catches and still throws.
        finally{tr?.Dispose();}
    }
}
