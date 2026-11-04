using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using NanoCAD.McpServer;

internal static class ModelParameterTests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        var bridge=Guid.NewGuid().ToString("D"); var document=Guid.NewGuid().ToString("D"); var op=Guid.NewGuid().ToString("D");
        var variable=Guid.NewGuid().ToString("B");
        Dictionary<string,object?> Input()=>new(){["bridge_instance_id"]=bridge,["document_id"]=document,["operation_id"]=op,
            ["expected_revision"]="0007",["units"]="Millimeters",["handle"]="00150",["variable_id"]=variable,["expected_expression"]="150",["value"]=151};
        ModelParameterArguments Parse(Dictionary<string,object?> input)=>(ModelParameterArguments)WriteValidation.Parse("set_model_parameter",Wire.Element(input));
        void Require(bool condition){if(!condition)throw new Exception("Model parameter contract assertion failed.");}
        await test("model parameter explicit body scope normalizes bounds and write identity",()=>
        {
            var one=Parse(Input());var input=Input();input["scope_handles"]=new[]{"00150","ab"};var scoped=Parse(input);
            Require(scoped.UsesExpressionPlan&&WriteRecovery.NotGuaranteed(scoped)&&scoped.ScopeHandles!.SequenceEqual(new[]{"150","AB"})&&scoped.Fingerprint()!=one.Fingerprint());
            input["scope_handles"]=Enumerable.Range(1,512).Select(i=>i.ToString("X")).ToArray();
            Require(Parse(input).ScopeHandles!.Length==512);
            foreach(var bad in new object[]{Array.Empty<string>(),new[]{"AB"},new[]{"150","0150"},new[]{"150","0"},"150"})
            {input["scope_handles"]=bad;try{Parse(input);}catch(DomainException){continue;}throw new Exception("Invalid body scope accepted.");}
            var request=Wire.Element(new{bridge_instance_id=bridge,document_id=document,expected_revision="1",operation_id=op,units="Millimeters",operations=new[]{new{operation="name_model_parameter",handles=new[]{"150"},scope_handles=new[]{"150","AB"},variable_id=variable,expected_name="V",name="Driver"}}});
            Require(ModifyValidation.Parse(request).Operations.Single() is ModelParameterNameEdit n&&n.ScopeHandles!.Length==2);
            var tool=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.set_model_parameter");Require(tool.InputSchema.GetRawText().Contains("scope_handles")&&tool.OutputSchema!.Value.GetRawText().Contains("rebuilt_bodies"));
            return Task.CompletedTask;
        });
        await test("model parameter canonical identity and fingerprint preserve expression precondition",()=>
        {
            var a=Parse(Input()); Require(a.Handle=="150" && a.VariableId==variable.ToUpperInvariant() && a.ExpectedRevision=="7" && a.Expression=="151");
            var b=Input();b["handle"]="150";b["variable_id"]=variable.ToUpperInvariant();b["expected_revision"]="7";
            Require(a.Fingerprint()==Parse(b).Fingerprint());
            b["expected_expression"]="150.0";Require(a.Fingerprint()!=Parse(b).Fingerprint());
            b=Input();b["value"]=152;Require(a.Fingerprint()!=Parse(b).Fingerprint());
            return Task.CompletedTask;
        });
        await test("model parameter rejects mixed value expression units stale identity and duplicate fields",()=>
        {
            foreach(var change in new (string,object?)[]{("value","151"),("units","Inches"),("variable_id",Guid.Empty.ToString("B")),("handle","0"),("expected_expression",""),("expected_revision","18446744073709551616"),("expression","150+1"),("evaluate",true)})
            {
                var input=Input();input[change.Item1]=change.Item2;
                try{Parse(input);}catch(DomainException){continue;}throw new Exception("Invalid model parameter accepted: "+change.Item1);
            }
            string valid=Wire.Element(Input()).GetRawText();using var duplicate=JsonDocument.Parse(valid[..^1]+",\"value\":152}");
            try{ModelParameterValidation.Parse(duplicate.RootElement);}catch(DomainException){return Task.CompletedTask;}
            throw new Exception("Duplicate field accepted.");
        });
        await test("model parameter exact replay retains committed and unknown outcomes",async()=>
        {
            foreach(bool committed in new[]{true,false})
            {
                using var queue=new WriteQueue(Environment.CurrentManagedThreadId);
                var request=new Request(Wire.Version,Guid.NewGuid().ToString("D"),"set_model_parameter",8000,Wire.Element(Input()));
                var (job,replay)=queue.Enqueue(request);Require(!replay && queue.Take()==job && job.TryStart());
                Result result;
                if(committed)
                {
                    var verified = new ModelParameterResult(op,"committed",false,new(bridge,document,"150"),Guid.NewGuid().ToString("B"),variable,"150","151",151,"Millimeters",42,"not_tested")
                    {
                        model_healthy = true,
                        downstream_values_changed = false
                    };
                    var legacyData = Wire.Element(verified with { model_healthy = null, downstream_values_changed = null });
                    Require(!legacyData.TryGetProperty("model_healthy", out _) && !legacyData.TryGetProperty("downstream_values_changed", out _));
                    result = Result.Ok(request.requestId, verified);
                    job.BeginCommit();job.RecordCommit(result);
                }
                else result=job.Error("WRITE_OUTCOME_UNKNOWN","Native result unavailable.","unknown");
                job.Complete(result);
                var (same,isReplay)=queue.Enqueue(request with{requestId=Guid.NewGuid().ToString("D")});Require(same==job && isReplay && queue.Take()==null);
                var replayed=WriteJob.ForCaller(await same.Completion,"replay-request",true);
                Require(replayed.request_id=="replay-request" && (committed
                    ? replayed.data is ModelParameterResult{replayed:true,model_healthy:true,downstream_values_changed:false}
                    : replayed.error?.details is WriteErrorDetails{replayed:true,outcome:"unknown"}));
                if (committed)
                {
                    var data = Wire.Element(replayed).GetProperty("data");
                    Require(data.GetProperty("model_healthy").GetBoolean() && !data.GetProperty("downstream_values_changed").GetBoolean());
                }
                var different=Input();different["value"]=152;
                try{queue.Enqueue(request with{arguments=Wire.Element(different)});}catch(DomainException e)when(e.Error.code=="OPERATION_ID_CONFLICT"){continue;}
                throw new Exception("Operation ID conflict was not retained.");
            }
        });
        await test("model parameter candidate exposes a bounded write contract",()=>
        {
            var tool=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.set_model_parameter");
            Require(Wire.WriteOperations.Contains("set_model_parameter")&&!Wire.ReadOperations.Contains("set_model_parameter")&&tool.Annotations?.ReadOnlyHint==false);
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!,"tools-list.json"),JsonSerializer.Serialize(ToolCatalog.Tools,Wire.Json));
            return Task.CompletedTask;
        });
    }
}
