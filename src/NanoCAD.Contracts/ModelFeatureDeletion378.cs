using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record ModelFeatureDeletionSpec378(string[] Targets,string FeatureId,string ExpectedDeletionToken,
    string ExpectedLastItemId,string ExpectedRemainingLastItemId,string[] RetirableDriverIds,string? ExpectedRetainedProfileId=null):EditSpec("delete_model_feature",Targets);
public sealed record ModelFeatureDeletionProfile384(string profile_id,string sketch_id,string owner_document_id,string profile_token,
    string? profile_host_solid_id,string? sketch_host_solid_id,string[] source_ids,string[] projection_ids,string disposition);
public sealed record ModelFeatureDeletionRead378(EntityRef source,string? body_mc_id,string feature_id,string? feature_kind,
    bool eligible,string? deletion_token,string? unavailable_reason,string? resulting_last_item_id,
    string[] required_deleted_ids,string[] retirable_driver_ids,string[] retained_forming_ids,JsonElement? dependency_observations,
    bool storage_retirement_proved,string storage_retirement_reason,string effect_scope,string cleanup_scope,ModelFeatureDeletionProfile384? retained_profile=null);
public sealed record ModelFeatureDeletionChange378(int operation_index,EntityRef source,EntityRef body_ref,string body_mc_id,
    string feature_id,string feature_kind,string last_item_id,string[] deleted_history_ids,string[] retired_driver_ids,
    string[] retained_driver_ids,ModelFeatureOrderChange345[] order_changes,bool source_representation_replaced,
    string storage_retention_scope,string cleanup_scope,ModelFeatureDeletionProfile384? retained_profile=null);
public sealed record ModelFeatureDeletionOutput378(string operation_id,string status,bool replayed,
    EntityRef[] modified,EntityRef[] erased,EntityRef[] created,CopyMapping[] copies,string undo_history,
    ModelFeatureDeletionChange378[] model_feature_deletions);

public static class ModelFeatureDeletionValidation378
{
    static DomainException Invalid(string path,string message)=>new("INVALID_ARGUMENT",message,false,new{path});
    static string Id(JsonElement value,string path)
    {
        if(value.ValueKind!=JsonValueKind.String||!Guid.TryParseExact(value.GetString(),"B",out var id)||id==Guid.Empty)
            throw Invalid(path,"Read an actual current nonempty native braced GUID.");
        return id.ToString("B").ToUpperInvariant();
    }
    public static string[] Ids(JsonElement value,string path,bool empty=false)
    {
        if(value.ValueKind!=JsonValueKind.Array||!empty&&value.GetArrayLength()==0)throw Invalid(path,"Expected actual current native IDs.");
        var ids=new List<string>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var item in value.EnumerateArray()){var id=Id(item,path);if(!seen.Add(id))throw Invalid(path,"Native IDs must be distinct.");ids.Add(id);}
        return ids.ToArray();
    }
    public static EditSpec Parse(JsonElement item,string path,string[] handles)
    {
        if(item.ValueKind!=JsonValueKind.Object||handles.Length!=1)throw Invalid(path,"Select one existing native Model Space part.");
        string[] allowed=["operation","handles","feature_id","expected_deletion_token","expected_last_item_id","expected_remaining_last_item_id","retirable_driver_ids","expected_retained_profile_id"];
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var field in item.EnumerateObject())if(!allowed.Contains(field.Name)||!seen.Add(field.Name))throw Invalid(path+"."+field.Name,"Unknown or duplicate field.");
        JsonElement Required(string name)=>item.TryGetProperty(name,out var value)?value:throw Invalid(path+"."+name,"Missing required field.");
        var op=Required("operation");if(op.ValueKind!=JsonValueKind.String||!op.ValueEquals("delete_model_feature"))throw Invalid(path+".operation","Use delete_model_feature.");
        var token=Required("expected_deletion_token");if(token.ValueKind!=JsonValueKind.String||token.GetString() is not {Length:64} text||text.Any(c=>!Uri.IsHexDigit(c)))
            throw Invalid(path+".expected_deletion_token","Read the fresh selected feature_deletion_ids deletion_token.");
        string feature=Id(Required("feature_id"),path+".feature_id"),last=Id(Required("expected_last_item_id"),path+".expected_last_item_id"),remaining=Id(Required("expected_remaining_last_item_id"),path+".expected_remaining_last_item_id");
        if(feature!=last||feature==remaining)throw Invalid(path,"Select the current non-base terminal feature and explicitly retain a different remaining terminal.");
        string? profile=item.TryGetProperty("expected_retained_profile_id",out var retained)?Id(retained,path+".expected_retained_profile_id"):null;
        return new ModelFeatureDeletionSpec378(handles,feature,text.ToUpperInvariant(),last,remaining,Ids(Required("retirable_driver_ids"),path+".retirable_driver_ids",true),profile);
    }
    public static void Batch(IReadOnlyList<EditSpec> edits)
    {if(edits.Any(edit=>edit is ModelFeatureDeletionSpec378)&&edits.Count!=1)throw new DomainException("INVALID_OPERATION_ORDER","Native feature deletion must be the sole operation.");}
}
