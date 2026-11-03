using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class SketchConstraintsTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        var bridge=Guid.NewGuid().ToString("D");var document=Guid.NewGuid().ToString("D");var sketch=Guid.NewGuid().ToString("B");
        Dictionary<string,object?> Input()=>new(){["bridge_instance_id"]=bridge,["document_id"]=document,["expected_revision"]="7",["handle"]="00150",["sketch_id"]=sketch};
        await test("sketch constraints require current body sketch and explicit context",()=>
        {
            var args=SketchConstraintsValidation.Parse(Wire.Element(Input()));
            if(args.ModelObjectId!=sketch.ToUpperInvariant() || args.Handles![0]!="150" || args.BridgeId!=bridge || args.DocumentId!=document)throw new Exception("Identity normalization failed.");
            foreach(var name in new[]{"bridge_instance_id","document_id","expected_revision","handle","sketch_id"})
            {
                var input=Input();input.Remove(name);
                try{SketchConstraintsValidation.Parse(Wire.Element(input));}catch(DomainException){continue;}throw new Exception("Missing context accepted: "+name);
            }
            foreach(var extra in new[]{"evaluate","network","apply_constraints"})
            {
                var input=Input();input[extra]=true;
                try{SketchConstraintsValidation.Parse(Wire.Element(input));}catch(DomainException){continue;}throw new Exception("Unsupported operation accepted.");
            }
            return Task.CompletedTask;
        });
        await test("sketch constraints actual readonly scope is published without solver mutation",()=>
        {
            var tool=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.inspect_sketch_constraints");
            if(!Wire.ReadOperations.Contains("inspect_sketch_constraints")||tool.Annotations is not{ReadOnlyHint:true,DestructiveHint:false})throw new Exception("Readonly relation scope is not published safely.");
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!,"tools-list.json"),JsonSerializer.Serialize(ToolCatalog.Tools,Wire.Json));
            return Task.CompletedTask;
        });
    }
}
