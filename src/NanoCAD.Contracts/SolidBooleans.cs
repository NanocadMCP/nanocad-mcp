using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record SolidBooleanSpec(string Operator, string[] SourceHandles, string LayerName) : EntitySpec("Solid3d", LayerName);

public static partial class CreateValidation
{
    private static SolidBooleanSpec ParseSolidBoolean(JsonElement item,string path,string layer)
    {
        Shape(item,path,"type","kind","layer","operator","source_handles");
        string op=Text(item,"operator",path,16);
        if(op is not ("union" or "intersection" or "subtract"))throw Invalid(path+".operator","Expected union, intersection or subtract.");
        var values=Required(item,"source_handles",path);
        if(values.ValueKind!=JsonValueKind.Array || values.GetArrayLength()!=2)throw Invalid(path,"Boolean requires two distinct committed source handles.");
        var handles=values.EnumerateArray().Select(v=>v.ValueKind==JsonValueKind.String?Validation.NormalizeHandle(v.GetString()!):throw Invalid(path,"Expected source handle.")).ToArray();
        if(handles[0]==handles[1])throw Invalid(path,"Boolean source handles must differ.");
        return new(op,handles,layer);
    }
}
