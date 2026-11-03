using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record ModelFeatureRevolutionSpec(string[] Targets,string ExpectedLastItemId,string PartOperation,
    string[] SourceHandles,string AxisHandle,SketchPlaneSpec Plane,double AngleDegrees,string Name)
    :EditSpec("add_model_revolution",Targets);
public sealed record ModelFeatureRevolutionMapping(int operation_index,EntityRef source,EntityRef body_ref,string body_mc_id,
    string sketch_id,string profile_id,string feature_id,string angle_variable_id,string part_operation,double angle_degrees,
    EntityRef axis_ref,EntityRef[] source_refs,double volume_before,double volume_after,bool source_representation_replaced);

public static class ModelFeatureRevolutionValidation
{
    public static void Batch(IReadOnlyList<EditSpec> edits)
    {
        if(edits.Any(edit=>edit is ModelFeatureRevolutionSpec)&&edits.Count!=1)
            throw new DomainException("INVALID_OPERATION_ORDER","add_model_revolution must be the sole operation in its request.");
    }
}

public static partial class ModifyValidation
{
    private static ModelFeatureRevolutionSpec ParseModelFeatureRevolution(JsonElement item,string path,string[] handles)
    {
        Shape(item,path,"operation","handles","expected_last_item_id","part_operation","source_handles","axis_handle","plane","angle_degrees","name");
        if(handles.Length!=1)throw Invalid(path+".handles","Append one native revolution to one existing body's history.");
        string last=Text(item,"expected_last_item_id",path,38);
        if(!Guid.TryParseExact(last,"B",out var id)||id==Guid.Empty)
            throw Invalid(path+".expected_last_item_id","Read the current nonempty last_item_id from inspect_model_history.");
        string mode=Text(item,"part_operation",path,8);
        if(mode is not ("join" or "cut"))throw Invalid(path+".part_operation","Use join or cut.");
        string axis=Validation.NormalizeHandle(Text(item,"axis_handle",path,16));
        if(axis==handles[0])throw Invalid(path+".axis_handle","Axis must be a separate committed native Line.");
        var values=Required(item,"source_handles",path);
        if(values.ValueKind!=JsonValueKind.Array||values.GetArrayLength()<1)
            throw Invalid(path+".source_handles","Select a complete closed Circle or Line/Arc contour.");
        var sources=new List<string>();var unique=new HashSet<string>(StringComparer.Ordinal);int index=0;
        foreach(var value in values.EnumerateArray())
        {
            string field=path+".source_handles["+index+++"]";
            if(value.ValueKind!=JsonValueKind.String)throw Invalid(field,"Expected a committed source handle.");
            string text;
            try{text=value.GetString()!;}catch(InvalidOperationException){throw Invalid(field,"Expected a valid Unicode handle.");}
            var handle=Validation.NormalizeHandle(text);
            if(!unique.Add(handle)||handle==handles[0]||handle==axis)
                throw Invalid(field,"Profile handles must be distinct and separate from body and axis.");
            sources.Add(handle);
        }
        var plane=DrawingWorkplane.Parse(Required(item,"plane",path));
        double angle=Number(Required(item,"angle_degrees",path),path+".angle_degrees",0,360);
        if(angle<=0||angle*Math.PI/180<=0)throw Invalid(path+".angle_degrees","Use a positive angle representable in native radians, at most360 degrees.");
        string name=item.TryGetProperty("name",out _)?Text(item,"name",path,64):"Revolution";
        return new(handles,id.ToString("B").ToUpperInvariant(),mode,sources.ToArray(),axis,plane,angle,name);
    }
}
