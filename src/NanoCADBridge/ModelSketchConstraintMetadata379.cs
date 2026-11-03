using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.DatabaseServices;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// These are current identity/ownership proofs, never a class/name cache list.
// Opaque data at a directly bound native record is an unverified observation;
// XData, Fields, foreign links and unrelated/user-owned records stay protected.
internal static class ModelSketchConstraintMetadata379
{
    internal const string Warning="NATIVE_STORAGE_PRESERVATION_UNVERIFIED: Current selected native control/construction storage observations changed after solve. Actual owner/identity paths and all previously observed user XData, Field status, unrelated records and foreign references remain checked; changed directly bound opaque payload or newly owned storage is not classified as a cache or certified preserved. Bounded actual differences are reported. Inspect the rebuilt part; Undo/rollback is not guaranteed.";
    internal sealed record Binding(string native_id,string owner_document_id,string? raw_handle,string[] owner_path,bool proved,string reason);
    internal sealed record Observation(bool retained,bool changed,string reason,JsonElement design,JsonElement body);
    static readonly StringComparer Ids=StringComparer.OrdinalIgnoreCase;
    static JsonNode? Copy(JsonNode? value)=>value==null?null:JsonNode.Parse(value.ToJsonString());
    static string? Text(JsonNode? value)=>value?.GetValue<string>();
    static bool Exact(JsonNode? a,JsonNode? b)=>a?.ToJsonString()==b?.ToJsonString();
    static string Handle(JsonNode value)=>Text(value["common"]?["handle"])!;
    internal static Binding[] Bind(IEnumerable<McObjectId> ids,Db.ObjectId body,McDocument owner,McDocument drawing,DocumentState state,Db.Transaction tx,Action cancel)
    {
        var output=new List<Binding>();var budget=new ModelInventoryBudget(cancel);
        foreach(var id in ids.Distinct())
        {
            cancel();string? handle=null;var path=new List<string>();bool proved=false;string reason="no_exact_current_resident_storage_binding";
            long value=id.Handle;
            if(value!=0&&state.Document.Database.TryGetObjectId(new Db.Handle(value),out var raw)&&!raw.IsNull&&!raw.IsErased&&!raw.IsEffectivelyErased&&
                (McObjectId.FromHandle(raw.Handle.Value,owner.ID)==id||McObjectId.FromHandle(raw.Handle.Value,drawing.ID)==id))
            {
                handle=Validation.NormalizeHandle(raw.Handle.ToString());var child=tx.GetObject(raw,Db.OpenMode.ForRead);var seen=new HashSet<Db.ObjectId>();
                while(child.ObjectId!=body)
                {
                    cancel();if(child.Database.UnmanagedObject!=state.Document.Database.UnmanagedObject||!seen.Add(child.ObjectId)||child.OwnerId.IsNull||!child.OwnerId.IsValid||child.OwnerId.IsErased||child.OwnerId.IsEffectivelyErased)break;
                    var parent=tx.GetObject(child.OwnerId,Db.OpenMode.ForRead);bool reverse=parent.ExtensionDictionary==child.ObjectId;
                    if(parent.GetType()==typeof(Db.DBDictionary)&&parent is Db.DBDictionary dictionary)
                    {int count=0;foreach(Db.DBDictionaryEntry entry in dictionary){cancel();if(entry.Value==child.ObjectId)count++;}reverse=count==1;}
                    if(!reverse)break;path.Add(Validation.NormalizeHandle(parent.Handle.ToString()));child=parent;
                }
                proved=child.ObjectId==body&&child.Database.UnmanagedObject==state.Document.Database.UnmanagedObject;
                reason=proved?"exact_current_native_ID_roundtrip_and_selected_body_reverse_owner_path":"resident_binding_without_complete_selected_body_reverse_owner_path";
            }
            var item=new Binding(id.ToString(),owner.ID.ToString(),handle,path.ToArray(),proved,reason);budget.Add(item);output.Add(item);
        }
        return output.OrderBy(item=>item.native_id,Ids).ToArray();
    }
    // Preserve reference multiplicity. A current owned record or an actual
    // mapped selected native record may be added; a foreign/old edge cannot
    // disappear under an opaque-payload warning.
    static bool Links(JsonArray? before,JsonArray? after,HashSet<string> allowed,Action cancel)
    {
        if(before==null||after==null)return before==null&&after==null;
        var pending=before.Select(value=>value?.ToJsonString()??"null").ToList();
        foreach(var value in after)
        {
            cancel();string wire=value?.ToJsonString()??"null";int index=pending.IndexOf(wire);
            if(index>=0){pending.RemoveAt(index);continue;}
            string? handle=Text(value);if(handle==null||!allowed.Contains(handle))return false;
        }
        return pending.Count==0;
    }
    static JsonArray References(JsonNode? node,Action cancel)
    {
        var output=new JsonArray();var stack=new Stack<JsonNode>();if(node!=null)stack.Push(node);
        while(stack.Count>0)
        {
            cancel();var item=stack.Pop();
            if(item is JsonObject obj)
            {
                if(Text(obj["native_type"])=="ObjectId"){output.Add(Copy(obj["handle"]));continue;}
                foreach(var field in obj)if(field.Value!=null)stack.Push(field.Value);
            }
            else if(item is JsonArray array)foreach(var value in array)if(value!=null)stack.Push(value);
        }
        return output;
    }
    static bool OwnedPath(string handle,Dictionary<string,JsonNode> records,string root,string? extension,Action cancel)
    {
        var seen=new HashSet<string>(Ids);string child=handle;
        while(seen.Add(child)&&records.TryGetValue(child,out var item))
        {
            cancel();string? parent=Text(item["common"]?["owner_handle"]);
            if(Ids.Equals(parent,root))return Ids.Equals(child,extension);
            if(parent==null||!records.TryGetValue(parent,out var owner))return false;
            bool reverse=Ids.Equals(Text(owner["common"]?["extension_handle"]),child);
            if(owner["entries"] is JsonArray entries)
                reverse=entries.Count(entry=>Ids.Equals(Text(entry?["handle"]),child)&&entry?["owned"]?.GetValue<bool>()==true&&
                    Ids.Equals(Text(entry?["owner_handle"]),parent)&&Text(entry?["runtime_type"])==Text(item["common"]?["runtime_type"]))==1;
            if(!reverse)return false;child=parent;
        }
        return false;
    }
    static bool Metadata(JsonObject before,JsonObject after,string root,IReadOnlyDictionary<string,Binding> native,HashSet<string> mapped,Action cancel)
    {
        var old=before["owned"]!.AsArray().ToDictionary(item=>Handle(item!),item=>item!,Ids);
        var current=after["owned"]!.AsArray().ToDictionary(item=>Handle(item!),item=>item!,Ids);
        if(old.Keys.Any(handle=>!current.ContainsKey(handle)))return false;
        var added=current.Keys.Except(old.Keys,Ids).ToHashSet(Ids);var allowed=mapped.Concat(added).ToHashSet(Ids);
        var a=before["root"]!.AsObject();var b=after["root"]!.AsObject();string extensionKey=a.ContainsKey("extension")?"extension":"extension_handle";
        string? extension=Text(b[extensionKey]);
        foreach(string handle in current.Keys)if(!OwnedPath(handle,current,root,extension,cancel))return false;
        bool Common(JsonObject prior,JsonObject actual,string ext)
        {
            if(!Links(prior["reactors"]?.AsArray(),actual["reactors"]?.AsArray(),allowed,cancel))return false;
            actual["reactors"]=Copy(prior["reactors"]);
            if(!Exact(prior[ext],actual[ext]))
            {
                string? child=Text(actual[ext]);
                if(prior[ext]!=null||child==null||!added.Contains(child))return false;
                actual[ext]=null;
            }
            return Exact(prior,actual);
        }
        if(!Common(a,b,extensionKey))return false;
        foreach(var pair in old)
        {
            cancel();var prior=pair.Value.AsObject();var actual=current[pair.Key].AsObject();
            if(!Common(prior["common"]!.AsObject(),actual["common"]!.AsObject(),"extension_handle"))return false;
            if(prior["entries"] is JsonArray entries)
            {
                if(actual["entries"] is not JsonArray observed)return false;
                var retained=new JsonArray();var remaining=observed.Select(entry=>entry!).ToList();
                foreach(var entry in entries)
                {
                    int index=remaining.FindIndex(value=>Exact(entry,value));if(index<0)return false;
                    retained.Add(Copy(remaining[index]));remaining.RemoveAt(index);
                }
                foreach(var entry in remaining)
                    if(entry["owned"]?.GetValue<bool>()!=true||Text(entry["handle"]) is not {} child||!added.Contains(child)||!Ids.Equals(Text(entry["owner_handle"]),pair.Key))return false;
                actual["entries"]=retained; // Ignore ordering/additions only after complete exact old membership proof.
            }
            if(!Exact(prior["data"],actual["data"]))
            {
                // Only the directly mapped record, not its user dictionaries
                // or descendants, receives this unclassified native allowance.
                if(!prior.ContainsKey("data")||!actual.ContainsKey("data")||!native.ContainsKey(pair.Key)||!Links(References(prior["data"],cancel),References(actual["data"],cancel),allowed,cancel))return false;
                actual["data"]=Copy(prior["data"]);
            }
            if(!Exact(prior,actual))return false;
        }
        foreach(string handle in added)
        {
            cancel();var item=current[handle];var common=item["common"]!;
            if(common["xdata"]!=null||!Links(new JsonArray(),common["reactors"]?.AsArray(),allowed,cancel)||
                !Links(new JsonArray(),References(item["data"],cancel),allowed,cancel))return false;
            if(item["entries"] is JsonArray entries&&entries.Any(entry=>entry?["owned"]?.GetValue<bool>()!=true||Text(entry?["handle"]) is not {} child||!current.ContainsKey(child)))return false;
        }
        after["owned"]=Copy(before["owned"]);return Exact(before,after);
    }
    internal static Observation Compare(ModelSketchConstraintParameterState379.Snapshot before,ModelSketchConstraintParameterState379.Snapshot after,Action cancel)
    {
        return Compare(before.Protected,after.Protected,before.Part.BodyMetadata,after.Part.BodyMetadata,before.StorageBindings,after.StorageBindings,cancel);
    }
    internal static Observation Compare(JsonElement beforeDesign,JsonElement afterDesign,JsonElement beforeBody,JsonElement afterBody,Binding[] beforeBindings,Binding[] afterBindings,Action cancel)
    {
        var left=JsonNode.Parse(beforeDesign.GetRawText())!.AsObject();var right=JsonNode.Parse(afterDesign.GetRawText())!.AsObject();
        var old=beforeBindings.Where(item=>item.proved&&item.raw_handle!=null).ToDictionary(item=>item.native_id,Ids);
        var now=afterBindings.Where(item=>item.proved&&item.raw_handle!=null).ToDictionary(item=>item.native_id,Ids);
        var matched=old.Where(pair=>now.TryGetValue(pair.Key,out var current)&&current.raw_handle==pair.Value.raw_handle&&current.owner_document_id==pair.Value.owner_document_id)
            .GroupBy(pair=>pair.Value.raw_handle!,Ids).Where(group=>group.Count()==1).ToDictionary(group=>group.Key,group=>group.Single().Value,Ids);
        var mapped=now.Values.Select(item=>item.raw_handle!).Concat(now.Values.SelectMany(item=>item.owner_path)).ToHashSet(Ids);
        bool changed=false;bool retained=true;
        void Walk(JsonNode? a,JsonNode? b,string? identity)
        {
            cancel();if(a is JsonObject obj&&b is JsonObject actual)
            {
                identity=Text(obj["profile_id"])??Text(obj["id"])??Text(obj["sketch_id"])??identity;
                if(Text(obj["scope"])=="exact_resident_DWG_owned_metadata")
                {
                    if(Exact(obj,actual))return;
                    if(identity==null||!old.TryGetValue(identity,out var proof)||!matched.ContainsKey(proof.raw_handle!)||Text(actual["scope"])!=Text(obj["scope"])) {retained=false;return;}
                    var prior=obj["metadata"]!.AsObject();var observed=actual["metadata"]!.AsObject();
                    if(!Metadata(prior,observed,proof.raw_handle!,matched,mapped,cancel)){retained=false;return;}
                    // Native/drawing reactor inventories retain all old links.
                    // Additions require exact local selected-native mapping.
                    var x=obj["reactor_observation"]!.AsObject();var y=actual["reactor_observation"]!.AsObject();
                    var nativeIds=now.Keys.ToHashSet(Ids);
                    if(!Links(x["native_reactor_ids"]?.AsArray(),y["native_reactor_ids"]?.AsArray(),nativeIds,cancel)){retained=false;return;}
                    y["native_reactor_ids"]=Copy(x["native_reactor_ids"]);
                    var entries=y["drawing_reactors"]!.AsArray().Select(item=>item!).ToList();
                    foreach(var row in x["drawing_reactors"]!.AsArray())
                    {int index=entries.FindIndex(item=>Exact(row,item));if(index<0){retained=false;return;}entries.RemoveAt(index);}
                    if(entries.Any(item=>Text(item["native_target_id"]) is not {} target||!now.ContainsKey(target)||!Ids.Equals(Text(item["handle"]),now[target].raw_handle))){retained=false;return;}
                    y["drawing_reactors"]=Copy(x["drawing_reactors"]);
                    if(!Exact(obj,actual)){retained=false;return;}changed=true;return;
                }
                foreach(var field in obj)if(actual.TryGetPropertyValue(field.Key,out var value))Walk(field.Value,value,identity);
            }
            else if(a is JsonArray array&&b is JsonArray observed&&array.Count==observed.Count)
                for(int index=0;index<array.Count;index++)Walk(array[index],observed[index],identity);
        }
        Walk(left,right,null);
        var bodyOld=JsonNode.Parse(beforeBody.GetRawText())!.AsObject();var bodyNow=JsonNode.Parse(afterBody.GetRawText())!.AsObject();
        if(!Exact(bodyOld,bodyNow))
        {
            // The existing capture normalizes the owned-root backreference.
            if(!Metadata(bodyOld,bodyNow,"$CURRENT_BODY_REPRESENTATION",matched,mapped,cancel))retained=false;else changed=true;
        }
        return new(retained,changed,retained?(changed?"unverified_direct_selected_native_payload_or_new_proved_owned_storage":"exact_observed_storage"):"prior_user_data_or_foreign_link_or_unproved_native_storage_changed",Wire.Element(right),Wire.Element(bodyNow));
    }
}
