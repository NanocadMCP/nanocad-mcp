using System.Text.Json;

namespace NanoCAD.Contracts;

// Additive typed branch of the existing extent operation. The established
// positive/negative, through-all and terminal-face branch remains unchanged.
public sealed record ModelExtrusionExtentsSpec351(string[] Targets,string FeatureId,string ExpectedProfileId,
    string ExpectedSketchId,string ExpectedExtentToken,string ExpectedLastItemId,string Direction,double NativeDistance,
    double TaperAngle,double? NativeDistance2,double? TaperAngle2,bool ReplaceDriverFormulas,string Extent2="distance")
    :EditSpec("set_model_extrusion_extent",Targets);
public sealed record ModelExtrusionExtentsRead351(string feature_id,string profile_id,string parent_sketch_id,string mc_document_id,
    string extent_token,string direction,string extent,string extent2,double native_distance,double taper_angle,
    double? native_distance2,double? taper_angle2,string? distance_variable_id,string? taper_angle_variable_id,
    string? distance2_variable_id,string? taper_angle2_variable_id,JsonElement driver_state,JsonElement plane,
    JsonElement body_bounds_wcs,string length_semantics,string secondary_driver_scope,JsonElement[] constraint_observations,NativeAssocManagerObservationRead356[] owned_metadata_observations,string expression_edit_scope,string owned_metadata_revision_scope,string? terminal_entity_id=null,string? terminal_entity2_id=null);
public sealed record ModelExtrusionExtentsChange351(int operation_index,EntityRef source,EntityRef body_ref,string body_mc_id,
    string feature_id,string profile_id,string parent_sketch_id,string mc_document_id,string last_item_id,bool source_representation_replaced,
    ModelExtrusionExtentsRead351 extents,string[] changed_variable_ids,string[] downstream_variable_ids,string[] created_variable_ids,
    string[] retained_downstream_ids,string[] unobserved_geometry_ids,ModelExtrusionOwnedMetadataChange357 owned_metadata_change);
public sealed record ModelExtrusionExtentsOutput351(string operation_id,string status,bool replayed,EntityRef[] modified,
    EntityRef[] erased,EntityRef[] created,CopyMapping[] copies,string undo_history,ModelExtrusionExtentsChange351[] model_extrusion_extents);

public static class ModelExtrusionExtentsValidation351
{
    static DomainException Invalid(string path,string message)=>new("INVALID_ARGUMENT",message,false,new{path});
    static string Text(JsonElement item,string key,string path)
    {if(!item.TryGetProperty(key,out var value)||value.ValueKind!=JsonValueKind.String)throw Invalid(path+"."+key,"Expected a string.");return value.GetString()!;}
    static string Id(JsonElement item,string key,string path)
    {if(!Guid.TryParseExact(Text(item,key,path),"B",out var id)||id==Guid.Empty)throw Invalid(path+"."+key,"Read the current native ID.");return id.ToString("B").ToUpperInvariant();}
    static void Shape(JsonElement item,string path,string[] fields)
    {if(item.ValueKind!=JsonValueKind.Object)throw Invalid(path,"Expected an object.");var names=new HashSet<string>(StringComparer.Ordinal);foreach(var field in item.EnumerateObject())if(!fields.Contains(field.Name)||!names.Add(field.Name))throw Invalid(path+"."+field.Name,"Unknown or duplicate field.");}
    public static bool Selects(JsonElement item)=>item.TryGetProperty("definition",out var d)&&d.ValueKind==JsonValueKind.Object&&
        d.TryGetProperty("direction",out var direction)&&direction.ValueKind==JsonValueKind.String&&direction.GetString() is "different" or "symmetric";
    public static EditSpec Parse(JsonElement item,string path,string[] handles)
    {
        Shape(item,path,["operation","handles","feature_id","expected_profile_id","expected_sketch_id","expected_extent_token","expected_last_item_id","definition"]);
        if(handles.Length!=1)throw Invalid(path,"Select one current native part.");
        string token=Text(item,"expected_extent_token",path);if(token.Length!=64||token.Any(c=>!Uri.IsHexDigit(c)))throw Invalid(path,"Read the current extrusion_extent_ids extent_token.");
        if(!item.TryGetProperty("definition",out var d))throw Invalid(path,"definition is required.");string at=path+".definition";
        Shape(d,at,["extent","direction","native_distance","taper_angle","native_distance2","taper_angle2","replace_driver_formulas","extent2"]);
        string direction=Text(d,"direction",at);if(direction is not ("different" or "symmetric"))throw Invalid(at,"Use native different or symmetric direction.");
        if(Text(d,"extent",at)!="distance")throw Invalid(at,"This branch selects native Dist extent; existing one-sided termination remains available.");
        double Number(string key,double min,double max)
        {if(!d.TryGetProperty(key,out var value)||value.ValueKind!=JsonValueKind.Number||!value.TryGetDouble(out var number)||!double.IsFinite(number)||number<min||number>max)throw Invalid(at+"."+key,"Finite value outside the supported drawing range.");return number;}
        double distance=Number("native_distance",.001,1e6),angle=Number("taper_angle",-Math.PI/2+1e-6,Math.PI/2-1e-6);
        double? distance2=null,angle2=null;string extent2=d.TryGetProperty("extent2",out _)?Text(d,"extent2",at):"distance";
        if(direction=="different")
        {
            if(extent2 is not ("distance" or "through_all"))throw Invalid(at+".extent2","Use distance or through_all on the negative side; terminal-face conversion is separate.");
            if(extent2=="distance")distance2=Number("native_distance2",.001,1e6);
            else if(d.TryGetProperty("native_distance2",out _))throw Invalid(at+".native_distance2","ThroughAll has no requested finite secondary length.");
            angle2=Number("taper_angle2",-Math.PI/2+1e-6,Math.PI/2-1e-6);
        }
        else if(d.TryGetProperty("extent2",out _)||d.TryGetProperty("native_distance2",out _)||d.TryGetProperty("taper_angle2",out _))throw Invalid(at,"Native Symmetric uses one retained length/taper parameter; secondary values are not independent inputs.");
        bool replace=false;if(d.TryGetProperty("replace_driver_formulas",out var flag)){if(flag.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw Invalid(at,"replace_driver_formulas must be boolean.");replace=flag.GetBoolean();}
        return new ModelExtrusionExtentsSpec351(handles,Id(item,"feature_id",path),Id(item,"expected_profile_id",path),Id(item,"expected_sketch_id",path),token.ToUpperInvariant(),
            Id(item,"expected_last_item_id",path),direction,distance,angle,distance2,angle2,replace,extent2);
    }
    public static void Batch(IReadOnlyList<EditSpec> edits)
    {if(edits.Any(edit=>edit is ModelExtrusionExtentsSpec351)&&edits.Count!=1)throw Invalid("$.operations","A native two-sided extrusion revision must be the sole operation; refresh resulting body refs.");}
}
