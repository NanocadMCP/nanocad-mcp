using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Completed managed snapshots only. Callers retain their own typed predicates;
// this helper neither reads/evaluates native data nor classifies generated data.
internal static class ModelRepresentationDiagnostics377
{
    const int SnapshotBytes=5120,ErrorBytes=8192;
    internal sealed record Identity(string operation,string body_mc_id,string before_representation_handle,string after_representation_handle,
        string before_native_document_id,string after_native_document_id,string? before_owner_document_id,string? after_owner_document_id,
        string? before_root_owner_handle,string? after_root_owner_handle,int before_owned_record_count,int after_owned_record_count);
    internal sealed record Comparison(string name,bool? retained,string comparison,JsonElement? before,JsonElement? after,string? unavailable_reason=null);
    sealed record Summary(string name,bool? retained,string comparison,string? before_sha256,string? after_sha256,string? unavailable_reason);
    sealed record Anchor(string? handle,string? owner_handle,string? runtime_type,string? extension_handle);
    sealed record Leaf(string path,string? before_kind,string? after_kind,string? before_sha256,string? after_sha256,JsonElement? before_identity,JsonElement? after_identity,Anchor? before_record,Anchor? after_record);
    static string Hash(JsonElement value)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json)));
    static string Hash(byte[] value)=>Convert.ToHexString(SHA256.HashData(value));
    static int Bytes(object value)=>JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json).Length;
    static string Segment(string value)=>value.Length<=96?value.Replace("~","~0").Replace("/","~1"):
        "@key-sha256="+Hash(Encoding.UTF8.GetBytes(value));
    static string Text(string value,int budget)
    {
        if(Bytes(value)<=budget)return value;
        string Prefix(int length)
        {if(length>0&&length<value.Length&&char.IsHighSurrogate(value[length-1])&&char.IsLowSurrogate(value[length]))length--;return value[..length];}
        int low=0,high=value.Length;
        while(low<high){int middle=low+(high-low+1)/2;if(Bytes(Prefix(middle))<=budget)low=middle;else high=middle-1;}
        return Prefix(low);
    }
    static Anchor? OwnedAnchor(JsonElement value,Anchor? prior)
    {
        if(value.ValueKind!=JsonValueKind.Object||!value.TryGetProperty("common",out var common)||common.ValueKind!=JsonValueKind.Object)return prior;
        string? Read(string name)=>common.TryGetProperty(name,out var field)&&field.ValueKind==JsonValueKind.String?Text(field.GetString()!,256):null;
        return new(Read("handle"),Read("owner_handle"),Read("runtime_type"),Read("extension_handle"));
    }
    internal static object Describe(Identity identity,IReadOnlyList<Comparison> compared,Action cancel)
    {
        var summaries=new List<Summary>();var leaves=new List<Leaf>();long changed=0;int reserved=0;
        void Add(string path,JsonElement? left,JsonElement? right,Anchor? leftOwner,Anchor? rightOwner)
        {
            cancel();changed++;
            if(path.Length>384)path="/@path-sha256="+Hash(Encoding.UTF8.GetBytes(path));
            // User XData/Xrecord/geometry payloads stay fingerprints. Only
            // actual metadata identity/class/owner/field-presence facts appear.
            bool structural=!path.Contains("/data/",StringComparison.Ordinal)&&!path.Contains("/xdata/",StringComparison.Ordinal)&&
                (path.EndsWith("/handle",StringComparison.Ordinal)||path.EndsWith("/owner_handle",StringComparison.Ordinal)||
                 path.EndsWith("/owner",StringComparison.Ordinal)||path.EndsWith("/extension",StringComparison.Ordinal)||
                 path.EndsWith("/extension_handle",StringComparison.Ordinal)||path.EndsWith("/runtime_type",StringComparison.Ordinal)||
                 path.EndsWith("/type",StringComparison.Ordinal)||path.EndsWith("/id",StringComparison.Ordinal)||
                 path.EndsWith("/mc_id",StringComparison.Ordinal)||path.EndsWith("/previous",StringComparison.Ordinal)||
                 path.EndsWith("/native_id",StringComparison.Ordinal)||path.EndsWith("/member_id",StringComparison.Ordinal)||
                 path.EndsWith("/element_id",StringComparison.Ordinal)||path.EndsWith("/native_type",StringComparison.Ordinal)||
                 path.EndsWith("/kind",StringComparison.Ordinal)||path.EndsWith("/identity_scope",StringComparison.Ordinal)||
                 path.EndsWith("/observation_scope",StringComparison.Ordinal)||
                 path.EndsWith("/terminal",StringComparison.Ordinal)||path.EndsWith("/has_fields",StringComparison.Ordinal)||
                 path.EndsWith("/HasFields",StringComparison.Ordinal)||path.Contains("/reactors/",StringComparison.Ordinal));
            JsonElement? Actual(JsonElement? value)=>structural&&value.HasValue&&Bytes(value.Value)<=256?value:null;
            var leaf=new Leaf(path,left?.ValueKind.ToString(),right?.ValueKind.ToString(),left.HasValue?Hash(left.Value):null,
                right.HasValue?Hash(right.Value):null,Actual(left),Actual(right),leftOwner,rightOwner);
            int size=Bytes(leaf)+1;if((long)reserved+size<=SnapshotBytes){leaves.Add(leaf);reserved+=size;}
        }
        void Missing(string path,JsonElement value,bool added,Anchor? owner)
        {
            cancel();owner=OwnedAnchor(value,owner);
            if(value.ValueKind==JsonValueKind.Object&&value.EnumerateObject().Any())
            {foreach(var field in value.EnumerateObject())Missing(path+"/"+Segment(field.Name),field.Value,added,owner);return;}
            if(value.ValueKind==JsonValueKind.Array&&value.GetArrayLength()>0)
            {for(int index=0;index<value.GetArrayLength();index++)Missing(path+"/"+index,value[index],added,owner);return;}
            Add(path,added?null:value,added?value:null,added?null:owner,added?owner:null);
        }
        void Walk(string path,JsonElement left,JsonElement right,Anchor? leftOwner=null,Anchor? rightOwner=null)
        {
            cancel();if(left.GetRawText()==right.GetRawText())return;
            leftOwner=OwnedAnchor(left,leftOwner);rightOwner=OwnedAnchor(right,rightOwner);
            if(left.ValueKind==JsonValueKind.Object&&right.ValueKind==JsonValueKind.Object)
            {
                foreach(var field in left.EnumerateObject())
                    if(right.TryGetProperty(field.Name,out var actual))Walk(path+"/"+Segment(field.Name),field.Value,actual,leftOwner,rightOwner);
                    else Missing(path+"/"+Segment(field.Name),field.Value,false,leftOwner);
                foreach(var field in right.EnumerateObject())if(!left.TryGetProperty(field.Name,out _))Missing(path+"/"+Segment(field.Name),field.Value,true,rightOwner);return;
            }
            if(left.ValueKind==JsonValueKind.Array&&right.ValueKind==JsonValueKind.Array)
            {
                for(int index=0;index<Math.Max(left.GetArrayLength(),right.GetArrayLength());index++)
                    if(index>=left.GetArrayLength())Missing(path+"/"+index,right[index],true,rightOwner);
                    else if(index>=right.GetArrayLength())Missing(path+"/"+index,left[index],false,leftOwner);
                    else Walk(path+"/"+index,left[index],right[index],leftOwner,rightOwner);return;
            }
            Add(path,left,right,leftOwner,rightOwner);
        }
        foreach(var value in compared)
        {
            cancel();summaries.Add(new(value.name,value.retained,value.comparison,value.before.HasValue?Hash(value.before.Value):null,
                value.after.HasValue?Hash(value.after.Value):null,value.unavailable_reason));
        }
        // Retained components may differ in harmless ordering under their own
        // typed predicates. Spend the existing diagnostic-byte budget on the
        // failed predicates first; this does not alter any admission decision.
        foreach(var value in compared.OrderBy(value=>value.retained==false?0:value.retained==null?1:2))
        {
            cancel();
            if(value.before.HasValue&&value.after.HasValue)Walk("/"+Segment(value.name),value.before.Value,value.after.Value);
        }
        object Result()=>new{scope="completed_managed_model_snapshots; exact_predicates_not_changed; no_Field_cache_or_storage_waiver",
            identity,component_count=summaries.Count,components=summaries.ToArray(),difference_scope="observed_changed_leaves; component_retained_flag_is_the_actual_guard_result",
            changed_leaf_count=changed,changed_leaves=leaves.ToArray(),
            omitted_changed_leaf_count=changed-leaves.Count,diagnostic_compaction=changed==leaves.Count?(string?)null:"actual_serialized_snapshot_byte_budget"};
        var result=Result();
        while(Bytes(result)>SnapshotBytes&&leaves.Count>0){leaves.RemoveAt(leaves.Count-1);result=Result();}
        if(Bytes(result)<=SnapshotBytes)return result;
        // The fixed caller component inventory normally fits. If a future
        // caller has larger identities/components, retain their complete hash,
        // all counts and explicit omission rather than emitting a huge receipt.
        return new{scope="completed_managed_model_snapshots",identity,identity_sha256=Hash(Wire.Element(identity)),component_count=summaries.Count,
            components_sha256=Hash(Wire.Element(summaries)),changed_leaf_count=changed,omitted_changed_leaf_count=changed,
            diagnostic_compaction="identity_or_component_inventory_exceeds_actual_snapshot_byte_budget"};
    }
    internal static object Error(string cause,string? stage,string message,string? body,string? feature,string? cleanup,object? nested,string? beforeRepresentation=null)
    {
        JsonElement? details=nested==null?null:Wire.Element(nested);
        object Result(object? value)=>new{cause=Text(cause,128),stage=stage==null?null:Text(stage,128),message=Text(message,768),
            body_mc_id=body,feature_id=feature,before_representation_handle=beforeRepresentation,cleanup_error=cleanup==null?null:Text(cleanup,128),nested_details=value};
        var result=Result(details);if(Bytes(result)<=ErrorBytes)return result;
        // Nested domains normally contain Describe's smaller snapshot. The
        // fallback is also real-byte bounded and keeps the complete details'
        // fingerprint; omission is diagnostic, never an admission change.
        var observed=details!.Value;
        while(observed.ValueKind==JsonValueKind.Object)
        {
            if(observed.TryGetProperty("nested_details",out var child)&&child.ValueKind==JsonValueKind.Object){observed=child;continue;}
            if(observed.TryGetProperty("snapshot_diagnostics",out child)&&child.ValueKind==JsonValueKind.Object){observed=child;continue;}
            break;
        }
        JsonElement? Read(string name)=>observed.ValueKind==JsonValueKind.Object&&observed.TryGetProperty(name,out var value)?value:null;
        JsonElement? Count(string name)=>Read(name) is {} value&&value.ValueKind==JsonValueKind.Number&&value.TryGetInt64(out _)?value:null;
        string? priorHash=Read("components_sha256") is {ValueKind:JsonValueKind.String} field?field.GetString():null;
        var compact=new{nested_details_sha256=Hash(details.Value),nested_details_bytes=Bytes(details.Value),
            component_count=Count("component_count"),changed_leaf_count=Count("changed_leaf_count"),
            omitted_changed_leaf_count=Count("changed_leaf_count"),snapshot_identity=Read("identity") is {ValueKind:JsonValueKind.Object} id&&Bytes(id)<=2048?id:(JsonElement?)null,
            components_sha256=Read("components") is {} values?Hash(values):priorHash is {Length:64}?priorHash:null,
            diagnostic_compaction="nested_details_exceed_actual_error_byte_budget; complete_details_hash_and_available_counts_retained"};
        return Result(compact);
    }
}
