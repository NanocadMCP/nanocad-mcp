using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using static NanoCADBridge.CadTableContentsReader364;

namespace NanoCADBridge;

// One resident indexed operation. Fields and block slots may be shifted by
// ordinary scalar insertion/movement. Native block insertion/attribute edit
// uses an existing retained static definition. Explicit delete removes a proved
// Field binding or block slot; other dependent slots remain exact.
internal static class CadTableContentsEditor364
{
    sealed record Expected(int Origin,Slot? Prior,TableContentAction364? Value=null,string? Format=null,double? Rotation=null,double? Scale=null,int Mask=0,double? Height=null,string? Font=null,ColorValue? Color=null,TableContentAction364? Block=null,JsonElement? Attributes=null);
    sealed record Plan(Expected[][] Slots,int[][] Origins,HashSet<int> FormattedCells,Dictionary<int,string> Layouts,Dictionary<int,TableCellFormatChanges> Appearance,CadTableFieldDeletion379.Selection? FieldDeletion,CadTableBlockContents384.Selection? Blocks,Dictionary<int,CadTableBlockContents384.Definition> BlockActions,Dictionary<int,Dictionary<string,string>> BlockValues);
    static bool Exact(object? a,object? b)=>JsonSerializer.Serialize(a,Wire.Json)==JsonSerializer.Serialize(b,Wire.Json);
    static void Fail(string message,object? details=null)=>throw new DomainException("NATIVE_TABLE_MISMATCH",message,false,details);
    static object Scalar(TableContentAction364 action)=>action.DataType switch{"String"=>action.Value!.Value.GetString()!,"Long"=>action.Value!.Value.GetInt32(),_=>action.Value!.Value.GetDouble()};
    static string CoverageText(Expected expected)=>expected.Value is {} value?value.DataType=="String"?value.Value!.Value.GetString()!:Convert.ToString(Scalar(value),System.Globalization.CultureInfo.InvariantCulture)!:expected.Prior?.stored_text??"";
    static Plan Prepare(Table table,Snapshot before,TableContentsEditing364 edit,Transaction tr,Action cancel)
    {
        Need(before.Token==edit.ContentsToken,"Native contents_token changed; inspect current indexed Table contents.","STALE_TABLE_STATE");
        var initial=before.Contents.Select(slots=>Enumerable.Range(0,slots.Length).ToArray()).ToArray();
        var final=TableContentSequence364.Plan(initial,before.Columns,edit.Actions,cancel);
        CadTableBlockContents384.Selection? blocks=null;var blockActions=new Dictionary<int,CadTableBlockContents384.Definition>();var blockValues=new Dictionary<int,Dictionary<string,string>>();
        var cells=before.Contents.Select(slots=>slots.Select(slot=>new Expected(slot.content_index,slot)).ToList()).ToArray();var selectedFields=new HashSet<string>(StringComparer.Ordinal);var formatted=new HashSet<int>();var layouts=new Dictionary<int,string>();var appearance=new Dictionary<int,TableCellFormatChanges>();
        for(int ordinal=0;ordinal<edit.Actions.Length;ordinal++)
        {
            cancel();var action=edit.Actions[ordinal];int position=checked(action.Row*before.Columns+action.Column);var cell=before.Cells[position];var slots=cells[position];
            Need(Anchor(cell),"Select a merged cell by its top-left anchor.","MERGED_CELL_COVERED");
            Need(action.Action is "set_format" or "set_layout" or "set_cell_format"?cell.format_editable:cell.content_editable,"Native cell content/format protection forbids this action.","CELL_PROTECTED");
            if(action.Action=="set_layout")
            {layouts[position]=action.ContentLayout!;continue;}
            if(action.Action=="set_cell_format")
            {appearance[position]=CadTableContentsAppearance365.Merge(appearance.GetValueOrDefault(position),action.CellFormat!);continue;}
            if(action.Action is "set_value" or "insert_value")Need(cell.format_editable,"Typed value edits also set an explicit native scalar format.","CELL_PROTECTED");
            if(action.Action=="insert_value")
            {slots.Insert(action.ContentIndex,new(checked(-1-ordinal),null,action,action.DataFormat,Mask:3));formatted.Add(position);continue;}
            if(action.Action=="insert_block")
            {
                blocks??=CadTableBlockContents384.Begin(table,before,tr,cancel);
                var source=CadTableBlockContents384.Remember(blocks,action.BlockHandle!,table.Database!,tr,cancel);
                blockActions.Add(ordinal,source);
                var attributes=action.BlockAttributes?.ToDictionary(value=>value.AttributeDefinitionHandle,value=>value.Value,StringComparer.Ordinal)??
                    source.Attributes.Values.Where(value=>!value.Constant).ToDictionary(value=>value.Handle,value=>TableEditingValidation.Literal(Wire.Element(value.Text),"native_attribute_default"),StringComparer.Ordinal);
                Need(attributes.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(source.Attributes.Values.Where(value=>!value.Constant).Select(value=>value.Handle)),
                    "Block insertion requires every actual nonconstant attribute definition exactly once; constant values remain unchanged.","UNSUPPORTED_TABLE_BLOCK_CONTENT");
                foreach(var attribute in attributes){cancel();CadTableBlockContents384.Literal(source,attribute.Key,attribute.Value,tr);}
                blockValues.Add(ordinal,attributes);
                slots.Insert(action.ContentIndex,new(checked(-1-ordinal),null,Block:action,Attributes:CadTableBlockContents384.Attributes(source,attributes)));continue;
            }
            var prior=slots[action.ContentIndex];
            string? block=prior.Block?.BlockHandle??prior.Prior?.block_handle;
            bool blockDelete=action.Action=="delete"&&block!=null;
            if(blockDelete||action.Action=="set_block_attribute")
            {
                Need(block!=null,"Select a current native Block slot for a block attribute edit.","UNSUPPORTED_TABLE_BLOCK_CONTENT");
                blocks??=CadTableBlockContents384.Begin(table,before,tr,cancel);
                var source=CadTableBlockContents384.Remember(blocks,block!,table.Database!,tr,cancel);
                if(action.Action=="set_block_attribute")
                {
                    blockActions.Add(ordinal,source);
                    var current=prior.Attributes??prior.Prior!.block_attributes!.Value;
                    slots[action.ContentIndex]=prior with{Attributes=CadTableBlockContents384.Change(current,source,action.AttributeDefinitionHandle!,action.AttributeValue!,tr)};continue;
                }
            }
            bool fieldDelete=action.Action=="delete"&&prior.Prior is {field_handle:not null,block_handle:null};
            if(fieldDelete)selectedFields.Add(prior.Prior!.field_handle!);
            else if(!blockDelete)Need(prior.Block==null&&(prior.Prior==null||Ordinary(prior.Prior)),"Selected scalar action requires an ordinary native value. Use set_block_attribute for literal block attributes; explicit delete selects an eligible native Field or static block slot. Field/block movement and Field assignment need their owning adapter.","UNSUPPORTED_TABLE_CONTENT");
            if(action.Action=="delete")slots.RemoveAt(action.ContentIndex);
            else if(action.Action=="move")
            {slots.RemoveAt(action.ContentIndex);slots.Insert(action.DestinationIndex!.Value,prior);}
            else if(action.Action=="set_value")
            {slots[action.ContentIndex]=prior with{Value=action,Format=action.DataFormat,Mask=prior.Mask|3};formatted.Add(position);}
            else
            {
                // Setting Scale while automatic scaling is enabled cannot be
                // promised as a retained explicit scale. No implicit override.
                Need(action.Scale==null||prior.Prior is {auto_scale:false},"Explicit scale editing requires an existing nonautomatic scalar slot.","OUT_OF_SCOPE");
                slots[action.ContentIndex]=prior with{Format=action.DataFormat??prior.Format,Rotation=action.Rotation??prior.Rotation,Scale=action.Scale??prior.Scale,
                    Mask=prior.Mask|(action.DataFormat!=null?2:0)|(action.Rotation!=null?4:0)|(action.Scale!=null?8:0)|(action.TextHeight!=null?128:0)|(action.TextStyleHandle!=null?64:0)|(action.ColorAci!=null||action.ColorRgb!=null?32:0),
                    Height=action.TextHeight??prior.Height,Font=action.TextStyleHandle??prior.Font,Color=action.ColorAci!=null||action.ColorRgb!=null?CadTableContentsAppearance365.ExpectedColor(action.ColorAci,action.ColorRgb):prior.Color};
                if(action.DataFormat!=null)formatted.Add(position);
            }
        }
        Need(cells.Select((slots,i)=>slots.Select(slot=>slot.Origin).SequenceEqual(final[i])).All(value=>value),"Managed content origin plan is inconsistent.","INVALID_OPERATION_ORDER");
        CadTableContentsAppearance365.ValidateBorders(before,appearance,cancel);
        var deletion=selectedFields.Count==0?null:CadTableFieldDeletion379.Prepare(table,before,selectedFields,tr,cancel);
        return new(cells.Select(slots=>slots.ToArray()).ToArray(),final,formatted,layouts,appearance,deletion,blocks,blockActions,blockValues);
    }
    internal static void CheckSource(Table table,TableContentsEditing364 edit,Database db,Transaction tr,Action cancel)
    {var before=Capture(table,tr,cancel);var plan=Prepare(table,before,edit,tr,cancel);Source(table,before,plan,db,tr,cancel);}
    static void Source(Table table,Snapshot before,Plan plan,Database db,Transaction tr,Action cancel)
    {
        Need(!table.ObjectId.IsNull&&table.Database?.UnmanagedObject==db.UnmanagedObject,"Indexed editing requires the exact resident local Table.","OUT_OF_SCOPE");
        using(var reactors=table.GetPersistentReactorIds())Need(reactors.Count==0,"Persistent Table dependencies require their owning adapter.","OUT_OF_SCOPE");
        if(plan.Blocks is {} blocks)CadTableBlockContents384.Verify(blocks,table,before,db,tr,cancel);
        for(int cell=0;cell<plan.Slots.Length;cell++)foreach(var expected in plan.Slots[cell])
        {
            cancel();string style=expected.Font??expected.Prior?.text_style_handle??before.Cells[cell].format!.text_style_handle;
            if(expected.Font!=null)CadTableContentsAppearance365.Coverage(style,CoverageText(expected),db,tr,cancel);
            if(expected.Value is {DataType:"String"} value)FontCoverage.Require(tr,CadStructureWriter.Find(db,style),value.Value!.Value.GetString()!);
        }
    }
    internal static string[] Apply(Table table,TableContentsEditing364 edit,Database db,Transaction tr,Action cancel)
    {
        var before=Capture(table,tr,cancel);var plan=Prepare(table,before,edit,tr,cancel);Source(table,before,plan,db,tr,cancel);var warnings=new List<string>();
        // The existing font engine checks the final visible scalar strings,
        // including moved/inserted slots, before any setter. Field/block fonts
        // remain untouched and are never evaluated for coverage.
        for(int cell=0;cell<plan.Slots.Length;cell++)foreach(var expected in plan.Slots[cell])
        {
            cancel();if(expected.Font==null)continue;
            string raw=CoverageText(expected);
            warnings.AddRange(CadTableContentsAppearance365.Coverage(expected.Font,raw,db,tr,cancel));
            if(expected.Value is {DataType:"Long" or "Double"})warnings.Add("TABLE_NUMERIC_DISPLAY_COVERAGE_UNVERIFIED: Preflight checks native raw numeric digits; native formatted glyphs are checked from actual post-edit scalar display and require rendered review.");
        }
        var id=table.ObjectId;var owner=table.OwnerId;
        for(int ordinal=0;ordinal<edit.Actions.Length;ordinal++)
        {
            var action=edit.Actions[ordinal];cancel();int row=action.Row,column=action.Column,index=action.ContentIndex;
            if(action.Action=="set_layout")
            {table.SetContentLayout(row,column,Enum.Parse<CellContentLayout>(action.ContentLayout!));continue;}
            if(action.Action=="set_cell_format")
            {CadTableContentsAppearance365.Apply(table,row,column,action.CellFormat!,db,tr,cancel);continue;}
            if(action.Action is "insert_value" or "insert_block")
            {int actual=table.CreateContent(row,column,index);Need(actual==index,"Native CreateContent returned a different slot index.","NATIVE_TABLE_MISMATCH");}
            if(action.Action=="insert_block")
            {
                var source=plan.BlockActions[ordinal];
                table.SetBlockTableRecordId(row,column,index,source.Id,action.AutoFit!.Value);cancel();
                foreach(var attribute in plan.BlockValues[ordinal])
                {cancel();table.SetBlockAttributeValue(row,column,index,source.Attributes[attribute.Key].Id,attribute.Value);}
            }
            else if(action.Action=="set_block_attribute")
            {
                var source=plan.BlockActions[ordinal];
                Need(table.GetContentTypes(row,column,index)==CellContentTypes.Block&&table.GetBlockTableRecordId(row,column,index)==source.Id,"Current indexed block binding differs from the prepared native attribute source.","NATIVE_TABLE_MISMATCH");
                table.SetBlockAttributeValue(row,column,index,source.Attributes[action.AttributeDefinitionHandle!].Id,action.AttributeValue!);
            }
            else if(action.Action is "set_value" or "insert_value")
            {
                table.SetValue(row,column,index,Scalar(action));cancel();
                table.SetDataType(row,column,index,new DataTypeParameter(Enum.Parse<DataType>(action.DataType!),Enum.Parse<UnitType>(action.UnitType!)));cancel();
                table.SetDataFormat(row,column,index,action.DataFormat!);
            }
            else if(action.Action=="delete")table.DeleteContent(row,column,index);
            else if(action.Action=="move")table.MoveContent(row,column,index,action.DestinationIndex!.Value);
            else
            {
                if(action.DataFormat!=null){table.SetDataFormat(row,column,index,action.DataFormat);cancel();}
                if(action.Rotation!=null){table.SetRotation(row,column,index,action.Rotation.Value);cancel();}
                if(action.Scale!=null){table.SetScale(row,column,index,action.Scale.Value);cancel();}
                if(action.TextHeight!=null){table.SetTextHeight(row,column,index,action.TextHeight.Value);cancel();}
                if(action.TextStyleHandle!=null){table.SetTextStyleId(row,column,index,CadTableContentsAppearance365.Font(action.TextStyleHandle,db,tr,cancel));cancel();}
                if(action.ColorAci!=null||action.ColorRgb!=null){using var color=CadTableContentsAppearance365.NativeColor(action.ColorAci,action.ColorRgb);table.SetContentColor(row,column,index,color);}
            }
        }
        // Capture new slot defaults before explicit layout regeneration. Existing
        // slots are still checked against the original snapshot and origin plan.
        var stored=Capture(table,tr,cancel);VerifyContents(before,stored,plan,null,cancel,warnings);
        cancel();table.GenerateLayout();cancel();table.RecomputeTableBlock(true);cancel();
        var after=Capture(table,tr,cancel);
        for(int cell=0;cell<plan.Slots.Length;cell++)for(int index=0;index<plan.Slots[cell].Length;index++)
        {
            cancel();if(plan.Slots[cell][index].Value is not {DataType:"String"} value)continue;
            FontCoverage.Require(tr,CadStructureWriter.Find(db,after.Contents[cell][index].text_style_handle),value.Value!.Value.GetString()!);
        }
        for(int cell=0;cell<plan.Slots.Length;cell++)for(int index=0;index<plan.Slots[cell].Length;index++)
        {cancel();if(plan.Slots[cell][index].Font!=null)warnings.AddRange(CadTableContentsAppearance365.Coverage(after.Contents[cell][index].text_style_handle,after.Contents[cell][index].stored_text,db,tr,cancel));}
        Need(table.ObjectId==id&&table.OwnerId==owner,"Indexed contents editing changed Table identity or owning space.","NATIVE_TABLE_MISMATCH");
        VerifyContents(before,after,plan,stored,cancel,warnings);
        if(plan.Blocks is {} blocks)
        {CadTableBlockContents384.Verify(blocks,table,after,db,tr,cancel);warnings.Add("TABLE_BLOCK_CONTENT_SCOPE: Native indexed block bindings and literal attribute values were edited; existing definition geometry/members/metadata and all other slots remain retained. autoFit is the native assignment input; current scale/automatic scaling are observed, not calculated. Native generated cell-block display may reflow inside the exact exclusive Table layout. No source definition, Field or foreign object was erased or rewritten; auxiliary cleanup and Undo are not guaranteed.");}
        if(plan.FieldDeletion is {} deletion)
        {
            CadTableFieldDeletion379.VerifyRetirement(table,deletion,tr,cancel);
            CadTableFieldDeletionMetadata379.Verify(before,after,plan.Origins,deletion,cancel);
            warnings.Add("TABLE_FIELD_BINDING_DELETED: Native DeleteContent removed "+deletion.Fields.Count+" selected unique Field binding(s). "+
                (deletion.Retained.Count==0?"Only proved exclusively owned selected storage/carriers may retire; unused surviving selected storage remains observed.":"Selected nonretirable storage, actual child definitions and typed persistent dependencies were retained exactly.")+" Every other slot/Field/data remains guarded. No Field erase, purge, reuse or property-evaluator freshness is claimed.");
            warnings.Add("TABLE_FIELD_AUXILIARY_CLEANUP_UNPROVED: This operation removes native cell content, not private Field storage. Native cleanup/Undo is not guaranteed; a started failure may leave partial changes. Inspect current bindings and retained data before another edit; unknown never means rollback.");
        }
        else CadTableContentsMetadata364.Verify(before,after,plan.Origins,cancel);
        if(before.RowHeights.Zip(after.RowHeights).Any(pair=>!TableSizeVerification.Equal(pair.First,pair.Second))||
            before.ColumnWidths.Zip(after.ColumnWidths).Any(pair=>!TableSizeVerification.Equal(pair.First,pair.Second)))warnings.Add(CadTableNativeLayout356.Warning);
        if(after.Contents.SelectMany(slots=>slots).Any(slot=>slot.field_handle!=null))warnings.Add("TABLE_RETAINED_FIELD_OBSERVATION: Actual indexed native Field bindings, readable definitions, owning graph and unselected content are checked. Private child/evaluator state is not a complete inventory; no Field was created, edited or evaluated directly.");
        return warnings.Distinct(StringComparer.Ordinal).ToArray();
    }
    static Slot Evaluation(Slot prior,Slot actual)
    {
        if(!prior.has_formula&&prior.field_handle==null)return prior;
        return prior with{stored_text=actual.stored_text,native_value=actual.native_value,double_bits=actual.double_bits,evaluation_unavailable_reason=actual.evaluation_unavailable_reason,
            scale=prior.auto_scale&&actual.auto_scale?actual.scale:prior.scale};
    }
    static void VerifyContents(Snapshot before,Snapshot after,Plan plan,Snapshot? stored,Action cancel,List<string> warnings)
    {
        if(before.Identity!=after.Identity||before.Rows!=after.Rows||before.Columns!=after.Columns||!Exact(before.Common,after.Common))Fail("Indexed editing changed Table topology, placement, style or root appearance.");
        for(int cell=0;cell<before.Cells.Length;cell++)
        {
            cancel();var oldCell=before.Cells[cell];var actualCell=after.Cells[cell];var expectedCell=oldCell with{content_count=plan.Slots[cell].Length};
            if(plan.Layouts.TryGetValue(cell,out var layout))expectedCell=expectedCell with{content_layout=layout};
            // The indexed data-format setters can materialize the same cell's
            // data-format override; every other cell/default/grid stays exact.
            if(plan.FormattedCells.Contains(cell)&&expectedCell.format is {} format&&actualCell.format is {} current)
                expectedCell=expectedCell with{format=format with{data_format=current.data_format}};
            if(expectedCell.format is {} priorFormat&&actualCell.format is {} currentFormat)
            {
                // Indexless legacy font/color/height getters can mirror content0.
                // Only the exactly verified first slot (or unchanged prior cell
                // default) is admissible when its origin/order/property changed.
                var first=plan.Slots[cell].FirstOrDefault();
                bool moved=first!=null&&(first.Origin!=0||before.Contents[cell].Length==0);
                var visible=after.Contents[cell].FirstOrDefault();
                if(visible!=null&&first!=null)
                {
                    if((moved||first.Font!=null)&&currentFormat.text_style_handle==visible.text_style_handle)priorFormat=priorFormat with{text_style_handle=currentFormat.text_style_handle};
                    if((moved||first.Height!=null)&&TableSizeVerification.Equal(currentFormat.text_height,visible.text_height))priorFormat=priorFormat with{text_height=currentFormat.text_height};
                    if((moved||first.Color!=null)&&Exact(currentFormat.content_color,visible.content_color))priorFormat=priorFormat with{content_color=currentFormat.content_color};
                }
                expectedCell=expectedCell with{format=CadTableContentsAppearance365.VerifyFormat(before,oldCell,priorFormat,currentFormat,plan.Appearance,cancel)};
            }
            if(!Exact(expectedCell,actualCell))Fail("Indexed editing changed merge/protection or unrequested cell/grid appearance.",new{oldCell.row,oldCell.column});
            var oldOverrides=before.CellOverrides[cell];var newOverrides=after.CellOverrides[cell];
            if(plan.FormattedCells.Contains(cell)){oldOverrides=oldOverrides.Where(value=>value!=148).ToArray();newOverrides=newOverrides.Where(value=>value!=148).ToArray();}
            var allowed=CadTableContentsAppearance365.CellOverrideIds(before,oldCell,plan.Appearance,cancel);
            foreach(var selected in plan.Slots[cell]){if(selected.Height!=null)allowed.Add(135);if(selected.Font!=null)allowed.Add(134);if(selected.Color!=null)allowed.Add(133);}
            oldOverrides=oldOverrides.Where(value=>!allowed.Contains(value)).ToArray();newOverrides=newOverrides.Where(value=>!allowed.Contains(value)).ToArray();
            if(!Exact(oldOverrides,newOverrides))Fail("Indexed editing changed unrelated cell override inventory.",new{oldCell.row,oldCell.column});
            if(after.Contents[cell].Length!=plan.Slots[cell].Length)Fail("Indexed editing produced an incorrect native slot count.",new{oldCell.row,oldCell.column});
            for(int index=0;index<plan.Slots[cell].Length;index++)
            {
                cancel();var expected=plan.Slots[cell][index];var actual=after.Contents[cell][index];
                var basis=expected.Prior??stored?.Contents[cell][index];
                if(expected.Value is {} value)
                {
                    if(!Ordinary(actual)||actual.data_type!=value.DataType||actual.unit_type!=value.UnitType||
                        !Exact(actual.native_value,Wire.Element(Scalar(value)))||value.DataType=="Double"&&actual.double_bits!=BitConverter.DoubleToInt64Bits(value.Value!.Value.GetDouble()).ToString("X16"))Fail("Indexed native scalar storage/type/unit differs from the requested value.",new{oldCell.row,oldCell.column,content_index=index});
                }
                if(expected.Block is {} block)
                {
                    if(actual.native_content_type!="Block"||actual.block_handle!=block.BlockHandle||actual.field_handle!=null||actual.has_formula||actual.formula!=""||actual.native_value.ValueKind!=JsonValueKind.Null)
                        Fail("Native block assignment did not retain the requested existing definition binding.",new{oldCell.row,oldCell.column,content_index=index,block_handle=block.BlockHandle});
                }
                if(expected.Attributes is {} attributes&&!Exact(actual.block_attributes,attributes))
                    Fail("Native cell-block attributes differ from the requested literal values or retained definitions/tags/constants.",new{oldCell.row,oldCell.column,content_index=index});
                string? requestedFormat=expected.Format;
                if(requestedFormat!=null&&requestedFormat.Length!=0&&actual.data_format!=requestedFormat)Fail("Explicit indexed data format was not retained.",new{oldCell.row,oldCell.column,content_index=index});
                if(requestedFormat==""&&actual.data_format!="")warnings.Add("TABLE_NATIVE_DEFAULT_FORMAT_RESOLVED: Empty requested indexed format resolved to a nonempty native default; current stored format is readable.");
                if(expected.Rotation!=null&&Math.Abs(Math.IEEERemainder(actual.rotation-expected.Rotation.Value,2*Math.PI))>1e-9)Fail("Requested indexed rotation was not retained.");
                if(expected.Scale!=null&&!TableSizeVerification.Equal(actual.scale,expected.Scale.Value))Fail("Requested indexed scale was not retained.");
                if(expected.Height!=null&&!TableSizeVerification.Equal(actual.text_height,expected.Height.Value))Fail("Requested indexed text height was not retained.");
                if(expected.Font!=null&&actual.text_style_handle!=expected.Font)Fail("Requested indexed font identity was not retained.");
                if(expected.Color!=null&&!Exact(actual.content_color,expected.Color))Fail("Requested indexed content color was not retained.");
                if(basis==null)continue; // New defaults were not invented before CreateContent.
                var wanted=Evaluation(basis,actual) with{content_index=index};
                if(expected.Attributes!=null)
                {
                    wanted=wanted with{block_attributes=expected.Attributes,stored_text=actual.stored_text};
                    if(wanted.auto_scale&&actual.auto_scale)
                    {
                        if(!TableSizeVerification.Equal(wanted.scale,actual.scale))warnings.Add("TABLE_BLOCK_AUTOFIT_REFLOW: Native automatic block scaling changed after literal attribute/layout regeneration; current scale is observed. Review symbol fit and attribute readability.");
                        wanted=wanted with{scale=actual.scale};
                    }
                }
                if(expected.Block is {AutoFit:true})
                {
                    if(!TableSizeVerification.Equal(wanted.scale,actual.scale)||wanted.auto_scale!=actual.auto_scale)
                        warnings.Add("TABLE_NATIVE_BLOCK_FIT_OBSERVED: Requested native autoFit resolved during layout; current block scale/automatic scaling are observed without an assumed numerical fit ratio.");
                    wanted=wanted with{scale=actual.scale,auto_scale=actual.auto_scale};
                }
                if(expected.Value!=null)wanted=wanted with{data_type=actual.data_type,unit_type=actual.unit_type,native_value=actual.native_value,double_bits=actual.double_bits,stored_text=actual.stored_text};
                if(requestedFormat!=null)wanted=wanted with{data_format=actual.data_format,stored_text=actual.stored_text};
                if(expected.Rotation!=null)wanted=wanted with{rotation=actual.rotation};
                if(expected.Scale!=null)wanted=wanted with{scale=actual.scale};
                if(expected.Height!=null)wanted=wanted with{text_height=actual.text_height};
                if(expected.Font!=null)wanted=wanted with{text_style_handle=actual.text_style_handle};
                if(expected.Color!=null)wanted=wanted with{content_color=actual.content_color};
                if((expected.Height!=null||expected.Font!=null)&&wanted.auto_scale&&actual.auto_scale)wanted=wanted with{scale=actual.scale};
                wanted=wanted with{overrides=actual.overrides};
                int mask=expected.Mask|(plan.Layouts.ContainsKey(cell)?16384:0)|CadTableContentsAppearance365.ContentMask(plan.Appearance.GetValueOrDefault(cell)); // installed CellProperties.ContentLayout
                if(expected.Block is {AutoFit:true})mask|=8|256; // installed Scale/AutoScale; new native autoFit assignment only
                if(!Exact(wanted,actual)||(basis.overrides&~mask)!=(actual.overrides&~mask))Fail("Indexed editing changed an unselected slot, binding, readable Field/block data, layout or unrelated content override.",new{oldCell.row,oldCell.column,content_index=index,original_content_index=expected.Origin});
            }
        }
    }
}
