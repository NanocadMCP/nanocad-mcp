using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record ModelFeatureExtrusionSpec(string[] Targets,string ExpectedLastItemId,string PartOperation,ModelExtrusionSpec Definition,
    string[]? SourceHandles=null)
    : EditSpec("add_model_extrusion",Targets);
public sealed record ModelFeatureMapping(int operation_index,EntityRef source,EntityRef body_ref,string body_mc_id,string sketch_id,
    string profile_id,string feature_id,string distance_variable_id,string part_operation,double volume_before,double volume_after,bool source_representation_replaced,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] EntityRef[]? source_refs=null);

public static partial class ModifyValidation
{
    private static ModelFeatureExtrusionSpec ParseModelFeature(JsonElement item,string path,string[] handles)
    {
        Shape(item,path,"operation","handles","expected_last_item_id","part_operation","definition","source_handles");
        if(handles.Length!=1)throw Invalid(path+".handles","Append one feature to one existing native model history.");
        string last=Text(item,"expected_last_item_id",path,38);
        if(!Guid.TryParseExact(last,"B",out var id)||id==Guid.Empty)throw Invalid(path+".expected_last_item_id","Read the nonempty last_item_id from current inspect_model_history.");
        string mode=Text(item,"part_operation",path,8);
        if(mode is not "join" and not "cut")throw Invalid(path+".part_operation","Use join for added material or cut for a pocket/hole.");
        var definition=Required(item,"definition",path);
        Shape(definition,path+".definition","plane","profile","distance","direction","name","editable_profile");
        var spec=(ModelExtrusionSpec)CreateValidation.ParseModelExtrusion(definition,path+".definition","0");
        string[]? sources=null;
        if(item.TryGetProperty("source_handles",out var sourceArray))
        {
            if(sourceArray.ValueKind!=JsonValueKind.Array||sourceArray.GetArrayLength()<1)
                throw Invalid(path+".source_handles","Use a nonempty committed profile selection from the active Model Space.");
            sources=sourceArray.EnumerateArray().Select(v=>
            {
                if(v.ValueKind!=JsonValueKind.String)throw Invalid(path+".source_handles","Every source must be a current object handle.");
                return Validation.NormalizeHandle(v.GetString()!);
            }).ToArray();
            if(sources.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=sources.Length||sources.Contains(handles[0],StringComparer.OrdinalIgnoreCase))
                throw Invalid(path+".source_handles","Source handles must be distinct and separate from the body handle.");
            if(spec.EditableProfile)throw Invalid(path+".definition.editable_profile","Projected drawing sources already retain editable geometry; do not also create hidden editable copies.");
        }
        return new(handles,id.ToString("B").ToUpperInvariant(),mode,spec,sources);
    }
}
