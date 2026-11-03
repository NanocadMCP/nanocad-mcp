using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class ModelRebuild419Tests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        string last=Guid.NewGuid().ToString("B").ToUpperInvariant();
        Dictionary<string,object> Edit()=>new(){["operation"]="rebuild_model",["handles"]=new[]{"AB"},["expected_last_item_id"]=last};
        ModifyArguments Parse(params object[] operations)=>ModifyValidation.Parse(Wire.Element(new{
            bridge_instance_id=Guid.NewGuid().ToString(),document_id=Guid.NewGuid().ToString(),operation_id=Guid.NewGuid().ToString(),
            expected_revision="11",units="Millimeters",operations}));
        void Reject(params object[] operations){try{Parse(operations);throw new Exception("Ambiguous rebuild request accepted.");}catch(DomainException){}}
        await test("model rebuild419 exposes scoped recovery without a UI selection",()=>
        {
            var request=Parse(Edit());
            if(request.Operations.Single() is not ModelRebuildSpec419 recovery||recovery.ExpectedLastItemId!=last||!WriteRecovery.NotGuaranteed(request))
                throw new Exception("Rebuild identity or recovery contract missing.");
            var tools=ToolCatalog.Tools.Single(tool=>tool.Name=="nanocad.modify_entities");
            if(!tools.InputSchema.GetRawText().Contains("rebuild_model")||!tools.OutputSchema!.Value.GetRawText().Contains("unresolved_nodes_complete"))
                throw new Exception("Scoped rebuild input/output not published.");
            return Task.CompletedTask;
        });
        await test("model rebuild419 rejects missing identity, implied cascade and mixed writes",()=>
        {
            var invalid=Edit();invalid["expected_last_item_id"]="";Reject(invalid);
            var cascade=Edit();cascade["unsuppress_all"]=true;Reject(cascade);
            var multiple=Edit();multiple["handles"]=new[]{"AB","AC"};Reject(multiple);
            Reject(Edit(),new{operation="erase",handles=new[]{"AB"}});
            return Task.CompletedTask;
        });
    }
}
