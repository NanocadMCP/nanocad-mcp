using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class ModelEdgeFeatureTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        var bridge=Guid.NewGuid().ToString("D");var doc=Guid.NewGuid().ToString("D");var operation=Guid.NewGuid().ToString("D");
        var last="{"+Guid.NewGuid().ToString("D").ToUpperInvariant()+"}";
        var token=new string('A',64);
        Dictionary<string,object> Feature(string kind,string marker="21")=>new()
        {
            ["operation"]=kind,["handles"]=new[]{"AB"},["expected_last_item_id"]=last,["topology_token"]=token,
            ["edges"]=new[]{marker},[kind=="add_model_fillet"?"radius":"distance"]=1.5
        };
        ModifyArguments Parse(params object[] edits)=>ModifyValidation.Parse(Wire.Element(new
            {bridge_instance_id=bridge,document_id=doc,operation_id=operation,expected_revision="7",units="Millimeters",operations=edits}));
        await test("native model edge feature contract separates markers from direct solid topology",()=>
        {
            foreach(var kind in new[]{"add_model_fillet","add_model_chamfer"})
            {
                var parsed=Parse(Feature(kind));
                if(parsed.Operations.Single() is not ModelEdgeFeatureSpec spec||spec.EdgeIds.Single()!="21"||spec.Size!=1.5||
                    !WriteRecovery.NotGuaranteed(parsed))throw new Exception("Native edge feature intent or uncertain recovery lost.");
                var changed=Feature(kind,"22");
                if(Parse(changed).Fingerprint()==parsed.Fingerprint())throw new Exception("Native edge identity omitted from operation ledger fingerprint.");
            }
            var read=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.inspect_solid_topology");
            if(read.Annotations?.ReadOnlyHint!=true||!read.OutputSchema!.Value.GetRawText().Contains("snapshot_token"))throw new Exception("Safe B-rep topology read not exposed.");
            var schema=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_entities").InputSchema.GetRawText();
            if(!schema.Contains("add_model_fillet")||!schema.Contains("add_model_chamfer")||!schema.Contains("topology_token"))
                throw new Exception("Native edge write not exposed.");
            return Task.CompletedTask;
        });
        await test("native model edge feature rejects duplicate or stale selectors and mixed batches",()=>
        {
            void Reject(params object[] edits)
            {try{Parse(edits);throw new Exception("Unsafe edge edit accepted.");}catch(DomainException){}}
            var repeated=Feature("add_model_fillet");repeated["edges"]=new[]{"21","21"};Reject(repeated);
            var stale=Feature("add_model_chamfer");stale["topology_token"]="old";Reject(stale);
            var zero=Feature("add_model_fillet");zero["radius"]=0;Reject(zero);
            Reject(Feature("add_model_fillet"),Feature("add_model_chamfer"));
            return Task.CompletedTask;
        });
    }
}
