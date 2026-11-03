using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record ModelProfileElementsSpec358(string[] Targets,string ProfileId,string ExpectedSketchId,
    string ExpectedElementToken,string ExpectedLastItemId,string[] RemoveElementIds,string[] AddSourceHandles,Point3[] RegionPoints)
    :EditSpec("edit_model_profile_elements",Targets);
public sealed record ModelProfileElement358(string element_id,string geometry_type,string? source_object_id,
    EntityRef? source_ref,JsonElement geometry);
public sealed record ModelProfileElementsRead358(string profile_id,string parent_sketch_id,string mc_document_id,string? feature_id,
    string element_token,ModelProfilePlane340 plane,ModelProfileElement358[] elements,JsonElement selected_profile_geometry,
    JsonElement constraint_observation,string scope);
public sealed record ModelRemovedElement358(string element_id,string? source_object_id,EntityRef? retained_source_ref,
    bool geometry_present,bool containing_membership,bool body_membership,bool document_projection_membership,
    string? observed_host_sketch_id,string? observed_host_solid_id,string disposition);
public sealed record ModelProfileElementsChange358(int operation_index,EntityRef source,EntityRef body_ref,string body_mc_id,
    string profile_id,string parent_sketch_id,string mc_document_id,string? feature_id,string element_token,string last_item_id,
    bool source_representation_replaced,ModelRemovedElement358[] removed_elements,EntityRef[] added_source_refs,string[] added_projection_ids,
    JsonElement constraint_observation,int selected_region_count,int selected_hole_count,string selected_geometry_sha256,string scope);
public sealed record ModelProfileElementsOutput358(string operation_id,string status,bool replayed,EntityRef[] modified,
    EntityRef[] erased,EntityRef[] created,CopyMapping[] copies,string undo_history,ModelProfileElementsChange358[] model_profile_elements);

public static class ModelProfileElementsValidation358
{
    static DomainException Invalid(string path,string message)=>new("INVALID_ARGUMENT",message,false,new{path});
    static string Text(JsonElement value,string key,string path)
    {if(!value.TryGetProperty(key,out var item)||item.ValueKind!=JsonValueKind.String)throw Invalid(path+"."+key,"Expected a string.");return item.GetString()!;}
    static string Id(string value,string path)
    {if(!Guid.TryParseExact(value,"B",out var id)||id==Guid.Empty)throw Invalid(path,"Use a current exact nonempty native ID.");return id.ToString("B").ToUpperInvariant();}
    public static string[] Ids(JsonElement value,string path)
    {
        if(value.ValueKind!=JsonValueKind.Array||value.GetArrayLength()==0)throw Invalid(path,"Choose exact native IDs.");
        var ids=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(var item in value.EnumerateArray())
            if(item.ValueKind!=JsonValueKind.String||!ids.Add(Id(item.GetString()!,path)))throw Invalid(path,"IDs must be unique native braced GUIDs.");
        return ids.OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public static EditSpec Parse(JsonElement item,string path,string[] handles)
    {
        if(handles.Length!=1)throw Invalid(path,"Select one current existing native part.");
        string[] names=["operation","handles","profile_id","expected_sketch_id","expected_element_token","expected_last_item_id","remove_element_ids","add_source_handles","region_points"];
        var seen=new HashSet<string>(StringComparer.Ordinal);foreach(var field in item.EnumerateObject())
            if(!names.Contains(field.Name)||!seen.Add(field.Name))throw Invalid(path+"."+field.Name,"Unknown or duplicate field.");
        if(Text(item,"operation",path)!="edit_model_profile_elements")throw Invalid(path,"Use edit_model_profile_elements.");
        string token=Text(item,"expected_element_token",path);if(token.Length!=64||!token.All(Uri.IsHexDigit))throw Invalid(path,"Read exact profile_element_ids metadata and its fresh element_token.");
        var remove=item.TryGetProperty("remove_element_ids",out var removals)?Ids(removals,path+".remove_element_ids"):[];
        var sources=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if(item.TryGetProperty("add_source_handles",out var additions))
        {
            if(additions.ValueKind!=JsonValueKind.Array)throw Invalid(path+".add_source_handles","Expected committed source handles.");
            foreach(var value in additions.EnumerateArray())
                if(value.ValueKind!=JsonValueKind.String||!sources.Add(Validation.NormalizeHandle(value.GetString()!))||sources.Contains(handles[0]))
                    throw Invalid(path+".add_source_handles","Use distinct committed drawing sources separate from the body.");
        }
        if(remove.Length==0&&sources.Count==0)throw Invalid(path,"Remove exact elements and/or add committed source projections.");
        var points=new List<Point3>();if(item.TryGetProperty("region_points",out var selected))
        {
            if(selected.ValueKind!=JsonValueKind.Array||selected.GetArrayLength()==0)throw Invalid(path+".region_points","Use distinct interior WCS points for remaining Circle cells.");
            foreach(var point in selected.EnumerateArray())
            {
                if(point.ValueKind!=JsonValueKind.Array||point.GetArrayLength()!=3)throw Invalid(path+".region_points","Use WCS [x,y,z].");
                var xyz=new double[3];for(int i=0;i<3;i++)if(point[i].ValueKind!=JsonValueKind.Number||!point[i].TryGetDouble(out xyz[i])||!double.IsFinite(xyz[i])||Math.Abs(xyz[i])>1e6)
                    throw Invalid(path+".region_points","Use finite drawing millimeters within1e6.");points.Add(new(xyz[0],xyz[1],xyz[2]));
            }
        }
        return new ModelProfileElementsSpec358(handles,Id(Text(item,"profile_id",path),path),Id(Text(item,"expected_sketch_id",path),path),
            token.ToUpperInvariant(),Id(Text(item,"expected_last_item_id",path),path),remove,sources.ToArray(),points.ToArray());
    }
    public static void Batch(IReadOnlyList<EditSpec> edits)
    {if(edits.Any(edit=>edit is ModelProfileElementsSpec358)&&edits.Count!=1)throw new DomainException("INVALID_OPERATION_ORDER","A native profile element lifecycle edit must be the sole operation.");}
}
