using System.Text.Json;

namespace NanoCAD.Contracts;

// Stored native bindings are observations, not selectors or generated copies.
public sealed record ModelPatternDirectionBinding439(string native_id,double parameter,int gs_marker,long data,
    string spacing_mode,bool reversed);

public static partial class ModifyValidation
{
    private static JsonElement PatternDirection439(JsonElement value,string path)
    {
        string kind=Text(value,"kind",path,16);
        switch(kind)
        {
            case "global_axis":
                Shape(value,path,"kind","axis");
                if(Text(value,"axis",path,1) is not ("X" or "Y" or "Z"))throw Invalid(path+".axis","Use X, Y or Z in the selected part's native owner document.");
                break;
            case "work_axis":
                Shape(value,path,"kind","axis_id");PatternId439(value,"axis_id",path);break;
            case "line":
                Shape(value,path,"kind","handle");Validation.NormalizeHandle(Text(value,"handle",path,16));break;
            case "edge":
                Shape(value,path,"kind","edge_id","edge_token");PatternId439(value,"edge_id",path);
                string token=Text(value,"edge_token",path,64);
                if(token.Length!=64||token.Any(c=>!Uri.IsHexDigit(c)))throw Invalid(path+".edge_token","Read the current selected body's native model edge token.");
                break;
            case "fixed_axis":
                Shape(value,path,"kind","origin","direction");
                foreach(string field in new[]{"origin","direction"})
                {
                    var vector=Required(value,field,path);
                    if(vector.ValueKind!=JsonValueKind.Array||vector.GetArrayLength()!=3)throw Invalid(path+"."+field,"Use three finite WCS coordinates/components.");
                    foreach(var number in vector.EnumerateArray())Number(number,path+"."+field,-1e9,1e9);
                }
                if(value.GetProperty("direction").EnumerateArray().All(n=>n.GetDouble()==0))throw Invalid(path+".direction","The native fixed axis needs a nonzero direction.");
                break;
            default:throw Invalid(path+".kind","Use global_axis, work_axis, fixed_axis, line or edge.");
        }
        return value.Clone();
    }
    private static string PatternId439(JsonElement item,string field,string path)
    {
        string text=Text(item,field,path,38);
        if(!Guid.TryParseExact(text,"B",out var id)||id==Guid.Empty)throw Invalid(path+"."+field,"Read an exact current native ID.");
        return id.ToString("B").ToUpperInvariant();
    }
}
