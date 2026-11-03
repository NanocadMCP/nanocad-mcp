using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record ModelDatumPlaneRevisionSpec354(string[] Targets,string PlaneId,string ExpectedDatumToken,
    string ExpectedRevisionToken,string ExpectedLastItemId,string[] ExpectedMirrorIds,SketchPlaneSpec Plane,string[]? ExpectedDirectConsumerIds=null)
    :EditSpec("set_model_datum_plane",Targets);
public sealed record ModelDatumMirrorConsumer354(string feature_id,string[] source_item_ids,bool has_sub_feature_items,
    string? native_order_before_id,string[] dependency_ids);
public sealed record ModelDatumPlaneRevisionRead354(EntityRef body_ref,string body_mc_id,string last_item_id,
    string revision_token,ModelDatumPlaneRead353 datum,ModelDatumMirrorConsumer354[] direct_mirrors,
    string[] downstream_ids,JsonElement body_geometry,string geometry_scope,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] JsonElement? direct_sketches=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string[]? direct_consumer_ids=null);
public sealed record ModelDatumPlaneRevisionChange354(int operation_index,EntityRef source_ref,EntityRef body_ref,
    bool representation_replaced,bool datum_representation_replaced,EntityRef[] native_representations,ModelDatumPlaneRevisionRead354 revision);
public sealed record ModelDatumPlaneRevisionOutput354(string operation_id,string status,bool replayed,EntityRef[] modified,
    EntityRef[] erased,EntityRef[] created,CopyMapping[] copies,string undo_history,ModelDatumPlaneRevisionChange354[] datum_plane_revisions);

public static class ModelDatumPlaneRevisionValidation354
{
    static DomainException Invalid(string path,string message)=>new("INVALID_ARGUMENT",message,false,new{path});
    public static EditSpec Parse(JsonElement value,string path,string[] handles)
    {
        string[] fields=["operation","handles","plane_id","expected_datum_token","expected_revision_token","expected_last_item_id","expected_mirror_ids","expected_direct_consumer_ids","definition"];
        var names=new HashSet<string>(StringComparer.Ordinal);
        foreach(var item in value.EnumerateObject())if(!fields.Contains(item.Name)||!names.Add(item.Name))throw Invalid(path+"."+item.Name,"Unknown/duplicate field.");
        if(handles.Length!=1)throw Invalid(path,"Select one current native part for this retained construction revision.");
        bool direct=value.TryGetProperty("expected_direct_consumer_ids",out var directConsumers);
        bool legacy=value.TryGetProperty("expected_mirror_ids",out var mirrors);
        if(direct==legacy)throw Invalid(path,"Supply exactly one current expected_direct_consumer_ids or legacy expected_mirror_ids set from datum_plane_revision_ids.");
        string consumersField=direct?"expected_direct_consumer_ids":"expected_mirror_ids";
        if(direct)mirrors=directConsumers;
        if(mirrors.ValueKind!=JsonValueKind.Array||mirrors.GetArrayLength()==0)throw Invalid(path+"."+consumersField,"Read the actual direct consumers from datum_plane_revision_ids.");
        var ids=new List<string>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var item in mirrors.EnumerateArray())
        {
            if(item.ValueKind!=JsonValueKind.String||!Guid.TryParseExact(item.GetString(),"B",out var id)||id==Guid.Empty||!seen.Add(id.ToString("B")))throw Invalid(path+"."+consumersField,"Choose distinct actual native consumer IDs.");
            ids.Add(id.ToString("B").ToUpperInvariant());
        }
        if(!value.TryGetProperty("definition",out var definition)||definition.ValueKind!=JsonValueKind.Object)throw Invalid(path+".definition","Expected fixed native plane definition.");
        var definitionNames=new HashSet<string>(StringComparer.Ordinal);
        foreach(var item in definition.EnumerateObject())if(item.Name is not ("kind" or "workplane")||!definitionNames.Add(item.Name))throw Invalid(path+".definition."+item.Name,"Unknown/duplicate field.");
        if(!definition.TryGetProperty("kind",out var kind)||kind.ValueKind!=JsonValueKind.String||kind.GetString()!="fixed"||!definition.TryGetProperty("workplane",out var plane))throw Invalid(path+".definition","Use kind fixed and an orthonormal WCS workplane.");
        return new ModelDatumPlaneRevisionSpec354(handles,ModelDatumPlaneValidation353.Id(value,"plane_id",path),
            ModelDatumPlaneValidation353.Token(value,"expected_datum_token",path),ModelDatumPlaneValidation353.Token(value,"expected_revision_token",path),
            ModelDatumPlaneValidation353.Id(value,"expected_last_item_id",path),direct?[]:ids.ToArray(),DrawingWorkplane.Parse(plane),direct?ids.ToArray():null);
    }
    public static void Batch(IReadOnlyList<EditSpec> edits)
    {if(edits.Any(edit=>edit is ModelDatumPlaneRevisionSpec354)&&edits.Count!=1)throw Invalid("$.operations","Revise one retained native plane and its existing part consumers per request.");}
}
