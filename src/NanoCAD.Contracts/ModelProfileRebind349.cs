using System.Text.Json;
using System.Text.Json.Serialization;

namespace NanoCAD.Contracts;

public sealed record ModelProfileRebindSpec349(string[] Targets,string FeatureId,string ExpectedCurrentProfileId,
    string ExpectedLastItemId,string ProfileId,string ExpectedSketchId,string ExpectedProfileToken,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] Point3[]? ReplacementRegions=null)
    :EditSpec("set_model_extrusion_profile",Targets);
public sealed record ModelProfileDisposition349(string profile_id,string parent_sketch_id,string mc_document_id,
    string? profile_host_solid_id,string? sketch_host_solid_id,string? dependent_feature_id,bool in_body_history,
    string disposition,string reuse_scope);
public sealed record ModelProfileRebindRead349(bool candidate,string? current_profile_id,string[] required_inputs,string scope);
public sealed record ModelProfileRebindChange349(int operation_index,EntityRef source,EntityRef body_ref,string body_mc_id,
    string feature_id,string previous_profile_id,string profile_id,string parent_sketch_id,string profile_token,
    string mc_document_id,string last_item_id,bool source_representation_replaced,string[] retained_driver_ids,
    int retained_downstream_count,int downstream_history_count,string downstream_history_sha256,ModelProfileDisposition349 previous_profile,
    bool profile_selection_created=false);
public sealed record ModelProfileRebindOutput349(string operation_id,string status,bool replayed,EntityRef[] modified,
    EntityRef[] erased,EntityRef[] created,CopyMapping[] copies,string undo_history,ModelProfileRebindChange349[] model_profile_rebinds);

public static class ModelProfileRebindValidation349
{
    static DomainException Invalid(string path,string message)=>new("INVALID_ARGUMENT",message,false,new{path});
    static string Text(JsonElement item,string key,string path)
    {
        if(!item.TryGetProperty(key,out var value)||value.ValueKind!=JsonValueKind.String)
            throw Invalid(path+"."+key,"Read the exact current native identity or profile token.");
        string text;try{text=value.GetString()!;}catch(InvalidOperationException){throw Invalid(path+"."+key,"Expected valid Unicode.");}
        return text;
    }
    static string Id(JsonElement item,string key,string path)
    {
        if(!Guid.TryParseExact(Text(item,key,path),"B",out var id)||id==Guid.Empty)
            throw Invalid(path+"."+key,"Read a current nonempty native ID.");
        return id.ToString("B").ToUpperInvariant();
    }
    public static EditSpec Parse(JsonElement item,string path,string[] handles)
    {
        if(item.ValueKind!=JsonValueKind.Object||handles.Length!=1)
            throw Invalid(path,"Select one retained native part.");
        string[] fields=["operation","handles","feature_id","expected_current_profile_id","expected_last_item_id",
            "profile_id","expected_sketch_id","expected_profile_token","replacement_regions"];
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var field in item.EnumerateObject())
            if(!fields.Contains(field.Name)||!seen.Add(field.Name))throw Invalid(path+"."+field.Name,"Unknown or duplicate field.");
        if(Text(item,"operation",path)!="set_model_extrusion_profile")throw Invalid(path+".operation","Use set_model_extrusion_profile.");
        string old=Id(item,"expected_current_profile_id",path);bool select=item.TryGetProperty("replacement_regions",out var regions);
        if(select==item.TryGetProperty("profile_id",out _))throw Invalid(path,"Choose exactly one existing profile_id or replacement_regions on the current acquired sketch.");
        string next=select?old:Id(item,"profile_id",path);
        if(!select&&old==next)throw Invalid(path+".profile_id","Select a different replacement profile.");
        Point3[]? points=null;
        if(select)
        {
            if(regions.ValueKind!=JsonValueKind.Array||regions.GetArrayLength()==0)throw Invalid(path+".replacement_regions","Select each desired bounded region by one interior WCS [x,y,z] point.");
            var selected=new List<Point3>();
            foreach(var point in regions.EnumerateArray())
            {
                if(point.ValueKind!=JsonValueKind.Array||point.GetArrayLength()!=3)throw Invalid(path+".replacement_regions","Use WCS [x,y,z] points.");
                var xyz=new double[3];for(int i=0;i<3;i++)if(point[i].ValueKind!=JsonValueKind.Number||!point[i].TryGetDouble(out xyz[i])||!double.IsFinite(xyz[i])||Math.Abs(xyz[i])>1e6)
                    throw Invalid(path+".replacement_regions","Use finite drawing millimeters within1e6.");
                var value=new Point3(xyz[0],xyz[1],xyz[2]);if(selected.Contains(value))throw Invalid(path+".replacement_regions","Select each bounded region once; duplicate region points would toggle it off.");selected.Add(value);
            }
            points=selected.ToArray();
        }
        string token=Text(item,"expected_profile_token",path);
        if(token.Length!=64||!token.All(Uri.IsHexDigit))throw Invalid(path+".expected_profile_token","Read the current exact profile_ids token, or use its fresh creation result.");
        return new ModelProfileRebindSpec349(handles,Id(item,"feature_id",path),old,Id(item,"expected_last_item_id",path),
            next,Id(item,"expected_sketch_id",path),token.ToUpperInvariant(),points);
    }
    public static void Batch(IReadOnlyList<EditSpec> edits)
    {
        if(edits.Any(edit=>edit is ModelProfileRebindSpec349)&&edits.Count!=1)
            throw new DomainException("INVALID_OPERATION_ORDER","Native extrusion profile rebind must be the sole operation.");
    }
}
