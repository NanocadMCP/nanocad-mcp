using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record ReplaceDimensionTextSpec(string[] Targets,string ExpectedText,string Text):EditSpec("replace_dimension_text",Targets);
public static class DimensionTextContent
{
    public static bool IsPlain(string text)
    {
        if(text.Length>256 || text.Any(char.IsControl) || text.IndexOfAny(new[]{'\\','{','}','%'})>=0)return false;
        var literal=text.Replace("<>","",StringComparison.Ordinal);
        return literal.IndexOfAny(new[]{'<','>'})<0 && text.Length-literal.Length<=2;
    }
    public static bool PreservesMeasurement(string text)=>IsPlain(text) && (text.Length==0 || text.Contains("<>",StringComparison.Ordinal));
}
public static partial class ModifyValidation
{
    private static ReplaceDimensionTextSpec ParseDimensionText(JsonElement item,string path,string[] handles)
    {
        Shape(item,path,"operation","handles","expected_text","text");
        if(handles.Length!=1)throw Invalid(path,"Dimension text replacement requires one handle.");
        string ReadText(string name)
        {
            var value=Required(item,name,path);
            if(value.ValueKind!=JsonValueKind.String || !DimensionTextContent.IsPlain(value.GetString()!))
                throw Invalid(path+"."+name,"At most256 plain characters, no fields/formatting/controls; at most one <> measurement token.");
            return value.GetString()!;
        }
        var expected=ReadText("expected_text");var text=ReadText("text");
        if(!DimensionTextContent.PreservesMeasurement(text))throw Invalid(path+".text","Use an empty string for the native value or one <> token with a plain prefix/suffix.");
        return new(handles,expected,text);
    }
}
