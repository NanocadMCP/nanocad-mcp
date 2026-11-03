using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;
using static NanoCADBridge.CadTableEditingReader;
using Color=Teigha.Colors.Color;
using Index=NanoCADBridge.CadTableContentsReader364;

namespace NanoCADBridge;

// Reusable native style records. Table content setters are never used here.
internal static class CadTableStyleWriter337
{
    internal sealed record Row(string row_type,double text_height,string alignment,string? text_style_handle,TableStyleColor337 content_color,
        bool background_none,TableStyleColor337 background_color,string data_type,string unit_type,string data_format,JsonElement grids);
    internal sealed record Stored(string description,double horizontal_margin,double vertical_margin,int bit_flags,string flow_direction,
        bool title_suppressed,bool header_suppressed,string? template_handle,Row[] rows,JsonElement named_cell_styles);
    internal sealed record State(Stored Properties,JsonElement Metadata,string Token);
    internal sealed record Consumer(ObjectId Id,Snapshot Before,JsonElement Metadata,string[] RowRoles,Index.Snapshot Contents);
    internal sealed record BorrowedConsumer(ObjectId Id,ObjectId Owner,Snapshot Before,JsonElement Metadata);
    internal sealed record Resolved(TableStyleEdit337 Edit,ObjectId Source,string SourceName,State Before,ObjectId CurrentStyle,
        Dictionary<string,ObjectId> TextStyles,Consumer[] Consumers,string[] FontWarnings,BorrowedConsumer[] BorrowedConsumers,CadTableStyleCloneReferences365.Reference[] CreationReferences,
        CadTableStyleIndexedConsumers367.Consumer[] IndexedConsumers);
    internal sealed record Prepared(TableStyle DetachedStyle,Stored PreservedSource,string[] Warnings);
    internal sealed record Applied(StructureChange Change,TableStyleChange337 Details);
    private static readonly RowType[] Roles={RowType.TitleRow,RowType.HeaderRow,RowType.DataRow};
    private static readonly GridLineType[] Lines={GridLineType.HorizontalTop,GridLineType.HorizontalInside,GridLineType.HorizontalBottom,
        GridLineType.VerticalLeft,GridLineType.VerticalInside,GridLineType.VerticalRight};
    private static void Require([DoesNotReturnIf(false)] bool value,string message,string code="UNSUPPORTED_TABLE_STYLE")
    {if(!value)throw new DomainException(code,message);}
    private static string? Id(ObjectId id)=>id.IsNull?null:Validation.NormalizeHandle(id.Handle.ToString());
    private static double Finite(double number)
    {Require(double.IsFinite(number),"Stored TableStyle contains a nonfinite native value.","INVALID_GEOMETRY");return number;}
    private static bool Exact(object? a,object? b)=>JsonSerializer.Serialize(a,Wire.Json)==JsonSerializer.Serialize(b,Wire.Json);
    private static TableStyleColor337 StoredColor(Color color)=>new(color.IsByLayer?"by_layer":color.IsByBlock?"by_block":color.IsByAci?"aci":color.IsByColor?"true_color":color.ColorMethod.ToString(),
        color.IsByAci?(int?)color.ColorIndex:null,color.IsByColor?new[]{(int)color.Red,color.Green,color.Blue}:null);
    internal static Stored Properties(TableStyle style,Action cancel)
    {
        var budget=new ModelInventoryBudget(cancel);var rows=new List<Row>();
        foreach(var role in Roles)
        {
            cancel();using var content=style.Color(role);using var background=style.BackgroundColor(role);var grids=new List<object>();
            foreach(var line in Lines)
            {
                cancel();using var color=style.GridColor(line,role);
                // Installed TableStyle bool getter means native Invisible(1).
                // Its bool setter already maps true to native Visible(0).
                grids.Add(new{line=line.ToString(),color=StoredColor(color),lineweight=(int)style.GridLineWeight(line,role),visible=!style.GridVisibility(line,role)});
            }
            var row=new Row(role.ToString(),Finite(style.TextHeight(role)),style.Alignment(role).ToString(),Id(style.TextStyle(role)),StoredColor(content),
                style.IsBackgroundColorNone(role),StoredColor(background),style.DataType(role).ToString(),style.UnitType(role).ToString(),style.Format(role),Wire.Element(grids));
            budget.Add(row);rows.Add(row);
        }
        var named=new List<object>();var names=new HashSet<string>(StringComparer.Ordinal);
        foreach(var item in style.CellStyles)
        {
            cancel();Require(item is string,"Native named cell-style inventory contains an unclassified entry.");string name=(string)item;
            Require(names.Add(name),"Native TableStyle repeats a named cell-style identity.");var grids=new List<object>();
            foreach(var line in Lines)
            {cancel();grids.Add(new{line=line.ToString(),linetype_handle=Id(style.GridLinetype(line,name)),line_style=style.GridLineStyle(line,name).ToString(),double_spacing=Finite(style.GridDoubleLineSpacing(line,name))});}
            var data=new{name,cell_class=style.CellClass(name).ToString(),text_height=Finite(style.TextHeight(name)),text_style_handle=Id(style.TextStyle(name)),
                margins=new[]{CellMargins.Top,CellMargins.Left,CellMargins.Bottom,CellMargins.Right}.Select(m=>Finite(style.Margin(m,name))).ToArray(),grids};
            budget.Add(data);named.Add(data);
        }
        var result=new Stored(style.Description,Finite(style.HorizontalCellMargin),Finite(style.VerticalCellMargin),style.BitFlags,style.FlowDirection.ToString(),
            style.IsTitleSuppressed,style.IsHeaderSuppressed,Id(style.Template),rows.ToArray(),Wire.Element(named));
        new ModelInventoryBudget(cancel).Add(result);return result;
    }
    private static JsonElement Metadata(DBObject source,Transaction tr,Action cancel,bool includeOwned=true)
    {
        var budget=new ModelInventoryBudget(cancel);using var xdata=source.XData;using var reactors=source.GetPersistentReactorIds();var links=new List<string?>();
        foreach(ObjectId link in reactors){cancel();budget.Add(Id(link));links.Add(Id(link));}
        var result=Wire.Element(new{handle=Id(source.ObjectId),owner_handle=Id(source.OwnerId),native_type=source.GetType().FullName,has_fields=source.HasFields,
            extension_handle=Id(source.ExtensionDictionary),xdata=xdata==null?null:NativeOwnedMetadata.Buffer(xdata,cancel,item=>budget.Add(item)),
            reactors=links.ToArray(),owned=includeOwned?(source is Table table?CadTableFormulaField358.Owned(table,tr,cancel,CadTableFormulaField358.Bindings(table,tr,cancel)):NativeOwnedMetadata.Capture(source,tr,cancel)):Wire.Element(Array.Empty<object>())});
        new ModelInventoryBudget(cancel).Add(result);return result;
    }
    internal static State Capture(TableStyle style,Transaction tr,Action cancel)
    {
        cancel();Require(style.GetType()==typeof(TableStyle),"Select an exact native TableStyle.");
        var properties=Properties(style,cancel);var metadata=Metadata(style,tr,cancel);
        var data=JsonSerializer.SerializeToUtf8Bytes(new{name=style.Name,properties,metadata},Wire.Json);
        Require(data.Length<=8*1024*1024,"TableStyle snapshot exceeds the actual8MiB byte budget.","RESPONSE_LIMIT_EXCEEDED");
        return new(properties,metadata,Convert.ToHexString(SHA256.HashData(data)));
    }
    private static string LocalName(TableStyle style,Database db,Transaction tr,Action cancel)
    {
        Require(style.Database==db&&!style.ObjectId.IsNull&&style.OwnerId==db.TableStyleDictionaryId,"TableStyle is not owned by this drawing's style dictionary.","STYLE_UNAVAILABLE");
        var dictionary=(DBDictionary)tr.GetObject(db.TableStyleDictionaryId,OpenMode.ForRead);string? name=null;var budget=new ModelInventoryBudget(cancel);
        foreach(DBDictionaryEntry entry in dictionary)
        {
            cancel();budget.Add(new{entry.Key,handle=Id(entry.Value)});
            if(entry.Value!=style.ObjectId)continue;
            Require(name==null,"TableStyle has multiple dictionary identities.","STYLE_UNAVAILABLE");name=entry.Key;
        }
        Require(name!=null&&BlockWriteValidation.OrdinaryName(name)&&style.Name==name,"Select an ordinary unqualified local TableStyle with a matching native dictionary name.","STYLE_UNAVAILABLE");
        return name;
    }
    private static bool Renders(TableStyleProperties337 properties)=>properties.HorizontalMargin!=null||properties.VerticalMargin!=null||properties.Rows!=null;
    private static ObjectId TextStyle(string handle,Database db,Transaction tr,Action cancel,List<string> warnings)
    {
        cancel();var id=CadStructureWriter.Find(db,handle);var source=tr.GetObject(id,OpenMode.ForRead) as TextStyleTableRecord;
        Require(source!=null&&source.GetType()==typeof(TextStyleTableRecord)&&source.OwnerId==db.TextStyleTableId&&!source.IsDependent&&!source.IsShapeFile,
            "Choose an existing ordinary local non-shape text style.","STYLE_UNAVAILABLE");
        // Missing/substituted installed families are an actual failure. A SHX,
        // big-font or unavailable glyph analyzer is an observation limitation.
        if(!string.IsNullOrEmpty(source.BigFontFileName)||string.IsNullOrWhiteSpace(source.Font.TypeFace))
            warnings.Add("TABLE_FONT_COVERAGE_UNVERIFIED: Selected native SHX/big-font or unresolved family needs its coverage adapter; review actual rendered glyphs.");
        else try{FontCoverage.Require(tr,id,string.Empty);}
        catch(DomainException error) when(error.Error.code=="FONT_CHECK_UNAVAILABLE")
        {warnings.Add("TABLE_FONT_COVERAGE_UNVERIFIED: Installed font coverage is unavailable; review actual rendered glyphs.");}
        return id;
    }
    private sealed record ConsumerInventory(Consumer[] Single,CadTableStyleIndexedConsumers367.Consumer[] Indexed);
    private static ConsumerInventory Consumers(ObjectId style,Stored defaults,TableStyleProperties337 edits,Database db,Transaction tr,Action cancel)
    {
        var result=new List<Consumer>();var indexed=new List<CadTableStyleIndexedConsumers367.Consumer>();var budget=new ModelInventoryBudget(cancel);
        foreach(ObjectId block in (BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead))
        {
            cancel();var owner=(BlockTableRecord)tr.GetObject(block,OpenMode.ForRead);
            foreach(ObjectId id in owner)
            {
                cancel();if(id.IsNull||!id.IsValid||id.IsErased||id.IsEffectivelyErased)continue;
                if(tr.GetObject(id,OpenMode.ForRead) is not Table table||table.TableStyle!=style)continue;
                Require(table.GetType()==typeof(Table)&&table.OwnerId==block&&!owner.IsDependent&&!owner.IsFromExternalReference&&!owner.IsFromOverlayReference,
                    "A referring Table is custom/external or has inconsistent ownership; its owning style-propagation adapter is required.","UNSUPPORTED_TABLE_STYLE_CONSUMER");
                var layer=(LayerTableRecord)tr.GetObject(table.LayerId,OpenMode.ForRead);
                Require(!layer.IsLocked&&!layer.IsDependent,"A referring Table is on a locked/dependent layer; unlock it before changing its inherited defaults.","LAYER_UNAVAILABLE");
                using(var reactors=table.GetPersistentReactorIds())Require(reactors.Count==0,"A referring Table has persistent dependencies needing its owning adapter.","UNSUPPORTED_TABLE_STYLE_CONSUMER");
                if(CadTableStyleIndexedConsumers367.HasMultiple(table,cancel))
                {
                    var value=CadTableStyleIndexedConsumers367.Capture(table,tr,cancel);budget.Add(new{handle=Id(id),value.Before,value.RowRoles});indexed.Add(value);continue;
                }
                Snapshot before;
                try{before=CadTableEditingReader.Capture(table,cancel,requireWorldXy:false,allowFormattedText:true,allowNumeric:true,allowFormulas:true);}
                catch(DomainException error) when(error.Error.code=="UNSUPPORTED_TABLE_CONTENT")
                {throw new DomainException("UNSUPPORTED_TABLE_STYLE_CONSUMER","A referring Table needs its native content adapter before a rendered style change: "+error.Error.message,false,new{table_handle=Id(id)});}
                var metadata=Metadata(table,tr,cancel);var roles=CadTableStyleTextInheritance373.RowRoles(table,cancel);
                // Original legacy admission above remains authoritative: no
                // multiple/linked/block/unclassified Field content is added.
                // Read actual indexed storage/layout as well, for the existing
                // complete same-style defaults verifier (never synthetic slots).
                var contents=Index.Capture(table,tr,cancel);
                Require(before.Identity==contents.Identity&&before.Rows==contents.Rows&&before.Columns==contents.Columns,
                    "Actual legacy/indexed Table observations disagree on root identity or topology.","OBJECT_READ_FAILED");
                var global=before.Common.GetProperty("table_overrides").EnumerateArray().Select(value=>value.GetInt32()).ToHashSet();
                foreach(var cell in before.Cells)
                {
                    cancel();if(!Anchor(cell)||cell.format==null)continue;int offset=checked(cell.row*before.Columns+cell.column);int bits=before.Values[offset]?.overrides??0;
                    int r=roles[cell.row]=="TitleRow"?0:roles[cell.row]=="HeaderRow"?1:2;
                    bool font=edits.Rows?.Any(value=>value.TextStyleHandle!=null)==true&&(bits&64)==0&&!before.CellOverrides[offset].Contains(134)&&!global.Contains(18+r);
                    bool height=edits.Rows?.Any(value=>value.TextHeight!=null)==true&&(bits&128)==0&&!before.CellOverrides[offset].Contains(135)&&!global.Contains(21+r);
                    if(font||height)_=CadTableStyleTextInheritance373.Resolve(defaults,roles[cell.row],cell.format.cell_style,Id(id)!,cell.row,cell.column,"single_capture_defaults",cancel);
                }
                var consumer=new Consumer(id,before,metadata,roles,contents);budget.Add(new{handle=Id(id),before,metadata,roles,contents});result.Add(consumer);
            }
        }
        return new(result.ToArray(),indexed.ToArray());
    }
    private static DomainException DependencyFailure(ObjectId id,DBObject? item,string reason) =>
        new("UNSUPPORTED_TABLE_STYLE_DEPENDENCY","A TableStyle persistent dependency is outside its inspected native Table consumers.",false,
            new{reactor_handle=id.IsNull||!id.IsValid?null:Id(id),reactor_state=id.IsNull?"null":!id.IsValid?"invalid":id.IsErased||id.IsEffectivelyErased?"erased":"live",
                runtime_type=item?.GetType().FullName,owner_handle=item==null?null:Id(item.OwnerId),classification_reason=reason});
    private static JsonElement BorrowedMetadata(Table table,Transaction tr,Action cancel) =>
        Wire.Element(new{native=Metadata(table,tr,cancel),layer_handle=Id(table.LayerId),linetype_handle=Id(table.LinetypeId),
            properties=EntityAdapter.StoredProperties(table)});
    private static BorrowedConsumer[] Borrowed(TableStyle style,Database db,Transaction tr,Action cancel)
    {
        // These are references borrowed from the existing base style, not Tables
        // to edit or refresh. No writable-layer check is needed; complete stored
        // content and owned-data observation still require their actual adapters.
        using var links=style.GetPersistentReactorIds();var values=new List<BorrowedConsumer>();var seen=new HashSet<ObjectId>();var budget=new ModelInventoryBudget(cancel);
        foreach(ObjectId id in links)
        {
            cancel();budget.Add(new{reactor_handle=id.IsNull||!id.IsValid?null:Id(id)});
            if(id.IsNull||!id.IsValid||id.IsErased||id.IsEffectivelyErased||!seen.Add(id))throw DependencyFailure(id,null,"Unavailable or repeated reactor identity.");
            var item=tr.GetObject(id,OpenMode.ForRead);
            if(CadNativeStyleDependencies349.OwningDictionary(style,style.Name,id,db.TableStyleDictionaryId,db,tr,cancel))continue;
            if(item is not Table table||table.GetType()!=typeof(Table)||table.TableStyle!=style.ObjectId||table.Database?.UnmanagedObject!=db.UnmanagedObject)
                throw DependencyFailure(id,item,"Reactor is not an exact local Table with the actual base-style link.");
            var ownerId=table.OwnerId;
            if(ownerId.IsNull||!ownerId.IsValid||ownerId.IsErased||ownerId.IsEffectivelyErased)
                throw DependencyFailure(id,item,"Table owner is unavailable.");
            var owner=tr.GetObject(ownerId,OpenMode.ForRead);
            if(owner is not BlockTableRecord block||block.GetType()!=typeof(BlockTableRecord)||block.OwnerId!=db.BlockTableId||
                block.IsDependent||block.IsFromExternalReference||block.IsFromOverlayReference)
                throw DependencyFailure(id,item,"Table does not have ordinary local block ownership.");
            Snapshot before;
            try{before=CadTableEditingReader.Capture(table,cancel,requireWorldXy:false,allowFormattedText:true,allowNumeric:true,allowFormulas:true);}
            catch(DomainException error) when(error.Error.code=="UNSUPPORTED_TABLE_CONTENT")
            {throw new DomainException("UNSUPPORTED_TABLE_STYLE_CONSUMER","A base-style Table needs its content owning adapter before complete source preservation can be observed.",false,
                new{table_handle=Id(id),content_reason=error.Error.message});}
            var metadata=BorrowedMetadata(table,tr,cancel);var value=new BorrowedConsumer(id,ownerId,before,metadata);
            budget.Add(new{handle=Id(id),owner_handle=Id(ownerId),before,metadata});values.Add(value);
        }
        return values.ToArray();
    }
    private static JsonElement RequiredBorrowed(Snapshot value,Action cancel)
    {
        // The user supplied a TableStyle token, not an expected Table token.
        // Required content/format/frame state is exact. Keep the unexposed native
        // override inventories separate: read-only346 already showed that their
        // provenance needs diagnosis rather than whole-cell token assumptions.
        var common=new Dictionary<string,JsonElement>(StringComparer.Ordinal);
        foreach(var item in value.Common.EnumerateObject()){cancel();if(item.Name!="table_overrides")common.Add(item.Name,item.Value.Clone());}
        var values=new ContentValue?[value.Values.Length];
        for(int index=0;index<values.Length;index++){cancel();values[index]=value.Values[index] is {} item?item with{overrides=0}:null;}
        cancel();return Wire.Element(new{value.Identity,value.Rows,value.Columns,value.RowHeights,value.ColumnWidths,value.Cells,common,values});
    }
    private static JsonElement BorrowedOverrides(Snapshot value,Action cancel)
    {
        var content=new int?[value.Values.Length];
        for(int index=0;index<content.Length;index++){cancel();content[index]=value.Values[index]?.overrides;}
        cancel();return Wire.Element(new{table=value.Common.GetProperty("table_overrides"),content,cells=value.CellOverrides});
    }
    private static void VerifyBorrowed(Resolved resolved,Database db,Transaction tr,Action cancel,Action<string>? warning=null)
    {
        foreach(var before in resolved.BorrowedConsumers)
        {
            cancel();Require(before.Id.IsValid&&!before.Id.IsErased&&!before.Id.IsEffectivelyErased,"A borrowed base-style consumer is no longer live.","STYLE_STATE_CHANGED");
            var item=tr.GetObject(before.Id,OpenMode.ForRead);
            Require(item is Table&&item.GetType()==typeof(Table)&&item.OwnerId==before.Owner&&item.Database?.UnmanagedObject==db.UnmanagedObject,
                "Borrowed base-style consumer type/owner/database changed.","STYLE_STATE_CHANGED");
            var table=(Table)item;Require(table.TableStyle==resolved.Source&&Exact(before.Metadata,BorrowedMetadata(table,tr,cancel)),
                "Borrowed base-style consumer link/root/owned metadata changed.","STYLE_STATE_CHANGED");
            var actual=CadTableEditingReader.Capture(table,cancel,requireWorldXy:false,allowFormattedText:true,allowNumeric:true,allowFormulas:true);
            Require(Exact(RequiredBorrowed(before.Before,cancel),RequiredBorrowed(actual,cancel)),"Borrowed base-style Table changed required raw content, format, frame or grid state.","TABLE_STATE_CHANGED");
            if(!Exact(BorrowedOverrides(before.Before,cancel),BorrowedOverrides(actual,cancel)))warning?.Invoke(
                "TABLE_STYLE_SOURCE_FORMAT_OBSERVATION: Retained base Tables have matching raw values and visible formatting, but native override-state flags changed during inspection. This clone does not reassign or refresh them; inspect their current formatting before later inherited edits.");
        }
    }
    private static JsonElement DetachBorrowed(TableStyle clone,Resolved resolved,Database db,Transaction tr,Action cancel)
    {
        var before=Metadata(clone,tr,cancel,includeOwned:false);
        var allowed=new HashSet<ObjectId>();foreach(var value in resolved.Consumers){cancel();allowed.Add(value.Id);}
        foreach(var value in resolved.BorrowedConsumers){cancel();allowed.Add(value.Id);}
        foreach(var value in resolved.CreationReferences){cancel();allowed.Add(value.Id);}
        foreach(var value in resolved.IndexedConsumers){cancel();allowed.Add(value.Id);}
        // Source references belong to the unchanged source graph. Clone may
        // omit them: no copied edge is required on a new detached style. Every
        // edge actually copied still needs the exact inspected identity below,
        // and is removed only from this detached object before any setter.
        var source=(TableStyle)tr.GetObject(resolved.Source,OpenMode.ForRead);
        if(CadNativeStyleDependencies349.OwningDictionary(source,source.Name,db.TableStyleDictionaryId,db.TableStyleDictionaryId,db,tr,cancel))
            allowed.Add(db.TableStyleDictionaryId);
        var remove=new List<ObjectId>();var seen=new HashSet<ObjectId>();var budget=new ModelInventoryBudget(cancel);
        using(var links=clone.GetPersistentReactorIds())foreach(ObjectId id in links)
        {
            cancel();budget.Add(new{reactor_handle=id.IsNull||!id.IsValid?null:Id(id)});
            if(!allowed.Contains(id)||!seen.Add(id))throw DependencyFailure(id,null,"Detached clone contains an unclassified/repeated borrowed reactor.");
            remove.Add(id);
        }
        foreach(var id in remove){cancel();clone.RemovePersistentReactor(id);}
        var actual=Metadata(clone,tr,cancel,includeOwned:false);
        var expected=System.Text.Json.Nodes.JsonNode.Parse(before.GetRawText())!;expected["reactors"]=new System.Text.Json.Nodes.JsonArray();
        Require(Exact(Wire.Element(expected),actual)&&!actual.GetProperty("has_fields").GetBoolean()&&
            Exact(actual.GetProperty("xdata"),resolved.Before.Metadata.GetProperty("xdata"))&&
            (resolved.Edit.Operation!="create_table_style"||actual.GetProperty("extension_handle").ValueKind==JsonValueKind.Null),
            "Detached style changed non-reactor metadata or retained unclassified create ownership.","API_EXCEPTION");
        VerifySource(resolved,db,tr,cancel);return actual;
    }
    private static void DependencyScope(TableStyle style,Consumer[] consumers,BorrowedConsumer[] borrowed,CadTableStyleCloneReferences365.Reference[] references,CadTableStyleIndexedConsumers367.Consumer[] indexed,Database db,Transaction tr,Action cancel,bool creating,bool rendering)
    {
        Require(!style.HasFields,"Field-bearing TableStyle needs its owning style adapter.");
        if(creating)Require(style.ExtensionDictionary.IsNull,"Cloning a TableStyle owned extension graph needs an owner-remapping adapter; existing-style metadata edits retain ordinary owned dictionaries/Xrecords.","UNSUPPORTED_STYLE_CLONE_OWNERSHIP");
        if(!style.Template.IsNull)
        {
            // A local shared template reference remains a reference. A template
            // owned by the source (directly or indirectly) cannot be silently
            // shared by its new clone without native ownership remapping.
            var pending=style.Template;var visited=new HashSet<ObjectId>();var budget=new ModelInventoryBudget(cancel);
            while(!pending.IsNull)
            {
                cancel();Require(pending.IsValid&&!pending.IsErased&&!pending.IsEffectivelyErased&&visited.Add(pending),"Native template ownership is unavailable or cyclic.");
                budget.Add(Id(pending));var item=tr.GetObject(pending,OpenMode.ForRead);
                Require(item.Database==style.Database,"A template reference belongs to another database.");
                if(pending==style.ObjectId){Require(!creating,"A source-owned template needs a native clone-ownership adapter.","UNSUPPORTED_STYLE_CLONE_OWNERSHIP");break;}
                pending=item.OwnerId;
            }
        }
        using var links=style.GetPersistentReactorIds();
        foreach(ObjectId id in links)
        {
            cancel();
            if(!creating&&!rendering)continue; // Description does not touch tables or their content.
            if(CadNativeStyleDependencies349.OwningDictionary(style,style.Name,id,db.TableStyleDictionaryId,db,tr,cancel))continue;
            if(creating)
            {
                if(!references.Any(reference=>reference.Id==id))throw DependencyFailure(id,null,"Creation source reactor has no exact inspected borrowed reference.");
            }
            else if(!(consumers.Any(table=>table.Id==id)||indexed.Any(table=>table.Id==id))||tr.GetObject(id,OpenMode.ForRead) is not Table)
                throw DependencyFailure(id,null,"Rendered style edit has no inspected native Table consumer for this reactor.");
        }
    }
    internal static Resolved Resolve(TableStyleEdit337 edit,Database db,Transaction tr,Action cancel)
    {
        cancel();var source=tr.GetObject(CadStructureWriter.Find(db,edit.SourceHandle),OpenMode.ForRead) as TableStyle;
        Require(source!=null&&source.GetType()==typeof(TableStyle),"Source handle is not an exact local TableStyle.","STYLE_UNAVAILABLE");
        string name=LocalName(source,db,tr,cancel);var state=Capture(source,tr,cancel);
        Require(state.Token==edit.SourceToken,"TableStyle changed; refresh its table_style_token.","STYLE_STATE_CHANGED");
        bool creating=edit.Operation=="create_table_style";
        Require(creating||name==edit.ExpectedName,"TableStyle name differs from expected_name.","STYLE_NAME_MISMATCH");
        var dictionary=(DBDictionary)tr.GetObject(db.TableStyleDictionaryId,OpenMode.ForRead);
        if(creating)foreach(DBDictionaryEntry entry in dictionary)
        {cancel();Require(!string.Equals(entry.Key,edit.Name,StringComparison.OrdinalIgnoreCase),"Destination TableStyle name already exists; no overwrite.","STYLE_EXISTS");}
        var fonts=new Dictionary<string,ObjectId>(StringComparer.Ordinal);var warnings=new List<string>();
        foreach(var row in edit.Properties.Rows??Array.Empty<TableStyleRowChanges337>())
        {cancel();if(row.TextStyleHandle is {} handle)fonts.Add(row.RowType,TextStyle(handle,db,tr,cancel,warnings));}
        var inventory=!creating&&Renders(edit.Properties)?Consumers(source.ObjectId,state.Properties,edit.Properties,db,tr,cancel):new ConsumerInventory(Array.Empty<Consumer>(),Array.Empty<CadTableStyleIndexedConsumers367.Consumer>());
        var tables=inventory.Single;
        var borrowed=!creating&&!Renders(edit.Properties)?Borrowed(source,db,tr,cancel):Array.Empty<BorrowedConsumer>();
        var references=creating?CadTableStyleCloneReferences365.Capture(source,db,tr,cancel):Array.Empty<CadTableStyleCloneReferences365.Reference>();
        DependencyScope(source,tables,borrowed,references,inventory.Indexed,db,tr,cancel,creating,Renders(edit.Properties));
        return new(edit,source.ObjectId,name,state,db.Tablestyle,fonts,tables,warnings.Distinct(StringComparer.Ordinal).ToArray(),borrowed,references,inventory.Indexed);
    }
    private static void VerifySource(Resolved resolved,Database db,Transaction tr,Action cancel)
    {
        cancel();var source=(TableStyle)tr.GetObject(resolved.Source,OpenMode.ForRead);
        Require(LocalName(source,db,tr,cancel)==resolved.SourceName&&Capture(source,tr,cancel).Token==resolved.Before.Token&&db.Tablestyle==resolved.CurrentStyle,
            "TableStyle source or current document default changed since preflight.","STYLE_STATE_CHANGED");
        VerifyBorrowed(resolved,db,tr,cancel);
        if(resolved.Edit.Operation=="create_table_style")CadTableStyleCloneReferences365.Verify(resolved.CreationReferences,source,db,tr,cancel);
        foreach(var consumer in resolved.Consumers)
        {
            cancel();var table=(Table)tr.GetObject(consumer.Id,OpenMode.ForRead);
            Require(CadTableEditingReader.Capture(table,cancel,requireWorldXy:false,allowFormattedText:true,allowNumeric:true,allowFormulas:true).Token==consumer.Before.Token&&Index.Capture(table,tr,cancel).Token==consumer.Contents.Token&&Exact(consumer.Metadata,Metadata(table,tr,cancel))&&Exact(consumer.RowRoles,CadTableStyleTextInheritance373.RowRoles(table,cancel)),
                "A referring Table changed since style preflight.","TABLE_STATE_CHANGED");
        }
        foreach(var consumer in resolved.IndexedConsumers)
        {cancel();CadTableStyleIndexedConsumers367.VerifyCurrent(consumer,(Table)tr.GetObject(consumer.Id,OpenMode.ForRead),tr,cancel);}
    }
    private static Color RoleColor(int? aci,RgbColor? rgb)=>rgb is {} value?Color.FromRgb(value.Red,value.Green,value.Blue):
        Color.FromColorIndex(aci==0?Teigha.Colors.ColorMethod.ByBlock:aci==256?Teigha.Colors.ColorMethod.ByLayer:Teigha.Colors.ColorMethod.ByAci,(short)aci!.Value);
    private static void Set(TableStyle style,Resolved resolved,Action cancel)
    {
        var p=resolved.Edit.Properties;
        if(p.Description!=null){cancel();style.Description=p.Description;}
        if(p.HorizontalMargin is {} horizontal){cancel();style.HorizontalCellMargin=horizontal;}
        if(p.VerticalMargin is {} vertical){cancel();style.VerticalCellMargin=vertical;}
        foreach(var row in p.Rows??Array.Empty<TableStyleRowChanges337>())
        {
            cancel();var role=Enum.Parse<RowType>(row.RowType);int mask=(int)role;
            if(row.TextHeight is {} height){cancel();style.SetTextHeight(height,mask);}
            if(row.Alignment is {} alignment){cancel();style.SetAlignment(Enum.Parse<CellAlignment>(alignment),mask);}
            if(resolved.TextStyles.TryGetValue(row.RowType,out var font)){cancel();style.SetTextStyle(font,mask);}
            if(row.ColorAci is {} aci){cancel();using var color=Color.FromColorIndex(Teigha.Colors.ColorMethod.ByAci,(short)aci);style.SetColor(color,mask);}
            if(row.ColorRgb is {} rgb){cancel();using var color=Color.FromRgb(rgb.Red,rgb.Green,rgb.Blue);style.SetColor(color,mask);}
            if(row.Background is {} background)
            {
                // The deprecated none setter can reset the stored color. Set
                // the mode first, then leave the requested color authoritative.
                cancel();style.SetBackgroundColorNone(background.None,mask);
                if(!background.None){cancel();using var color=RoleColor(background.ColorAci,background.ColorRgb);style.SetBackgroundColor(color,mask);}
            }
            foreach(var grid in row.Grids??Array.Empty<TableGridFormatChanges>())
            {
                cancel();int line=(int)Enum.Parse<GridLineType>(grid.Line);
                if(grid.Visible is {} visible)style.SetGridVisibility(visible,line,mask);
                if(grid.Lineweight is {} weight)style.SetGridLineWeight((LineWeight)weight,line,mask);
                if(grid.ColorAci!=null||grid.ColorRgb!=null){using var color=RoleColor(grid.ColorAci,grid.ColorRgb);style.SetGridColor(color,line,mask);}
            }
        }
    }
    private static void RequestedAndRetained(Stored before,Stored after,TableStyleProperties337 p,Action cancel,string operation,string source,string stage)
    {
        void Retained(bool good,string message,object expectedValues,object actualValues,string part)
        {
            if(!good)throw new DomainException("NATIVE_TABLE_STYLE_MISMATCH",message,false,
                CadTableStyleCloneDiagnostics367.Capture(operation,source,Wire.Element(expectedValues),Wire.Element(actualValues),cancel,
                    stage+"_"+part,"requested_and_retained_expected_defaults","actual_completed_native_defaults"));
        }
        var expected=before with{description=p.Description??before.description,horizontal_margin=p.HorizontalMargin??before.horizontal_margin,vertical_margin=p.VerticalMargin??before.vertical_margin,rows=after.rows,named_cell_styles=after.named_cell_styles};
        Retained(Exact(expected,after),"Native TableStyle changed an unrequested global/default/template field.",expected,after,"global_defaults");
        var expectedRows=new List<Row>();bool rowsMatch=true;
        foreach(var prior in before.rows)
        {
            cancel();var actual=after.rows.Single(row=>row.row_type==prior.row_type);var change=p.Rows?.SingleOrDefault(row=>row.RowType==prior.row_type);
            var rowExpected=prior with{text_height=change?.TextHeight??prior.text_height,alignment=change?.Alignment??prior.alignment,
                text_style_handle=change?.TextStyleHandle??prior.text_style_handle,
                content_color=change?.ColorRgb is {} rgb?new("true_color",null,rgb.Channels()):change?.ColorAci is {} aci?new("aci",aci,null):prior.content_color,
                background_none=change?.Background?.None??prior.background_none,
                background_color=change?.Background is {} background?background.None?actual.background_color:TableStyleRoleDefaults365.ExpectedColor(background.ColorAci,background.ColorRgb):prior.background_color,
                grids=TableStyleRoleDefaults365.ExpectedGrids(prior.grids,change?.Grids)};
            expectedRows.Add(rowExpected);rowsMatch&=Exact(rowExpected,actual);
        }
        // Compare every actual role before reporting, so one receipt identifies
        // all requested/unrequested row fields instead of only the first role.
        Retained(rowsMatch,"Native requested TableStyle row defaults differ, or an unrequested row/background/data/grid default changed.",
            new{rows=expectedRows},new{rows=after.rows},"row_defaults");
        var oldNamed=before.named_cell_styles.EnumerateArray().ToArray();var newNamed=after.named_cell_styles.EnumerateArray().ToArray();
        Retained(oldNamed.Length==newNamed.Length,"Native TableStyle changed named-cell-style inventory.",
            new{named_cell_styles=oldNamed},new{named_cell_styles=newNamed},"named_inventory");
        for(int index=0;index<oldNamed.Length;index++)
        {
            cancel();var old=oldNamed[index].EnumerateObject().ToDictionary(field=>field.Name,field=>field.Value.Clone());var current=newNamed[index];
            // Row setters may update their native named alias. Do not invent an
            // alias from a locale-sensitive name; keep every unrelated observed
            // field exact and compare the complete alias result to the preview.
            if(!Exact(old["text_height"],current.GetProperty("text_height")))
            {
                Retained(p.Rows?.Any(row=>row.TextHeight==current.GetProperty("text_height").GetDouble())==true,"A named style acquired a height not requested for any native row alias.",
                    new{named_cell_styles=new[]{Wire.Element(old)}},new{named_cell_styles=new[]{current}},"named_height");
                old["text_height"]=current.GetProperty("text_height").Clone();
            }
            if(!Exact(old["text_style_handle"],current.GetProperty("text_style_handle")))
            {
                Retained(p.Rows?.Any(row=>row.TextStyleHandle!=null&&row.TextStyleHandle==current.GetProperty("text_style_handle").GetString())==true,"A named style acquired a font identity not requested for any native row alias.",
                    new{named_cell_styles=new[]{Wire.Element(old)}},new{named_cell_styles=new[]{current}},"named_font");
                old["text_style_handle"]=current.GetProperty("text_style_handle").Clone();
            }
            var margins=old["margins"].EnumerateArray().Select(value=>value.GetDouble()).ToArray();var actualMargins=current.GetProperty("margins");
            for(int m=0;m<4;m++)if(margins[m]!=actualMargins[m].GetDouble())
            {
                double? requested=m is 0 or 2?p.VerticalMargin:p.HorizontalMargin;
                Retained(requested==actualMargins[m].GetDouble(),"Native named margin changed outside its selected axis/value.",
                    new{named_cell_styles=new[]{Wire.Element(old)}},new{named_cell_styles=new[]{current}},"named_margin");margins[m]=actualMargins[m].GetDouble();
            }
            old["margins"]=Wire.Element(margins);
            Retained(Exact(Wire.Element(old),current),"Native TableStyle changed an unrelated observed named-cell-style field.",
                new{named_cell_styles=new[]{Wire.Element(old)}},new{named_cell_styles=new[]{current}},"named_retained_defaults");
        }
    }
    internal static Prepared Preview(Resolved resolved,Database db,Transaction tr,List<DBObject> owned,Action cancel)
    {
        VerifySource(resolved,db,tr,cancel);var source=(TableStyle)tr.GetObject(resolved.Source,OpenMode.ForRead);cancel();var value=source.Clone();
        Require(!ReferenceEquals(source,value),"Native style clone returned its source; no setter was called.","API_EXCEPTION");
        if(value is not DBObject clone)throw new DomainException("API_EXCEPTION","Native TableStyle clone is not a database object; unproved native ownership was not disposed.");
        if(!clone.ObjectId.IsNull||!clone.OwnerId.IsNull||clone.UnmanagedObject==IntPtr.Zero||clone.UnmanagedObject==source.UnmanagedObject||owned.Any(item=>item.UnmanagedObject==clone.UnmanagedObject))
            throw new DomainException("API_EXCEPTION","Native TableStyle clone returned a resident/source/owned alias; no setter/disposal was performed.");
        owned.Add(clone);
        if(clone.GetType()!=typeof(TableStyle))throw new DomainException("API_EXCEPTION","Native TableStyle clone is not the exact detached type; no setter was called.");
        var prepared=(TableStyle)clone;var clonedProperties=Properties(prepared,cancel);
        // Actual369 Clone retained every observed default except the two legacy
        // margins. Restore those actual source values on this proved detached
        // object; other differences still fail before any preservation setter.
        var margins=resolved.Before.Properties;
        if(!Exact(margins,clonedProperties with{horizontal_margin=margins.horizontal_margin,vertical_margin=margins.vertical_margin}))
            throw new DomainException("API_EXCEPTION","Native TableStyle clone changed retained defaults.",false,
                CadTableStyleCloneDiagnostics367.Capture(resolved.Edit.Operation,Id(resolved.Source)!,Wire.Element(margins),Wire.Element(clonedProperties),cancel));
        // Remove copied resident notification edges before any clone setter.
        var cloneMetadata=DetachBorrowed(prepared,resolved,db,tr,cancel);
        if(clonedProperties.horizontal_margin!=margins.horizontal_margin||clonedProperties.vertical_margin!=margins.vertical_margin)
        {
            if(clonedProperties.horizontal_margin!=margins.horizontal_margin){cancel();prepared.HorizontalCellMargin=margins.horizontal_margin;}
            if(clonedProperties.vertical_margin!=margins.vertical_margin){cancel();prepared.VerticalCellMargin=margins.vertical_margin;}
            Require(Exact(cloneMetadata,Metadata(prepared,tr,cancel,includeOwned:false)),"Detached source-margin preservation changed native metadata or dependencies.","API_EXCEPTION");
            clonedProperties=Properties(prepared,cancel);
        }
        if(!Exact(margins,clonedProperties))throw new DomainException("API_EXCEPTION","Native TableStyle clone changed retained defaults after actual source-margin preservation.",false,
            CadTableStyleCloneDiagnostics367.Capture(resolved.Edit.Operation,Id(resolved.Source)!,Wire.Element(margins),Wire.Element(clonedProperties),cancel));
        // A detached source copy does not prove requested defaults in the
        // owning drawing. Keep it unchanged until native posting; existing
        // edits use the resident source. Requested row/alias/default and glyph
        // checks run on the actual resident result before consumer layout.
        var warnings=resolved.FontWarnings.ToList();
        warnings.Add("TABLE_STYLE_RESIDENT_VALIDATION: Requested defaults and inherited font coverage are checked in the owning drawing before native Table layout; recovery/Undo is not guaranteed.");
        if(resolved.Edit.Operation=="create_table_style")VerifyBorrowed(resolved,db,tr,cancel,warnings.Add);
        var aliases=ChangedNames(resolved.Before.Properties,clonedProperties,cancel);
        // Bound the actual public evidence before any resident setter. These
        // are result bytes, not a native named-style/model-count ceiling.
        Require(JsonSerializer.SerializeToUtf8Bytes(new{aliases,warnings},Wire.Json).Length<=Wire.PayloadBudget-16384,"Changed native alias evidence exceeds the actual response-byte budget.","RESPONSE_LIMIT_EXCEEDED");
        VerifySource(resolved,db,tr,cancel);return new(prepared,clonedProperties,warnings.Distinct(StringComparer.Ordinal).ToArray());
    }
    private static string[] ChangedNames(Stored before,Stored after,Action cancel)
    {
        var result=new List<string>();var old=before.named_cell_styles.EnumerateArray().ToArray();var current=after.named_cell_styles.EnumerateArray().ToArray();
        for(int index=0;index<old.Length;index++){cancel();if(!Exact(old[index],current[index]))result.Add(current[index].GetProperty("name").GetString()!);}
        return result.ToArray();
    }
    private static string[] CheckFonts(Table table,Consumer consumer,Resolved resolved,Stored expected,Database db,Transaction tr,Action cancel,Snapshot? after=null)
    {
        if(resolved.TextStyles.Count==0)return Array.Empty<string>();
        var before=consumer.Before;var current=after??before;var warnings=new List<string>();
        var global=before.Common.GetProperty("table_overrides").EnumerateArray().Select(value=>value.GetInt32()).ToHashSet();
        foreach(var prior in before.Cells)
        {
            cancel();if(!Anchor(prior)||prior.format==null)continue;
            int offset=checked(prior.row*before.Columns+prior.column);var cell=current.Cells[offset];
            string role=consumer.RowRoles[prior.row];int roleIndex=role=="TitleRow"?0:role=="HeaderRow"?1:2;
            bool explicitFont=((before.Values[offset]?.overrides??0)&64)!=0||before.CellOverrides[offset].Contains(134)||global.Contains(18+roleIndex);
            if(cell.content_count==0)continue; // no visible content to analyze
            // Coverage starts with the actual native effective getter. A title
            // using named Data keeps that identity; no display-name/row guess.
            string effective=CadTableStyleTextInheritance373.EffectiveFont(table,cell.row,cell.column,cell.content_count==0?null:0,db,tr,cancel);
            if(effective!=prior.format.text_style_handle)
                warnings.AddRange(CadTableValueEditor336.CheckFontCoverage(table,cell,CadStructureWriter.Find(db,effective),tr,cancel));
            // Before layout also cover the proved inherited target, if the
            // getter still exposes the retained prior font. This is not a
            // promise that native inheritance occurred.
            if(after==null&&!explicitFont)
            {
                var oldDefault=CadTableStyleTextInheritance373.Resolve(resolved.Before.Properties,role,prior.format.cell_style,Id(table.ObjectId)!,prior.row,prior.column,"single_before_defaults",cancel);
                var newDefault=CadTableStyleTextInheritance373.Resolve(expected,role,prior.format.cell_style,Id(table.ObjectId)!,prior.row,prior.column,"single_requested_defaults",cancel);
                if(prior.format.text_style_handle==oldDefault.Font&&newDefault.Font is {} candidate&&candidate!=effective&&candidate!=prior.format.text_style_handle)
                    warnings.AddRange(CadTableValueEditor336.CheckFontCoverage(table,prior,CadStructureWriter.Find(db,candidate),tr,cancel));
            }
            if(before.Values[offset]?.data_type is "Long" or "Double")warnings.Add("TABLE_NUMERIC_DISPLAY_COVERAGE_UNVERIFIED: Native numeric display may reformat after inheritance; inspect actual rendered glyphs after the style change.");
        }
        return warnings.Distinct(StringComparer.Ordinal).ToArray();
    }
    private static void VerifyTextInheritance(Table table,Consumer consumer,Snapshot after,Resolved resolved,Stored expected,Action cancel)
    {
        var before=consumer.Before;
        Require(Exact(consumer.RowRoles,CadTableStyleTextInheritance373.RowRoles(table,cancel)),"Native style inheritance changed single-content row-role identities.","NATIVE_TABLE_STYLE_MISMATCH");
        var global=before.Common.GetProperty("table_overrides").EnumerateArray().Select(value=>value.GetInt32()).ToHashSet();
        foreach(var prior in before.Cells)
        {
            cancel();if(!Anchor(prior)||prior.format==null)continue;int offset=checked(prior.row*before.Columns+prior.column);
            var cell=after.Cells[offset];Require(cell.format!=null,"Native style inheritance lost an anchor's format.","NATIVE_TABLE_STYLE_MISMATCH");
            string role=consumer.RowRoles[prior.row];int r=role=="TitleRow"?0:role=="HeaderRow"?1:2;int bits=before.Values[offset]?.overrides??0;
            bool fontSelected=resolved.Edit.Properties.Rows?.Any(value=>value.TextStyleHandle!=null)==true,heightSelected=resolved.Edit.Properties.Rows?.Any(value=>value.TextHeight!=null)==true;
            bool explicitFont=(bits&64)!=0||before.CellOverrides[offset].Contains(134)||global.Contains(18+r),explicitHeight=(bits&128)!=0||before.CellOverrides[offset].Contains(135)||global.Contains(21+r);
            bool inherited=fontSelected&&!explicitFont||heightSelected&&!explicitHeight;
            var oldDefault=inherited?CadTableStyleTextInheritance373.Resolve(resolved.Before.Properties,role,prior.format.cell_style,Id(table.ObjectId)!,prior.row,prior.column,"single_before_defaults",cancel):
                new CadTableStyleTextInheritance373.Defaults(prior.format.text_style_handle,prior.format.text_height,null,"retained_explicit_or_unselected_text");
            var newDefault=inherited?CadTableStyleTextInheritance373.Resolve(expected,role,prior.format.cell_style,Id(table.ObjectId)!,prior.row,prior.column,"single_after_defaults",cancel):oldDefault;
            CadTableStyleTextInheritance373.VerifyText(Id(table.ObjectId)!,prior.row,prior.column,prior.format.cell_style,role,oldDefault,newDefault,
                prior.format.text_style_handle,cell.format.text_style_handle,prior.format.text_height,cell.format.text_height,
                fontSelected,heightSelected,explicitFont,explicitHeight);
        }
    }
    private static void VerifyCreatedMetadata(TableStyle result,Resolved resolved,State actual,Database db,Transaction tr,Action cancel)
    {
        bool xdataRetained=Exact(resolved.Before.Metadata.GetProperty("xdata"),actual.Metadata.GetProperty("xdata"));
        bool extensionAbsent=actual.Metadata.GetProperty("extension_handle").ValueKind==JsonValueKind.Null;
        var observed=new List<string?>();var permitted=new List<string?>();var seen=new HashSet<ObjectId>();bool reactorsProved=true;
        // Posting a new resident record may register its canonical owning
        // dictionary. No copied source consumer or arbitrary native class/name
        // is admitted: the existing helper proves the exact local reverse link.
        using(var links=result.GetPersistentReactorIds())foreach(ObjectId link in links)
        {
            cancel();observed.Add(Id(link));
            if(seen.Add(link)&&CadNativeStyleDependencies349.OwningDictionary(result,result.Name,link,db.TableStyleDictionaryId,db,tr,cancel))
                permitted.Add(Id(link));
            else reactorsProved=false;
        }
        bool reactorSnapshotMatches=Exact(actual.Metadata.GetProperty("reactors"),Wire.Element(observed));
        if(xdataRetained&&extensionAbsent&&reactorsProved&&reactorSnapshotMatches)return;
        var expected=Wire.Element(new{xdata=resolved.Before.Metadata.GetProperty("xdata"),extension_handle=(string?)null,
            reactors=new{captured=permitted.ToArray(),current=permitted.ToArray()}});
        var current=Wire.Element(new{xdata=actual.Metadata.GetProperty("xdata"),extension_handle=actual.Metadata.GetProperty("extension_handle"),
            reactors=new{captured=actual.Metadata.GetProperty("reactors"),current=observed.ToArray()}});
        throw new DomainException("NATIVE_TABLE_STYLE_MISMATCH","Native style clone did not retain XData or gained unowned dependencies.",false,
            new{source_handle=Id(resolved.Source),created_style_handle=Id(result.ObjectId),owning_dictionary_handle=Id(db.TableStyleDictionaryId),
                xdata_retained=xdataRetained,extension_absent=extensionAbsent,reactors_proved=reactorsProved,reactor_snapshot_matches=reactorSnapshotMatches,
                actual_reactor_count=observed.Count,proved_owner_dictionary_reactor_count=permitted.Count,
                metadata_difference=CadTableStyleCloneDiagnostics367.Capture(resolved.Edit.Operation,Id(resolved.Source)!,expected,current,cancel,
                    "resident_TableStyle_clone_metadata_after_requested_setters","retained_source_xdata_no_extension_and_exact_proved_owner_dictionary_reactors",
                    "actual_created_style_xdata_extension_and_reactors")});
    }
    internal static Applied Apply(StructureWriteArguments args,int index,Resolved resolved,Prepared prepared,Database db,Transaction tr,WriteJob job)
    {
        Action cancel=job.CheckCancellation;VerifySource(resolved,db,tr,cancel);var warnings=prepared.Warnings.ToList();
        bool creating=resolved.Edit.Operation=="create_table_style";TableStyle result;ObjectId id;
        if(creating)
        {
            cancel();id=prepared.DetachedStyle.PostTableStyleToDatabase(db,resolved.Edit.Name!);result=prepared.DetachedStyle;
            Require(!id.IsNull&&id==result.ObjectId,"Native style posting did not return its exact new resident identity.","NATIVE_TABLE_STYLE_MISMATCH");
            tr.AddNewlyCreatedDBObject(result,true);
            Require(LocalName(result,db,tr,cancel)==resolved.Edit.Name&&db.Tablestyle==resolved.CurrentStyle,
                "Native style posting did not establish its exact local owning dictionary/current default.","NATIVE_TABLE_STYLE_MISMATCH");
            var posted=Properties(result,cancel);
            if(!Exact(prepared.PreservedSource,posted))throw new DomainException("NATIVE_TABLE_STYLE_MISMATCH","Native posting changed retained source defaults before requested setters.",false,
                CadTableStyleCloneDiagnostics367.Capture(resolved.Edit.Operation,Id(resolved.Source)!,Wire.Element(prepared.PreservedSource),Wire.Element(posted),cancel,
                    "resident_TableStyle_posted_before_requested_setters","proved_detached_source_copy_defaults","actual_posted_source_copy_defaults"));
            Set(result,resolved,cancel);
        }
        else
        {
            result=(TableStyle)tr.GetObject(resolved.Source,OpenMode.ForWrite);id=result.ObjectId;Set(result,resolved,cancel);
        }
        Require(LocalName(result,db,tr,cancel)==(creating?resolved.Edit.Name:resolved.SourceName)&&db.Tablestyle==resolved.CurrentStyle,
            "Native TableStyle owner/name/current document default changed unexpectedly.","NATIVE_TABLE_STYLE_MISMATCH");
        var actual=Capture(result,tr,cancel);RequestedAndRetained(resolved.Before.Properties,actual.Properties,resolved.Edit.Properties,cancel,resolved.Edit.Operation,Id(resolved.Source)!,"resident_TableStyle_after_requested_setters");
        // Complete requested/retained defaults above are validated in the
        // actual owning database, never against detached requested setters.
        if(creating)
        {
            Require(Capture((TableStyle)tr.GetObject(resolved.Source,OpenMode.ForRead),tr,cancel).Token==resolved.Before.Token,"Native style posting changed its source.","NATIVE_TABLE_STYLE_MISMATCH");
            VerifyCreatedMetadata(result,resolved,actual,db,tr,cancel);
            VerifyBorrowed(resolved,db,tr,cancel,warnings.Add);
            CadTableStyleCloneReferences365.Verify(resolved.CreationReferences,(TableStyle)tr.GetObject(resolved.Source,OpenMode.ForRead),db,tr,cancel);
        }
        else Require(Exact(resolved.Before.Metadata,actual.Metadata),"Native style edit changed identity/owned metadata or persistent dependencies.","NATIVE_TABLE_STYLE_MISMATCH");
        // Existing native glyph helpers consume actual resident named/role
        // defaults and retained source strings before explicit Table layout.
        foreach(var consumer in resolved.Consumers)
        {cancel();warnings.AddRange(CheckFonts((Table)tr.GetObject(consumer.Id,OpenMode.ForRead),consumer,resolved,actual.Properties,db,tr,cancel));}
        foreach(var consumer in resolved.IndexedConsumers)
        {cancel();warnings.AddRange(CadTableStyleIndexedConsumers367.CheckFonts(consumer,resolved.Before.Properties,actual.Properties,resolved.Edit.Properties,db,tr,cancel));}
        foreach(var consumer in resolved.Consumers)
        {
            cancel();var table=(Table)tr.GetObject(consumer.Id,OpenMode.ForWrite);table.GenerateLayout();cancel();table.RecomputeTableBlock(true);cancel();
            Snapshot? after=null;Index.Snapshot? contents=null;
            try
            {
                after=CadTableEditingReader.Capture(table,cancel,requireWorldXy:false,allowFormattedText:true,allowNumeric:true,allowFormulas:true);
                contents=Index.Capture(table,tr,cancel);
                CadTableStyleConsumerPreservation375.VerifyLegacyMetadata(consumer,after,Metadata(table,tr,cancel),contents,cancel);
                warnings.AddRange(CadTableStyleConsumerPreservation375.VerifySingle(consumer,after,contents,
                    resolved.Before.Properties,actual.Properties,resolved.Edit.Properties,cancel));
                VerifyTextInheritance(table,consumer,after,resolved,actual.Properties,cancel);
                CadTableFormulaEditor343.ObserveEvaluation(consumer.Before,after,cancel,warnings);
                warnings.AddRange(CheckFonts(table,consumer,resolved,actual.Properties,db,tr,cancel,after));
            }
            catch(DomainException error)
            {
                if(CadTableStyleConsumerPreservation375.HasContext(error))throw;
                throw CadTableStyleConsumerPreservation375.Diagnostics(error,Id(consumer.Id)!,"single_consumer_after_native_layout",
                    new{consumer.Before,consumer.RowRoles},new{after,contents},cancel);
            }
        }
        foreach(var consumer in resolved.IndexedConsumers)
        {
            cancel();var table=(Table)tr.GetObject(consumer.Id,OpenMode.ForWrite);table.GenerateLayout();cancel();table.RecomputeTableBlock(true);cancel();
            CadTableStyleIndexedConsumers367.Consumer? after=null;
            try
            {
                after=CadTableStyleIndexedConsumers367.Capture(table,tr,cancel);
                Require(Exact(consumer.RowRoles,after.RowRoles),"Native style inheritance changed indexed row-role identities.","NATIVE_TABLE_STYLE_MISMATCH");
                warnings.AddRange(CadTableStyleIndexedConsumers367.Verify(consumer,after.Before,resolved.Before.Properties,actual.Properties,resolved.Edit.Properties,cancel));
                warnings.AddRange(CadTableStyleIndexedConsumers367.CheckFonts(consumer,resolved.Before.Properties,actual.Properties,resolved.Edit.Properties,db,tr,cancel,after.Before));
            }
            catch(DomainException error)
            {
                if(CadTableStyleConsumerPreservation375.HasContext(error))throw;
                throw CadTableStyleConsumerPreservation375.Diagnostics(error,Id(consumer.Id)!,"indexed_consumer_after_native_layout",
                    new{consumer.Before,consumer.RowRoles},new{after},cancel);
            }
        }
        var refreshedProperties=Properties(result,cancel);var refreshedMetadata=Metadata(result,tr,cancel);
        if(db.Tablestyle!=resolved.CurrentStyle||!Exact(actual.Properties,refreshedProperties)||!Exact(actual.Metadata,refreshedMetadata))
            throw CadTableStyleConsumerPreservation375.Diagnostics(new DomainException("NATIVE_TABLE_STYLE_MISMATCH","Native table refresh changed its reusable style/current default."),
                Id(id)!,"reusable_style_after_all_consumer_layouts",new{current_style=Id(resolved.CurrentStyle),actual.Properties,actual.Metadata},
                new{current_style=Id(db.Tablestyle),properties=refreshedProperties,metadata=refreshedMetadata},cancel);
        var styleRef=new EntityRef(args.BridgeId,args.DocumentId,Id(id)!);var sourceRef=new EntityRef(args.BridgeId,args.DocumentId,Id(resolved.Source)!);
        var unique=resolved.Consumers.Any(consumer=>consumer.Before.Values.Any(value=>value?.formula!=null))?
            CadTableFormulaEditor343.BoundedWarnings(warnings,cancel):warnings.Distinct(StringComparer.Ordinal).ToArray();
        var aliases=ChangedNames(resolved.Before.Properties,actual.Properties,cancel);
        return new(new(index,resolved.Edit.Operation,styleRef,"table_style",result.Name,creating),new(index,styleRef,sourceRef,actual.Token,resolved.Consumers.Length+resolved.IndexedConsumers.Length,aliases.Length==0?null:aliases,unique.Length==0?null:unique));
    }
    internal static TableStyleRead337 ReadProperties(TableStyle style,Database db,Transaction tr,Action cancel)
    {
        var properties=Properties(style,cancel);TableStyleEditing337 editing;
        try{LocalName(style,db,tr,cancel);var state=Capture(style,tr,cancel);Require(!style.HasFields,"Field-bearing TableStyle needs its owning adapter.");editing=new(true,state.Token);}
        catch(OperationCanceledException){throw;}
        catch(DomainException error){editing=new(false,null,error.Error.code+": "+error.Error.message);}
        catch(System.Exception error){editing=new(false,null,"Native TableStyle editing metadata unavailable: "+error.GetType().Name);}
        return new(properties.description,properties.horizontal_margin,properties.vertical_margin,
            properties.rows.Select(row=>new TableStyleRowRead337(row.row_type,row.text_height,row.alignment,row.text_style_handle,row.content_color,row.background_none,row.background_color,row.grids)).ToArray(),editing,
            properties.named_cell_styles.EnumerateArray().Select(cell=>cell.GetProperty("name").GetString()!).ToArray(),
            CadNativeStyleLifecycle352.Describe(style,db,tr,cancel));
    }
    internal static (TableStyleRead337? Value,string? Reason) TryReadProperties(TableStyle style,Database db,Transaction tr,Action cancel)
    {
        try
        {
            var value=ReadProperties(style,db,tr,cancel);Require(JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json).Length<=Wire.PayloadBudget-16384,
                "Compact TableStyle metadata exceeds the actual response-byte budget.","RESPONSE_LIMIT_EXCEEDED");return(value,null);
        }
        catch(OperationCanceledException){throw;}
        catch(DomainException error){return(null,error.Error.code+": "+(error.Error.message.Length<=1024?error.Error.message:"Native TableStyle metadata unavailable; reason exceeds diagnostic byte allowance."));}
        catch(System.Exception error){return(null,"Native TableStyle metadata unavailable: "+error.GetType().Name);}
    }
}
