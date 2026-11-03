using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class SolidSliceTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        await test("separate solid accepts disconnected-volume operation without a cutting plane",()=>
        {
            var edit=new Dictionary<string,object>{["operation"]="separate_solid",["handles"]=Enumerable.Range(1,300).Select(i=>i.ToString("X")).ToArray()};
            var request=new Dictionary<string,object>{["bridge_instance_id"]=Guid.NewGuid().ToString(),["document_id"]=Guid.NewGuid().ToString(),["expected_revision"]="1",["operation_id"]=Guid.NewGuid().ToString(),["units"]="Millimeters",["operations"]=new[]{edit}};
            var parsed=ModifyValidation.Parse(Wire.Element(request));
            if(parsed.Operations.Single() is not SeparateSolidSpec {Handles.Length:300}||!WriteRecovery.NotGuaranteed(parsed))throw new Exception("Separate contract missing.");
            edit["plane_origin"]=new[]{0,0,0};
            try{ModifyValidation.Parse(Wire.Element(request));throw new Exception("Cutting parameter accepted for separation.");}catch(DomainException){}
            return Task.CompletedTask;
        });
        await test("solid slice plane keeps both sides with canonical normal and explicit recovery",()=>
        {
            var edit=new Dictionary<string,object>{["operation"]="slice_solid",["handles"]=new[]{"AB"},["plane_origin"]=new[]{0,25,0},["plane_normal"]=new[]{0,10,0}};
            var request=new Dictionary<string,object>{["bridge_instance_id"]=Guid.NewGuid().ToString(),["document_id"]=Guid.NewGuid().ToString(),["expected_revision"]="7",["operation_id"]=Guid.NewGuid().ToString(),["units"]="Millimeters",["operations"]=new[]{edit}};
            ModifyArguments Parse()=>ModifyValidation.Parse(Wire.Element(request));
            var first=Parse();if(((SliceSolidSpec)first.Operations.Single()).PlaneNormal!=new Point3(0,1,0)||!WriteRecovery.NotGuaranteed(first))throw new Exception("Slice contract lost direction or recovery policy.");
            edit["plane_normal"]=new[]{0,1,0};if(Parse().Fingerprint()!=first.Fingerprint())throw new Exception("Equivalent normals changed write identity.");
            edit["handles"]=Enumerable.Range(1,300).Select(i=>i.ToString("X")).ToArray();if(Parse().Operations[0].Handles.Length!=300)throw new Exception("Artificial source ceiling.");
            edit["plane_normal"]=new[]{0,0,0};try{Parse();throw new Exception("Zero normal accepted.");}catch(DomainException){}
            var tool=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_entities");
            if(!tool.InputSchema.GetRawText().Contains("slice_solid")||!tool.OutputSchema!.Value.GetRawText().Contains("solid_slice_results"))throw new Exception("Missing public slice contract.");
            return Task.CompletedTask;
        });
    }
}
