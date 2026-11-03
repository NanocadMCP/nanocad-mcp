using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class ModelFeatureSuppressionTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        var last="{"+Guid.NewGuid().ToString("D").ToUpperInvariant()+"}";
        var feature="{"+Guid.NewGuid().ToString("D").ToUpperInvariant()+"}";
        Dictionary<string,object> Request(bool expected=false,bool desired=true)=>new()
        {
            ["operation"]="set_model_feature_suppressed",["handles"]=new[]{"AB"},
            ["expected_last_item_id"]=last,["feature_id"]=feature,
            ["expected_suppressed"]=expected,["suppressed"]=desired
        };
        ModifyArguments Parse(params object[] edits)=>ModifyValidation.Parse(Wire.Element(new
        {
            bridge_instance_id=Guid.NewGuid().ToString("D"),document_id=Guid.NewGuid().ToString("D"),
            operation_id=Guid.NewGuid().ToString("D"),expected_revision="11",units="Millimeters",operations=edits
        }));
        void Reject(params object[] edits)
        {try{Parse(edits);throw new Exception("Unsafe feature-state request accepted.");}catch(DomainException){}}
        await test("native model feature suppression uses exact history identity and honest recovery",()=>
        {
            var args=Parse(Request());
            if(args.Operations.Single() is not ModelFeatureSuppressionSpec edit||edit.FeatureId!=feature||
               edit.ExpectedLastItemId!=last||!edit.Suppressed||!WriteRecovery.NotGuaranteed(args))
                throw new Exception("Feature-state contract lost.");
            var schema=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_entities").InputSchema.GetRawText();
            if(!schema.Contains("set_model_feature_suppressed")||!schema.Contains("expected_suppressed"))
                throw new Exception("Feature-state operation not exposed.");
            return Task.CompletedTask;
        });
        await test("native model feature suppression rejects no-op or ambiguous state request",()=>
        {
            Reject(Request(false,false));
            var bad=Request();bad["feature_id"]="bad";Reject(bad);
            var extra=Request();extra["erase"]=true;Reject(extra);
            Reject(Request(),Request(true,false));
            var restore=Parse(Request(true,false));
            if(restore.Operations.Single() is not ModelFeatureSuppressionSpec edit||edit.Suppressed)
                throw new Exception("Restore state lost.");
            return Task.CompletedTask;
        });
    }
}
