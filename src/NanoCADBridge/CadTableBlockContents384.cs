using System.Text;
using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using static NanoCADBridge.CadTableContentsReader364;

namespace NanoCADBridge;

// Existing local static definition -> one indexed native Table block binding.
// Definitions are read/retained, never cloned, rewritten, erased or reparented.
internal static class CadTableBlockContents384
{
    internal sealed record AttributeSource(ObjectId Id,string Handle,string Tag,bool Constant,string Text,ObjectId Font);
    internal sealed record Definition(ObjectId Id,string Handle,JsonElement State,Dictionary<string,AttributeSource> Attributes);
    internal sealed record Selection(Dictionary<string,Definition> Definitions,string Layout);
    static bool Exact(object? a,object? b)=>JsonSerializer.Serialize(a,Wire.Json)==JsonSerializer.Serialize(b,Wire.Json);
    static bool Live(ObjectId id)=>!id.IsNull&&id.IsValid&&!id.IsErased&&!id.IsEffectivelyErased;
    static string Id(ObjectId id)
    {Require(Live(id),"A native block-content identity is unavailable.");return Validation.NormalizeHandle(id.Handle.ToString());}
    static string? Ref(ObjectId id)=>id.IsNull?null:Id(id);
    static bool Local(DBObject item,Database db)=>db.UnmanagedObject!=IntPtr.Zero&&item.Database?.UnmanagedObject==db.UnmanagedObject;
    static void Require(bool value,string message,object? details=null)
    {if(!value)throw new DomainException("UNSUPPORTED_TABLE_BLOCK_CONTENT",message,false,details);}
    static JsonElement Metadata(DBObject item,Transaction tr,Action cancel)
    {
        cancel();using var xdata=item.XData;using var reactors=item.GetPersistentReactorIds();var budget=new ModelInventoryBudget(cancel);
        return Wire.Element(new{handle=Id(item.ObjectId),owner_handle=Id(item.OwnerId),runtime_type=item.GetType().FullName,
            has_fields=item.HasFields,extension=Ref(item.ExtensionDictionary),xdata=xdata==null?null:NativeOwnedMetadata.Buffer(xdata,cancel,value=>budget.Add(value)),
            reactors=reactors.Cast<ObjectId>().Select(Id).OrderBy(value=>value,StringComparer.Ordinal).ToArray(),owned=NativeOwnedMetadata.Capture(item,tr,cancel)});
    }
    internal static Definition Capture(string handle,Database db,Transaction tr,Action cancel)
    {
        cancel();var id=CadStructureWriter.Find(db,handle);var item=tr.GetObject(id,OpenMode.ForRead);
        Require(item.GetType()==typeof(BlockTableRecord)&&Local(item,db)&&item.ObjectId==id&&item.OwnerId==db.BlockTableId,
            "Select an existing local block-table definition.",new{handle,runtime_type=item.GetType().FullName,owner=Ref(item.OwnerId)});
        var block=(BlockTableRecord)item;
        Require(!block.IsAnonymous&&!block.IsDynamicBlock&&!block.IsLayout&&!block.IsDependent&&!block.IsFromExternalReference&&!block.IsFromOverlayReference,
            "Block contents support ordinary named static local definitions; anonymous, dynamic, layout and external definitions need their owning adapter.",new{handle,block.Name});
        Require(!block.HasFields,"A Field-bearing source definition needs its stored Field adapter.",new{handle});
        var attributes=new Dictionary<string,AttributeSource>(StringComparer.Ordinal);var members=new List<object>();var seen=new HashSet<ObjectId>();var budget=new ModelInventoryBudget(cancel);
        foreach(ObjectId member in block)
        {
            cancel();Require(Live(member)&&seen.Add(member),"Source block membership is unavailable or repeated.",new{handle,member=Ref(member)});
            var value=tr.GetObject(member,OpenMode.ForRead);
            Require(value is Entity&&Local(value,db)&&value.ObjectId==member&&value.OwnerId==id,
                "A source member has no exact native definition owner.",new{handle,member=Id(member),owner=Ref(value.OwnerId),runtime_type=value.GetType().FullName});
            var entity=(Entity)value;var type=entity.GetType();
            Require(type==typeof(Line)||type==typeof(Circle)||type==typeof(Arc)||type==typeof(Ellipse)||type==typeof(Polyline)||type==typeof(Spline)||
                type==typeof(DBText)||type==typeof(MText)||type==typeof(AttributeDefinition),
                "This source block member needs its geometry/ownership adapter; no unobserved member is omitted.",new{handle,member=Id(member),runtime_type=type.FullName});
            Require(!entity.HasFields&&entity.Annotative!=AnnotativeStates.True,"Field-bearing or annotative source members need their owning adapter.",new{handle,member=Id(member),runtime_type=type.FullName});
            object? attribute=null;
            if(entity is AttributeDefinition source)
            {
                Require(!source.IsMTextAttributeDefinition,"Multiline block attribute values require their native cell-block adapter; ordinary single-line attributes remain supported.",new{handle,attribute=Id(member),source.Tag});
                var entry=new AttributeSource(member,Id(member),source.Tag,source.Constant,source.TextString,source.TextStyleId);
                Require(attributes.TryAdd(entry.Handle,entry),"Source attribute definition identity repeats.");
                attribute=new{source.Tag,source.Constant,source.Invisible,source.Prompt,source.Preset,source.Verifiable,source.LockPositionInBlock,font=Ref(source.TextStyleId)};
            }
            var summary=EntityAdapter.Summary(entity,ObjectId.Null,"00000000-0000-0000-0000-000000000000","00000000-0000-0000-0000-000000000000");
            cancel();var geometry=EntityAdapter.Detail(entity,summary,CancellationToken.None);cancel();
            var state=new{metadata=Metadata(entity,tr,cancel),geometry,properties=CadBlockMover.PreservedProperties(entity),attribute};budget.Add(state);members.Add(state);
        }
        var result=Wire.Element(new{metadata=Metadata(block,tr,cancel),block.Name,origin=new[]{block.Origin.X,block.Origin.Y,block.Origin.Z},
            units=(int)block.Units,scaling=(int)block.BlockScaling,block.Explodable,members});budget.Add(result);
        return new(id,Id(id),result,attributes);
    }
    internal static Definition Remember(Selection selection,string handle,Database db,Transaction tr,Action cancel)
    {if(!selection.Definitions.TryGetValue(handle,out var value))selection.Definitions.Add(handle,value=Capture(handle,db,tr,cancel));return value;}
    internal static JsonElement Attributes(Definition source,IReadOnlyDictionary<string,string> selected)
    {
        Require(selected.Keys.All(source.Attributes.ContainsKey),"A selected attribute is not an actual member of this block definition.");
        return Wire.Element(source.Attributes.Values.Select(attribute=>new{definition_handle=attribute.Handle,owner_handle=source.Handle,tag=attribute.Tag,constant=attribute.Constant,
            value=selected.GetValueOrDefault(attribute.Handle,attribute.Text)}).ToArray());
    }
    internal static void Literal(Definition source,string definition,string text,Transaction tr)
    {
        Require(source.Attributes.TryGetValue(definition,out var attribute)&&!attribute.Constant,"Select an actual nonconstant attribute definition in this cell's block.",new{block=source.Handle,attribute_definition_handle=definition});
        Require(Live(attribute!.Font),"Selected source attribute has no current native text style.");
        var owner=tr.GetObject(source.Id,OpenMode.ForRead);var db=owner.Database!;var font=tr.GetObject(attribute.Font,OpenMode.ForRead);
        Require(font.GetType()==typeof(TextStyleTableRecord)&&Local(font,db)&&font.OwnerId==db.TextStyleTableId,"Selected source attribute font is not an actual local text-style member.");
        FontCoverage.Require(tr,attribute!.Font,text);
    }
    internal static JsonElement Change(JsonElement current,Definition source,string definition,string text,Transaction tr)
    {
        Literal(source,definition,text,tr);bool found=false;var values=new List<object>();
        foreach(var value in current.EnumerateArray())
        {
            string id=value.GetProperty("definition_handle").GetString()!;bool selected=id==definition;found|=selected;
            if(selected)TableEditingValidation.Literal(value.GetProperty("value"),"current_native_block_attribute");
            values.Add(new{definition_handle=id,owner_handle=value.GetProperty("owner_handle").GetString(),tag=value.GetProperty("tag").GetString(),
                constant=value.GetProperty("constant").GetBoolean(),value=selected?text:value.GetProperty("value").GetString()});
        }
        Require(found,"Selected attribute has no current native cell-block value.");return Wire.Element(values);
    }
    // The indexed Table binding identifies the retained source definition.
    // A generated INSERT may instead use a private anonymous render definition;
    // admit that definition only through its actual closed Table-owned graph.
    static string Layout(Table table,Snapshot snapshot,Transaction tr,Action cancel)
    {
        var db=table.Database!;var id=table.BlockTableRecord;
        Require(Live(id),"Block contents require the Table's current resident layout storage.");
        var item=tr.GetObject(id,OpenMode.ForRead);
        Require(item.GetType()==typeof(BlockTableRecord)&&Local(item,db)&&item.OwnerId==db.BlockTableId,"Table layout block ownership is unavailable.");
        var block=(BlockTableRecord)item;
        Require(block.IsAnonymous&&!block.IsLayout&&!block.IsFromExternalReference,"Selected Table has no ordinary anonymous native layout block.");
        using var refs=block.GetBlockReferenceIds(true,false);var references=refs.Cast<ObjectId>().ToArray();
        Require(references.Length==1&&references[0]==table.ObjectId,"Table layout has no exclusive actual reverse Table reference.");
        var bindings=snapshot.Contents.SelectMany(slots=>slots).Where(slot=>slot.block_handle!=null).Select(slot=>slot.block_handle!).ToHashSet(StringComparer.Ordinal);
        var members=new HashSet<ObjectId>();var values=new List<DBObject>();
        foreach(ObjectId member in block)
        {
            cancel();Require(Live(member)&&members.Add(member),"Table layout membership is unavailable or repeated.");var value=tr.GetObject(member,OpenMode.ForRead);
            Require(Local(value,db)&&value.ObjectId==member&&value.OwnerId==id,"Layout member ownership is inconsistent.");values.Add(value);
        }
        var renderDefinitions=new HashSet<ObjectId>();
        foreach(var value in values)
        {
            cancel();if(value.GetType()!=typeof(BlockReference))continue;
            var reference=(BlockReference)value;
            Require(!reference.IsDynamicBlock,"A generated cell-block reference is dynamic.",new{member=Id(reference.ObjectId),definition=Ref(reference.BlockTableRecord)});
            if(!bindings.Contains(Id(reference.BlockTableRecord))&&renderDefinitions.Add(reference.BlockTableRecord))
            {
                Require(bindings.Count!=0,"A generated block reference has no authoritative native Table block-slot binding.",new{member=Id(reference.ObjectId),definition=Ref(reference.BlockTableRecord)});
                RenderDefinition(reference,block,members,tr,cancel);
            }
            Plain(reference,tr,cancel);
            foreach(ObjectId attributeId in reference.AttributeCollection)
            {
                cancel();var attribute=tr.GetObject(attributeId,OpenMode.ForRead);
                Require(attribute.GetType()==typeof(AttributeReference)&&Local(attribute,db)&&attribute.OwnerId==reference.ObjectId,"A generated cell-block attribute has no exact native reference owner.");Plain(attribute,tr,cancel);
            }
        }
        return Id(id);
    }
    static void RenderDefinition(BlockReference reference,BlockTableRecord layout,HashSet<ObjectId> layoutMembers,Transaction tr,Action cancel)
    {
        cancel();var db=layout.Database!;var id=reference.BlockTableRecord;var item=tr.GetObject(id,OpenMode.ForRead);
        Require(item.GetType()==typeof(BlockTableRecord)&&Local(item,db)&&item.ObjectId==id&&item.OwnerId==db.BlockTableId,
            "A generated cell-block definition has no exact local block-table owner.",new{member=Id(reference.ObjectId),definition=Id(id),runtime_type=item.GetType().FullName,owner=Ref(item.OwnerId)});
        var block=(BlockTableRecord)item;
        Require(block.IsAnonymous&&!block.IsDynamicBlock&&!block.IsLayout&&!block.IsDependent&&!block.IsFromExternalReference&&!block.IsFromOverlayReference,
            "An unmatched generated definition is not a local anonymous render block.",new{member=Id(reference.ObjectId),definition=Id(id)});
        var tableItem=tr.GetObject(db.BlockTableId,OpenMode.ForRead);
        Require(tableItem.GetType()==typeof(BlockTable)&&Local(tableItem,db)&&tableItem.ObjectId==db.BlockTableId,"Native block-table identity is unavailable.");
        var symbols=(BlockTable)tableItem;
        Require(symbols.Has(block.Name)&&symbols[block.Name]==id,"Anonymous render definition lacks exact current block-table reverse membership.",new{definition=Id(id)});
        Plain(block,tr,cancel);
        using var references=block.GetBlockReferenceIds(true,false);var seenReferences=new HashSet<ObjectId>();
        foreach(ObjectId refId in references)
        {
            cancel();Require(Live(refId)&&seenReferences.Add(refId)&&layoutMembers.Contains(refId),
                "Anonymous render definition is shared outside the selected Table layout or its references are unavailable.",new{definition=Id(id),reference=Ref(refId),layout=Id(layout.ObjectId)});
            var current=tr.GetObject(refId,OpenMode.ForRead);
            Require(current.GetType()==typeof(BlockReference)&&Local(current,db)&&current.ObjectId==refId&&current.OwnerId==layout.ObjectId&&((BlockReference)current).BlockTableRecord==id,
                "Anonymous render definition has an unproved forward/reverse native reference.",new{definition=Id(id),reference=Id(refId),owner=Ref(current.OwnerId),runtime_type=current.GetType().FullName});
        }
        Require(seenReferences.Contains(reference.ObjectId),"Anonymous render definition omits its current Table-owned reference.",new{definition=Id(id),member=Id(reference.ObjectId)});
        var members=new HashSet<ObjectId>();
        foreach(ObjectId memberId in block)
        {
            cancel();Require(Live(memberId)&&members.Add(memberId),"Anonymous render definition membership is unavailable or repeated.");var current=tr.GetObject(memberId,OpenMode.ForRead);
            Require(current is Entity&&Local(current,db)&&current.ObjectId==memberId&&current.OwnerId==id,
                "Anonymous render member lacks its exact native owner.",new{definition=Id(id),member=Id(memberId),owner=Ref(current.OwnerId),runtime_type=current.GetType().FullName});
            var type=current.GetType();
            Require(type==typeof(Line)||type==typeof(Circle)||type==typeof(Arc)||type==typeof(Ellipse)||type==typeof(Polyline)||type==typeof(Spline)||type==typeof(DBText)||type==typeof(MText)||type==typeof(AttributeDefinition),
                "Anonymous render member requires its stored-data adapter; nested references and unobserved classes are not normalized.",new{definition=Id(id),member=Id(memberId),runtime_type=type.FullName});
            Require(((Entity)current).Annotative!=AnnotativeStates.True&&!(current is AttributeDefinition attribute&&attribute.IsMTextAttributeDefinition),
                "Annotative or multiline attribute render data requires its owning adapter.",new{member=Id(memberId)});Plain(current,tr,cancel);
        }
    }
    static void Plain(DBObject value,Transaction tr,Action cancel)
    {
        var data=Metadata(value,tr,cancel);
        Require(!value.HasFields&&value.ExtensionDictionary.IsNull&&data.GetProperty("xdata").ValueKind==JsonValueKind.Null&&data.GetProperty("reactors").GetArrayLength()==0,
            "Generated cell-block display contains retained Field, XData, extension data or persistent dependencies requiring its owning adapter.",
            new{handle=Id(value.ObjectId),owner=Ref(value.OwnerId),runtime_type=value.GetType().FullName});
    }
    internal static Selection Begin(Table table,Snapshot before,Transaction tr,Action cancel)=>new(new(StringComparer.Ordinal),Layout(table,before,tr,cancel));
    internal static void Verify(Selection selection,Table table,Snapshot snapshot,Database db,Transaction tr,Action cancel)
    {
        Require(Layout(table,snapshot,tr,cancel)==selection.Layout,"Block contents editing changed the retained native Table layout block identity.");
        foreach(var source in selection.Definitions.Values)
        {cancel();Require(Exact(source.State,Capture(source.Handle,db,tr,cancel).State),"Native cell-block editing changed source definition geometry, members, attributes or metadata.",new{block_handle=source.Handle});}
    }
    static string Bound(string value)
    {
        var result=new StringBuilder();int bytes=0;
        foreach(var rune in value.EnumerateRunes()){if(bytes+rune.Utf8SequenceLength>1536)return result+" [diagnostic omitted]";bytes+=rune.Utf8SequenceLength;result.Append(rune.ToString());}return result.ToString();
    }
    internal static object? Read(Table table,Snapshot snapshot,CellValue cell,Slot slot,Transaction tr,Action cancel)
    {
        if(slot.block_handle==null)return null;
        try
        {
            var source=Capture(slot.block_handle,table.Database!,tr,cancel);_=Layout(table,snapshot,tr,cancel);
            Require(Anchor(cell)&&cell.content_editable,"Selected native block slot is protected or is not a merged anchor.");
            return new{eligible=true,reason=(string?)null,scope="actual_local_static_definition; indexed_native_cell_binding_and_literal_single_line_attribute_values; definition_not_edited_or_erased",
                editable_attribute_definition_handles=source.Attributes.Values.Where(attribute=>!attribute.Constant).Select(attribute=>attribute.Handle).ToArray()};
        }
        catch(OperationCanceledException){throw;}
        catch(DomainException error){return new{eligible=false,reason=Bound(error.Error.code+": "+error.Error.message+" "+JsonSerializer.Serialize(error.Error.details,Wire.Json)),scope="native_block_content_adapter_unavailable",editable_attribute_definition_handles=Array.Empty<string>()};}
        catch(Exception error){return new{eligible=false,reason=Bound("Native block-content observation unavailable: "+error.GetType().Name+"/"+(error is Teigha.Runtime.Exception native?native.ErrorStatus.ToString():"not_native")+": "+error.Message),scope="native_block_content_adapter_unavailable",editable_attribute_definition_handles=Array.Empty<string>()};}
    }
}
