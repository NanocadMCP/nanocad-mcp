using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Completed source snapshots only. A requested projection changes source
// association edges, not geometry, XData, owned records or unrelated reactors.
internal static class ModelProfileSourcePreservation360
{
    static readonly StringComparer Ids=StringComparer.OrdinalIgnoreCase;
    static string Key(JsonElement value)=>value.GetProperty("handle").GetString()!;
    static string Hash(JsonElement value)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json)));
    static string? Text(JsonNode? value)=>value?.GetValue<string>();
    static HashSet<string> Links(JsonArray values)=>values.Select(value=>Text(value)??"$NULL").ToHashSet(Ids);
    static void Remove(JsonArray values,HashSet<string> changing)
    {for(int i=values.Count-1;i>=0;i--)if(changing.Contains(Text(values[i])??"$NULL"))values.RemoveAt(i);}
    internal static bool Retained(JsonElement before,JsonElement after,string sketch,
        IReadOnlyDictionary<string,string[]> retired,IReadOnlyDictionary<string,string[]> added,
        IReadOnlySet<string> previouslyLinked,IReadOnlySet<string> currentlyLinked,
        Func<JsonElement,JsonElement,bool> equivalent,Action cancel,out string reason,out JsonElement normalizedBefore,out JsonElement normalizedAfter)
    {
        normalizedBefore=before;normalizedAfter=after;reason="source_identity_or_inventory_changed";
        var old=before.EnumerateArray().ToDictionary(Key,Ids);var current=after.EnumerateArray().ToDictionary(Key,Ids);
        if(old.Count!=current.Count||old.Keys.Any(handle=>!current.ContainsKey(handle)))return false;
        var priorValues=new List<JsonElement>();var actualValues=new List<JsonElement>();bool retained=true;
        foreach(var pair in old.OrderBy(pair=>pair.Key,Ids))
        {
            cancel();var prior=JsonNode.Parse(pair.Value.GetRawText())!.AsObject();var actual=JsonNode.Parse(current[pair.Key].GetRawText())!.AsObject();
            string source=pair.Value.GetProperty("native_id").GetString()!;
            var removing=(retired.TryGetValue(source,out var deleted)?deleted:[]).ToHashSet(Ids);
            var inserting=(added.TryGetValue(source,out var created)?created:[]).ToHashSet(Ids);
            // A source-to-sketch link can disappear only when this request
            // retires its last selected projection, and appear only for a
            // requested new source projection. Existing sketch links persist.
            if(removing.Count>0&&previouslyLinked.Contains(source)&&!currentlyLinked.Contains(source))removing.Add(sketch);
            if(inserting.Count>0&&!previouslyLinked.Contains(source)&&currentlyLinked.Contains(source))inserting.Add(sketch);
            bool admitted=CanonicalReactors(prior,actual,removing,inserting,cancel,out string sourceReason);
            var left=Wire.Element(prior);var right=Wire.Element(actual);priorValues.Add(left);actualValues.Add(right);
            if(!admitted||!equivalent(left,right))
            {if(retained)reason=sourceReason+";source_handle="+pair.Key;retained=false;}
        }
        normalizedBefore=Wire.Element(priorValues);normalizedAfter=Wire.Element(actualValues);
        if(retained)reason="exact_sources_except_proved_requested_projection_or_sketch_reactor_deltas";
        return retained;
    }
    static bool CanonicalReactors(JsonObject before,JsonObject after,HashSet<string> removing,HashSet<string> inserting,Action cancel,out string reason)
    {
        reason="source_geometry_identity_properties_or_exact_readable_metadata_changed";
        var a=before["reactor_observation"]!.AsObject();var b=after["reactor_observation"]!.AsObject();
        var oldNative=a["native_reactor_ids"] as JsonArray;var newNative=b["native_reactor_ids"] as JsonArray;
        var removedNative=new HashSet<string>(Ids);var addedNative=new HashSet<string>(Ids);
        if(oldNative!=null&&newNative!=null)
        {
            var oldIds=Links(oldNative);var newIds=Links(newNative);
            removedNative=oldIds.Except(newIds,Ids).ToHashSet(Ids);addedNative=newIds.Except(oldIds,Ids).ToHashSet(Ids);
            if(!removedNative.IsSubsetOf(removing)||!addedNative.IsSubsetOf(inserting))
            {reason="unrequested_native_source_reactor_changed";return false;}
        }
        else if(oldNative!=null||newNative!=null){reason="native_source_reactor_observation_changed_availability";return false;}
        var oldRaw=before["metadata"]!["root"]!["reactors"]!.AsArray();var newRaw=after["metadata"]!["root"]!["reactors"]!.AsArray();
        var oldHandles=Links(oldRaw);var newHandles=Links(newRaw);
        var removedHandles=oldHandles.Except(newHandles,Ids).ToHashSet(Ids);var addedHandles=newHandles.Except(oldHandles,Ids).ToHashSet(Ids);
        bool Bound(JsonObject observation,JsonArray? native,HashSet<string> handles,HashSet<string> allowed)
        {
            if(handles.Count==0)return true;if(native==null)return false;var nativeIds=Links(native);
            var rows=observation["drawing_reactors"]!.AsArray();
            foreach(string handle in handles)
            {
                cancel();var matches=rows.Where(row=>Text(row?["handle"])==handle).ToArray();
                if(matches.Length!=1)return false;string? target=Text(matches[0]?["native_target_id"]);
                // A raw handle alone, an SDK runtime name, or a changed
                // unclassified reactor never establishes operation ownership.
                if(target==null||!allowed.Contains(target)||!nativeIds.Contains(target))return false;
            }
            return true;
        }
        if(!Bound(a,oldNative,removedHandles,removing)||!Bound(b,newNative,addedHandles,inserting))
        {reason="drawing_source_reactor_delta_not_bound_to_requested_native_projection_or_sketch";return false;}
        if(oldNative!=null)Remove(oldNative,removedNative);if(newNative!=null)Remove(newNative,addedNative);
        Remove(oldRaw,removedHandles);Remove(newRaw,addedHandles);
        void Rows(JsonObject observation,HashSet<string> handles)
        {
            var rows=observation["drawing_reactors"]!.AsArray();
            for(int i=rows.Count-1;i>=0;i--){cancel();if(handles.Contains(Text(rows[i]?["handle"])??"$NULL"))rows.RemoveAt(i);}
        }
        Rows(a,removedHandles);Rows(b,addedHandles);return true;
    }
    internal static object Describe(JsonElement before,JsonElement after,JsonElement normalizedBefore,JsonElement normalizedAfter,string reason,Action cancel)
    {
        const int maxBytes=8192;var differences=new List<object>();var associations=new List<object>();long changed=0;int bytes=1536;
        var rawOld=before.EnumerateArray().ToDictionary(Key,Ids);var rawCurrent=after.EnumerateArray().ToDictionary(Key,Ids);
        foreach(var pair in rawOld)
        {
            cancel();if(!rawCurrent.TryGetValue(pair.Key,out var actual))continue;
            var prior=pair.Value.GetProperty("reactor_observation");var observed=actual.GetProperty("reactor_observation");
            if(prior.GetRawText()==observed.GetRawText())continue;
            // Reactor reference identities/types/owners are diagnostic facts;
            // XData payloads and other user-owned values remain fingerprints.
            var item=new{source_handle=pair.Key,before=prior,after=observed};int size=JsonSerializer.SerializeToUtf8Bytes(item,Wire.Json).Length+1;
            if(bytes+size<=maxBytes/2){associations.Add(item);bytes+=size;}
        }
        string Segment(string value)=>value.Length<=96?value.Replace("~","~0").Replace("/","~1"):"@sha256="+Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
        void Add(string path,JsonElement? left,JsonElement? right)
        {
            cancel();changed++;if(path.Length>384)path="/@path-sha256="+Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(path)));
            var item=new{path,before_kind=left?.ValueKind.ToString(),after_kind=right?.ValueKind.ToString(),before_sha256=left.HasValue?Hash(left.Value):null,after_sha256=right.HasValue?Hash(right.Value):null};
            int size=JsonSerializer.SerializeToUtf8Bytes(item,Wire.Json).Length+1;if(bytes+size<=maxBytes){differences.Add(item);bytes+=size;}
        }
        void Walk(string path,JsonElement left,JsonElement right)
        {
            cancel();if(left.GetRawText()==right.GetRawText())return;
            if(left.ValueKind==JsonValueKind.Object&&right.ValueKind==JsonValueKind.Object)
            {
                foreach(var field in left.EnumerateObject()){cancel();if(right.TryGetProperty(field.Name,out var actual))Walk(path+"/"+Segment(field.Name),field.Value,actual);else Add(path+"/"+Segment(field.Name),field.Value,null);}
                foreach(var field in right.EnumerateObject()){cancel();if(!left.TryGetProperty(field.Name,out _))Add(path+"/"+Segment(field.Name),null,field.Value);}return;
            }
            if(left.ValueKind==JsonValueKind.Array&&right.ValueKind==JsonValueKind.Array)
            {
                for(int i=0;i<Math.Max(left.GetArrayLength(),right.GetArrayLength());i++)
                {cancel();if(i>=left.GetArrayLength())Add(path+"/"+i,null,right[i]);else if(i>=right.GetArrayLength())Add(path+"/"+i,left[i],null);else Walk(path+"/"+i,left[i],right[i]);}return;
            }
            Add(path,left,right);
        }
        var old=normalizedBefore.EnumerateArray().ToDictionary(Key,Ids);var current=normalizedAfter.EnumerateArray().ToDictionary(Key,Ids);
        foreach(var pair in old){cancel();if(current.TryGetValue(pair.Key,out var actual))Walk("/sources/"+Segment(pair.Key),pair.Value,actual);else Add("/sources/"+Segment(pair.Key),pair.Value,null);}
        foreach(var pair in current){cancel();if(!old.ContainsKey(pair.Key))Add("/sources/"+Segment(pair.Key),null,pair.Value);}
        var result=new{scope="completed_managed_source_snapshots; leaf_fingerprints_no_raw_user_data; requested_association_deltas_only",reason,
            before_sha256=Hash(before),after_sha256=Hash(after),reactor_changes=associations.ToArray(),changed_field_count=changed,changed_fields=differences.ToArray(),omitted_changed_field_count=changed-differences.Count};
        if(JsonSerializer.SerializeToUtf8Bytes(result,Wire.Json).Length>maxBytes)
            return new{scope="completed_managed_source_snapshots",reason,before_sha256=Hash(before),after_sha256=Hash(after),changed_field_count=changed,
                diagnostic_unavailable_reason="Leaf fingerprints exceed actual diagnostic byte budget."};
        return result;
    }
}
