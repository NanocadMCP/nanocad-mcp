using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record RegionBooleanEditSpec(string[] Targets,string Operator,string[] ToolHandles)
    :EditSpec("boolean_region",Targets);
public sealed record RegionPlaneValue(double[] point,double[] normal);
public sealed record RegionTopologyValue(int face_count,int boundary_loop_count,int edge_count,bool checked_valid);
public sealed record RegionBooleanChange(int operation_index,string @operator,EntityRef target,
    EntityRef[] tools,double area_before,double area_after,RegionPlaneValue plane,RegionTopologyValue topology_after);

public static class RegionBooleanValidation
{
    public static void Batch(IReadOnlyList<EditSpec> edits)
    {
        if(edits.Any(edit=>edit is RegionBooleanEditSpec)&&edits.Count!=1)
            throw new DomainException("INVALID_OPERATION_ORDER","boolean_region must be the sole operation; its committed tool Regions stay read-only.");
    }
}

public static partial class ModifyValidation
{
    private static RegionBooleanEditSpec ParseRegionBooleanEdit(JsonElement item,string path,string[] handles)
    {
        if(handles.Length!=1)throw Invalid(path+".handles","Select exactly one existing native target Region.");
        Shape(item,path,"operation","handles","operator","tool_handles");
        var operation=Text(item,"operator",path,16);
        if(operation is not ("union" or "intersection" or "subtract"))
            throw Invalid(path+".operator","Expected union, intersection or subtract.");
        var values=Required(item,"tool_handles",path);
        if(values.ValueKind!=JsonValueKind.Array||values.GetArrayLength()<1)
            throw Invalid(path+".tool_handles","Select at least one committed coplanar tool Region.");
        var tools=new List<string>();var unique=new HashSet<string>(StringComparer.Ordinal);int index=0;
        foreach(var value in values.EnumerateArray())
        {
            var field=path+".tool_handles["+index+++"]";
            if(value.ValueKind!=JsonValueKind.String)throw Invalid(field,"Expected a native Region handle.");
            string text;
            try{text=value.GetString()!;}catch(InvalidOperationException){throw Invalid(field,"Expected a valid Unicode handle.");}
            var handle=Validation.NormalizeHandle(text);
            if(!unique.Add(handle))throw Invalid(field,"Tool handles must be distinct.");
            if(handle==handles[0])throw Invalid(field,"The target cannot also be its own tool Region.");
            tools.Add(handle);
        }
        return new(handles,operation,tools.ToArray());
    }
}
