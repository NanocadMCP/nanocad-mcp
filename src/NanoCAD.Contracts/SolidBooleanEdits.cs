using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record SolidBooleanEditSpec(string[] Targets,string Operator,string[] ToolHandles)
    :EditSpec("boolean_solid",Targets);
public sealed record SolidBooleanChange(int operation_index,string @operator,EntityRef target,
    EntityRef[] tools,double volume_before,double volume_after);

public static partial class ModifyValidation
{
    private static SolidBooleanEditSpec ParseSolidBooleanEdit(JsonElement item,string path,string[] handles)
    {
        if(handles.Length!=1)throw Invalid(path+".handles","Select exactly one direct target solid.");
        Shape(item,path,"operation","handles","operator","tool_handles");
        var operation=Text(item,"operator",path,16);
        if(operation is not ("union" or "intersection" or "subtract"))
            throw Invalid(path+".operator","Expected union, intersection or subtract.");
        var values=Required(item,"tool_handles",path);
        if(values.ValueKind!=JsonValueKind.Array||values.GetArrayLength()<1)
            throw Invalid(path+".tool_handles","Select at least one committed tool solid.");
        var tools=values.EnumerateArray().Select(v=>v.ValueKind==JsonValueKind.String
            ?Validation.NormalizeHandle(v.GetString()!)
            :throw Invalid(path+".tool_handles","Expected native tool handles.")).ToArray();
        if(tools.Distinct(StringComparer.Ordinal).Count()!=tools.Length)
            throw Invalid(path+".tool_handles","Tool handles must be distinct.");
        if(tools.Contains(handles[0],StringComparer.Ordinal))
            throw Invalid(path+".tool_handles","The target cannot also be its own tool solid.");
        return new(handles,operation,tools);
    }
}
