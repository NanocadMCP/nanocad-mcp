using System.Security.Cryptography;
using System.Text.Json;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// The owner walker has already proved exact local native residency/ownership.
// This comparison admits only new manager anchors during verified secondary
// driver allocation. It does not invent a manager-to-MCS-variable identity map.
internal static class ModelExtrusionOwnedMetadata357
{
    internal const string Scope="all_prior_root_owned_records_owners_typed_values_and_dictionary_entry_order_retained; only_additive_resident_AssocManager_under_prior_owned_dictionary_with_verified_new_secondary_feature_drivers; private_manager_to_driver_mapping_unobserved";
    internal const string Warning="NATIVE_SECONDARY_MANAGER_ANCHORS_ADDED: Native secondary extrusion drivers and their selected-feature dependencies were verified. New resident AssocManager anchors were observed under an existing owned dictionary; all prior readable owned records, identities, owners and typed values remain checked. The SDK exposes no private manager-to-driver mapping; no per-driver manager ownership or complete private-state preservation is claimed.";
    static string Handle(JsonElement record)=>record.GetProperty("common").GetProperty("handle").GetString()!;
    static string Type(JsonElement record)=>record.GetProperty("common").GetProperty("runtime_type").GetString()!;
    static Dictionary<string,JsonElement> Records(JsonElement metadata,Action cancel)
    {
        var result=new Dictionary<string,JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach(var record in metadata.GetProperty("owned").EnumerateArray())
        {cancel();if(!result.TryAdd(Handle(record),record))throw new DomainException("MODEL_STATE_UNAVAILABLE","A managed owned snapshot repeats a native identity.");}
        return result;
    }
    static bool Fields(JsonElement before,JsonElement after,string excluded,Action cancel)
    {
        if(before.ValueKind!=JsonValueKind.Object||after.ValueKind!=JsonValueKind.Object)return false;
        var left=before.EnumerateObject().Where(field=>field.Name!=excluded).ToArray();
        var right=after.EnumerateObject().Where(field=>field.Name!=excluded).ToArray();
        if(left.Length!=right.Length)return false;
        foreach(var field in left){cancel();if(!after.TryGetProperty(field.Name,out var actual)||field.Value.GetRawText()!=actual.GetRawText())return false;}
        return true;
    }
    internal static string[] AddedManagerHandles(JsonElement before,JsonElement after,Action cancel)
    {
        var prior=Records(before,cancel);return Records(after,cancel).Where(pair=>!prior.ContainsKey(pair.Key)&&Type(pair.Value)=="Teigha.DatabaseServices.AssocManager")
            .Select(pair=>pair.Key).OrderBy(value=>value,StringComparer.OrdinalIgnoreCase).ToArray();
    }
    internal static ModelExtrusionOwnedMetadataChange357 Describe(JsonElement before,JsonElement after,Action cancel)
    {
        var added=AddedManagerHandles(before,after,cancel);
        return new(added.Length,Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(added,Wire.Json))),false,Scope);
    }
    internal static bool RetainedWithSecondaryAllocation(JsonElement before,JsonElement after,string[] verifiedAllocatedDrivers,Action cancel,out string reason)
    {
        reason="exact_snapshot";if(before.GetRawText()==after.GetRawText())return true;
        if(verifiedAllocatedDrivers.Length==0){reason="owned_metadata_changed_without_verified_secondary_driver_allocation";return false;}
        if(!Fields(before,after,"owned",cancel)){reason="stored_root_metadata_changed";return false;}
        var old=Records(before,cancel);var current=Records(after,cancel);
        if(old.Keys.Any(id=>!current.ContainsKey(id))){reason="prior_owned_identity_removed";return false;}
        var added=current.Keys.Except(old.Keys,StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // A changed existing record is never excused by the presence of new drivers.
        foreach(var id in added)
        {
            cancel();var record=current[id];var common=record.GetProperty("common");string? owner=common.GetProperty("owner_handle").GetString();
            if(Type(record)!="Teigha.DatabaseServices.AssocManager"||owner==null||!old.TryGetValue(owner,out var parent)||Type(parent)!="Teigha.DatabaseServices.DBDictionary"||
                common.GetProperty("extension_handle").ValueKind!=JsonValueKind.Null||!record.TryGetProperty("assoc_manager_observation",out var observed)||
                record.EnumerateObject().Count()!=2||observed.GetProperty("adapter").GetString()!="resident_assoc_manager_anchor356"||
                observed.GetProperty("private_state_complete").ValueKind!=JsonValueKind.False||observed.GetProperty("observation_scope").GetString()!=NativeAssocManagerObservation356.Scope)
            {reason="new_owned_record_is_not_an_observed_manager_under_a_prior_owned_dictionary";return false;}
        }
        var registered=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var pair in old)
        {
            cancel();var actual=current[pair.Key];
            if(Type(pair.Value)!="Teigha.DatabaseServices.DBDictionary")
            {if(pair.Value.GetRawText()!=actual.GetRawText()){reason="prior_owned_record_data_owner_or_identity_changed";return false;}continue;}
            if(!Fields(pair.Value,actual,"entries",cancel)){reason="prior_dictionary_metadata_changed";return false;}
            var priorEntries=pair.Value.GetProperty("entries").EnumerateArray().ToArray();int retained=0;
            foreach(var entry in actual.GetProperty("entries").EnumerateArray())
            {
                cancel();string id=entry.GetProperty("handle").GetString()!;
                if(!added.Contains(id))
                {if(retained>=priorEntries.Length||priorEntries[retained++].GetRawText()!=entry.GetRawText()){reason="prior_dictionary_entry_value_or_order_changed";return false;}continue;}
                if(entry.GetProperty("owned").ValueKind!=JsonValueKind.True||entry.GetProperty("owner_handle").GetString()!=pair.Key||
                    entry.GetProperty("runtime_type").GetString()!="Teigha.DatabaseServices.AssocManager"||current[id].GetProperty("common").GetProperty("owner_handle").GetString()!=pair.Key||
                    !registered.Add(id)||priorEntries.Any(prior=>prior.GetProperty("name").GetString()==entry.GetProperty("name").GetString()))
                {reason="new_manager_dictionary_membership_is_not_unique_and_additive";return false;}
            }
            if(retained!=priorEntries.Length){reason="prior_dictionary_entry_removed";return false;}
        }
        if(!registered.SetEquals(added)){reason="a_new_manager_has_no_exact_prior_owned_dictionary_entry";return false;}
        reason="verified_secondary_driver_allocation_with_additive_observed_manager_anchors";return true;
    }
}
