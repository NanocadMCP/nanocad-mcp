using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class ModelFeatureTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        string id=Guid.NewGuid().ToString("D"),last=Guid.NewGuid().ToString("B");
        object Definition()=>new{plane=new{origin=new[]{0d,0,10},x_axis=new[]{1d,0,0},y_axis=new[]{0d,1,0}},profile=new{kind="circle",center=new[]{0d,0},radius=2},distance=5,direction="negative"};
        object Edit(string operation="cut",string? expected=null,object? definition=null)=>new{operation="add_model_extrusion",handles=new[]{"AB"},expected_last_item_id=expected??last,part_operation=operation,definition=definition??Definition()};
        ModifyArguments Parse(params object[] operations)=>ModifyValidation.Parse(Wire.Element(new{bridge_instance_id=id,document_id=id,expected_revision="1",operation_id=id,units="Millimeters",operations}));
        void Need(bool valid){if(!valid)throw new Exception("Native history append contract differs.");}
        void Reject(params object[] operations){try{Parse(operations);}catch(DomainException){return;}throw new Exception("Ambiguous history append accepted.");}
        await test("model feature append retains history precondition geometry and recovery intent",()=>{
            var args=Parse(Edit());var edit=(ModelFeatureExtrusionSpec)args.Operations.Single();
            Need(edit.PartOperation=="cut"&&edit.ExpectedLastItemId==last.ToUpperInvariant()&&edit.Definition.Direction=="negative"&&edit.Definition.Profile.Radius==2);
            Need(WriteRecovery.NotGuaranteed(args)&&args.Fingerprint()!=Parse(Edit("join")).Fingerprint()&&args.Fingerprint()!=Parse(Edit(expected:Guid.NewGuid().ToString("B"))).Fingerprint());
            return Task.CompletedTask;
        });
        await test("model feature append rejects mixed transactions unknown modes and misplaced profiles",()=>{
            Reject(Edit(),new{operation="move",handles=new[]{"AB"},displacement=new[]{1d,0}});
            Reject(Edit("subtract"));Reject(Edit(expected:id));Reject(Edit(expected:Guid.Empty.ToString("B")));
            Reject(Edit(definition:new{plane=new{origin=new[]{0d,0,0},x_axis=new[]{2d,0,0},y_axis=new[]{0d,1,0}},profile=new{kind="circle",center=new[]{0d,0},radius=2},distance=5,direction="negative"}));
            return Task.CompletedTask;
        });
        await test("model feature append keeps exact drawing sources separate from hidden editable copies",()=>{
            object Linked(string[] sources,bool editable=false)=>new{operation="add_model_extrusion",handles=new[]{"AB"},source_handles=sources,
                expected_last_item_id=last,part_operation="cut",definition=new{plane=new{origin=new[]{0d,0,10},x_axis=new[]{1d,0,0},y_axis=new[]{0d,1,0}},
                profile=new{kind="circle",center=new[]{0d,0},radius=2},distance=5,direction="negative",editable_profile=editable}};
            var linked=Parse(Linked(new[]{"AC"}));var edit=(ModelFeatureExtrusionSpec)linked.Operations.Single();
            Need(edit.SourceHandles is ["AC"]&&linked.Fingerprint()!=Parse(Edit()).Fingerprint());
            Reject(Linked(new[]{"AC","ac"}));Reject(Linked(new[]{"AB"}));Reject(Linked(new[]{"AC"},true));
            return Task.CompletedTask;
        });
        await test("model feature append schema exposes native ownership and replacement provenance",()=>{
            var tool=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_entities");
            var schema=tool.InputSchema.GetRawText();Need(schema.Contains("add_model_extrusion")&&schema.Contains("expected_last_item_id")&&schema.Contains("source_handles")&&schema.Contains("ROLLBACK_NOT_GUARANTEED"));
            Need(tool.OutputSchema!.Value.GetRawText().Contains("source_representation_replaced")&&tool.OutputSchema!.Value.GetRawText().Contains("source_refs"));
            var reference=new EntityRef(id,id,"AB");var change=new ModelFeatureMapping(0,reference,reference,last,last,last,last,last,"cut",100,90,false);
            var result=new ModifiedEntities(id,"committed",false,new[]{reference},Array.Empty<EntityRef>(),Array.Empty<EntityRef>(),Array.Empty<CopyMapping>(),"not_guaranteed",model_history_changes:new[]{change});
            Need(Wire.Element(result).GetProperty("model_history_changes")[0].GetProperty("volume_after").GetDouble()==90);
            return Task.CompletedTask;
        });
    }
}
