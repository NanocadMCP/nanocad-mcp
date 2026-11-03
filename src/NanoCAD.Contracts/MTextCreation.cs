using System.Text;
using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record MTextSpec(string[] Paragraphs,Point2 Position,double Height,double Width,string TextStyle,string LayerName):EntitySpec("MText",LayerName);
public static class PlainMTextEncoding
{
    public static string Encode(IEnumerable<string> paragraphs)
    {
        var result=new StringBuilder();bool first=true;
        foreach(var paragraph in paragraphs)
        {
            if(!first)result.Append(@"\P");first=false;
            foreach(char c in paragraph)
                switch(c)
                {
                    case '\\':result.Append(@"\\");break;
                    case '{':result.Append(@"\{");break;
                    case '}':result.Append(@"\}");break;
                    case '^':result.Append("^ ");break;
                    default:result.Append(c);break;
                }
        }
        return result.ToString();
    }
}
public static partial class CreateValidation
{
    private static MTextSpec ParseMText(JsonElement item,string path,string layer)
    {
        Shape(item,path,"type","layer","paragraphs","position","height","width","text_style");
        var values=Required(item,"paragraphs",path);
        if(values.ValueKind!=JsonValueKind.Array||values.GetArrayLength() <1)throw Invalid(path+".paragraphs","Expected a nonempty plain paragraph array.");
        var paragraphs=new List<string>();
        foreach(var value in values.EnumerateArray())
        {
            if(value.ValueKind!=JsonValueKind.String)throw Invalid(path+".paragraphs","Each paragraph must be a string.");
            var text=value.GetString()!;
            if(text.Length>2048||text.Any(char.IsControl)||text.Contains("%<")||text.Contains("%%"))throw Invalid(path+".paragraphs","Paragraph exceeds2048 characters or contains controls, fields or CAD percent codes.");
            paragraphs.Add(text);
        }
        var plain=string.Join("\n",paragraphs);
        if(plain.Length>4096||!plain.Any(c=>!char.IsWhiteSpace(c)))throw Invalid(path+".paragraphs","Expected visible literal content and at most4096 characters including paragraph separators.");
        var encoded=PlainMTextEncoding.Encode(paragraphs);
        if(encoded.Length>8192||MTextContent.PlainText(encoded)!=plain)throw Invalid(path+".paragraphs","Plain paragraph encoding is not reversible.");
        return new(paragraphs.ToArray(),Point(item,"position",path),Number(Required(item,"height",path),path+".height",0.1,10000),
            Number(Required(item,"width",path),path+".width",1,1000000),Text(item,"text_style",path,255),layer);
    }
}
