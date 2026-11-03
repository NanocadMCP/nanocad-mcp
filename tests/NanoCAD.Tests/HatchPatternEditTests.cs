using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class HatchPatternEditTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        var bridge=Guid.NewGuid().ToString("D");var doc=Guid.NewGuid().ToString("D");var id=Guid.NewGuid().ToString("D");
        object Edit(double expectedAngle=0,double expectedScale=1,double angle=.2,double scale=2,string handle="AF")=>new{operation="set_hatch_pattern",handles=new[]{handle},expected_angle=expectedAngle,expected_scale=expectedScale,angle,scale};
        ModifyArguments Parse(params object[] edits)=>ModifyValidation.Parse(Wire.Element(new{bridge_instance_id=bridge,document_id=doc,operation_id=id,expected_revision="7",units="Millimeters",operations=edits}));
        void Check(bool ok){if(!ok)throw new Exception("Hatch pattern edit assertion failed.");}
        void Deny(params object[] edits){try{Parse(edits);}catch(DomainException){return;}throw new Exception("Invalid Hatch edit accepted.");}
        await test("hatch pattern edit preserves ordered expectations and every replay parameter",()=>{
            var request=Parse(Edit());var edit=(SetHatchPatternSpec)request.Operations.Single();Check(edit.Angle==.2&&edit.Scale==2&&edit.ExpectedAngle==0&&edit.ExpectedScale==1);
            Check(request.Fingerprint()==Parse(Edit(handle:"00af")).Fingerprint());
            foreach(var change in new[]{Edit(expectedAngle:.1),Edit(expectedScale:2),Edit(angle:.3),Edit(scale:3)})Check(request.Fingerprint()!=Parse(change).Fingerprint());
            Check(Parse(Edit(),Edit(.2,2,.3,3)).Fingerprint()!=Parse(Edit(.2,2,.3,3),Edit()).Fingerprint());return Task.CompletedTask;
        });
        await test("hatch pattern edit rejects ambiguous scope angle scale fields without an operation-count ceiling",()=>{
            Deny(Edit(angle:2*Math.PI));Deny(Edit(expectedAngle:-.1));Deny(Edit(scale:.009));Deny(Edit(expectedScale:101));
            Deny(new{operation="set_hatch_pattern",handles=new[]{"AF","B0"},expected_angle=0,expected_scale=1,angle=.1,scale=2});
            Deny(new{operation="set_hatch_pattern",handles=new[]{"AF"},expected_angle=0,expected_scale=1,angle=.1,scale=2,pattern_name="USER"});
            Deny();Check(Parse(Enumerable.Range(0,9).Select(_=>Edit()).ToArray()).Operations.Length==9);return Task.CompletedTask;
        });
        await test("hatch pattern compare and set preserves boundary origin and visual properties",()=>{
            object Data(double angle,double scale,string layer="27",double x=0)=>new{geometry=new{pattern_name="ANSI31",pattern_angle=angle,pattern_scale=scale,loops=new[]{new{vertices=new[]{new[]{0,0},new[]{10,0},new[]{10,10}},associated_handles=Array.Empty<string>()}}},origin=new[]{x,0},layer};
            var before=Wire.Element(Data(0,1));var edit=new SetHatchPatternSpec(new[]{"AF"},0,1,.2,2);var expected=HatchPatternEditGeometry.Expected(before,edit);
            HatchPatternEditGeometry.Verify(expected,Wire.Element(Data(.2,2)));
            foreach(var wrong in new[]{Wire.Element(Data(.3,2)),Wire.Element(Data(.2,3)),Wire.Element(Data(.2,2,"28")),Wire.Element(Data(.2,2,x:1))})
            {try{HatchPatternEditGeometry.Verify(expected,wrong);}catch(DomainException){continue;}throw new Exception("Collateral hatch change accepted.");}
            try{HatchPatternEditGeometry.Expected(before,edit with{ExpectedAngle=.1});}catch(DomainException e)when(e.Error.code=="HATCH_PATTERN_CHANGED"){return Task.CompletedTask;}
            throw new Exception("Stale pattern expectation accepted.");
        });
        await test("hatch pattern edit publishes legacy limits beside native association and focused guidance",async()=>{
            var tool=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_entities");var operations=tool.InputSchema.GetProperty("properties").GetProperty("operations");
            var variants=operations.GetProperty("items").GetProperty("oneOf").EnumerateArray().ToArray();
            string Name(JsonElement variant)=>variant.GetProperty("properties").GetProperty("operation").GetProperty("const").GetString()!;
            var variant=variants.Single(v=>Name(v)=="set_hatch_pattern");
            Check(ToolCatalog.Tools.Count==ToolCatalog.OperationNames.Length+1&&ToolCatalog.Tools.Any(t=>t.Name=="nanocad.get_guidance")&&
                tool.Annotations!.ReadOnlyHint==false&&!operations.TryGetProperty("maxItems",out _)&&
                variant.GetProperty("properties").GetProperty("handles").GetProperty("maxItems").GetInt32()==1&&variant.GetProperty("properties").GetProperty("scale").GetProperty("maximum").GetDouble()==100&&
                variants.Any(v=>Name(v)=="set_hatch_properties")&&variants.Any(v=>Name(v)=="set_hatch_association"));
            var result=await new ToolService().CallAsync("get_guidance",new Dictionary<string,JsonElement>{
                ["topic"]=Wire.Element("schema"),["operation"]=Wire.Element("modify_entities"),["variant"]=Wire.Element("set_hatch_pattern")},CancellationToken.None);
            using var focused=JsonDocument.Parse(result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Single().Text);
            var branch=focused.RootElement.GetProperty("properties").GetProperty("operations").GetProperty("items");
            Check(branch.GetProperty("description").GetString()!.Contains("set_hatch_properties")&&
                branch.GetProperty("properties").GetRawText()==variant.GetProperty("properties").GetRawText());
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!,"tools-list.json"),JsonSerializer.Serialize(ToolCatalog.Tools,Wire.Json));
        });
    }
}
