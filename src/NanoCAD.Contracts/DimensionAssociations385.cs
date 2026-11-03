using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record DimensionSourcePoint385(string Role,string SourceHandle,string Snap);
public sealed record DimensionAssociationEdit385(string Action,string[] Targets,string AssociationToken,DimensionSourcePoint385[]? Bindings)
    :EditSpec(Action,Targets);
public sealed record DimensionAssociationChange385(int operation_index,EntityRef dimension_ref,string association_token,string? association_handle,string[] source_handles,string[] warnings);
public sealed record DimensionAssociationOutput385(string operation_id,string status,bool replayed,EntityRef[] modified,EntityRef[] erased,
    EntityRef[] created,CopyMapping[] copies,string undo_history,DimensionAssociationChange385[] dimension_association_changes);

public static class DimensionAssociationValidation385
{
    public static readonly string[] Actions=["associate_dimension","update_dimension_association","detach_dimension_association"];
    public static readonly string[] Roles=["xline1","xline2","center","chord","far_chord"];
    public static readonly string[] Snaps=["start","end","midpoint","center","quadrant_x_positive","quadrant_y_positive","quadrant_x_negative","quadrant_y_negative"];
    static DomainException Bad(string path,string message)=>new("INVALID_ARGUMENT",message,false,new{path});
    static void Shape(JsonElement value,string path,params string[] fields)
    {
        if(value.ValueKind!=JsonValueKind.Object)throw Bad(path,"Expected an object.");
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var field in value.EnumerateObject())if(!seen.Add(field.Name)||!fields.Contains(field.Name))throw Bad(path+"."+field.Name,"Unknown or duplicate field.");
        foreach(string field in fields)if(!value.TryGetProperty(field,out _))throw Bad(path+"."+field,"Required field.");
    }
    static string Text(JsonElement value,string path)
    {if(value.ValueKind!=JsonValueKind.String)throw Bad(path,"Expected a string.");return value.GetString()!;}
    public static DimensionSourcePoint385[] ParseBindings(JsonElement value,string path)
    {
        if(value.ValueKind!=JsonValueKind.Array||value.GetArrayLength()!=2)throw Bad(path,"Supply the two actual defining-point roles of an aligned, rotated, radial or diametric dimension.");
        var result=value.EnumerateArray().Select((item,index)=>
        {
            string at=path+"["+index+"]";Shape(item,at,"role","source_handle","snap");
            string role=Text(item.GetProperty("role"),at+".role"),snap=Text(item.GetProperty("snap"),at+".snap");
            if(!Roles.Contains(role)||!Snaps.Contains(snap))throw Bad(at,"Use a published native defining-point role and supported ordinary source snap.");
            return new DimensionSourcePoint385(role,Validation.NormalizeHandle(Text(item.GetProperty("source_handle"),at+".source_handle")),snap);
        }).ToArray();
        if(result.Select(item=>item.Role).Distinct(StringComparer.Ordinal).Count()!=2)throw Bad(path,"Defining-point roles must be distinct.");
        return result;
    }
    public static DimensionAssociationEdit385 Parse(JsonElement item,string path,string[] handles,string action)
    {
        Shape(item,path,action=="associate_dimension"?["operation","handles","association_token","bindings"]:["operation","handles","association_token"]);
        if(handles.Length!=1)throw Bad(path+".handles","Address one current dimension per association edit.");
        string token=Text(item.GetProperty("association_token"),path+".association_token");
        if(token.Length!=64||token.Any(value=>!Uri.IsHexDigit(value)))throw Bad(path+".association_token","Read the current geometry.association.association_token.");
        return new(action,handles,token.ToUpperInvariant(),action=="associate_dimension"?ParseBindings(item.GetProperty("bindings"),path+".bindings"):null);
    }
    public static void Batch(IReadOnlyCollection<EditSpec> edits)
    {
        if(!edits.Any(edit=>edit is DimensionAssociationEdit385))return;
        if(edits.Any(edit=>edit is not DimensionAssociationEdit385)||edits.SelectMany(edit=>edit.Handles).Distinct(StringComparer.Ordinal).Count()!=edits.Count)
            throw new DomainException("INVALID_OPERATION_ORDER","Use a dedicated association batch with distinct dimension targets; source geometry is retained.");
    }
}
