using System.Security.Cryptography;
using System.Text.Json;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Completed native Model Space observations. A requested projection can have
// a resident drawing representation; all unrelated members and duplicate-body
// protection remain exact. Runtime names alone never authorize a delta.
internal static class ModelProfileDrawingMembership360
{
    static readonly StringComparer Ids=StringComparer.OrdinalIgnoreCase;
    static string Handle(JsonElement row)=>row.GetProperty("handle").GetString()!;
    static string? Projection(JsonElement row)=>row.GetProperty("native_projection_id").GetString();
    static string Hash(JsonElement value)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json)));
    internal static bool Retained(JsonElement before,JsonElement after,string previousBody,string currentBody,
        IReadOnlySet<string> removed,IReadOnlySet<string> added,Action cancel,out string reason)
    {
        reason="unrequested_ModelSpace_membership_delta";
        var old=before.EnumerateArray().ToDictionary(Handle,Ids);var current=after.EnumerateArray().ToDictionary(Handle,Ids);
        if(!old.ContainsKey(previousBody)||!current.ContainsKey(currentBody))
        {reason="current_or_prior_body_representation_missing_from_ModelSpace";return false;}
        if(!Ids.Equals(previousBody,currentBody)&&current.ContainsKey(previousBody))
        {reason="prior_body_representation_remains_alongside_replacement";return false;}
        foreach(var pair in old)
        {
            cancel();if(current.ContainsKey(pair.Key)||Ids.Equals(pair.Key,previousBody))continue;
            if(Projection(pair.Value) is not {} native||!removed.Contains(native))return false;
        }
        foreach(var pair in current)
        {
            cancel();if(old.ContainsKey(pair.Key)||Ids.Equals(pair.Key,currentBody))continue;
            if(Projection(pair.Value) is not {} native||!added.Contains(native))return false;
        }
        reason="exact_ModelSpace_members_except_current_body_and_proved_requested_native_projections";return true;
    }
    internal static object Describe(JsonElement before,JsonElement after,string previousBody,string currentBody,string reason,Action cancel)
    {
        const int maxBytes=8192;var old=before.EnumerateArray().ToDictionary(Handle,Ids);var current=after.EnumerateArray().ToDictionary(Handle,Ids);
        var removed=new List<JsonElement>();var added=new List<JsonElement>();int removedCount=0,addedCount=0,bytes=1536;
        void Add(JsonElement row,List<JsonElement> values)
        {int size=JsonSerializer.SerializeToUtf8Bytes(row,Wire.Json).Length+1;if(bytes+size<=maxBytes){values.Add(row.Clone());bytes+=size;}}
        foreach(var pair in old.OrderBy(pair=>pair.Key,Ids)){cancel();if(!current.ContainsKey(pair.Key)){removedCount++;Add(pair.Value,removed);}}
        foreach(var pair in current.OrderBy(pair=>pair.Key,Ids)){cancel();if(!old.ContainsKey(pair.Key)){addedCount++;Add(pair.Value,added);}}
        var result=new{scope="completed_ModelSpace_membership; actual_handle_type_owner_and_exact_native_projection_match; not_private_user_data_inventory",
            reason,previous_body_handle=previousBody,current_body_handle=currentBody,prior_body_still_present=current.ContainsKey(previousBody),
            before_count=old.Count,after_count=current.Count,before_sha256=Hash(before),after_sha256=Hash(after),
            removed_count=removedCount,added_count=addedCount,removed_members=removed,added_members=added,
            omitted_removed_count=removedCount-removed.Count,omitted_added_count=addedCount-added.Count};
        if(JsonSerializer.SerializeToUtf8Bytes(result,Wire.Json).Length>maxBytes)
            return new{scope="completed_ModelSpace_membership",reason,previous_body_handle=previousBody,current_body_handle=currentBody,
                prior_body_still_present=current.ContainsKey(previousBody),before_count=old.Count,after_count=current.Count,
                before_sha256=Hash(before),after_sha256=Hash(after),removed_count=removedCount,added_count=addedCount,
                diagnostic_unavailable_reason="Actual serialized membership diagnostics exceed byte budget."};
        return result;
    }
}
