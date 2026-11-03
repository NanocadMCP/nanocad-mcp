using System.Text.Json;
using System.Text.Json.Serialization;

namespace NanoCAD.Contracts;

public sealed record ModelProfileCreate340(string[] Targets,string ExpectedLastItemId,string[] SourceHandles,
    SketchPlaneSpec Plane,string Name):EditSpec("create_model_profile",Targets);
public sealed record ModelProfileExtrude340(string[] Targets,string ExpectedLastItemId,string ProfileId,
    string ExpectedSketchId,string ExpectedProfileToken,string PartOperation,double Distance,string Direction,string Name)
    :EditSpec("add_model_extrusion_from_profile",Targets);
public sealed record ModelProfileEditing340(bool reusable,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? profile_token=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? unavailable_reason=null);
public sealed record ModelProfilePlane340(double[] origin_wcs,double[] x_axis_wcs,double[] y_axis_wcs,double[] normal_wcs);
public sealed record ModelProfileOwnership348(string mc_document_id,string? profile_host_solid_id,string? sketch_host_solid_id,
    string scope);
public sealed record ModelProfileConsumer411(string feature_id,string kind,string body_mc_id,string[] profile_ids);
public sealed record ModelProfileRead340(string kind,string? parent_sketch_id,ModelProfileEditing340 profile_editing,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] ModelProfilePlane340? plane=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string[]? source_object_ids=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string[]? projection_ids=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] JsonElement? contour_geometry=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? dependent_feature_id=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? profile_id=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] ModelProfileOwnership348? ownership=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? support_id=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] ModelProfileConsumer411[]? forward_consumers=null);
public sealed record ModelProfileIdentity348(string profile_id,string? parent_sketch_id,string? profile_host_solid_id,
    string? dependent_feature_id);
public sealed record ModelProfileInventory348(string mc_document_id,string scope,ModelProfileIdentity348[] items,
    bool complete,string? next_cursor);
public sealed record ModelProfileDiscoveryRead348(ModelProfileInventory348? Inventory,ModelProfileRead340[] Profiles);
public sealed record ModelProfileChange340(int operation_index,string operation,EntityRef source,EntityRef body_ref,
    string body_mc_id,string sketch_id,string profile_id,string profile_token,string last_item_id,EntityRef[] source_refs,
    bool source_representation_replaced,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? feature_id=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? distance_variable_id=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] ModelProfileOwnership348? profile_ownership=null);
// Same common modify envelope, with one dedicated additive mapping. Root can
// publish this field alongside the existing ModifiedEntities alternatives.
public sealed record ModelProfileOutput340(string operation_id,string status,bool replayed,EntityRef[] modified,
    EntityRef[] erased,EntityRef[] created,CopyMapping[] copies,string undo_history,ModelProfileChange340[] model_profile_changes);

public static class ModelProfileLifecycleValidation340
{
    static DomainException Invalid(string path,string message)=>new("INVALID_ARGUMENT",message,false,new{path});
    static string Text(JsonElement item,string key,string path,int max)
    {
        if(!item.TryGetProperty(key,out var value)||value.ValueKind!=JsonValueKind.String)throw Invalid(path+"."+key,"Expected a string.");
        string text;try{text=value.GetString()!;}catch(InvalidOperationException){throw Invalid(path+"."+key,"Expected valid Unicode.");}
        if(string.IsNullOrWhiteSpace(text)||text.Length>max||text.Any(char.IsControl))throw Invalid(path+"."+key,"Expected a nonempty bounded string without control characters.");
        return text;
    }
    static string Id(JsonElement item,string key,string path)
    {
        string text=Text(item,key,path,38);
        if(!Guid.TryParseExact(text,"B",out var id)||id==Guid.Empty)throw Invalid(path+"."+key,"Read a current nonempty native history ID.");
        return id.ToString("B").ToUpperInvariant();
    }
    public static EditSpec Parse(JsonElement item,string path,string[] handles)
    {
        if(item.ValueKind!=JsonValueKind.Object||handles.Length!=1)throw Invalid(path,"Select exactly one existing native body.");
        string kind=Text(item,"operation",path,64);
        string[] allowed=kind switch{
            "create_model_profile"=>["operation","handles","expected_last_item_id","source_handles","plane","name"],
            "add_model_extrusion_from_profile"=>["operation","handles","expected_last_item_id","profile_id","expected_sketch_id","expected_profile_token","part_operation","distance","direction","name"],
            _=>throw Invalid(path+".operation","Unknown profile lifecycle operation.")};
        var names=new HashSet<string>(StringComparer.Ordinal);
        foreach(var field in item.EnumerateObject())if(!allowed.Contains(field.Name)||!names.Add(field.Name))throw Invalid(path+"."+field.Name,"Unknown or duplicate field.");
        string last=Id(item,"expected_last_item_id",path),name=item.TryGetProperty("name",out _)?Text(item,"name",path,64):kind=="create_model_profile"?"Reusable profile":"Profile extrusion";
        if(kind=="create_model_profile")
        {
            if(!item.TryGetProperty("source_handles",out var sources)||sources.ValueKind!=JsonValueKind.Array||sources.GetArrayLength()==0)throw Invalid(path+".source_handles","Select committed Circle or complete closed Line/Arc source geometry.");
            var selected=new List<string>();var unique=new HashSet<string>(StringComparer.Ordinal);int index=0;
            foreach(var source in sources.EnumerateArray())
            {
                string p=path+".source_handles["+index+++"]";
                if(source.ValueKind!=JsonValueKind.String)throw Invalid(p,"Expected a committed source handle.");
                string text;try{text=source.GetString()!;}catch(InvalidOperationException){throw Invalid(p,"Expected valid Unicode.");}
                string handle=Validation.NormalizeHandle(text);
                if(handle==handles[0]||!unique.Add(handle))throw Invalid(p,"Source handles must be distinct and separate from the body.");
                selected.Add(handle);
            }
            if(!item.TryGetProperty("plane",out var plane))throw Invalid(path+".plane","Supply the explicit source workplane.");
            return new ModelProfileCreate340(handles,last,selected.ToArray(),DrawingWorkplane.Parse(plane),name);
        }
        string token=Text(item,"expected_profile_token",path,64);
        if(token.Length!=64||!token.All(Uri.IsHexDigit))throw Invalid(path+".expected_profile_token","Read the current native profile token from inspect_model_history using profile_ids, or use the fresh creation result.");
        string operation=Text(item,"part_operation",path,8),direction=Text(item,"direction",path,8);
        if(operation is not ("join" or "cut"))throw Invalid(path+".part_operation","Use join or cut.");
        if(direction is not ("positive" or "negative"))throw Invalid(path+".direction","Direction is relative to the native sketch plane normal.");
        if(!item.TryGetProperty("distance",out var distance)||distance.ValueKind!=JsonValueKind.Number||!distance.TryGetDouble(out double d)||!double.IsFinite(d)||d<=0||d>1e6)throw Invalid(path+".distance","Use a positive finite distance, at most1e6 drawing millimeters.");
        return new ModelProfileExtrude340(handles,last,Id(item,"profile_id",path),Id(item,"expected_sketch_id",path),token.ToUpperInvariant(),operation,d,direction,name);
    }
    public static void Batch(IReadOnlyList<EditSpec> edits)
    {
        if(edits.Any(e=>e is ModelProfileCreate340 or ModelProfileExtrude340)&&edits.Count!=1)throw new DomainException("INVALID_OPERATION_ORDER","A native profile lifecycle operation must be the sole operation in its request.");
    }
}
