using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// Identity/default selection only. No cloned preview setter, consumer setter,
// regeneration, style reassignment or loose dependency-name classification.
internal static class CadNativeStyleLifecycle352
{
    internal sealed record Values(JsonElement Defaults,JsonElement Metadata,string Token);
    internal sealed record Defaults(ObjectId Table,ObjectId Leader,ObjectId Text,ObjectId Dimension,ObjectId Layer);
    internal sealed record Consumer(ObjectId Id,JsonElement Value,bool Opaque);
    internal sealed record Resolved(NativeStyleLifecycle352 Edit,ObjectId Target,ObjectId Dictionary,Values Before,
        JsonElement Inventory,Defaults Current,Consumer[] Consumers);
    static void Need([DoesNotReturnIf(false)] bool value,string message,string code="NATIVE_STYLE_STATE_CHANGED")
    {if(!value)throw new DomainException(code,message);}
    static bool Live(ObjectId id)=>!id.IsNull&&id.IsValid&&!id.IsErased&&!id.IsEffectivelyErased;
    static string? Id(ObjectId id)=>id.IsNull?null:Validation.NormalizeHandle(id.Handle.ToString());
    static bool Exact(JsonElement a,JsonElement b)=>a.GetRawText()==b.GetRawText();
    static double N(double value)=>double.IsFinite(value)?value:throw new DomainException("INVALID_GEOMETRY","Native identity consumer anchor is nonfinite.");
    static double[] P(Point3d value)=>[N(value.X),N(value.Y),N(value.Z)];
    static double[] V(Vector3d value)=>[N(value.X),N(value.Y),N(value.Z)];
    static string Name(DBObject style)=>style switch{TableStyle table=>table.Name,MLeaderStyle leader=>leader.Name,_=>throw new DomainException("OUT_OF_SCOPE","Expected an exact native annotation style.")};
    static ObjectId Dictionary(DBObject style,Database db)=>style is TableStyle?db.TableStyleDictionaryId:db.MLeaderStyleDictionaryId;
    static Defaults Current(Database db)=>new(db.Tablestyle,db.MLeaderstyle,db.Textstyle,db.Dimstyle,db.Clayer);
    static Values Capture(DBObject style,Transaction tr,Action cancel)
    {
        cancel();var defaults=style is TableStyle table?Wire.Element(CadTableStyleWriter337.Properties(table,cancel)):
            CadMLeaderStyleWriter337.Values((MLeaderStyle)style,cancel);
        var metadata=CadNativeStyleIdentityMetadata352.Metadata(style,style.Database!,tr,cancel);
        var stored=Wire.Element(new{name=Name(style),defaults,metadata});new ModelInventoryBudget(cancel).Add(stored);
        return new(defaults,metadata,Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(stored,Wire.Json))));
    }
    static DBObject Identity(ObjectId id,ObjectId dictionaryId,Database db,Transaction tr,Action cancel)
    {
        cancel();Need(Live(id)&&Live(dictionaryId),"Native style/dictionary is unavailable.","STYLE_UNAVAILABLE");
        var style=tr.GetObject(id,OpenMode.ForRead);
        Need((dictionaryId==db.TableStyleDictionaryId?style.GetType()==typeof(TableStyle):style.GetType()==typeof(MLeaderStyle))&&
            style.OwnerId==dictionaryId&&style.Database?.UnmanagedObject==db.UnmanagedObject&&BlockWriteValidation.OrdinaryName(Name(style)),
            "Select an exact ordinary local style in the actual native dictionary.","OUT_OF_SCOPE");
        Need(CadNativeStyleDependencies349.OwningDictionary(style,Name(style),dictionaryId,dictionaryId,db,tr,cancel),
            "Native root/dictionary/name identity is inconsistent.","STYLE_UNAVAILABLE");
        return style;
    }
    static JsonElement Inventory(ObjectId dictionaryId,Transaction tr,Action cancel)
    {
        var dictionary=(DBDictionary)tr.GetObject(dictionaryId,OpenMode.ForRead);var items=new List<object>();var budget=new ModelInventoryBudget(cancel);
        foreach(DBDictionaryEntry entry in dictionary)
        {
            cancel();Need(Live(entry.Value),"A native dictionary member is unavailable.","STYLE_UNAVAILABLE");
            var item=tr.GetObject(entry.Value,OpenMode.ForRead);var value=new{handle=Id(entry.Value),name=entry.Key,
                owner=Id(item.OwnerId),native_type=item.GetType().FullName};budget.Add(value);items.Add(value);
        }
        return Wire.Element(items.OrderBy(value=>JsonSerializer.Serialize(value,Wire.Json),StringComparer.Ordinal));
    }
    static JsonElement RenamedInventory(Resolved before)
    {
        // Derive the single allowed name change only from the retained snapshot.
        // Re-reading the current dictionary for an expectation would conceal
        // unrelated identity/name changes caused by the native setter.
        var entries=JsonNode.Parse(before.Inventory.GetRawText())!.AsArray();int matches=0;
        foreach(var entry in entries)
            if(entry!["handle"]!.GetValue<string>()==Id(before.Target))
            {entry["name"]=before.Edit.Name;matches++;}
        Need(matches==1,"The retained style dictionary snapshot has inconsistent target membership.");
        return Wire.Element(entries.Select(entry=>JsonSerializer.Deserialize<JsonElement>(entry!.ToJsonString(Wire.Json)))
            .OrderBy(entry=>entry.GetRawText(),StringComparer.Ordinal));
    }
    static Consumer[] Consumers(ObjectId target,bool table,Database db,Transaction tr,Action cancel)
    {
        var result=new List<Consumer>();var budget=new ModelInventoryBudget(cancel);
        foreach(ObjectId blockId in (BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead))
        {
            cancel();if(!Live(blockId))continue;var block=(BlockTableRecord)tr.GetObject(blockId,OpenMode.ForRead);
            foreach(ObjectId id in block)
            {
                cancel();if(!Live(id))continue;var item=tr.GetObject(id,OpenMode.ForRead);
                bool refers=table?item is Table value&&value.TableStyle==target:item is MLeader leader&&leader.MLeaderStyle==target;
                if(!refers)continue;
                Need(item is Entity entity&&CadNativeStyleDependencies349.OrdinaryConsumer(entity,table?typeof(Table):typeof(MLeader),db,tr,cancel),
                    "A used consumer has custom/external/inconsistent ownership; its owning adapter is required.","UNSUPPORTED_NATIVE_STYLE_CONSUMER");
                JsonElement? typed=null;bool opaque=item.HasFields;
                if(item is Table native)
                {
                    try{typed=Wire.Element(CadTableEditingReader.Capture(native,cancel,requireWorldXy:false,allowFormattedText:true,allowNumeric:true,allowFormulas:true));}
                    catch(DomainException error) when(error.Error.code=="UNSUPPORTED_TABLE_CONTENT"){cancel();opaque=true;}
                }
                else
                {
                    try
                    {var captured=CadMLeaderStyleWriter337.ConsumerState((MLeader)item,tr,cancel);typed=Wire.Element(new{captured.Preserved,captured.Effective,captured.PreviewLayout});}
                    catch(DomainException error) when(error.Error.code=="UNSUPPORTED_MLEADER_STYLE"||error.Error.code=="UNSUPPORTED_OWNED_METADATA"&&
                        (error.Error.message.StartsWith("Field-bearing owned metadata",StringComparison.Ordinal)||error.Error.message.StartsWith("Native owned data needs",StringComparison.Ordinal)||
                         error.Error.message.StartsWith("A native metadata value needs",StringComparison.Ordinal))){cancel();opaque=true;}
                }
                var root=(Entity)item;var metadata=CadNativeStyleIdentityMetadata352.Metadata(root,db,tr,cancel);
                // These root anchors and links are observed without child-edit
                // admission. Field/linked-derived contents/layout stay opaque.
                if(root is Table countTable)Need(countTable.Rows.Count>=0&&countTable.Columns.Count>=0,"Native Table topology counts are invalid.","INVALID_GEOMETRY");
                object anchors=root is Table t?new{position=P(t.Position),direction=V(t.Direction),normal=V(t.Normal),rows=t.Rows.Count,columns=t.Columns.Count}:
                    new{normal=V(((MLeader)root).Normal),content_type=((MLeader)root).ContentType.ToString(),annotation_scale=((MLeader)root).EnableAnnotationScale};
                var snapshot=Wire.Element(new{style_handle=Id(target),metadata,properties=EntityAdapter.StoredProperties(root),anchors,typed,
                    observation_scope=opaque?"root_ID_owner_style_display_readable_owned_data_typed_geometry_where_available_opaque_field_content_layout_unobserved":"typed_native_content_and_full_observed_layout"});
                budget.Add(new{handle=Id(id),snapshot});result.Add(new(id,snapshot,opaque));
            }
        }
        return result.ToArray();
    }
    static void Dependencies(DBObject style,Consumer[] consumers,ObjectId dictionary,Database db,Transaction tr,Action cancel)
    {
        var admitted=consumers.Select(item=>item.Id).ToHashSet();using var reactors=style.GetPersistentReactorIds();
        foreach(ObjectId id in reactors)
        {
            cancel();if(id==dictionary&&CadNativeStyleDependencies349.OwningDictionary(style,Name(style),id,dictionary,db,tr,cancel))continue;
            Need(admitted.Contains(id),"A style reactor is outside the actual dictionary and inspected retained native consumers.","UNSUPPORTED_NATIVE_STYLE_DEPENDENCY");
        }
    }
    internal static Resolved Resolve(NativeStyleLifecycle352 edit,Database db,Transaction tr,Action cancel)
    {
        bool table=NativeStyleLifecycleValidation352.Table(edit);var dictionary=table?db.TableStyleDictionaryId:db.MLeaderStyleDictionaryId;
        var target=CadStructureWriter.Find(db,edit.Handle);var style=Identity(target,dictionary,db,tr,cancel);
        Need(Name(style)==edit.ExpectedName,"Native style name differs from expected_name.","STYLE_NAME_MISMATCH");
        var before=Capture(style,tr,cancel);Need(before.Token==edit.StyleToken,"Native style changed; refresh lifecycle.style_token.","STYLE_STATE_CHANGED");
        var current=Current(db);
        if(NativeStyleLifecycleValidation352.Current(edit))
        {
            var expected=CadStructureWriter.Find(db,edit.ExpectedCurrentHandle!);
            Need(expected==(table?current.Table:current.Leader),"Current native selection differs from expected_current_handle.","CURRENT_STYLE_MISMATCH");
            var previous=tr.GetObject(expected,OpenMode.ForRead);
            Need(previous.OwnerId==dictionary&&previous.Database?.UnmanagedObject==db.UnmanagedObject,
                "The previous current style is outside this actual owning dictionary.","CURRENT_STYLE_MISMATCH");
        }
        else Need(!((DBDictionary)tr.GetObject(dictionary,OpenMode.ForRead)).Contains(edit.Name!),"Destination native style name already exists.","STYLE_EXISTS");
        var consumers=Consumers(target,table,db,tr,cancel);Dependencies(style,consumers,dictionary,db,tr,cancel);
        var result=new Resolved(edit,target,dictionary,before,Inventory(dictionary,tr,cancel),current,consumers);
        new ModelInventoryBudget(cancel).Add(new{before,result.Inventory,consumers=consumers.Select(value=>new{handle=Id(value.Id),value.Value}).ToArray()});return result;
    }
    internal static void Verify(Resolved before,Database db,Transaction tr,Action cancel,string code="STYLE_STATE_CHANGED",bool after=false)
    {
        bool table=NativeStyleLifecycleValidation352.Table(before.Edit),select=NativeStyleLifecycleValidation352.Current(before.Edit);
        var style=Identity(before.Target,before.Dictionary,db,tr,cancel);string expectedName=after&&!select?before.Edit.Name!:before.Edit.ExpectedName;
        Need(Name(style)==expectedName,"Retained native style name/dictionary identity differs.",code);
        var actual=Capture(style,tr,cancel);Need(Exact(before.Before.Defaults,actual.Defaults)&&Exact(before.Before.Metadata,actual.Metadata),
            "Native identity/default selection changed stored style defaults/templates/owned metadata.",code);
        Need(Exact(after&&!select?RenamedInventory(before):before.Inventory,
            Inventory(before.Dictionary,tr,cancel)),"Native style dictionary changed unrelated identities/names/owners.",code);
        var expected=after&&select?before.Current with{Table=table?before.Target:before.Current.Table,Leader=table?before.Current.Leader:before.Target}:before.Current;
        Need(Current(db)==expected,"Native identity/default selection changed an unexpected current record.",code);
        var consumers=Consumers(before.Target,table,db,tr,cancel).ToDictionary(value=>value.Id);
        Need(consumers.Count==before.Consumers.Length,"Native style consumer membership changed.",code);
        foreach(var prior in before.Consumers)
        {cancel();Need(consumers.TryGetValue(prior.Id,out var value)&&Exact(prior.Value,value.Value),"Native name/default selection changed retained consumer contents/layout/appearance/metadata.",code);}
        Dependencies(style,consumers.Values.ToArray(),before.Dictionary,db,tr,cancel);
    }
    internal static StructureChange Apply(StructureWriteArguments args,int index,Resolved source,Database db,Transaction tr,WriteJob job,List<string>? warnings)
    {
        Verify(source,db,tr,job.CheckCancellation);bool table=NativeStyleLifecycleValidation352.Table(source.Edit);
        warnings?.Add("NATIVE_STYLE_IDENTITY_OBSERVATION: Native identity/default selection retains observed stored defaults, root/owner/style links, readable owned metadata and typed layout where available. Opaque native/field-derived internals and individual automatic dynamic-column heights/equal-valued override masks are unobserved; no content setter or regeneration was requested.");
        if(NativeStyleLifecycleValidation352.Current(source.Edit))
        {if(table)db.Tablestyle=source.Target;else db.MLeaderstyle=source.Target;}
        else
        {
            // Installed TableStyle.set_Name throws NotImplementedException.
            // Its owning dictionary has the actual native rename setter.
            // MLeaderStyle.Name has its own real native setter. Use one route,
            // never both a style setter and a dictionary setter.
            if(table)((DBDictionary)tr.GetObject(source.Dictionary,OpenMode.ForWrite)).SetName(source.Edit.ExpectedName,source.Edit.Name!);
            else ((MLeaderStyle)tr.GetObject(source.Target,OpenMode.ForWrite)).Name=source.Edit.Name!;
        }
        job.CheckCancellation();Verify(source,db,tr,job.CheckCancellation,"NATIVE_STYLE_LIFECYCLE_MISMATCH",after:true);
        return new(index,source.Edit.Operation,new(args.BridgeId,args.DocumentId,source.Edit.Handle),
            NativeStyleLifecycleValidation352.RecordKind(source.Edit),Name(tr.GetObject(source.Target,OpenMode.ForRead)),false);
    }
    internal static void VerifyCommitted(IReadOnlyDictionary<int,Resolved> targets,Database db,Action cancel)
    {
        using var tr=db.TransactionManager.StartTransaction();
        foreach(var value in targets.Values){cancel();Verify(value,db,tr,cancel,"NATIVE_STYLE_LIFECYCLE_MISMATCH",after:true);}
    }
    internal static NativeStyleLifecycleRead352 Describe(DBObject style,Database db,Transaction tr,Action cancel)
    {
        string scope="same_native_style_ID_dictionary_current_defaults_observed_owned_data_and_consumers_typed_layout_where_available_opaque_field_internals_unobserved";
        try
        {
            var dictionary=Dictionary(style,db);_=Identity(style.ObjectId,dictionary,db,tr,cancel);var values=Capture(style,tr,cancel);
            bool table=style is TableStyle;return new(true,table?["rename_table_style","set_current_table_style"]:["rename_mleader_style","set_current_mleader_style"],
                values.Token,Id(table?db.Tablestyle:db.MLeaderstyle),scope);
        }
        catch(OperationCanceledException){throw;}
        catch(DomainException error){return new(false,[],null,null,scope,error.Error.code+": "+
            (error.Error.message.Length<=1024?error.Error.message:"Native lifecycle snapshot unavailable; diagnostic exceeds the compact reason allowance."));}
        catch(System.Exception error){return new(false,[],null,null,scope,"Native lifecycle snapshot unavailable: "+error.GetType().Name);}
    }
}
