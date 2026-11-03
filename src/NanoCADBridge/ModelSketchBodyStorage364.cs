using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Completed snapshots and independently proved native/raw paths only. Runtime
// class names and dictionary entry names never establish operation ownership.
internal static class ModelSketchBodyStorage364
{
    internal sealed record Proof(string native_id,string native_owner_document_id,string? raw_handle,string[] raw_owner_handles,bool proved,string reason);
    static readonly StringComparer Ids=StringComparer.OrdinalIgnoreCase;
    static string? Text(JsonNode? value)=>value?.GetValue<string>();
    static string Handle(JsonNode value)=>Text(value["common"]?["handle"])!;
    static string Hash(JsonElement value)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json)));
    internal static bool Retained(JsonElement before,JsonElement after,IReadOnlyList<Proof> proofs,Action cancel,
        out string reason,out JsonElement normalizedAfter,IReadOnlyCollection<string>? newReactorTargets=null)
    {
        normalizedAfter=after;reason="body_owned_metadata_changed_without_proved_new_native_storage";
        if(before.GetRawText()==after.GetRawText()){reason="exact_body_owned_metadata";return true;}
        var prior=JsonNode.Parse(before.GetRawText())!.AsObject();var actual=JsonNode.Parse(after.GetRawText())!.AsObject();
        var targets=(newReactorTargets??[]).ToHashSet(Ids);
        bool Reactors(JsonNode? previous,JsonNode? current)
        {
            if(previous is not JsonObject p||current is not JsonObject c)return previous?.ToJsonString()==current?.ToJsonString();
            var old=p["reactors"] as JsonArray;var now=c["reactors"] as JsonArray;
            if(old?.ToJsonString()==now?.ToJsonString())return true;
            var counts=new Dictionary<string,int>(StringComparer.Ordinal);
            foreach(var row in old??new JsonArray()){cancel();string key=row?.ToJsonString()??"null";counts[key]=counts.GetValueOrDefault(key)+1;}
            foreach(var row in now??new JsonArray())
            {cancel();string key=row?.ToJsonString()??"null";if(counts.GetValueOrDefault(key)>0)counts[key]--;else if(row is not JsonValue value||!value.TryGetValue<string>(out var handle)||!targets.Contains(handle))return false;}
            if(counts.Values.Any(count=>count!=0))return false;
            if(p.ContainsKey("reactors"))c["reactors"]=old==null?null:JsonNode.Parse(old.ToJsonString());else c.Remove("reactors");
            return true;
        }
        if(!Reactors(prior["root"],actual["root"]))
        {reason="body_root_reactor_link_changed_without_exact_new_native_target";return false;}
        if(prior["root"]!.ToJsonString()!=actual["root"]!.ToJsonString()){reason="body_metadata_root_changed";return false;}
        var a=prior["owned"]!.AsArray();var b=actual["owned"]!.AsArray();
        if(a.Select(item=>Handle(item!)).Distinct(Ids).Count()!=a.Count||b.Select(item=>Handle(item!)).Distinct(Ids).Count()!=b.Count)
        {reason="body_owned_record_identity_duplicated";return false;}
        var old=a.ToDictionary(item=>Handle(item!),item=>item!,Ids);var current=b.ToDictionary(item=>Handle(item!),item=>item!,Ids);
        if(old.Keys.Any(handle=>!current.ContainsKey(handle))){reason="existing_body_owned_record_removed";return false;}
        var added=current.Keys.Except(old.Keys,Ids).ToHashSet(Ids);var anchors=new Dictionary<string,Proof>(Ids);
        foreach(var proof in proofs)
        {
            cancel();if(!proof.proved||proof.raw_handle==null||!added.Contains(proof.raw_handle))continue;
            if(!anchors.TryAdd(proof.raw_handle,proof)){reason="ambiguous_native_storage_anchor";return false;}
            string? owner=Text(current[proof.raw_handle]["common"]?["owner_handle"]);
            if(proof.raw_owner_handles.Length<2||!Ids.Equals(owner,proof.raw_owner_handles[0])){reason="native_storage_raw_owner_path_disagrees";return false;}
            // All intermediate owners were independently read and reverse-bound
            // by the native helper. The terminal selected body is normalized in
            // the existing snapshot adapter, never inferred from a name.
            for(int i=0;i<proof.raw_owner_handles.Length-1;i++)
            {
                if(!current.TryGetValue(proof.raw_owner_handles[i],out var parent)){reason="native_storage_owner_missing_from_body_snapshot";return false;}
                string expected=i==proof.raw_owner_handles.Length-2?"$CURRENT_BODY_REPRESENTATION":proof.raw_owner_handles[i+1];
                if(!Ids.Equals(Text(parent["common"]?["owner_handle"]),expected)){reason="native_storage_owner_chain_disagrees";return false;}
            }
        }
        foreach(string handle in added)
        {
            cancel();string currentHandle=handle;var visited=new HashSet<string>(Ids);
            while(!anchors.ContainsKey(currentHandle))
            {
                if(!visited.Add(currentHandle)||!current.TryGetValue(currentHandle,out var item)){reason="new_native_storage_owner_cycle_or_missing_record";return false;}
                string? parent=Text(item["common"]?["owner_handle"]);
                if(parent==null||!added.Contains(parent)){reason="added_body_owned_record_has_no_exact_new_native_anchor";return false;}
                currentHandle=parent;
            }
        }
        // Remove only descriptors whose actual owned child is in that proved
        // new tree. Every pre-existing record and its enumeration stays exact.
        foreach(var pair in old)
        {
            cancel();var item=current[pair.Key];
            if(!Reactors(pair.Value["common"],item["common"]))
            {reason="body_owned_reactor_link_changed_without_exact_new_native_target";return false;}
            if(item["entries"] is not JsonArray entries)continue;
            for(int index=entries.Count-1;index>=0;index--)
            {
                cancel();var entry=entries[index]!;string? child=Text(entry["handle"]);
                if(child==null||!added.Contains(child))continue;
                var common=current[child]["common"]!;
                if(entry["owned"]?.GetValue<bool>()!=true||!Ids.Equals(Text(entry["owner_handle"]),pair.Key)||
                    !Ids.Equals(Text(common["owner_handle"]),pair.Key)||Text(entry["runtime_type"])!=Text(common["runtime_type"]))
                {reason="new_native_storage_dictionary_reverse_binding_disagrees";return false;}
                entries.RemoveAt(index);
            }
        }
        for(int index=b.Count-1;index>=0;index--){cancel();if(added.Contains(Handle(b[index]!)))b.RemoveAt(index);}
        normalizedAfter=Wire.Element(actual);
        if(before.GetRawText()!=normalizedAfter.GetRawText()){reason="retained_body_metadata_changed_outside_exact_new_native_storage";return false;}
        reason="exact_existing_body_metadata_except_proved_new_native_storage";return true;
    }
    internal static object Describe(JsonElement before,JsonElement after,IReadOnlyList<Proof> proofs,string reason,object bodyDetails)
    {
        var body=JsonNode.Parse(Wire.Element(bodyDetails).GetRawText())!.AsObject();var all=Wire.Element(proofs);
        // The current body leaf diagnostic stays available. Reserve space for
        // the exact mapping failure and cap the final serialized object, not
        // UTF-16 lengths or an estimated number of leaves.
        object Result(object observations)=>new{scope="exact_returned_typed_sweep_ID_and_resident_DBObject_owner_path; no_class_or_cache_waiver",reason,
            before_sha256=Hash(before),after_sha256=Hash(after),storage_proof_count=proofs.Count,storage_proofs_sha256=Hash(all),storage_proofs=observations,
            omitted_storage_proof_count=observations is Proof[] rows?proofs.Count-rows.Length:proofs.Count,body_diagnostics=body};
        object observations=proofs.Count==1&&JsonSerializer.SerializeToUtf8Bytes(proofs,Wire.Json).Length<=1536?proofs.ToArray():Array.Empty<Proof>();
        var result=Result(observations);
        if(body["changed_metadata_leaves"] is JsonArray leaves)
            while(JsonSerializer.SerializeToUtf8Bytes(result,Wire.Json).Length>4096&&leaves.Count>0)
            {leaves.RemoveAt(leaves.Count-1);body["omitted_metadata_leaf_count"]=body["changed_metadata_leaf_count"]!.GetValue<long>()-leaves.Count;}
        if(JsonSerializer.SerializeToUtf8Bytes(result,Wire.Json).Length<=4096)return result;
        body.Clear();body["diagnostic_compaction"]="body_leaf_details_exceed_actual4096_byte_budget; exact_metadata_and_storage_fingerprints_retained";
        return Result(Array.Empty<Proof>());
    }
}
