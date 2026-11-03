using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Completed managed body snapshots only. Metadata payloads stay fingerprints;
// this diagnoses an actual failed preservation guard without admitting caches.
internal static class ModelSketchBodyDiagnostics363
{
    static string Hash(JsonElement value)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json)));
    static string Segment(string value)=>value.Length<=96?value.Replace("~","~0").Replace("/","~1"):"@sha256="+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    internal static object Capture(bool empty,string body,string beforeHandle,string afterHandle,JsonElement beforeDisplay,JsonElement afterDisplay,
        JsonElement beforeMetadata,JsonElement afterMetadata,bool displayChanged,bool metadataChanged,Action cancel)
    {
        var leaves=new List<object>();long changes=0;int used=1536;
        void Add(string path,JsonElement? before,JsonElement? after)
        {
            cancel();changes++;if(path.Length>384)path="/@path-sha256="+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)));
            // Only metadata structure/identity facts are exposed. XData and
            // Xrecord payload leaves keep exact fingerprints, never raw values.
            bool structural=!path.Contains("/data/",StringComparison.Ordinal)&&!path.Contains("/xdata/",StringComparison.Ordinal)&&
                (path.EndsWith("/handle",StringComparison.Ordinal)||path.EndsWith("/owner_handle",StringComparison.Ordinal)||path.EndsWith("/extension_handle",StringComparison.Ordinal)||
                 path.EndsWith("/runtime_type",StringComparison.Ordinal)||path.EndsWith("/name",StringComparison.Ordinal)||path.EndsWith("/HasFields",StringComparison.Ordinal));
            JsonElement? Value(JsonElement? value)=>structural&&value.HasValue&&JsonSerializer.SerializeToUtf8Bytes(value.Value,Wire.Json).Length<=256?value:null;
            var leaf=new{path,before_kind=before?.ValueKind.ToString(),after_kind=after?.ValueKind.ToString(),before_sha256=before.HasValue?Hash(before.Value):null,
                after_sha256=after.HasValue?Hash(after.Value):null,before_value=Value(before),after_value=Value(after)};
            int size=JsonSerializer.SerializeToUtf8Bytes(leaf,Wire.Json).Length+1;if(used+size<=3584){leaves.Add(leaf);used+=size;}
        }
        void Missing(string path,JsonElement value,bool added)
        {
            cancel();
            if(value.ValueKind==JsonValueKind.Object&&value.EnumerateObject().Any())
            {foreach(var field in value.EnumerateObject())Missing(path+"/"+Segment(field.Name),field.Value,added);return;}
            if(value.ValueKind==JsonValueKind.Array&&value.GetArrayLength()>0)
            {for(int index=0;index<value.GetArrayLength();index++)Missing(path+"/"+index,value[index],added);return;}
            Add(path,added?null:value,added?value:null);
        }
        void Walk(string path,JsonElement before,JsonElement after)
        {
            cancel();if(before.GetRawText()==after.GetRawText())return;
            if(before.ValueKind==JsonValueKind.Object&&after.ValueKind==JsonValueKind.Object)
            {
                foreach(var field in before.EnumerateObject())if(after.TryGetProperty(field.Name,out var actual))Walk(path+"/"+Segment(field.Name),field.Value,actual);else Missing(path+"/"+Segment(field.Name),field.Value,false);
                foreach(var field in after.EnumerateObject())if(!before.TryGetProperty(field.Name,out _))Missing(path+"/"+Segment(field.Name),field.Value,true);return;
            }
            if(before.ValueKind==JsonValueKind.Array&&after.ValueKind==JsonValueKind.Array)
            {
                for(int index=0;index<Math.Max(before.GetArrayLength(),after.GetArrayLength());index++)
                    if(index>=before.GetArrayLength())Missing(path+"/"+index,after[index],true);else if(index>=after.GetArrayLength())Missing(path+"/"+index,before[index],false);else Walk(path+"/"+index,before[index],after[index]);return;
            }
            Add(path,before,after);
        }
        Walk("/body_owned_metadata",beforeMetadata,afterMetadata);
        object Result(bool details)=>new{scope="completed_managed_body_snapshots; metadata_payload_leaf_fingerprints; no_cache_waiver",body_mc_id=body,
            before_representation_handle=beforeHandle,after_representation_handle=afterHandle,body_empty=empty,display_changed=displayChanged,owned_metadata_changed=metadataChanged,
            before_display=details?(JsonElement?)beforeDisplay:null,after_display=details?(JsonElement?)afterDisplay:null,
            before_display_sha256=Hash(beforeDisplay),after_display_sha256=Hash(afterDisplay),before_owned_metadata_sha256=Hash(beforeMetadata),after_owned_metadata_sha256=Hash(afterMetadata),
            changed_metadata_leaf_count=changes,changed_metadata_leaves=details?leaves.ToArray():[],omitted_metadata_leaf_count=changes-(details?leaves.Count:0),
            diagnostic_compaction=details?(string?)null:"complete_body_detail_exceeds_actual4096_byte_budget; fingerprints_and_counts_retained"};
        var result=Result(true);return JsonSerializer.SerializeToUtf8Bytes(result,Wire.Json).Length<=4096?result:Result(false);
    }
}
