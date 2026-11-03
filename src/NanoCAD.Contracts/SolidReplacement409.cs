using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record SolidReplacement409(string[] Targets,string SourceHandle):EditSpec("replace_solid",Targets);

public static partial class ModifyValidation
{
    private static SolidReplacement409 ParseSolidReplacement409(JsonElement item,string path,string[] handles)
    {
        Shape(item,path,"operation","handles","source_handle");
        if(handles.Length!=1)throw Invalid(path+".handles","Replace one direct solid per operation.");
        var value=Required(item,"source_handle",path);
        if(value.ValueKind!=JsonValueKind.String)throw Invalid(path+".source_handle","Expected a committed native solid handle.");
        var source=Validation.NormalizeHandle(value.GetString()!);
        if(source==handles[0])throw Invalid(path+".source_handle","Replacement source must differ from the target.");
        return new(handles,source);
    }
}
