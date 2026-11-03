using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record ReplaceTextSpec(string[] Targets,string ExpectedText,string Text):EditSpec("replace_text",Targets);

public static partial class ModifyValidation
{
    private static ReplaceTextSpec ParseReplaceText(JsonElement item,string path,string[] handles)
    {
        Shape(item,path,"operation","handles","expected_text","text");
        if(handles.Length!=1)throw Invalid(path+".handles","Text replacement requires exactly one native Text handle.");
        string Literal(string name,int min,int max)
        {
            var value=Required(item,name,path);
            if(value.ValueKind!=JsonValueKind.String)throw Invalid(path+"."+name,"Expected literal text.");
            var text=value.GetString()!;
            if(text.Length<min || text.Length>max || text.Any(char.IsControl) || text.Contains("%<") || text.Contains("%%"))
                throw Invalid(path+"."+name,"Expected bounded single-line literal without fields or CAD percent codes.");
            return text;
        }
        return new(handles,Literal("expected_text",0,16384),Literal("text",1,4096));
    }
}
