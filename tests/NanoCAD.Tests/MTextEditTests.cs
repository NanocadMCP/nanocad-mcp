using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class MTextEditTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        string bridge=Guid.NewGuid().ToString("D"),doc=Guid.NewGuid().ToString("D"),id=Guid.NewGuid().ToString("D");
        object Edit(string[]? expected=null,string[]? paragraphs=null,string[]? handles=null)=>new{operation="replace_mtext",handles=handles??new[]{"00Af"},expected_paragraphs=expected??new[]{"Old"},paragraphs=paragraphs??new[]{"New","",@"C:\Temp {A} ^2"}};
        ModifyArguments Parse(params object[] edits)=>ModifyValidation.Parse(Wire.Element(new{bridge_instance_id=bridge,document_id=doc,expected_revision="19",operation_id=id,units="Millimeters",operations=edits}));
        void Reject(params object[] edits){try{Parse(edits);}catch(DomainException){return;}throw new Exception("Unsafe MText replacement accepted.");}
        await test("MText replacement fingerprints expected paragraphs replacement order and normalized identity",()=>{
            var args=Parse(Edit());var spec=(ReplaceMTextSpec)args.Operations.Single();
            if(spec.Handles.Single()!="AF"||spec.Paragraphs[2]!=@"C:\Temp {A} ^2"||spec.ExpectedParagraphs.Single()!="Old")throw new Exception("MText intent changed.");
            if(args.Fingerprint()!=Parse(Edit(handles:new[]{"AF"})).Fingerprint()||args.Fingerprint()==Parse(Edit(expected:new[]{"Other"})).Fingerprint()||args.Fingerprint()==Parse(Edit(paragraphs:new[]{"Other"})).Fingerprint())throw new Exception("Replay intent incomplete.");
            var a=Edit(paragraphs:new[]{"Next"});var b=Edit(expected:new[]{"Next"},paragraphs:new[]{"Final"});
            if(Parse(a,b).Fingerprint()==Parse(b,a).Fingerprint())throw new Exception("Ordered replacements collapsed.");return Task.CompletedTask;
        });
        await test("MText replacement bounds literal paragraphs controls total work and erase ordering",()=>{
            Reject(Edit(handles:new[]{"A","B"}));Reject(Edit(paragraphs:Array.Empty<string>()));Reject(Edit(paragraphs:new[]{""," "}));
            foreach(string bad in new[]{"a\nb","a\tb","%<field>%","%%c10",new string('x',2049)})Reject(Edit(paragraphs:new[]{bad}));
            Reject(Edit(expected:new[]{"%<field>%"}));Parse(Edit(paragraphs:Enumerable.Repeat("x",33).ToArray()));
            Reject(Edit(paragraphs:new[]{new string('x',2048),new string('y',2048)}));
            Parse(Enumerable.Range(0,17).Select(_=>Edit()).ToArray());
            Reject(Enumerable.Range(0,9).Select(_=>Edit(expected:new[]{new string('x',2048),new string('x',2047)},paragraphs:new[]{new string('y',2048),new string('y',2047)})).ToArray());
            Reject(new{operation="erase",handles=new[]{"AF"}},Edit());
            Reject(new{operation="replace_mtext",handles=new[]{"AF"},paragraphs=new[]{"New"}});
            if(Parse(Edit(expected:new[]{""})).Operations.Length!=1)throw new Exception("Empty existing literal refused.");return Task.CompletedTask;
        });
        await test("MText replacement canonical guard rejects formatting without losing literal escapes",()=>{
            foreach(var paragraphs in new[]{new[]{""," a ",@"C:\Temp {B} ^ "},new[]{"",""},new[]{@"\P is literal"}})
            {var encoded=MTextEditContent.Encode(paragraphs,false);if(!MTextEditContent.IsCanonical(encoded)||MTextContent.PlainText(encoded)!=string.Join("\n",paragraphs))throw new Exception("Canonical literal changed.");}
            foreach(string raw in new[]{@"\Lunderlined\l",@"{plain}",@"\H2x;tall",@"\C1;red",@"\S1/2;",@"%<field>%",@"\U+0041",@"bad\"})
                if(MTextEditContent.IsCanonical(raw))throw new Exception("Rich or unknown content accepted for destructive replacement.");return Task.CompletedTask;
        });
        await test("MText replacement schema retains existing tool count and explicit bounded paragraphs",()=>{
            var tool=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_entities");
            var variant=tool.InputSchema.GetProperty("properties").GetProperty("operations").GetProperty("items").GetProperty("oneOf").EnumerateArray().Single(v=>v.GetProperty("properties").GetProperty("operation").GetProperty("const").GetString()=="replace_mtext");
            var p=variant.GetProperty("properties");
            if(ToolCatalog.Tools.Count!=ToolCatalog.OperationNames.Length||tool.Annotations?.ReadOnlyHint!=false||p.GetProperty("handles").GetProperty("maxItems").GetInt32()!=1||p.GetProperty("paragraphs").TryGetProperty("maxItems",out _)||p.GetProperty("expected_paragraphs").GetProperty("items").GetProperty("maxLength").GetInt32()!=2048)throw new Exception("MText edit scope changed.");
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!,"tools-list.json"),JsonSerializer.Serialize(ToolCatalog.Tools,Wire.Json));return Task.CompletedTask;
        });
    }
}
