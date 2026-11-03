using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Index=NanoCADBridge.CadTableContentsReader364;

namespace NanoCADBridge;

// Existing indexed storage/read-only owner adapter; no scalar setter, Field
// evaluator, indexless replacement or reconstruction is used for style edits.
internal static class CadTableStyleIndexedConsumers367
{
    internal sealed record Consumer(ObjectId Id,Index.Snapshot Before,string[] RowRoles);
    static void Need([DoesNotReturnIf(false)] bool good,string reason,string code="NATIVE_TABLE_STYLE_MISMATCH")
    {if(!good)throw new DomainException(code,reason);}
    static bool Same(object? a,object? b)=>JsonSerializer.Serialize(a,Wire.Json)==JsonSerializer.Serialize(b,Wire.Json);
    internal static bool HasMultiple(Table table,Action cancel)
    {
        for(int row=0;row<table.Rows.Count;row++)for(int column=0;column<table.Columns.Count;column++)
        {cancel();if(table.Cells[row,column].Contents.Count>1)return true;}
        return false;
    }
    internal static Consumer Capture(Table table,Transaction tr,Action cancel)
    {
        Need(!table.HasFields,"Multiple-content style inheritance requires ordinary scalar slots; Table Fields need their owning propagation adapter.","UNSUPPORTED_TABLE_STYLE_CONSUMER");
        // Check discriminators/bindings before scalar/value or Field reads.
        for(int row=0;row<table.Rows.Count;row++)for(int column=0;column<table.Columns.Count;column++)
        for(int index=0;index<table.Cells[row,column].Contents.Count;index++)
        {
            cancel();Need(table.GetContentTypes(row,column,index)==CellContentTypes.Value&&table.GetFieldId(row,column,index).IsNull&&
                !table.Cells[row,column].Contents[index].HasFormula,
                "Multiple-content TableStyle consumers require unlinked ordinary scalar content; Field/block/formula slots retain their existing propagation barrier.","UNSUPPORTED_TABLE_STYLE_CONSUMER");
        }
        var before=Index.Capture(table,tr,cancel);
        Need(before.Contents.SelectMany(slots=>slots).All(Index.Ordinary),"A multiple-content consumer has an unsupported native scalar datatype/unit/binding.","UNSUPPORTED_TABLE_STYLE_CONSUMER");
        var item=tr.GetObject(table.TableStyle,OpenMode.ForRead);
        Need(item.GetType()==typeof(TableStyle)&&item is TableStyle style&&style.OwnerId==table.Database!.TableStyleDictionaryId,
            "Indexed consumer style must remain an exact current local native style.","UNSUPPORTED_TABLE_STYLE_CONSUMER");
        var defaults=CadTableStyleWriter337.Properties((TableStyle)item,cancel);
        var roles=CadTableStyleTextInheritance373.RowRoles(table,cancel);
        foreach(var cell in before.Cells)
        {cancel();if(cell.format!=null)_=CadTableStyleTextInheritance373.Resolve(defaults,roles[cell.row],cell.format.cell_style,CadTableStyleTextInheritance373.Handle(table.ObjectId),cell.row,cell.column,"indexed_capture_defaults",cancel);}
        return new(table.ObjectId,before,roles);
    }
    internal static void VerifyCurrent(Consumer before,Table table,Transaction tr,Action cancel)
    {
        var actual=Capture(table,tr,cancel);
        Need(before.Id==actual.Id&&before.Before.Token==actual.Before.Token&&Same(before.RowRoles,actual.RowRoles),
            "An indexed Table consumer changed since the complete style preflight.","TABLE_STATE_CHANGED");
    }
    static T Inherited<T>(T prior,T actual,T oldDefault,T newDefault,bool enabled,bool explicitValue,string reason)
    {
        bool allowed=explicitValue||!enabled||Same(oldDefault,newDefault)||!Same(prior,oldDefault)?Same(prior,actual):Same(actual,prior)||Same(actual,newDefault);
        if(!allowed)throw new DomainException("NATIVE_TABLE_STYLE_MISMATCH",reason,false,
            new{prior,actual,old_default=oldDefault,new_default=newDefault,selected=enabled,explicit_value=explicitValue,
                predicate="explicit_unselected_or_unproved_inheritance_retains_prior; proved_selected_default_accepts_prior_or_actual_requested_default"});
        return actual;
    }
    internal static string[] CheckFonts(Consumer consumer,CadTableStyleWriter337.Stored beforeStyle,CadTableStyleWriter337.Stored afterStyle,
        TableStyleProperties337 edit,Database db,Transaction tr,Action cancel,Index.Snapshot? after=null)
    {
        if(edit.Rows?.Any(row=>row.TextStyleHandle!=null)!=true)return Array.Empty<string>();
        var warnings=new List<string>();var snapshot=after??consumer.Before;
        foreach(var cell in snapshot.Cells)
        {
            cancel();if(!Index.Anchor(cell)||cell.format==null)continue;
            int offset=checked(cell.row*snapshot.Columns+cell.column);var prior=consumer.Before.Cells[offset];
            string roleName=consumer.RowRoles[cell.row];string handle=CadTableStyleTextInheritance373.Handle(consumer.Id);
            var old=CadTableStyleTextInheritance373.Resolve(beforeStyle,roleName,prior.format!.cell_style,handle,cell.row,cell.column,"indexed_before_defaults",cancel);
            var current=CadTableStyleTextInheritance373.Resolve(afterStyle,roleName,prior.format.cell_style,handle,cell.row,cell.column,"indexed_requested_defaults",cancel);
            var flags=consumer.Before.CellOverrides[offset];
            var global=consumer.Before.Common.GetProperty("table_overrides").EnumerateArray().Select(value=>value.GetInt32()).ToHashSet();
            int role=roleName=="TitleRow"?0:roleName=="HeaderRow"?1:2;
            var table=(Table)tr.GetObject(consumer.Id,OpenMode.ForRead);
            foreach(var slot in snapshot.Contents[offset])
            {
                cancel();var oldSlot=consumer.Before.Contents[offset][slot.content_index];
                bool explicitFont=(oldSlot.overrides&64)!=0||flags.Contains(134)||global.Contains(18+role);
                string effective=CadTableStyleTextInheritance373.EffectiveFont(table,cell.row,cell.column,slot.content_index,db,tr,cancel);
                if(effective!=oldSlot.text_style_handle)warnings.AddRange(CadTableContentsAppearance365.Coverage(effective,slot.stored_text,db,tr,cancel));
                if(after==null&&!explicitFont&&oldSlot.text_style_handle==old.Font&&current.Font is {} candidate&&candidate!=effective&&candidate!=oldSlot.text_style_handle)
                    warnings.AddRange(CadTableContentsAppearance365.Coverage(candidate,oldSlot.stored_text,db,tr,cancel));
            }
        }
        return warnings.Distinct(StringComparer.Ordinal).ToArray();
    }
    internal static string[] Verify(Consumer consumer,Index.Snapshot after,CadTableStyleWriter337.Stored beforeStyle,
        CadTableStyleWriter337.Stored afterStyle,TableStyleProperties337 edit,Action cancel)
        =>VerifyCore(consumer,after,beforeStyle,afterStyle,edit,cancel,false);
    internal static string[] VerifySingle(Consumer consumer,Index.Snapshot after,CadTableStyleWriter337.Stored beforeStyle,
        CadTableStyleWriter337.Stored afterStyle,TableStyleProperties337 edit,Action cancel)
        =>VerifyCore(consumer,after,beforeStyle,afterStyle,edit,cancel,true);
    static string[] VerifyCore(Consumer consumer,Index.Snapshot after,CadTableStyleWriter337.Stored beforeStyle,
        CadTableStyleWriter337.Stored afterStyle,TableStyleProperties337 edit,Action cancel,bool single)
    {
        var before=consumer.Before;var warnings=new List<string>();
        string handle=CadTableStyleTextInheritance373.Handle(consumer.Id);int observedRow=-1,observedColumn=-1,slotIndex=-1;
        void Equal(object? expected,object? actual,string reason,string phase)
        {
            if(Same(expected,actual))return;
            throw CadTableStyleConsumerPreservation375.Diagnostics(new DomainException("NATIVE_TABLE_STYLE_MISMATCH",reason),handle,phase,
                new{row=observedRow,column=observedColumn,content_index=slotIndex,state=expected},new{row=observedRow,column=observedColumn,content_index=slotIndex,state=actual},cancel);
        }
        try
        {
        Need(before.Identity==after.Identity&&before.Rows==after.Rows&&before.Columns==after.Columns&&before.Cells.Length==after.Cells.Length,
            "Indexed style inheritance changed Table identity or topology.");
        var common=before.Common.EnumerateObject().ToDictionary(value=>value.Name,value=>value.Value.Clone(),StringComparer.Ordinal);
        var global=before.Common.GetProperty("table_overrides").EnumerateArray().Select(value=>value.GetInt32()).ToHashSet();
        // Style identity, source/root properties, suppression, flow and masks
        // remain exact; this operation only has margin and role setters.
        foreach(var field in new[]{("horizontal_margin",4,edit.HorizontalMargin,beforeStyle.horizontal_margin,afterStyle.horizontal_margin),
            ("vertical_margin",5,edit.VerticalMargin,beforeStyle.vertical_margin,afterStyle.vertical_margin)})
        {
            cancel();double old=common[field.Item1].GetDouble(),actual=after.Common.GetProperty(field.Item1).GetDouble();
            Inherited(old,actual,field.Item4,field.Item5,field.Item3!=null,global.Contains(field.Item2),"Indexed inherited margin or explicit table margin changed incorrectly.");
            common[field.Item1]=after.Common.GetProperty(field.Item1).Clone();
        }
        var rowDefaults=new List<JsonElement>();int roleIndex=0;
        foreach(var oldRow in before.Common.GetProperty("row_defaults").EnumerateArray())
        {
            cancel();var actual=after.Common.GetProperty("row_defaults")[roleIndex];var oldDefault=beforeStyle.rows[roleIndex];var wanted=afterStyle.rows[roleIndex];
            var selected=edit.Rows?.SingleOrDefault(row=>row.RowType==oldDefault.row_type);
            var row=oldRow.EnumerateObject().ToDictionary(value=>value.Name,value=>value.Value.Clone(),StringComparer.Ordinal);
            void Field(string name,object old,object next,bool enabled,int mask)
            {
                var a=oldRow.GetProperty(name);var b=actual.GetProperty(name);
                Inherited(a,b,Wire.Element(old),Wire.Element(next),enabled,global.Contains(mask+roleIndex),"Indexed row-default changed an explicit/unrequested property: "+name);
                row[name]=b.Clone();
            }
            Field("text_height",oldDefault.text_height,wanted.text_height,selected?.TextHeight!=null,21);
            Field("text_style_handle",oldDefault.text_style_handle!,wanted.text_style_handle!,selected?.TextStyleHandle!=null,18);
            Field("alignment",oldDefault.alignment,wanted.alignment,selected?.Alignment!=null,15);
            Field("content_color",oldDefault.content_color,wanted.content_color,selected?.ColorAci!=null||selected?.ColorRgb!=null,6);
            Field("background_none",oldDefault.background_none,wanted.background_none,selected?.Background!=null,9);
            // A disabled fill's stored color is retained; no arbitrary color
            // normalization merely because it is invisible is admitted.
            Field("background_color",oldDefault.background_color,wanted.background_color,selected?.Background is {None:false},12);
            var grids=new List<JsonElement>();int lineIndex=0;
            foreach(var grid in oldRow.GetProperty("grids").EnumerateArray())
            {
                var b=actual.GetProperty("grids")[lineIndex];var oldGrid=oldDefault.grids[lineIndex];var newGrid=wanted.grids[lineIndex];
                string line=grid.GetProperty("line").GetString()!;var choice=selected?.Grids?.SingleOrDefault(value=>value.Line==line);
                var fields=grid.EnumerateObject().ToDictionary(value=>value.Name,value=>value.Value.Clone(),StringComparer.Ordinal);
                foreach(var item in new[]{("color",choice?.ColorAci!=null||choice?.ColorRgb!=null,40),("lineweight",choice?.Lineweight!=null,70),("visible",choice?.Visible!=null,100)})
                {
                    bool own=global.Contains(item.Item3+roleIndex*6+lineIndex);
                    Inherited(grid.GetProperty(item.Item1),b.GetProperty(item.Item1),oldGrid.GetProperty(item.Item1),newGrid.GetProperty(item.Item1),item.Item2,own,"Indexed row grid changed an explicit/unrequested property.");
                    fields[item.Item1]=b.GetProperty(item.Item1).Clone();
                }
                Need(Same(Wire.Element(fields),b),"Indexed row grid changed an unrelated property.");grids.Add(Wire.Element(fields));lineIndex++;
            }
            row["grids"]=Wire.Element(grids);Need(Same(Wire.Element(row),actual),"Indexed row defaults changed stored type/unit or unrelated fields.");rowDefaults.Add(Wire.Element(row));roleIndex++;
        }
        common["row_defaults"]=Wire.Element(rowDefaults);
        Equal(Wire.Element(common),after.Common,"Style inheritance changed source/header/root/layout/override state.","consumer_common_defaults");
        for(int offset=0;offset<before.Cells.Length;offset++)
        {
            cancel();var cell=before.Cells[offset];var actual=after.Cells[offset];observedRow=cell.row;observedColumn=cell.column;slotIndex=-1;
            Need(Same(cell with{format=actual.format},actual)&&Same(before.CellOverrides[offset],after.CellOverrides[offset]),
                "Indexed style inheritance changed content count/order/layout, merge/protection or cell override inventory.");
            Need(before.Contents[offset].Length==after.Contents[offset].Length,"Native style changed indexed slot count.");
            var role=beforeStyle.rows.Single(value=>value.row_type==consumer.RowRoles[cell.row]);var nextRole=afterStyle.rows.Single(value=>value.row_type==role.row_type);
            var choice=edit.Rows?.SingleOrDefault(value=>value.RowType==role.row_type);var flags=before.CellOverrides[offset].ToHashSet();int r=role.row_type=="TitleRow"?0:role.row_type=="HeaderRow"?1:2;
            bool roleAppearance=cell.format?.cell_style.Length==0;
            var oldText=cell.format==null?null:CadTableStyleTextInheritance373.Resolve(beforeStyle,role.row_type,cell.format.cell_style,CadTableStyleTextInheritance373.Handle(consumer.Id),cell.row,cell.column,"indexed_before_defaults",cancel);
            var nextText=cell.format==null?null:CadTableStyleTextInheritance373.Resolve(afterStyle,role.row_type,cell.format.cell_style,CadTableStyleTextInheritance373.Handle(consumer.Id),cell.row,cell.column,"indexed_after_defaults",cancel);
            for(int index=0;index<before.Contents[offset].Length;index++)
            {
                cancel();var old=before.Contents[offset][index];var current=after.Contents[offset][index];slotIndex=index;
                bool retainedFormula=single&&old.has_formula&&current.has_formula&&old.formula==current.formula&&old.field_handle==current.field_handle;
                Need((Index.Ordinary(old)&&Index.Ordinary(current)||retainedFormula)&&old.content_index==current.content_index&&old.overrides==current.overrides,
                    "Native style changed scalar/formula binding/index/override bits.");
                string? oldFont=oldText?.Font??role.text_style_handle,nextFont=nextText?.Font??nextRole.text_style_handle;
                bool fontSelected=edit.Rows?.Any(value=>value.TextStyleHandle!=null)==true&&oldFont!=nextFont;
                string font=Inherited(old.text_style_handle,current.text_style_handle,oldFont,nextFont,fontSelected,(old.overrides&64)!=0||flags.Contains(134)||global.Contains(18+r),"Indexed explicit/unrequested font identity changed.")!;
                double oldHeight=oldText?.Height??role.text_height,nextHeight=nextText?.Height??nextRole.text_height;
                double height=Inherited(old.text_height,current.text_height,oldHeight,nextHeight,edit.Rows?.Any(value=>value.TextHeight!=null)==true&&oldHeight!=nextHeight,
                    (old.overrides&128)!=0||flags.Contains(135)||global.Contains(21+r),"Indexed explicit/unrequested text height changed.");
                var color=Inherited(old.content_color,current.content_color,WireColor(role.content_color),WireColor(nextRole.content_color),roleAppearance&&(choice?.ColorAci!=null||choice?.ColorRgb!=null),
                    (old.overrides&32)!=0||flags.Contains(133)||global.Contains(6+r),"Indexed explicit/unrequested content color changed.");
                var expected=old with{text_style_handle=font,text_height=height,content_color=color};
                if(retainedFormula)expected=expected with{native_value=current.native_value,double_bits=current.double_bits,stored_text=current.stored_text,evaluation_unavailable_reason=current.evaluation_unavailable_reason};
                else if((old.data_type is "Long" or "Double")&&old.stored_text!=current.stored_text)
                {expected=expected with{stored_text=current.stored_text};warnings.Add("TABLE_NUMERIC_DISPLAY_REFLOWED: Native numeric display text is observed separately; exact raw value/type/unit/bits/dataformat and bindings remain checked.");}
                if(old.auto_scale&&current.auto_scale&&(old.overrides&8)==0&&old.scale!=current.scale)
                {expected=expected with{scale=current.scale};warnings.Add("TABLE_NATIVE_AUTO_SCALE_REFLOWED: Native style layout reflowed unoverridden automatic scale; manual scale/auto mode/rotation/override bits and exact stored inputs remain checked.");}
                Equal(expected,current,"Native style changed raw values/types/units/bits/formats, rotation/manual scale, bindings or unselected presentation.","consumer_native_content");
            }
            slotIndex=-1;
            if(cell.format is {} format&&actual.format is {} changed)
            {
                // Indexless effective text getters follow content0; an explicit
                // override on content1 must not manufacture an override on0.
                // Actual cell-wide IDs still protect the shared default.
                int bits=before.Contents[offset].FirstOrDefault()?.overrides??0;
                string? priorFont=oldText?.Font??role.text_style_handle,nextFont=nextText?.Font??nextRole.text_style_handle;
                double priorHeight=oldText?.Height??role.text_height,nextHeight=nextText?.Height??nextRole.text_height;
                var expected=format with{
                    text_style_handle=Inherited(format.text_style_handle,changed.text_style_handle,priorFont,nextFont,edit.Rows?.Any(value=>value.TextStyleHandle!=null)==true&&priorFont!=nextFont,
                        (bits&64)!=0||flags.Contains(134)||global.Contains(18+r),"Explicit/unrequested cell font changed.")!,
                    text_height=Inherited(format.text_height,changed.text_height,priorHeight,nextHeight,edit.Rows?.Any(value=>value.TextHeight!=null)==true&&priorHeight!=nextHeight,
                        (bits&128)!=0||flags.Contains(135)||global.Contains(21+r),"Explicit/unrequested cell height changed."),
                    alignment=Inherited(format.alignment,changed.alignment,role.alignment,nextRole.alignment,roleAppearance&&choice?.Alignment!=null,(bits&16)!=0||flags.Contains(130)||global.Contains(15+r),"Explicit/unrequested cell alignment changed."),
                    content_color=Inherited(format.content_color,changed.content_color,WireColor(role.content_color),WireColor(nextRole.content_color),roleAppearance&&(choice?.ColorAci!=null||choice?.ColorRgb!=null),(bits&32)!=0||flags.Contains(133)||global.Contains(6+r),"Explicit/unrequested cell color changed."),
                    background_none=Inherited(format.background_none,changed.background_none,role.background_none,nextRole.background_none,roleAppearance&&choice?.Background!=null,(bits&512)!=0||flags.Contains(131)||global.Contains(9+r),"Explicit/unrequested fill state changed."),
                    background_color=Inherited(format.background_color,changed.background_color,WireColor(role.background_color),WireColor(nextRole.background_color),roleAppearance&&choice?.Background is {None:false},(bits&512)!=0||flags.Contains(132)||global.Contains(12+r),"Explicit/unrequested fill color changed.")};
                // The native indexless getters can alias the actual first slot.
                // Only that exactly verified first slot or unchanged cell value
                // can replace the observed font/height; no locale-name mapping.
                if(before.Contents[offset].Length>0)
                {
                    var first=before.Contents[offset][0];var last=after.Contents[offset][0];
                    if(first.text_style_handle!=last.text_style_handle&&changed.text_style_handle==last.text_style_handle)expected=expected with{text_style_handle=changed.text_style_handle};
                    if(first.text_height!=last.text_height&&changed.text_height==last.text_height)expected=expected with{text_height=changed.text_height};
                    if(!Same(first.content_color,last.content_color)&&Same(changed.content_color,last.content_color))expected=expected with{content_color=changed.content_color};
                }
                var margins=format.margins.ToArray();
                for(int m=0;m<margins.Length;m++)
                {
                    double? requested=m is 0 or 2?edit.VerticalMargin:edit.HorizontalMargin;
                    double baseline=oldText?.Named is {} named?named.GetProperty("margins")[m].GetDouble():m is 0 or 2?beforeStyle.vertical_margin:beforeStyle.horizontal_margin;
                    double wanted=nextText?.Named is {} nextNamed?nextNamed.GetProperty("margins")[m].GetDouble():m is 0 or 2?afterStyle.vertical_margin:afterStyle.horizontal_margin;
                    int bit=1<<(m switch{0=>11,1=>10,2=>13,_=>12});
                    margins[m]=Inherited(margins[m],changed.margins[m],baseline,wanted,requested!=null,(bits&bit)!=0||global.Contains(m is 0 or 2?5:4),"Explicit/unrequested indexed cell margin changed.");
                }
                var grids=new List<Index.GridValue>();
                foreach(var grid in format.grids)
                {
                    var other=changed.grids.Single(value=>value.line==grid.line);var oldGrid=role.grids.EnumerateArray().Single(value=>value.GetProperty("line").GetString()==grid.line);var newGrid=nextRole.grids.EnumerateArray().Single(value=>value.GetProperty("line").GetString()==grid.line);
                    var selected=choice?.Grids?.SingleOrDefault(value=>value.Line==grid.line);int nativeLine=Array.IndexOf(new[]{"HorizontalTop","HorizontalInside","HorizontalBottom","VerticalLeft","VerticalInside","VerticalRight"},grid.line);
                    int side=grid.line switch{"HorizontalTop"=>0,"VerticalRight"=>1,"HorizontalBottom"=>2,"VerticalLeft"=>3,_=>-1};
                    var desired=grid with{
                        color=Inherited(grid.color,other.color,JsonColor(oldGrid.GetProperty("color")),JsonColor(newGrid.GetProperty("color")),roleAppearance&&(selected?.ColorAci!=null||selected?.ColorRgb!=null),(grid.overrides&8)!=0||side>=0&&flags.Contains(136+side)||global.Contains(40+r*6+nativeLine),"Explicit/unrequested grid color changed."),
                        lineweight=Inherited(grid.lineweight,other.lineweight,oldGrid.GetProperty("lineweight").GetInt32(),newGrid.GetProperty("lineweight").GetInt32(),roleAppearance&&selected?.Lineweight!=null,(grid.overrides&2)!=0||side>=0&&flags.Contains(140+side)||global.Contains(70+r*6+nativeLine),"Explicit/unrequested grid weight changed."),
                        visibility=Inherited(grid.visibility,other.visibility,oldGrid.GetProperty("visible").GetBoolean()?"Visible":"Invisible",newGrid.GetProperty("visible").GetBoolean()?"Visible":"Invisible",roleAppearance&&selected?.Visible!=null,(grid.overrides&16)!=0||side>=0&&flags.Contains(144+side)||global.Contains(100+r*6+nativeLine),"Explicit/unrequested grid visibility changed.")};
                    Need(Same(desired,other),"Native style changed grid overrides/linetype/style/spacing or unrelated border data.");grids.Add(desired);
                }
                expected=expected with{margins=margins,grids=grids.ToArray()};Equal(expected,changed,"Native style changed cell-style name/dataformat or unrelated cell appearance.","consumer_cell_format");
            }
            else Need(cell.format==null&&actual.format==null,"Native style changed covered/anchor format availability.");
        }
        var origins=before.Contents.Select(slots=>Enumerable.Range(0,slots.Length).ToArray()).ToArray();
        if(!single)
        {
            try{CadTableContentsMetadata364.Verify(before,after,origins,cancel);}
            catch(DomainException error){throw CadTableStyleConsumerPreservation375.Diagnostics(error,handle,"indexed_owned_graph_after_native_layout",before.Metadata,after.Metadata,cancel);}
        }
        if(!Same(before.RowHeights,after.RowHeights)||!Same(before.ColumnWidths,after.ColumnWidths))warnings.Add(CadTableNativeLayout356.Warning);
        if(before.Cells.Any(cell=>cell.format?.cell_style==""))warnings.Add("TABLE_INDEXED_UNNAMED_CELL_STYLE: Actual empty assigned names retain their observed RowType and use native role font/height defaults with actual override/effective-value guards. No named alias or automatic inheritance of unchanged values is inferred.");
        if(before.Cells.Any(cell=>cell.format?.cell_style.Length>0))warnings.Add("TABLE_NAMED_APPEARANCE_LINEAGE_UNOBSERVED: Exact named font/height/margins are observed; no named-to-RowType alias is inferred for unexposed named alignment/color/fill/grid defaults. Those effective properties retain their prior values.");
        warnings.Add("TABLE_INDEXED_STYLE_INHERITANCE: All scalar order/storage/overrides/owned data retained. Equal-valued inheritance provenance is unobserved; effective values can retain prior or take actual requested native defaults. Review every arranged line and landing in the shared image.");
        return warnings.Distinct(StringComparer.Ordinal).ToArray();
        }
        catch(DomainException error)
        {
            if(CadTableStyleConsumerPreservation375.HasContext(error))throw;
            int offset=observedRow>=0&&observedColumn>=0?checked(observedRow*before.Columns+observedColumn):-1;
            object Observed(Index.Snapshot value)=>new{identity=value.Identity,rows=value.Rows,columns=value.Columns,
                row=observedRow,column=observedColumn,content_index=slotIndex,common=value.Common,
                cell=offset>=0&&offset<value.Cells.Length?value.Cells[offset]:null,
                contents=offset>=0&&offset<value.Contents.Length?value.Contents[offset]:null};
            throw CadTableStyleConsumerPreservation375.Diagnostics(error,handle,"consumer_defaults_or_content_after_native_layout",Observed(before),Observed(after),cancel);
        }
    }
    static Index.ColorValue WireColor(TableStyleColor337 value)=>new(value.method,value.aci,value.rgb);
    static Index.ColorValue JsonColor(JsonElement value)=>new(value.GetProperty("method").GetString()!,value.GetProperty("aci").ValueKind==JsonValueKind.Null?null:value.GetProperty("aci").GetInt32(),
        value.GetProperty("rgb").ValueKind==JsonValueKind.Null?null:value.GetProperty("rgb").EnumerateArray().Select(item=>item.GetInt32()).ToArray());
}
