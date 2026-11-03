using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record SetHatchAssociation358(string[] Targets,string HatchToken,string Action,string? OuterBoundaryHandle,string[] HoleBoundaryHandles,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string[][]? BoundaryGroups=null)
    :EditSpec("set_hatch_association",Targets);
public sealed record HatchAssociationRead358(bool candidate,string scope,string[] actions,string[] required_inputs,string boundary_scope,
    string[] boundary_types,string topology_verification,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] HatchAssociationLoopRead390[]? source_loops=null);
public sealed record HatchAssociationLoopRead390(int loop_index,int loop_type,string[] source_handles);
public sealed record HatchAssociationLoopChange390(int boundary_group_index,string role,EntityRef[] source_refs,string geometry_scope);
public sealed record HatchAssociationSource362(EntityRef source_ref,string boundary_type,string role);
public sealed record HatchAssociationChange358(int operation_index,EntityRef hatch_ref,string action,string hatch_token,bool associative,
    EntityRef[] previous_sources,EntityRef[] current_sources,int loop_count,string association_scope,HatchAssociationSource362[] verified_boundaries,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] HatchAssociationLoopChange390[]? verified_loops=null);
public sealed record HatchAssociationOutput358(string operation_id,string status,bool replayed,EntityRef[] modified,EntityRef[] erased,EntityRef[] created,
    CopyMapping[] copies,string undo_history,HatchAssociationChange358[] hatch_association_changes);

public static class HatchAssociationValidation358
{
    public static string[][] Groups(SetHatchAssociation358 edit)=>edit.BoundaryGroups??
        (edit.OuterBoundaryHandle==null?[]:edit.HoleBoundaryHandles.Prepend(edit.OuterBoundaryHandle).Select(handle=>new[]{handle}).ToArray());
    public static string[] Sources(SetHatchAssociation358 edit)=>Groups(edit).SelectMany(group=>group).ToArray();
    static DomainException Bad(string path,string message)=>new("INVALID_ARGUMENT",message,false,new{path});
    static string Text(JsonElement item,string key,string path)
    {if(!item.TryGetProperty(key,out var value)||value.ValueKind!=JsonValueKind.String)throw Bad(path+"."+key,"Expected a string.");return value.GetString()!;}
    public static EditSpec Parse(JsonElement item,string path,string[] handles)
    {
        bool grouped=item.TryGetProperty("outer_boundary_handles",out _)||item.TryGetProperty("hole_boundary_groups",out _);
        string action=Text(item,"action",path);string[] fields=action=="detach"?["operation","handles","hatch_token","action"]:
            action=="rebind"?grouped?["operation","handles","hatch_token","action","outer_boundary_handles","hole_boundary_groups"]:
                ["operation","handles","hatch_token","action","outer_boundary_handle","hole_boundary_handles"]:throw Bad(path+".action","Use detach or rebind.");
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var p in item.EnumerateObject())if(!fields.Contains(p.Name)||!seen.Add(p.Name))throw Bad(path+"."+p.Name,"Unknown or duplicate field.");
        foreach(string field in fields)if(!item.TryGetProperty(field,out _))throw Bad(path+"."+field,"Required field.");
        if(handles.Length!=1||Text(item,"operation",path)!="set_hatch_association")throw Bad(path,"Address one exact native Hatch per association operation.");
        string token=Text(item,"hatch_token",path);if(token.Length!=64||!token.All(Uri.IsHexDigit))throw Bad(path+".hatch_token","Read the current geometry.editing.hatch_token.");
        string? outer=null;var holes=new List<string>();string[][]? groups=null;
        if(action=="rebind")
        {
            if(grouped)
            {
                var distinct=new HashSet<string>(handles,StringComparer.Ordinal);
                string[] Group(JsonElement values,string key)
                {
                    if(values.ValueKind!=JsonValueKind.Array||values.GetArrayLength()==0)throw Bad(key,"Use a nonempty group of existing native curve handles.");
                    var sources=new List<string>();
                    foreach(var value in values.EnumerateArray())
                    {
                        if(value.ValueKind!=JsonValueKind.String)throw Bad(key,"Expected hex handles.");
                        string id=Validation.NormalizeHandle(value.GetString()!);if(!distinct.Add(id))throw Bad(key,"Hatch and every source in all boundary groups must be distinct.");sources.Add(id);
                    }
                    return sources.ToArray();
                }
                var boundaries=new List<string[]>{Group(item.GetProperty("outer_boundary_handles"),path+".outer_boundary_handles")};
                var holeGroups=item.GetProperty("hole_boundary_groups");if(holeGroups.ValueKind!=JsonValueKind.Array)throw Bad(path+".hole_boundary_groups","Use an array of nonempty hole groups; empty means no holes.");
                int index=0;foreach(var group in holeGroups.EnumerateArray()){boundaries.Add(Group(group,path+".hole_boundary_groups["+index+"]"));index++;}
                groups=boundaries.ToArray();
            }
            else
            {
                outer=Validation.NormalizeHandle(Text(item,"outer_boundary_handle",path));
                var values=item.GetProperty("hole_boundary_handles");if(values.ValueKind!=JsonValueKind.Array)throw Bad(path+".hole_boundary_handles","Use distinct native closed boundary handles; empty means no holes.");
                var distinct=new HashSet<string>(handles,StringComparer.Ordinal);if(!distinct.Add(outer))throw Bad(path,"Hatch and source boundaries must be distinct.");
                foreach(var value in values.EnumerateArray())
                {if(value.ValueKind!=JsonValueKind.String)throw Bad(path+".hole_boundary_handles","Expected hex handles.");string id=Validation.NormalizeHandle(value.GetString()!);if(!distinct.Add(id))throw Bad(path,"Source boundaries must be distinct.");holes.Add(id);}
            }
        }
        return new SetHatchAssociation358(handles,token.ToUpperInvariant(),action,outer,holes.ToArray(),groups);
    }
    public static void Batch(IReadOnlyCollection<EditSpec> edits)
    {
        if(!edits.Any(edit=>edit is SetHatchAssociation358))return;
        if(edits.Any(edit=>edit is not SetHatchAssociation358)||edits.SelectMany(edit=>edit.Handles).Distinct(StringComparer.Ordinal).Count()!=edits.Count)
            throw new DomainException("INVALID_OPERATION_ORDER","Association changes use a dedicated batch of distinct Hatches; shared source mutation conflicts are checked before writing.");
    }
}
