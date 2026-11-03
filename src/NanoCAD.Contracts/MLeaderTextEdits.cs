using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.Contracts;

public sealed record ReplaceMLeaderTextSpec(string[] Targets,string ExpectedText,string Text):EditSpec("replace_mleader_text",Targets);
public static class MLeaderTextLiteral
{
    public static bool Supported(string text,int min,int max)=>text.Length>=min&&text.Length<=max&&!text.Any(char.IsControl)&&
        text.IndexOfAny(new[]{'\\','{','}'})<0&&!text.Contains("%<")&&!text.Contains("%%");
}
public static partial class ModifyValidation
{
    private static ReplaceMLeaderTextSpec ParseMLeaderText(JsonElement item,string path,string[] handles)
    {
        Shape(item,path,"operation","handles","expected_text","text");
        if(handles.Length!=1)throw Invalid(path+".handles","Exactly one native MLeader required.");
        string Literal(string name,int min,int max)
        {
            var value=Required(item,name,path);
            if(value.ValueKind!=JsonValueKind.String || !MLeaderTextLiteral.Supported(value.GetString()!,min,max))
                throw Invalid(path+"."+name,"Expected bounded single-line literal without fields or formatting codes.");
            return value.GetString()!;
        }
        return new(handles,Literal("expected_text",0,16384),Literal("text",1,4096));
    }
}
public static class MLeaderTextVerification
{
    public static void Verify(JsonElement before,JsonElement after,string text)
    {
        if(before.TryGetProperty("multi_line_snapshot",out var multi)&&multi.ValueKind==JsonValueKind.True)
        {VerifyMulti(before,after,text);return;}
        var expected=JsonNode.Parse(before.GetRawText())!;expected["text"]=text;
        if(before.TryGetProperty("landing_connection",out var connection) && after.TryGetProperty("connection",out var newConnection) &&
            connection.ValueKind==JsonValueKind.Array && connection.GetArrayLength()==3 &&
            connection.EnumerateArray().All(v=>v.TryGetDouble(out var n)&&double.IsFinite(n)) && Unchanged(connection,newConnection) &&
            before.TryGetProperty("text_metadata",out var oldMetadata) && after.TryGetProperty("text_metadata",out var newMetadata) &&
            oldMetadata.TryGetProperty("location",out var oldTextLocation) && newMetadata.TryGetProperty("location",out var newTextLocation) &&
            before.TryGetProperty("location",out var oldLocation) && after.TryGetProperty("location",out var newLocation) &&
            Unchanged(oldTextLocation,oldLocation) && Unchanged(newTextLocation,newLocation))
        {
            // Text reflows around its fixed native attachment. All connection,
            // polyline, style and orientation fields still compare unchanged.
            expected["location"]=JsonNode.Parse(newLocation.GetRawText());
            expected["text_metadata"]!["location"]=JsonNode.Parse(newTextLocation.GetRawText());
            expected["connection"]=JsonNode.Parse(connection.GetRawText());
        }
        using var doc=JsonDocument.Parse(expected.ToJsonString());
        if(!Unchanged(doc.RootElement,after))
        {
            var differences=new List<string>();FindDifferences(doc.RootElement,after,"$",differences);
            throw new DomainException("NATIVE_MLEADER_MISMATCH","MLeader literal or observed style/placement/vertices changed unexpectedly.",false,new{changed_properties=differences});
        }
    }
    private static void VerifyMulti(JsonElement before,JsonElement after,string text)
    {
        var expected=JsonNode.Parse(before.GetRawText())!;expected["text"]=text;
        // Native glyph insertion may reflow around fixed branch landings.
        // The complete topology and all actual vertices remain compared.
        JsonElement current=default,old=default;
        bool fixedConnections=after.TryGetProperty("connections",out current)&&before.TryGetProperty("connections",out old)&&current.GetArrayLength()==old.GetArrayLength();
        if(fixedConnections)
        {
            foreach(var connection in current.EnumerateArray())
            {
                int branch=connection.GetProperty("branch").GetInt32();
                var prior=old.EnumerateArray().SingleOrDefault(item=>item.GetProperty("branch").GetInt32()==branch);
                if(prior.ValueKind!=JsonValueKind.Object||!Unchanged(prior.GetProperty("landing_connections"),connection.GetProperty("landing_connections"))||
                   !prior.GetProperty("landing_connections").EnumerateArray().Any(p=>Unchanged(p,connection.GetProperty("point")))){fixedConnections=false;break;}
            }
        }
        if(fixedConnections)
        {
            var layout=after.GetProperty("layout");var beforeLayout=before.GetProperty("layout");
            bool consistent=Unchanged(beforeLayout.GetProperty("text_location"),beforeLayout.GetProperty("embedded").GetProperty("location"))&&
                Unchanged(layout.GetProperty("text_location"),layout.GetProperty("embedded").GetProperty("location"));
            if(consistent)
            {
                expected["layout"]!["text_location"]=JsonNode.Parse(layout.GetProperty("text_location").GetRawText());
                expected["layout"]!["embedded"]!["location"]=JsonNode.Parse(layout.GetProperty("embedded").GetProperty("location").GetRawText());
                expected["layout"]!["embedded"]!["height"]=JsonNode.Parse(layout.GetProperty("embedded").GetProperty("height").GetRawText());
                expected["connections"]=JsonNode.Parse(current.GetRawText());
            }
        }
        using var document=JsonDocument.Parse(expected.ToJsonString());
        if(!Unchanged(document.RootElement,after))
        {
            var changed=new List<string>();FindDifferences(document.RootElement,after,"$",changed);
            throw new DomainException("NATIVE_MLEADER_MISMATCH","Native MLeader text replacement changed retained branches, arrows, style or content/layout controls.",false,new{changed_properties=changed});
        }
    }
    public static bool Unchanged(JsonElement a,JsonElement b)
    {
        if(a.ValueKind!=b.ValueKind)return false;
        return a.ValueKind switch
        {
            JsonValueKind.Object=>a.EnumerateObject().Count()==b.EnumerateObject().Count()&&a.EnumerateObject().All(p=>b.TryGetProperty(p.Name,out var other)&&Unchanged(p.Value,other)),
            JsonValueKind.Array=>a.GetArrayLength()==b.GetArrayLength()&&a.EnumerateArray().Zip(b.EnumerateArray()).All(p=>Unchanged(p.First,p.Second)),
            JsonValueKind.Number=>NumberUnchanged(a,b),
            _=>a.ToString()==b.ToString()
        };
    }
    private static bool NumberUnchanged(JsonElement a,JsonElement b)
    {
        // Branch/line indices and enum values must not inherit a relative
        // geometric tolerance. Absolute tolerance cannot grow with WCS offset.
        if(a.TryGetInt64(out var ai)&&b.TryGetInt64(out var bi))return ai==bi;
        return double.IsFinite(a.GetDouble())&&double.IsFinite(b.GetDouble())&&Math.Abs(a.GetDouble()-b.GetDouble())<=1e-7;
    }
    private static void FindDifferences(JsonElement a,JsonElement b,string path,List<string> result)
    {
        if(result.Count>=12||Unchanged(a,b))return;
        if(a.ValueKind==JsonValueKind.Object&&b.ValueKind==JsonValueKind.Object)
        {
            foreach(var property in a.EnumerateObject())
            {if(result.Count>=12)break;if(b.TryGetProperty(property.Name,out var value))FindDifferences(property.Value,value,path+"."+property.Name,result);else result.Add(path+"."+property.Name);}
            foreach(var property in b.EnumerateObject())if(result.Count<12&&!a.TryGetProperty(property.Name,out _))result.Add(path+"."+property.Name);
        }
        else if(a.ValueKind==JsonValueKind.Array&&b.ValueKind==JsonValueKind.Array&&a.GetArrayLength()==b.GetArrayLength())
        {for(int i=0;i<a.GetArrayLength()&&result.Count<12;i++)FindDifferences(a[i],b[i],path+"["+i+"]",result);}
        else result.Add(path);
    }
}
