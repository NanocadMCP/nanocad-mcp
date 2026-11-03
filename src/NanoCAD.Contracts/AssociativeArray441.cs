using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record CreateAssociativePolarArray441(string[] Targets,Point2 BasePoint,Point2 Center,double Radius,double StartAngleDegrees,int Count,double AngleStepDegrees,bool RotateItems)
    :EditSpec("create_associative_polar_array",Targets);
public sealed record SetAssociativePolarArray441(string[] Targets,string ArrayToken,int? Count,double? AngleStepDegrees,bool? RotateItems)
    :EditSpec("set_associative_polar_array",Targets);
public sealed record CreateAssociativePathArray441(string[] Targets,Point2 BasePoint,string PathHandle,int Count,double ItemSpacing,bool AlignItems)
    :EditSpec("create_associative_path_array",Targets);
public sealed record SetAssociativePathArray441(string[] Targets,string ArrayToken,int? Count,double? ItemSpacing,bool? AlignItems,string? PathHandle=null)
    :EditSpec("set_associative_path_array",Targets);

public sealed record AssociativePolarArrayObservation441(string array_token,string kind,string array_handle,string[] source_handles,
    int count,int rows,int levels,double radius,double start_angle_degrees,double angle_step_degrees,double fill_angle_degrees,bool rotate_items,
    double row_spacing,double level_spacing,double row_elevation,double[] native_base_point,double[] base_normal,double[] position_wcs,double[] scale_factors,double rotation,
    string driver_expression_observation);
public sealed record AssociativePathArrayObservation441(string array_token,string kind,string array_handle,string[] source_handles,
    string? path_handle,int count,int rows,int levels,double item_spacing,string method,bool align_items,double start_offset,double end_offset,
    double row_spacing,double level_spacing,double row_elevation,double[] native_base_point,double[] base_normal,double[] position_wcs,double[] scale_factors,double rotation,
    string path_direction_observation,string driver_expression_observation,AssociativePathBindingObservation441 path_binding_observation,
    AssociativeArrayItemsObservation446? item_geometry_observation=null,
    AssociativeArrayEvaluationObservation446? evaluation_observation=null);
public sealed record AssociativeArrayItemsObservation446(long native_item_count,long active_item_count,long erased_item_count,
    string locator_transform_sha256,long? materialized_source_reference_count,string? source_owner_handle,string? representation_unavailable_reason,string scope);
public sealed record AssociativeArrayEvaluationObservation446(string state,bool complete,string? unavailable_reason,
    string? controller_handle,string? controller_status,string? action_body_handle,
    string? root_write_dependency_handle,string? root_write_dependency_status,
    string? owning_network_handle,string? database_top_network_handle,bool? reaches_database_top_network,
    AssociativeArrayNetworkObservation446[] owner_chain,string scope);
public sealed record AssociativeArrayNetworkObservation446(string network_handle,string? status,string? parent_network_handle,
    bool? contains_observed_child_action,bool? is_database_top_network,string? unavailable_reason);
public sealed record AssociativePathBindingObservation441(string state,string? unavailable_reason,bool? simple,bool? external,
    string? native_leaf_handle,bool? native_leaf_live,bool? same_space,bool? same_database,
    bool? native_curve_geometry_available,string native_curve_geometry_observation);

