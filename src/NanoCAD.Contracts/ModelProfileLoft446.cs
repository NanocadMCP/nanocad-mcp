using System.Text.Json;
using System.Text.Json.Serialization;

namespace NanoCAD.Contracts;

public sealed record ModelProfileSelection446(string ProfileId,string ExpectedSketchId,string ExpectedProfileToken);
public sealed record ModelProfileLoft446(string[] Targets,string ExpectedLastItemId,ModelProfileSelection446[] Profiles,
    string PartOperation,string Name,string? PathHandle,string[][] GuideHandles):EditSpec("add_model_loft_from_profiles",Targets);
public sealed record ModelProfileConsumed446(string profile_id,string sketch_id,string profile_token,string sketch_token,ModelProfileOwnership348 ownership);
public static class ModelProfileLoftValidation446
{
    static DomainException Invalid(string path,string message)=>new("INVALID_ARGUMENT",message,false,new{path});
    static string Text(JsonElement item,string key,string path)
    {
        if(!item.TryGetProperty(key,out var value)||value.ValueKind!=JsonValueKind.String||string.IsNullOrWhiteSpace(value.GetString()))throw Invalid(path+"."+key,"Expected a nonempty string.");
        return value.GetString()!;
    }
    static string Id(JsonElement item,string key,string path)
    {string value=Text(item,key,path);if(!Guid.TryParseExact(value,"B",out var parsed)||parsed==Guid.Empty)throw Invalid(path+"."+key,"Read an exact current native ID.");return parsed.ToString("B").ToUpperInvariant();}
    static void Fields(JsonElement item,string path,params string[] allowed)
    {var seen=new HashSet<string>(StringComparer.Ordinal);foreach(var field in item.EnumerateObject())if(!allowed.Contains(field.Name)||!seen.Add(field.Name))throw Invalid(path+"."+field.Name,"Unknown or duplicate field.");}
    static string Handle(JsonElement item,string path,string body)
    {if(item.ValueKind!=JsonValueKind.String)throw Invalid(path,"Expected a committed native curve handle.");var result=Validation.NormalizeHandle(item.GetString()!);if(result==body)throw Invalid(path,"A path curve must be separate from the selected body.");return result;}
    public static EditSpec Parse(JsonElement item,string path,string[] targets)
    {
        if(item.ValueKind!=JsonValueKind.Object||targets.Length!=1)throw Invalid(path,"Select one existing native part.");
        Fields(item,path,"operation","handles","expected_last_item_id","profiles","part_operation","name","path_handle","guide_handles");
        if(Text(item,"operation",path)!="add_model_loft_from_profiles")throw Invalid(path+".operation","Use the retained-profile loft operation.");
        string last=Id(item,"expected_last_item_id",path),part=Text(item,"part_operation",path);
        if(part is not ("join" or "cut"))throw Invalid(path+".part_operation","Use join or cut.");
        string name=item.TryGetProperty("name",out _)?Text(item,"name",path):"Retained profile loft";
        if(name.Length>64||name.Any(char.IsControl))throw Invalid(path+".name","Use a name of at most64 characters without control characters.");
        if(!item.TryGetProperty("profiles",out var rows)||rows.ValueKind!=JsonValueKind.Array||rows.GetArrayLength()<2)throw Invalid(path+".profiles","Supply at least two ordered existing profile selections.");
        var profiles=new List<ModelProfileSelection446>();var ids=new HashSet<string>(StringComparer.OrdinalIgnoreCase);int index=0;
        foreach(var row in rows.EnumerateArray())
        {
            string p=path+".profiles["+index+++"]";if(row.ValueKind!=JsonValueKind.Object)throw Invalid(p,"Expected a native profile selection.");
            Fields(row,p,"profile_id","expected_sketch_id","expected_profile_token");
            string id=Id(row,"profile_id",p),sketch=Id(row,"expected_sketch_id",p),token=Text(row,"expected_profile_token",p);
            if(!ids.Add(id))throw Invalid(p+".profile_id","Select each actual profile once, in intended loft order.");
            if(token.Length!=64||!token.All(Uri.IsHexDigit))throw Invalid(p+".expected_profile_token","Read the current exact profile token.");
            profiles.Add(new(id,sketch,token.ToUpperInvariant()));
        }
        string? center=null;if(item.TryGetProperty("path_handle",out var pathValue)&&pathValue.ValueKind!=JsonValueKind.Null)center=Handle(pathValue,path+".path_handle",targets[0]);
        var guides=new List<string[]>();
        if(item.TryGetProperty("guide_handles",out var guideValue))
        {
            if(guideValue.ValueKind!=JsonValueKind.Array)throw Invalid(path+".guide_handles","Expected ordered native guide chains.");
            foreach(var guide in guideValue.EnumerateArray())
            {if(guide.ValueKind!=JsonValueKind.Array||guide.GetArrayLength()==0)throw Invalid(path+".guide_handles","Each rail must contain one or more ordered native curve handles.");var chain=guide.EnumerateArray().Select(v=>Handle(v,path+".guide_handles",targets[0])).ToArray();if(chain.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=chain.Length)throw Invalid(path+".guide_handles","A rail cannot repeat the same curve identity.");guides.Add(chain);}
        }
        if(center!=null&&guides.Count>0)throw Invalid(path,"Choose a center path or guide rails, not both.");
        return new ModelProfileLoft446(targets,last,profiles.ToArray(),part,name,center,guides.ToArray());
    }
}
