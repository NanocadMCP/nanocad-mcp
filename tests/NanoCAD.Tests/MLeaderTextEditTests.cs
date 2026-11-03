using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class MLeaderTextEditTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        await test("MLeader literal replacement publishes the dedicated bounded write route",()=>{
            var input=Wire.Element(new{bridge_instance_id=Guid.NewGuid().ToString("D"),document_id=Guid.NewGuid().ToString("D"),operation_id=Guid.NewGuid().ToString("D"),expected_revision="0",units="Millimeters",operations=new[]{new{operation="replace_mleader_text",handles=new[]{"AF"},expected_text="Old",text="New"}}});
            var parsed=ModifyValidation.Parse(input);
            var tool=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_entities");
            if(parsed.Operations.Single() is not ReplaceMLeaderTextSpec)throw new Exception("MLeader typed parser missing.");
            if(!tool.InputSchema.GetRawText().Contains("replace_mleader_text"))throw new Exception("MLeader schema missing.");
            // Detailed operation instructions are delivered by get_guidance;
            // the compact catalog intentionally contains only the tool purpose.
            if(string.IsNullOrWhiteSpace(tool.Description))throw new Exception("Compact write purpose missing.");
            if(tool.Annotations!.ReadOnlyHint!=false)throw new Exception("MLeader write annotation missing.");
            if(ToolCatalog.Tools.Count!=ToolCatalog.OperationNames.Length+1 || !ToolCatalog.Tools.Any(t=>t.Name=="nanocad.get_guidance"))throw new Exception("Domain operations and focused guidance catalog disagree.");
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!,"tools-list.json"),JsonSerializer.Serialize(ToolCatalog.Tools,Wire.Json));return Task.CompletedTask;
        });
        await test("MLeader public parser protects identity expected content and bounded batches",()=>{
            string bridge=Guid.NewGuid().ToString("D"),doc=Guid.NewGuid().ToString("D"),op=Guid.NewGuid().ToString("D");
            object Item(string handle="AF",string old="Old",string text="New")=>new{operation="replace_mleader_text",handles=new[]{handle},expected_text=old,text};
            ModifyArguments Parse(params object[] edits)=>ModifyValidation.Parse(Wire.Element(new{bridge_instance_id=bridge,document_id=doc,operation_id=op,expected_revision="1",units="Millimeters",operations=edits}));
            string fingerprint=Parse(Item()).Fingerprint();
            foreach(var changed in new[]{Item("B0"),Item(old:"Other"),Item(text:"Other")})if(Parse(changed).Fingerprint()==fingerprint)throw new Exception("MLeader replay fingerprint loses intent.");
            foreach(var invalid in new[]{new[]{Item(text:"")},new[]{Item(text:@"\P")}})
            {try{Parse(invalid);}catch(DomainException){continue;}throw new Exception("Unsafe MLeader batch accepted.");}
            return Task.CompletedTask;
        });
        await test("MLeader prototype literal guard preserves formatting boundary",()=>{
            if(!MLeaderTextLiteral.Supported("Leader №7",1,4096)||!MLeaderTextLiteral.Supported("",0,16384))throw new Exception("Valid prototype literal refused.");
            foreach(var value in new[]{"a\nb",@"\P",@"\fArial;Text","{Text}","%%d","%<field>%",new string('x',4097)})if(MLeaderTextLiteral.Supported(value,1,4096))throw new Exception("Unsupported literal accepted.");
            return Task.CompletedTask;
        });
        await test("MLeader prototype verifier preserves exact indices and absolute placement tolerance",()=>{
            var before=Wire.Element(new{text="Old",location=new[]{10.0,20.0,0.0},style="AF",vertices=new[]{new[]{0.0,0.0,0.0},new[]{5.0,5.0,0.0}}});
            var next=JsonNode.Parse(before.GetRawText())!;next["text"]="New";MLeaderTextVerification.Verify(before,Wire.Element(next),"New");
            next["location"]![1]=21;
            try{MLeaderTextVerification.Verify(before,Wire.Element(next),"New");throw new Exception("Moved source accepted.");}
            catch(DomainException e)when(e.Error.code=="NATIVE_MLEADER_MISMATCH"){
                if(!Wire.Element(e.Error.details ?? throw new Exception("Missing details")).GetRawText().Contains("$.location[1]"))throw new Exception("Changed property not identified.");
            }
            if(MLeaderTextVerification.Unchanged(Wire.Element(new{branch=1000000000}),Wire.Element(new{branch=1000000001})) || MLeaderTextVerification.Unchanged(Wire.Element(new{coordinate=1000000000.5}),Wire.Element(new{coordinate=1000000001.5})))throw new Exception("Tolerance hid source change.");
            if(!MLeaderTextVerification.Unchanged(Wire.Element(new{coordinate=1.1}),Wire.Element(new{coordinate=1.100000001})))throw new Exception("Native rounding rejected.");
            return Task.CompletedTask;
        });
        await test("MLeader reflow requires unchanged native connection vertices and consistent text location",()=>{
            var before=Wire.Element(new{text="Old",connection=new[]{10.0,20.0,0.0},landing_connection=new[]{10.0,20.0,0.0},location=new[]{10.0,21.5,0.0},text_metadata=new{location=new[]{10.0,21.5,0.0},height=3},style="AF",vertices=new[]{new[]{0.0,0.0,0.0},new[]{5.0,5.0,0.0}}});
            var next=JsonNode.Parse(before.GetRawText())!;next["text"]="New";next["location"]![1]=22;next["text_metadata"]!["location"]![1]=22;
            MLeaderTextVerification.Verify(before,Wire.Element(next),"New");
            var cached=JsonNode.Parse(before.GetRawText())!;cached["connection"]![0]=8;MLeaderTextVerification.Verify(Wire.Element(cached),Wire.Element(next),"New");
            void Reject(JsonNode changed){try{MLeaderTextVerification.Verify(before,Wire.Element(changed),"New");}catch(DomainException e)when(e.Error.code=="NATIVE_MLEADER_MISMATCH"){return;}throw new Exception("Changed source accepted.");}
            var bad=next.DeepClone();bad["connection"]![1]=20.5;Reject(bad);
            bad=next.DeepClone();bad["vertices"]![1]![1]=4.5;Reject(bad);
            bad=next.DeepClone();bad["text_metadata"]!["location"]![1]=23;Reject(bad);
            bad=next.DeepClone();bad["style"]="B0";Reject(bad);
            bad=next.DeepClone();bad.AsObject().Remove("connection");Reject(bad);
            return Task.CompletedTask;
        });
    }
}