public static class AssociativeArrayValidation441
{
    public static bool Handles(EditSpec edit)=>edit is CreateAssociativePolarArray441 or SetAssociativePolarArray441 or CreateAssociativePathArray441 or SetAssociativePathArray441;
    public static EditSpec Parse(JsonElement input,string path,string[] handles,string operation)
    {
        DomainException Bad(string message)=>new("INVALID_ARGUMENT",message,false,new{path});
        bool create=operation.StartsWith("create_",StringComparison.Ordinal),polar=operation.EndsWith("polar_array",StringComparison.Ordinal);
        if(operation is not ("create_associative_polar_array" or "set_associative_polar_array" or "create_associative_path_array" or "set_associative_path_array"))throw Bad("Choose an actual supported native associative-array operation.");
        string[] allowed=(create?new[]{"operation","handles","base_point","count"}:new[]{"operation","handles","array_token","count"})
            .Concat(polar?(create?new[]{"center","radius","start_angle_degrees","angle_step_degrees","rotate_items"}:new[]{"angle_step_degrees","rotate_items"}):
                new[]{"path_handle","item_spacing","align_items"}).ToArray();
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var field in input.EnumerateObject())if(!seen.Add(field.Name)||!allowed.Contains(field.Name))throw Bad("Unknown or duplicate array field: "+field.Name+".");
        if(handles.Length==0||!create&&handles.Length!=1)throw Bad("Choose source geometry for creation, or exactly one retained native array root for editing.");
        int? count=null;
        if(input.TryGetProperty("count",out var number))
        {if(number.ValueKind!=JsonValueKind.Number||!number.TryGetInt32(out int value)||value<1)throw Bad("count must be a positive native Int32 including the seed; no artificial count ceiling.");count=value;}
        else if(create)throw Bad("count is required and includes the seed.");
        double? Number(string field,bool positive,bool required)
        {
            if(!input.TryGetProperty(field,out var item)){if(required)throw Bad(field+" is required.");return null;}
            if(item.ValueKind!=JsonValueKind.Number||!item.TryGetDouble(out double value)||!double.IsFinite(value)||(positive&&value<=0))throw Bad(field+" must be a finite "+(positive?"positive ":"")+"number.");
            return value;
        }
        bool? Flag(string field)
        {
            if(!input.TryGetProperty(field,out var item)){if(create)throw Bad(field+" is required.");return null;}
            if(item.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw Bad(field+" must be boolean.");return item.GetBoolean();
        }
        Point2 Point(string field)
        {
            if(!input.TryGetProperty(field,out var item)||item.ValueKind!=JsonValueKind.Array||item.GetArrayLength()!=2||
                item.EnumerateArray().Any(value=>value.ValueKind!=JsonValueKind.Number||!value.TryGetDouble(out double n)||!double.IsFinite(n)||Math.Abs(n)>1e6))
                throw Bad(field+" requires explicit WCSXY drawing-millimeter coordinates.");
            return new(item[0].GetDouble(),item[1].GetDouble());
        }
        string token="";
        if(!create)
        {
            if(!input.TryGetProperty("array_token",out var item)||item.ValueKind!=JsonValueKind.String||item.GetString() is not {} text||text.Length!=64||!text.All(Uri.IsHexDigit))throw Bad("Read the actual native array's current array_token.");
            token=text.ToUpperInvariant();
        }
        if(polar)
        {
            var step=Number("angle_step_degrees",false,create);if(step==0)throw Bad("Use a nonzero signed angle step in degrees; negative means clockwise.");
            var rotate=Flag("rotate_items");
            if(create)return new CreateAssociativePolarArray441(handles,Point("base_point"),Point("center"),Number("radius",true,true)!.Value,
                Number("start_angle_degrees",false,true)!.Value,count!.Value,step!.Value,rotate!.Value);
            if(count==null&&step==null&&rotate==null)throw Bad("Supply at least one count, angle_step_degrees or rotate_items change.");
            return new SetAssociativePolarArray441(handles,token,count,step,rotate);
        }
        var spacing=Number("item_spacing",true,create);var align=Flag("align_items");
        string? pathHandle=null;
        if(input.TryGetProperty("path_handle",out var selectedPath))
        {
            if(selectedPath.ValueKind!=JsonValueKind.String)throw Bad("Supply an explicit actual native path_handle string; omit it to keep the current path.");
            pathHandle=Validation.NormalizeHandle(selectedPath.GetString()!);if(handles.Contains(pathHandle,StringComparer.OrdinalIgnoreCase))throw Bad("The path must be separate from acquired sources or the selected array root.");
        }
        if(create)return new CreateAssociativePathArray441(handles,Point("base_point"),pathHandle??throw Bad("Supply the exact live native path_handle."),count!.Value,spacing!.Value,align!.Value);
        if(count==null&&spacing==null&&align==null&&pathHandle==null)throw Bad("Supply at least one count, item_spacing, align_items or explicit path_handle change.");
        return new SetAssociativePathArray441(handles,token,count,spacing,align,pathHandle);
    }
}
