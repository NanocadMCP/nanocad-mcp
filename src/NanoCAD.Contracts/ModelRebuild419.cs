using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record ModelRebuildSpec419(string[] Targets,string ExpectedLastItemId)
    : EditSpec("rebuild_model",Targets);
public sealed record ModelRebuildNode419(string mc_id,string type,string status,bool suppressed,bool beyond_end_of_part,
    string? source_verification_error=null);
public sealed record ModelRebuildChange419(EntityRef source,EntityRef body_ref,string body_mc_id,
    string last_item_id,bool native_rebuild_accepted,bool model_healthy,bool source_representation_replaced,
    ModelRebuildNode419[] unresolved_nodes,int unresolved_count,bool unresolved_nodes_complete,
    string[] suppressed_feature_ids,int suppressed_feature_count,bool suppressed_feature_ids_complete);
public sealed record ModelRebuildOutput419(string operation_id,string status,bool replayed,
    EntityRef[] modified,EntityRef[] erased,EntityRef[] created,CopyMapping[] copies,string undo_history,
    ModelRebuildChange419[] model_rebuilds);

public static partial class ModifyValidation
{
    private static ModelRebuildSpec419 ParseModelRebuild419(JsonElement item,string path,string[] handles)
    {
        Shape(item,path,"operation","handles","expected_last_item_id");
        if(handles.Length!=1)throw Invalid(path+".handles","Select one current native part representation.");
        string last=Text(item,"expected_last_item_id",path,38);
        if(!Guid.TryParseExact(last,"B",out var id)||id==Guid.Empty)
            throw Invalid(path+".expected_last_item_id","Read the exact current last_item_id from inspect_model_history.");
        return new(handles,id.ToString("B").ToUpperInvariant());
    }
}
