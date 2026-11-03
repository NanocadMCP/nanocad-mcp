using NanoCAD.Contracts;
using NanoCAD.Ipc;
using NanoCAD.McpServer;

internal static class WriteRecoveryTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        await test("write recovery distinguishes unavailable rollback from operations that never started",()=>{
            if(WriteRecovery.UnknownFailure(false,false,false,true)||!WriteRecovery.UnknownFailure(true,false,false,true)||
               WriteRecovery.UnknownFailure(true,false,false,false)||!WriteRecovery.UnknownFailure(false,true,false,false)||!WriteRecovery.UnknownFailure(false,false,true,false))
                throw new Exception("Non-atomic failure was mislabeled as rollback or not-started.");
            return Task.CompletedTask;
        });
        await test("write recovery recognizes associative hatch and keeps non-associative path separate",()=>{
            var id=Guid.NewGuid().ToString();var linked=new CreateArguments(id,id,"1",id,"Millimeters",new EntitySpec[]{new AssociativeHatchSpec("AB","0")});
            if(!WriteRecovery.NotGuaranteed(linked)||WriteRecovery.NotGuaranteed(linked with{Entities=new EntitySpec[]{new CircleSpec(new(0,0),1,"0")}}))throw new Exception("Wrong recovery policy.");
            var result=Result.Ok(id,new CreatedEntities(id,"committed",false,new[]{new EntityRef(id,id,"AB")},"not_guaranteed"),null,WriteRecovery.Warning);
            var replay=WriteJob.ForCaller(result,Guid.NewGuid().ToString(),true);
            if(replay.data is not CreatedEntities {replayed:true,undo_history:"not_guaranteed"}||!replay.warnings.SequenceEqual(result.warnings))throw new Exception("Replay lost recovery information.");
            return Task.CompletedTask;
        });
        await test("write recovery is visible before invoking hatch or viewport and does not require approval",()=>{
            foreach(string name in new[]{"nanocad.create_entities","nanocad.modify_structure"})
                if(ToolCatalog.Tools.Single(t=>t.Name==name).Description?.Contains("rollback",StringComparison.OrdinalIgnoreCase)!=true)throw new Exception("No upfront recovery notice.");
            if(!ServerGuidance.Instructions.Contains("without demanding an extra permission")||ServerGuidance.Instructions.Contains("bootstrap.dxf"))throw new Exception("Agent guidance not aligned with user policy.");
            return Task.CompletedTask;
        });
    }
}
