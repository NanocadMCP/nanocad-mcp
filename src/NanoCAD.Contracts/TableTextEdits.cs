using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.Contracts;

public sealed record TableTextCellEdit(int Row,int Column,string ExpectedText,string Text);
public sealed record ReplaceTableTextSpec(string[] Targets,TableTextCellEdit[] Cells):EditSpec("replace_table_text",Targets);
public static partial class ModifyValidation
{
    private static ReplaceTableTextSpec ParseTableText(JsonElement item,string path,string[] handles)
    {
        Shape(item,path,"operation","handles","cells");
        if(handles.Length!=1)throw Invalid(path+".handles","Exactly one native Table handle required.");
        var cells=Required(item,"cells",path);
        if(cells.ValueKind!=JsonValueKind.Array || cells.GetArrayLength()<1)throw Invalid(path+".cells","Expected at least one literal cell edit.");
        var result=new List<TableTextCellEdit>();var seen=new HashSet<(int,int)>();
        foreach(var cell in cells.EnumerateArray())
        {
            var label=path+".cells["+result.Count+"]";Shape(cell,label,"row","column","expected_text","text");
            int Index(string name,int max){var value=Required(cell,name,label);if(value.ValueKind!=JsonValueKind.Number || !value.TryGetInt32(out int n) || n<0 || n>max)throw Invalid(label+"."+name,"Index outside supported bounds.");return n;}
            string Literal(string name){var value=Required(cell,name,label);if(value.ValueKind!=JsonValueKind.String)throw Invalid(label+"."+name,"Literal string required.");var text=value.GetString()!;if(text.Length>4096 || text.Any(char.IsControl) || text.IndexOfAny(new[]{'\\','{','}'})>=0 || text.Contains("%<") || text.Contains("%%"))throw Invalid(label+"."+name,"Unsupported literal or formatting/field code.");return text;}
            int row=Index("row",int.MaxValue),column=Index("column",int.MaxValue);
            if(!seen.Add((row,column)))throw Invalid(label,"Duplicate cell within one operation; use explicit ordered operations.");
            result.Add(new(row,column,Literal("expected_text"),Literal("text")));
        }
        return new(handles,result.ToArray());
    }
}

public static class TableTextVerification
{
    public static void Verify(JsonElement before,JsonElement after,TableTextCellEdit[] edits)
    {
        var expected=JsonNode.Parse(before.GetRawText())!;
        int columns=before.GetProperty("columns").GetInt32();
        foreach(var edit in edits)expected["cells"]![edit.Row*columns+edit.Column]!["text"]=edit.Text;
        // A literal replacement can wrap/reflow in its existing column. Keep
        // column widths, placement, style and every unrequested cell field;
        // observe only the native layout sizes which can follow new text.
        foreach(string field in new[]{"width","height"})
        {
            if(!after.TryGetProperty(field,out var size)||size.ValueKind!=JsonValueKind.Number||!size.TryGetDouble(out double value)||!double.IsFinite(value)||value<=0)
                throw new DomainException("NATIVE_TABLE_MISMATCH","Native text layout has an unavailable or invalid table size.");
            expected[field]=JsonNode.Parse(size.GetRawText());
        }
        var heights=after.GetProperty("row_heights");
        if(heights.ValueKind!=JsonValueKind.Array||heights.GetArrayLength()!=before.GetProperty("rows").GetInt32()||
           heights.EnumerateArray().Any(size=>size.ValueKind!=JsonValueKind.Number||!size.TryGetDouble(out double value)||!double.IsFinite(value)||value<=0))
            throw new DomainException("NATIVE_TABLE_MISMATCH","Native text layout has unavailable or invalid row heights.");
        expected["row_heights"]=JsonNode.Parse(heights.GetRawText());
        using var document=JsonDocument.Parse(expected.ToJsonString());
        if(!Equivalent(document.RootElement,after))throw new DomainException("NATIVE_TABLE_MISMATCH","Table content, column widths, formatting or placement differs from the requested literal replacement.");
    }
    public static bool Reflowed(JsonElement before,JsonElement after)=>
        new[]{"width","height","row_heights"}.Any(field=>!Equivalent(before.GetProperty(field),after.GetProperty(field)));
    internal static bool Equivalent(JsonElement a,JsonElement b)
    {
        if(a.ValueKind!=b.ValueKind)return false;
        return a.ValueKind switch
        {
            JsonValueKind.Object=>a.EnumerateObject().Count()==b.EnumerateObject().Count() && a.EnumerateObject().All(p=>b.TryGetProperty(p.Name,out var other)&&Equivalent(p.Value,other)),
            JsonValueKind.Array=>a.GetArrayLength()==b.GetArrayLength() && a.EnumerateArray().Zip(b.EnumerateArray()).All(p=>Equivalent(p.First,p.Second)),
            JsonValueKind.Number=>double.IsFinite(a.GetDouble()) && double.IsFinite(b.GetDouble()) && Math.Abs(a.GetDouble()-b.GetDouble())<=Math.Max(1e-7,1e-8*Math.Max(Math.Abs(a.GetDouble()),Math.Abs(b.GetDouble()))),
            _=>a.ToString()==b.ToString()
        };
    }
}
