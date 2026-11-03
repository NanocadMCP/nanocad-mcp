using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record ModelSketchConstraintParameterSpec379(string[] Targets,string SketchId,string ConstraintId,
    string ExpectedParameterToken,string ExpectedVariableId,double Value):EditSpec("set_sketch_constraint_parameter",Targets);
public sealed record ModelSketchConstraintParameterRead379(string constraint_id,bool eligible,string? parameter_token,
    string? unavailable_reason,string? variable_id,string? member_id,string? kind,double? control_value,
    string control_value_unit,string? variable_expression,string? variable_host_id,JsonElement? circle_geometry,
    string[] profile_ids,string[] consumer_feature_ids,string effect_scope,JsonElement? binding_scope=null);
public sealed record ModelSketchConstraintParameterChange379(int operation_index,EntityRef source,EntityRef body_ref,
    string body_mc_id,string sketch_id,string constraint_id,string variable_id,string member_id,string kind,
    double previous_value,double value,string value_unit,string previous_expression,double expression_numeric_value,
    double? previous_radius,double? radius,string[] profile_ids,string[] consumer_feature_ids,bool source_representation_replaced,string storage_preservation,JsonElement? storage_diagnostics);
public sealed record ModelSketchConstraintParameterOutput379(string operation_id,string status,bool replayed,
    EntityRef[] modified,EntityRef[] erased,EntityRef[] created,CopyMapping[] copies,string undo_history,
    ModelSketchConstraintParameterChange379[] sketch_constraint_parameter_changes);

public static class ModelSketchConstraintParameterValidation379
{
    static DomainException Invalid(string path,string message)=>new("INVALID_ARGUMENT",message,false,new{path});
    static string Id(JsonElement value,string path)
    {
        if(value.ValueKind!=JsonValueKind.String||!Guid.TryParseExact(value.GetString(),"B",out var id)||id==Guid.Empty)
            throw Invalid(path,"Read a current nonempty native braced GUID.");
        return id.ToString("B").ToUpperInvariant();
    }
    public static string[] Ids(JsonElement value,string path)
    {
        if(value.ValueKind!=JsonValueKind.Array||value.GetArrayLength()==0)throw Invalid(path,"Select current existing dimensional relation IDs.");
        var ids=new List<string>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var item in value.EnumerateArray()){string id=Id(item,path);if(!seen.Add(id))throw Invalid(path,"IDs must be distinct.");ids.Add(id);}
        return ids.ToArray();
    }
    public static EditSpec Parse(JsonElement item,string path,string[] handles)
    {
        if(handles.Length!=1)throw Invalid(path,"Select one existing native Model Space part.");
        string[] allowed=["operation","handles","sketch_id","constraint_id","expected_parameter_token","expected_variable_id","value"];
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var field in item.EnumerateObject())if(!allowed.Contains(field.Name)||!seen.Add(field.Name))throw Invalid(path+"."+field.Name,"Unknown or duplicate field.");
        JsonElement Required(string name)=>item.TryGetProperty(name,out var value)?value:throw Invalid(path+"."+name,"Missing required field.");
        var token=Required("expected_parameter_token");
        if(token.ValueKind!=JsonValueKind.String||token.GetString() is not {Length:64} text||text.Any(c=>!Uri.IsHexDigit(c)))
            throw Invalid(path+".expected_parameter_token","Read the fresh sketch_constraint_parameter_ids parameter_token in inspect_model_history.");
        var number=Required("value");if(number.ValueKind!=JsonValueKind.Number||!number.TryGetDouble(out double scalar)||!double.IsFinite(scalar)||scalar<=0)
            throw Invalid(path+".value","Use a positive finite value in the fresh readiness relation's millimeters or radians.");
        return new ModelSketchConstraintParameterSpec379(handles,Id(Required("sketch_id"),path+".sketch_id"),Id(Required("constraint_id"),path+".constraint_id"),
            text.ToUpperInvariant(),Id(Required("expected_variable_id"),path+".expected_variable_id"),scalar);
    }
    public static void Batch(IReadOnlyList<EditSpec> edits)
    {if(edits.Any(edit=>edit is ModelSketchConstraintParameterSpec379)&&edits.Count!=1)throw new DomainException("INVALID_OPERATION_ORDER","A native sketch control-value edit must be the sole operation.");}
}
