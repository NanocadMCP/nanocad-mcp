using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Completed managed snapshots only. Report bounded exact leaf fingerprints
// when the protected retained graph actually fails; never waive native data.
internal static class DrawingProfileRetainedDiagnostics366
{
    static string Hash(JsonElement value)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json)));
    static string Segment(string value)=>value.Length<=96?value.Replace("~","~0").Replace("/","~1"):
        "@sha256="+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    internal static object Capture(string body,string sketch,string source,string constraint,JsonElement before,JsonElement after,Action cancel)
    {
        var leaves=new List<object>();long changes=0;int used=1024;
        void Add(string path,JsonElement? old,JsonElement? current)
        {
            cancel();changes++;if(path.Length>384)path="/@path-sha256="+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)));
            var leaf=new{path,before_kind=old?.ValueKind.ToString(),after_kind=current?.ValueKind.ToString(),
                before_sha256=old.HasValue?Hash(old.Value):null,after_sha256=current.HasValue?Hash(current.Value):null};
            int size=JsonSerializer.SerializeToUtf8Bytes(leaf,Wire.Json).Length+1;
            if(used+size<=3584){leaves.Add(leaf);used+=size;}
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
        void Walk(string path,JsonElement old,JsonElement current)
        {
            cancel();if(old.GetRawText()==current.GetRawText())return;
            if(old.ValueKind==JsonValueKind.Object&&current.ValueKind==JsonValueKind.Object)
            {
                foreach(var field in old.EnumerateObject())if(current.TryGetProperty(field.Name,out var value))Walk(path+"/"+Segment(field.Name),field.Value,value);else Missing(path+"/"+Segment(field.Name),field.Value,false);
                foreach(var field in current.EnumerateObject())if(!old.TryGetProperty(field.Name,out _))Missing(path+"/"+Segment(field.Name),field.Value,true);return;
            }
            if(old.ValueKind==JsonValueKind.Array&&current.ValueKind==JsonValueKind.Array)
            {
                for(int index=0;index<Math.Max(old.GetArrayLength(),current.GetArrayLength());index++)
                    if(index>=old.GetArrayLength())Missing(path+"/"+index,current[index],true);else if(index>=current.GetArrayLength())Missing(path+"/"+index,old[index],false);else Walk(path+"/"+index,old[index],current[index]);return;
            }
            Add(path,old,current);
        }
        Walk("/protected_retained_graph",before,after);
        object Result(bool details)=>new{scope="completed_managed_retained_graph_raw_leaf_fingerprints; no_cache_or_revision_waiver",body_id=body,sketch_id=sketch,source_id=source,constraint_id=constraint,
            before_sha256=Hash(before),after_sha256=Hash(after),changed_leaf_count=changes,changed_leaves=details?leaves.ToArray():[],
            omitted_leaf_count=changes-(details?leaves.Count:0),diagnostic_compaction=details?(string?)null:"actual_complete_detail_exceeds4096_byte_budget; identities_fingerprints_and_counts_retained"};
        var result=Result(true);return JsonSerializer.SerializeToUtf8Bytes(result,Wire.Json).Length<=4096?result:Result(false);
    }
}
