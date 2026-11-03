using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record ModelSketchConstraintRemoval430(string[] Targets,string SketchId,string ExpectedBindingToken,string[] ConstraintIds):EditSpec("delete_sketch_constraints",Targets);
public sealed record ModelSketchConstraintDeletion430(string sketch_id,string[] deleted_constraint_ids,string[] retired_control_variable_ids,string[] retained_control_variable_ids,bool native_rebuild_accepted,bool model_healthy,string[] unresolved_history_ids);
public sealed record ModelSketchConstraintRemovalOutput430(string operation_id,string status,bool replayed,EntityRef[] modified,EntityRef[] erased,EntityRef[] created,CopyMapping[] copies,string undo_history,ModelSketchConstraintDeletion430[] sketch_constraint_deletions);

public static class ModelSketchConstraintRemovalValidation430
{
    static DomainException Invalid(string path,string message)=>new("INVALID_ARGUMENT",message,false,new{path});
    static string Id(JsonElement value,string path)
    {
        if(value.ValueKind!=JsonValueKind.String||!Guid.TryParseExact(value.GetString(),"B",out var id)||id==Guid.Empty)throw Invalid(path,"Read a current nonempty native braced GUID.");
        return id.ToString("B").ToUpperInvariant();
    }
    public static EditSpec Parse(JsonElement item,string path,string[] handles)
    {
        if(handles.Length!=1)throw Invalid(path,"Select one native part containing the sketch.");
        string[] allowed=["operation","handles","sketch_id","expected_binding_token","constraint_ids"];var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var field in item.EnumerateObject())if(!allowed.Contains(field.Name)||!seen.Add(field.Name))throw Invalid(path+"."+field.Name,"Unknown or duplicate field.");
        JsonElement Required(string name)=>item.TryGetProperty(name,out var value)?value:throw Invalid(path+"."+name,"Missing required field.");
        var token=Required("expected_binding_token");if(token.ValueKind!=JsonValueKind.String||token.GetString() is not{Length:64} text||text.Any(c=>!Uri.IsHexDigit(c)))throw Invalid(path+".expected_binding_token","Read the fresh selected sketch native_inputs.binding_token.");
        var values=Required("constraint_ids");if(values.ValueKind!=JsonValueKind.Array||values.GetArrayLength()==0)throw Invalid(path+".constraint_ids","Select actual current relation IDs from inspect_sketch_constraints or the sketch relation observation.");
        var ids=new List<string>();var unique=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var value in values.EnumerateArray()){string id=Id(value,path+".constraint_ids");if(!unique.Add(id))throw Invalid(path+".constraint_ids","Constraint IDs must be distinct.");ids.Add(id);}
        return new ModelSketchConstraintRemoval430(handles,Id(Required("sketch_id"),path+".sketch_id"),text.ToUpperInvariant(),ids.ToArray());
    }
    public static void Batch(IReadOnlyList<EditSpec> edits)
    {if(edits.Any(edit=>edit is ModelSketchConstraintRemoval430)&&edits.Count!=1)throw new DomainException("INVALID_OPERATION_ORDER","Delete selected relations together as one sketch operation; perform dependent edits after its actual result.");}
}
