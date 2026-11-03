using System.Security.Cryptography;
using System.Text.Json;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Completed managed snapshots only. No native getters, private-network reads,
// routine-log values, or alteration of a guard decision.
internal static class ModelExtrusionGuardDiagnostics357
{
    const int DetailsBytes=8192;
    sealed record Component(string name,bool retained,string comparison,string before_sha256,string after_sha256);
    sealed record Difference(string path,string? before_sha256,string? after_sha256);
    static string Hash(JsonElement value)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json)));
    static string Segment(string text)=>text.Length<=96?text.Replace("~","~0").Replace("/","~1"):
        "@key-sha256="+Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
    internal static object Describe(ModelExtrusionExtentsState351.Snapshot before,ModelExtrusionExtentsState351.Snapshot after,
        bool metadataRetained,string metadataReason,Action cancel)
    {
        var components=new List<Component>();var differences=new List<Difference>();long changed=0;int bytes=2048;
        void Add(string path,JsonElement? left,JsonElement? right)
        {
            cancel();if(left.HasValue&&right.HasValue&&left.Value.GetRawText()==right.Value.GetRawText())return;
            changed++;var item=new Difference(path.Length<=384?path:"/@path-sha256="+Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(path))),left.HasValue?Hash(left.Value):null,right.HasValue?Hash(right.Value):null);
            int size=JsonSerializer.SerializeToUtf8Bytes(item,Wire.Json).Length+1;if((long)bytes+size<=DetailsBytes){differences.Add(item);bytes+=size;}
        }
        void Walk(string path,JsonElement left,JsonElement right)
        {
            cancel();if(left.GetRawText()==right.GetRawText())return;
            if(left.ValueKind==JsonValueKind.Object&&right.ValueKind==JsonValueKind.Object)
            {
                foreach(var property in left.EnumerateObject()){cancel();if(right.TryGetProperty(property.Name,out var actual))Walk(path+"/"+Segment(property.Name),property.Value,actual);else Add(path+"/"+Segment(property.Name),property.Value,null);}
                foreach(var property in right.EnumerateObject()){cancel();if(!left.TryGetProperty(property.Name,out _))Add(path+"/"+Segment(property.Name),null,property.Value);}return;
            }
            if(left.ValueKind==JsonValueKind.Array&&right.ValueKind==JsonValueKind.Array)
            {
                for(int index=0;index<Math.Max(left.GetArrayLength(),right.GetArrayLength());index++)
                {cancel();if(index>=left.GetArrayLength())Add(path+"/"+index,null,right[index]);else if(index>=right.GetArrayLength())Add(path+"/"+index,left[index],null);else Walk(path+"/"+index,left[index],right[index]);}return;
            }
            Add(path,left,right);
        }
        void Compare(string name,JsonElement left,JsonElement right,bool retained,string comparison)
        {cancel();components.Add(new(name,retained,comparison,Hash(left),Hash(right)));if(!retained)Walk("/"+name,left,right);}
        Compare("body_metadata",before.Metadata,after.Metadata,metadataRetained,"exact_prior_state_with_scoped_additive_secondary_manager_anchors");
        Compare("body_display",before.Display,after.Display,before.Display.GetRawText()==after.Display.GetRawText(),"exact_stored_JSON");
        Compare("retained_sources",before.Sources,after.Sources,ModelExtrusionExtentsState351.Equal(before.Sources,after.Sources,cancel),"existing_structured_snapshot_comparison");
        Compare("terminal_references",before.Terminal,after.Terminal,ModelExtrusionExtentsState351.Equal(before.Terminal,after.Terminal,cancel),"existing_structured_snapshot_comparison");
        var result=new{scope="completed_managed_extent_snapshots_only; fingerprints_not_raw_drawing_values; no_private_manager_driver_mapping_claim",metadata_reason=metadataReason,
            components=components.ToArray(),changed_field_count=changed,changed_fields=differences.ToArray(),omitted_changed_field_count=changed-differences.Count};
        if(JsonSerializer.SerializeToUtf8Bytes(result,Wire.Json).Length>DetailsBytes)
            return new{scope="completed_managed_extent_snapshots_only",metadata_reason=metadataReason,components=components.ToArray(),changed_field_count=changed,diagnostic_unavailable_reason="Field fingerprints exceed the actual diagnostic byte budget."};
        return result;
    }
}
