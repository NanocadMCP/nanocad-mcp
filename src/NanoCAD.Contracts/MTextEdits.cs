using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record ReplaceMTextSpec(string[] Targets,string[] ExpectedParagraphs,string[] Paragraphs):EditSpec("replace_mtext",Targets);

public static class MTextEditContent
{
    public static string Encode(string[] paragraphs,bool requireVisible)
    {
        if(paragraphs.Length <1 || paragraphs.Any(p=>p is null||p.Length>2048||p.Any(char.IsControl)||p.Contains("%<")||p.Contains("%%")))
            throw new DomainException("INVALID_ARGUMENT","MText requires a nonempty array of literal paragraphs, each at most2048 characters, without controls, fields or CAD percent codes.");
        string plain=string.Join("\n",paragraphs),encoded=PlainMTextEncoding.Encode(paragraphs);
        if(plain.Length>4096 || encoded.Length>8192 || requireVisible&&!plain.Any(c=>!char.IsWhiteSpace(c)) || MTextContent.PlainText(encoded)!=plain)
            throw new DomainException("INVALID_ARGUMENT","MText content exceeds4096 plain/8192 encoded characters, is not reversible, or replacement is blank.");
        return encoded;
    }
    public static bool IsCanonical(string contents)
    {
        var plain=MTextContent.PlainText(contents);if(plain is null)return false;
        try{return Encode(plain.Split('\n'),false)==contents;}
        catch(DomainException){return false;}
    }
}

public static partial class ModifyValidation
{
    private static ReplaceMTextSpec ParseReplaceMText(JsonElement item,string path,string[] handles)
    {
        Shape(item,path,"operation","handles","expected_paragraphs","paragraphs");
        if(handles.Length!=1)throw Invalid(path+".handles","MText replacement requires exactly one native MText handle.");
        string[] Paragraphs(string name,bool visible)
        {
            var values=Required(item,name,path);
            if(values.ValueKind!=JsonValueKind.Array || values.GetArrayLength() <1 || values.EnumerateArray().Any(v=>v.ValueKind!=JsonValueKind.String))
                throw Invalid(path+"."+name,"Expected a nonempty array of literal strings.");
            var result=values.EnumerateArray().Select(v=>v.GetString()!).ToArray();MTextEditContent.Encode(result,visible);return result;
        }
        return new(handles,Paragraphs("expected_paragraphs",false),Paragraphs("paragraphs",true));
    }
}
