using System.Text.Json;
using static NanoCAD.Contracts.StructureWriteValidation;

namespace NanoCAD.Contracts;

public sealed record DrawingGroupEdit431(string Action,string? Name,string? Handle,string? ExpectedName,string? ExpectedToken,
    string[] AddHandles,string[] RemoveHandles,string? Description,bool? Selectable):StructureEdit(Action);
public sealed record DrawingGroupChange431(int operation_index,string operation,EntityRef group_ref,string name,string[] member_handles,
    string? group_token,bool dissolved);
public sealed record DrawingGroupResult431(string operation_id,string status,bool replayed,StructureChange[] changes,string undo_history,
    DrawingGroupChange431[] group_changes);

public static class DrawingGroups431
{
    public static DrawingGroupEdit431 Parse(JsonElement input,string path)
    {
        string action=Text(input,"operation",path,48);
        if(action is not("create_group" or "set_group" or "dissolve_group"))throw Invalid(path,"Unknown group operation.");
        Shape(input,path,"operation","name","handle","expected_name","expected_group_token","handles","add_handles","remove_handles","description","selectable");
        string? Read(string key,int max=255,bool empty=false)=>input.TryGetProperty(key,out _)?Text(input,key,path,max,empty):null;
        string[] Handles(string key)
        {
            if(!input.TryGetProperty(key,out var value))return [];
            if(value.ValueKind!=JsonValueKind.Array)throw Invalid(path+"."+key,"Use actual native entity handles.");
            var result=new List<string>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var row in value.EnumerateArray())
            {if(row.ValueKind!=JsonValueKind.String)throw Invalid(path+"."+key,"Use actual native entity handles.");var handle=Validation.NormalizeHandle(row.GetString()!);if(!seen.Add(handle))throw Invalid(path+"."+key,"Select each entity once.");result.Add(handle);}
            return result.ToArray();
        }
        var name=Read("name");var handle=Read("handle",16);if(handle!=null)handle=Validation.NormalizeHandle(handle);
        var expected=Read("expected_name");var token=Read("expected_group_token",64);var description=Read("description",4096,true);
        bool? selectable=null;if(input.TryGetProperty("selectable",out var flag)){if(flag.ValueKind is not(JsonValueKind.True or JsonValueKind.False))throw Invalid(path+".selectable","Use a boolean.");selectable=flag.GetBoolean();}
        var create=Handles("handles");var add=Handles("add_handles");var remove=Handles("remove_handles");
        if(name!=null&&!BlockWriteValidation.OrdinaryName(name))throw Invalid(path+".name","Use an ordinary local group name.");
        if(action=="create_group")
        {
            if(name==null||create.Length==0||handle!=null||expected!=null||token!=null||add.Length!=0||remove.Length!=0||input.TryGetProperty("add_handles",out _)||input.TryGetProperty("remove_handles",out _))throw Invalid(path,"Create a named group from a nonempty explicit handles selection; do not supply an existing group identity.");
            add=create;
        }
        else
        {
            if(handle==null||expected==null||token==null||token.Length!=64||!token.All(Uri.IsHexDigit)||input.TryGetProperty("handles",out _))throw Invalid(path,"Read groups and provide the actual handle, exact expected_name and fresh expected_group_token.");
            if(action=="dissolve_group"&&(name!=null||description!=null||selectable!=null||input.TryGetProperty("add_handles",out _)||input.TryGetProperty("remove_handles",out _)))throw Invalid(path,"Dissolve only the native group; its member entities are retained.");
            if(action=="set_group"&&name==null&&description==null&&selectable==null&&add.Length==0&&remove.Length==0)throw Invalid(path,"Select a membership, name, description or selection change.");
        }
        if(add.Intersect(remove,StringComparer.OrdinalIgnoreCase).Any())throw Invalid(path,"An entity cannot be added and removed by the same change.");
        return new(action,name,handle,expected,token?.ToUpperInvariant(),add,remove,description,selectable);
    }
    public static void Batch(IReadOnlyList<StructureEdit> operations)
    {
        if(!operations.Any(o=>o is DrawingGroupEdit431))return;
        if(operations.Any(o=>o is not DrawingGroupEdit431))throw Invalid("operations","Group membership operations use a separate structural batch.","INVALID_OPERATION_ORDER");
        var edits=operations.Cast<DrawingGroupEdit431>().ToArray();
        if(edits.Where(e=>e.Handle!=null).GroupBy(e=>e.Handle,StringComparer.OrdinalIgnoreCase).Any(g=>g.Count()>1)||
           edits.Where(e=>e.Name!=null).GroupBy(e=>e.Name,StringComparer.OrdinalIgnoreCase).Any(g=>g.Count()>1))throw Invalid("operations","Change each existing group and each destination name once in a batch.");
    }
}
