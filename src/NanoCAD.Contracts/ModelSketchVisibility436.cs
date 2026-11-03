using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record ModelSketchVisibilitySpec436(string[] Targets,string SketchId,string ExpectedVisibilityToken,
    bool? SketchVisible,bool? DimensionsVisible,string[]? ConstraintIds):EditSpec("set_model_sketch_visibility",Targets);

public sealed record ModelSketchDimensionPresentation436(string constraint_id,string? dimension_id,double? native_visibility,bool? visible,string? unavailable_reason);
public sealed record ModelSketchPresentation436(string sketch_id,string? visibility_token,bool? in_edit_mode,double? native_visibility,bool? sketch_visible,
    bool? dimensions_visible,bool dimensions_complete,ModelSketchDimensionPresentation436[]? dimensions,string? unavailable_reason);
public sealed record ModelSketchVisibilityChange436(int operation_index,EntityRef source,EntityRef body_ref,string sketch_id,
    bool? requested_sketch_visible,bool? requested_dimensions_visible,string[] selected_constraint_ids,bool sketch_parent_enabled,
    ModelSketchPresentation436 before,ModelSketchPresentation436 after,string[] changed_constraint_ids,string[] unavailable_constraint_ids);
public sealed record ModelSketchVisibilityOutput436(string operation_id,string status,bool replayed,EntityRef[] modified,
    EntityRef[] erased,EntityRef[] created,CopyMapping[] copies,string undo_history,ModelSketchVisibilityChange436[] model_sketch_visibility_changes);

public static class ModelSketchVisibilityValidation436
{
    static DomainException Invalid(string path,string message)=>new("INVALID_ARGUMENT",message,false,new{path});
    static string Id(JsonElement value,string path)
    {
        if(value.ValueKind!=JsonValueKind.String||!Guid.TryParseExact(value.GetString(),"B",out var id)||id==Guid.Empty)
            throw Invalid(path,"Read a current nonempty native braced GUID.");
        return id.ToString("B").ToUpperInvariant();
    }
    public static EditSpec Parse(JsonElement item,string path,string[] handles)
    {
        if(item.ValueKind!=JsonValueKind.Object)throw Invalid(path,"Expected object.");
        if(handles.Length!=1)throw Invalid(path+".handles","Select one existing native Model Space part containing the sketch.");
        string[] allowed=["operation","handles","sketch_id","expected_visibility_token","sketch_visible","dimensions_visible","constraint_ids"];
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var field in item.EnumerateObject())if(!allowed.Contains(field.Name)||!seen.Add(field.Name))throw Invalid(path+"."+field.Name,"Unknown or duplicate field.");
        JsonElement Required(string name)=>item.TryGetProperty(name,out var value)?value:throw Invalid(path+"."+name,"Missing required field.");
        var token=Required("expected_visibility_token");
        if(token.ValueKind!=JsonValueKind.String||token.GetString() is not {Length:64} text||text.Any(c=>!Uri.IsHexDigit(c)))
            throw Invalid(path+".expected_visibility_token","Read the current inspect_sketch_constraints presentation.visibility_token.");
        bool? OptionalBool(string name)
        {
            if(!item.TryGetProperty(name,out var value))return null;
            if(value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw Invalid(path+"."+name,"Expected boolean.");
            return value.GetBoolean();
        }
        bool? sketch=OptionalBool("sketch_visible"),dimensions=OptionalBool("dimensions_visible");
        if(!sketch.HasValue&&!dimensions.HasValue)throw Invalid(path,"Set sketch_visible, dimensions_visible or both.");
        if(sketch==false&&dimensions==true)throw Invalid(path,"Showing dimensions requires a visible parent sketch; omit sketch_visible or set it true.");
        string[]? ids=null;
        if(item.TryGetProperty("constraint_ids",out var selected))
        {
            if(!dimensions.HasValue)throw Invalid(path+".constraint_ids","constraint_ids selects dimensional controls only with dimensions_visible.");
            if(selected.ValueKind!=JsonValueKind.Array||selected.GetArrayLength()==0)throw Invalid(path+".constraint_ids","Select current dimensional constraint IDs, or omit the field to select all current dimensional controls.");
            var values=new List<string>();var unique=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var value in selected.EnumerateArray())
            {
                string id=Id(value,path+".constraint_ids["+values.Count+"]");
                if(!unique.Add(id))throw Invalid(path+".constraint_ids","Constraint IDs must be distinct.");
                values.Add(id);
            }
            ids=values.ToArray();
        }
        return new ModelSketchVisibilitySpec436(handles,Id(Required("sketch_id"),path+".sketch_id"),text.ToUpperInvariant(),sketch,dimensions,ids);
    }
    public static void Batch(IReadOnlyList<EditSpec> edits)
    {
        if(edits.Any(edit=>edit is ModelSketchVisibilitySpec436)&&edits.Count!=1)
            throw new DomainException("INVALID_OPERATION_ORDER","Set one selected sketch's presentation as the sole operation; use the returned visibility state before a later edit.");
    }
}
