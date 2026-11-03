using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class MTextCreateTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        string bridge=Guid.NewGuid().ToString("D"),doc=Guid.NewGuid().ToString("D"),operation=Guid.NewGuid().ToString("D");
        object Entity(string[]? paragraphs=null,double height=3,double width=100,string style="Standard",double x=10)=>new{type="MText",paragraphs=paragraphs??new[]{"First","Second"},position=new[]{x,20.0},height,width,text_style=style};
        CreateArguments Parse(params object[] entities)=>CreateValidation.Parse(Wire.Element(new{bridge_instance_id=bridge,document_id=doc,operation_id=operation,expected_revision="12",units="Millimeters",entities}));
        void Reject(params object[] entities){try{Parse(entities);}catch(DomainException){return;}throw new Exception("Invalid MText creation accepted.");}
        await test("MText encoding reverses literal punctuation Unicode whitespace and paragraph boundaries",()=>{
            var cases=new[]{new[]{"First","",@"C:\Temp {A} ^2","  end  "},new[]{@"\P literal ^ \\ {{} }",@"x^J is literal"},new[]{"% sign","  leading", "trailing  "},new[]{"a","","b"}};
            foreach(var lines in cases)
                if(MTextContent.PlainText(PlainMTextEncoding.Encode(lines))!=string.Join("\n",lines))throw new Exception("Codec lost literal content.");
            if(PlainMTextEncoding.Encode(new[]{@"\P"})!=@"\\P" || PlainMTextEncoding.Encode(new[]{"a","b"})!=@"a\Pb")throw new Exception("Escaped literal and paragraph code confused.");
            return Task.CompletedTask;
        });
        await test("MText creation fingerprints paragraph order placement width height and style",()=>{
            var args=Parse(Entity());var spec=(MTextSpec)args.Entities.Single();
            if(spec.Paragraphs[0]!="First"||spec.Width!=100||spec.Height!=3||spec.Position!=new Point2(10,20)||spec.TextStyle!="Standard")throw new Exception("MText intent lost.");
            foreach(var changed in new[]{Entity(new[]{"Second","First"}),Entity(width:101),Entity(height:4),Entity(style:"Other"),Entity(x:11)})
                if(args.Fingerprint()==Parse(changed).Fingerprint())throw new Exception("MText parameter missing from replay identity.");
            return Task.CompletedTask;
        });
        await test("MText creation rejects malformed paragraphs raw formatting controls and excessive work",()=>{
            Reject(Entity(Array.Empty<string>()));Reject(Entity(new[]{"","  "}));Reject(Entity(new[]{"a\nb"}));Reject(Entity(new[]{"a\tb"}));Reject(Entity(new[]{"%<field>%"}));Reject(Entity(new[]{"%%c10"}));
            Reject(Entity(new[]{new string('x',2049)}));Parse(Entity(Enumerable.Repeat("x",33).ToArray()));Reject(Entity(new[]{new string('x',2048),new string('y',2048)}));
            Reject(Entity(width:0));Reject(Entity(width:1000001));Reject(Entity(height:0.09));Reject(Entity(height:10001));Reject(Entity(style:""));
            Reject(new{type="MText",paragraphs=new[]{"a"},position=new[]{0,0},height=3,width=100});
            Reject(new{type="MText",paragraphs=new[]{"a"},position=new[]{0,0},height=3,width=100,text_style="Standard",contents=@"\P"});
            Parse(Enumerable.Range(0,17).Select(_=>Entity()).ToArray());
            var maximum=Parse(Enumerable.Range(0,16).Select(_=>Entity(new[]{new string('x',2048),new string('y',2047)})).ToArray());
            if(maximum.Entities.Length!=16)throw new Exception("Valid total bound refused.");return Task.CompletedTask;
        });
        await test("MText creation stays outside block definitions and publishes bounded schema with16 tools",()=>{
            bool refused=false;try{CreateValidation.ParseEntities(Wire.Element(new[]{Entity()}));}catch(DomainException){refused=true;}
            var blocks=BlockWriteSchema.Define().GetProperty("properties").GetProperty("entities").GetProperty("items").GetProperty("oneOf");var tool=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.create_entities");
            var variant=tool.InputSchema.GetProperty("properties").GetProperty("entities").GetProperty("items").GetProperty("oneOf").EnumerateArray().Single(v=>v.GetProperty("properties").GetProperty("type").GetProperty("const").GetString()=="MText");
            var props=variant.GetProperty("properties");
            if(!refused||blocks.EnumerateArray().Any(v=>v.GetProperty("properties").GetProperty("type").GetProperty("const").GetString()=="MText")||ToolCatalog.Tools.Count!=ToolCatalog.OperationNames.Length||props.GetProperty("paragraphs").TryGetProperty("maxItems",out _)||props.GetProperty("width").GetProperty("minimum").GetInt32()!=1||tool.Annotations?.ReadOnlyHint!=false)throw new Exception("MText capability boundary changed.");
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!,"tools-list.json"),JsonSerializer.Serialize(ToolCatalog.Tools,Wire.Json));return Task.CompletedTask;
        });
    }
}

