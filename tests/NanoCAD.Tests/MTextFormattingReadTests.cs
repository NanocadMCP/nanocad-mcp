using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class MTextFormattingReadTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        void Check(bool ok,string why){if(!ok)throw new Exception(why);}
        await test("MText formatting schema resolves references and shares stored color semantics",()=>{
            Check(ToolCatalog.Tools.Where(tool=>!tool.OutputSchema.HasValue).Select(tool=>tool.Name).SequenceEqual(new[]{"nanocad.get_guidance"}),
                "Only the text-only guidance tool omits a structured output schema.");
            foreach(var tool in ToolCatalog.Tools.Where(tool=>tool.OutputSchema.HasValue))
            {
                var schema=tool.OutputSchema!.Value;
                void Refs(JsonElement node)
                {
                    if(node.ValueKind==JsonValueKind.Array){foreach(var child in node.EnumerateArray())Refs(child);}
                    if(node.ValueKind!=JsonValueKind.Object)return;
                    foreach(var property in node.EnumerateObject())
                    {
                        if(property.Name=="$ref")
                        {
                            string path=property.Value.GetString()!;
                            Check(path.StartsWith("#/$defs/",StringComparison.Ordinal),"Only local output references expected");
                            Check(schema.GetProperty("$defs").TryGetProperty(path[8..],out _),"Unresolved output reference "+path);
                        }
                        else Refs(property.Value);
                    }
                }
                Refs(schema);
            }
            var read=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.inspect_objects");
            var defs=read.OutputSchema!.Value.GetProperty("$defs");
            var format=defs.GetProperty("mtext_formatting").GetProperty("properties");
            Check(format.GetProperty("text_style_ref").GetProperty("$ref").GetString()=="#/$defs/ref","Style identity shares document and bridge scope");
            Check(format.GetProperty("background").GetProperty("properties").GetProperty("color").GetRawText()==defs.GetProperty("properties").GetProperty("properties").GetProperty("color").GetRawText(),"Background color must use existing stored color conventions");
            Check(read.Annotations!.ReadOnlyHint==true&&ToolCatalog.Tools.Select(t=>t.Name).Order().SequenceEqual(
                ToolCatalog.OperationNames.Append("get_guidance").Select(op=>"nanocad."+op).Order()),"Exact approved tool set and read-only annotation remain");
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!,"tools-list.json"),JsonSerializer.Serialize(ToolCatalog.Tools,Wire.Json));
            return Task.CompletedTask;
        });
        await test("MText formatting keeps drawing background choice separate from stored color",()=>{
            var reference=new EntityRef("11111111-1111-1111-1111-111111111111","22222222-2222-2222-2222-222222222222","29");
            object[] colors={new{method="aci",aci=(int?)3,rgb=(int[]?)null},new{method="true_color",aci=(int?)null,rgb=new[]{10,120,230}},new{method="by_layer",aci=(int?)null,rgb=(int[]?)null}};
            foreach(var color in colors)
            {
                var format=new MTextFormatting(reference,"Exactly",1.5,"LeftToRight","NoColumns",new(true,true,1.25,color));
                var value=Wire.Element(format);var background=value.GetProperty("background");
                Check(background.GetProperty("use_drawing_background").GetBoolean(),"Drawing background choice retained even when stored color exists");
                Check(background.GetProperty("color").GetRawText()==Wire.Element(color).GetRawText(),"Stored color must not resolve to an invented window RGB");
                Check(value.GetProperty("text_style_ref").GetProperty("document_id").GetString()==reference.document_id,"Style retains document identity");
                Check(value.GetProperty("line_spacing_factor").GetDouble()==1.5,"Spacing factor must not be converted to a height");
            }
            return Task.CompletedTask;
        });
    }
}
