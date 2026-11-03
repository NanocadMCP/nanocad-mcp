using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class ModelSketchSupportRecovery440Tests
{
    internal static async Task Run(Func<string,Func<Task>,Task> test)
    {
        await test("block440 lost support recovery scopes explicit frame replacement without widening creation",()=>
        {
            static void Need(bool ok,string message){if(!ok)throw new Exception(message);}
            static void Reject(Action action){try{action();}catch(DomainException){return;}throw new Exception("Unscoped support recovery was accepted.");}
            const string datum="{00000000-0000-0000-0000-000000000041}",old="{00000000-0000-0000-0000-000000000042}";
            var input=new JsonObject{["kind"]="workplane",["plane_id"]=datum,["expected_datum_token"]=new string('A',64)};
            ModelSketchSupportSpec396 Parse()=>ModelSketchSupportValidation396.Parse(Wire.Element(input),"$",allowRecovery:true);
            ModelManufacturingEdit ParseEdit()=>(ModelManufacturingEdit)ModifyValidation.Parse(Wire.Element(new{
                bridge_instance_id="00000000-0000-0000-0000-000000000011",document_id="00000000-0000-0000-0000-000000000012",
                operation_id="00000000-0000-0000-0000-000000000013",expected_revision="0",units="Millimeters",
                operations=new[]{new{operation="set_model_sketch_support",handles=new[]{"A1"},feature_id=old,expected_last_item_id=old,definition=input}}})).Operations.Single();
            var ordinary=ModelSketchSupportValidation396.Parse(Wire.Element(input),"$");
            Need(!ordinary.RecoverLostSupport&&ordinary.FrameReferencePolicy=="preserve"&&new ModelSketchSupportSpec396("workplane",datum,new string('A',64)).FrameReferencePolicy=="preserve","Healthy support or record defaults changed.");
            ParseEdit();
            input["recover_lost_support"]=true;
            Reject(()=>Parse());
            input["expected_support_id"]=old;
            var recovery=Parse();
            Need(recovery.RecoverLostSupport&&recovery.ExpectedSupportId==old&&recovery.Id==datum&&recovery.FrameReferencePolicy=="preserve","Recovery lost its explicit identities or preservation default.");
            ParseEdit();
            Reject(()=>ModelSketchSupportValidation396.Parse(Wire.Element(input),"$"));
            foreach(string policy in new[]{"preserve","use_target_datum"})
            {
                input["frame_reference_policy"]=policy;
                Need(Parse().FrameReferencePolicy==policy&&ParseEdit().Definition.GetProperty("frame_reference_policy").GetString()==policy,"The explicit recovery frame policy was lost by its public edit route.");
                Reject(()=>ModelSketchSupportValidation396.Parse(Wire.Element(input),"$"));
                input["recover_lost_support"]=false;input.Remove("expected_support_id");
                Reject(()=>Parse());Reject(()=>ParseEdit());
                input.Remove("recover_lost_support");Reject(()=>Parse());Reject(()=>ParseEdit());
                input["recover_lost_support"]=true;input["expected_support_id"]=old;
            }
            foreach(var invalid in new JsonNode?[]{JsonValue.Create("automatic"),JsonValue.Create(true),null})
            {input["frame_reference_policy"]=invalid;Reject(()=>Parse());Reject(()=>ParseEdit());}
            var face=new JsonObject{["kind"]="face",["face_id"]=old,["face_token"]=new string('A',64),["recover_lost_support"]=true,["expected_support_id"]=old,["frame_reference_policy"]="use_target_datum"};
            Reject(()=>ModelSketchSupportValidation396.Parse(Wire.Element(face),"$",allowRecovery:true));
            input.Remove("frame_reference_policy");
            input["recover_lost_support"]=false;
            Reject(()=>Parse());
            input.Remove("expected_support_id");
            Need(!Parse().RecoverLostSupport&&Parse().FrameReferencePolicy=="preserve","Explicit healthy behavior changed.");
            input["expected_support_id"]="invented";input["recover_lost_support"]=true;
            Reject(()=>Parse());
            var branches=ToolCatalog.Tools.Single(tool=>tool.Name=="nanocad.modify_entities").InputSchema.GetProperty("properties").GetProperty("operations").GetProperty("items").GetProperty("oneOf").EnumerateArray().ToArray();
            var supports=branches.Where(branch=>branch.GetProperty("properties").GetProperty("operation").TryGetProperty("const",out var operation)&&operation.GetString()=="set_model_sketch_support").ToArray();
            var recoveryDefinition=supports.Single(branch=>branch.GetProperty("properties").GetProperty("definition").GetProperty("properties").TryGetProperty("recover_lost_support",out var flag)&&flag.GetProperty("const").ValueKind==JsonValueKind.True).GetProperty("properties").GetProperty("definition");
            var publishedPolicy=recoveryDefinition.GetProperty("properties").GetProperty("frame_reference_policy");
            Need(publishedPolicy.GetProperty("enum").EnumerateArray().Select(value=>value.GetString()).ToHashSet().SetEquals(new[]{"preserve","use_target_datum"})&&publishedPolicy.GetProperty("default").GetString()=="preserve"&&!recoveryDefinition.GetProperty("required").EnumerateArray().Any(value=>value.GetString()=="frame_reference_policy"),"Published policy must be optional and preserve older recovery requests.");
            Need(supports.Where(branch=>!branch.GetProperty("properties").GetProperty("definition").GetProperty("properties").TryGetProperty("frame_reference_policy",out _)).All(branch=>!branch.GetProperty("properties").GetProperty("definition").GetProperty("additionalProperties").GetBoolean())&&supports.Count(branch=>branch.GetProperty("properties").GetProperty("definition").GetProperty("properties").TryGetProperty("frame_reference_policy",out _))==1,"Ordinary support schemas admitted recovery frame replacement.");
            var creationSupport=branches.Single(branch=>branch.GetProperty("properties").GetProperty("operation").TryGetProperty("const",out var operation)&&operation.GetString()=="create_model_sketch").GetProperty("properties").GetProperty("support").GetProperty("oneOf");
            Need(creationSupport.EnumerateArray().All(branch=>!branch.GetProperty("properties").TryGetProperty("frame_reference_policy",out _)&&!branch.GetProperty("additionalProperties").GetBoolean()),"Creation support schema admitted a recovery-only frame policy.");
            return Task.CompletedTask;
        });
        await test("block440 fixed recovery datum context is explicit and ordinary creation stays unchanged",()=>
        {
            static void Need(bool ok,string message){if(!ok)throw new Exception(message);}
            static void Reject(Action action){try{action();}catch(DomainException){return;}throw new Exception("Incomplete recovery context was accepted.");}
            const string sketch="{00000000-0000-0000-0000-000000000051}",face="{00000000-0000-0000-0000-000000000052}";
            var input=JsonNode.Parse("""{"operation":"create_model_datum_plane","handles":["A1"],"expected_last_item_id":"{00000000-0000-0000-0000-000000000053}","name":"Replacement datum","definition":{"kind":"fixed","workplane":{"origin":[0,0,0],"x_axis":[1,0,0],"y_axis":[0,1,0]}}}""")!.AsObject();
            ModelDatumPlaneCreate353 Parse()=>(ModelDatumPlaneCreate353)ModelDatumPlaneValidation353.Parse(Wire.Element(input),"$",["A1"]);
            Need(Parse().RecoveryContext==null,"Ordinary datum creation was redirected.");
            input["recovery_context"]=new JsonObject{["sketch_id"]=sketch};Reject(()=>Parse());
            input["recovery_context"]!["expected_support_id"]=face;
            Need(Parse().RecoveryContext==new ModelSketchSupportRecoveryContext440(sketch,face),"Exact failed sketch/support context changed.");
            input["recovery_context"]!["extra"]=true;Reject(()=>Parse());
            return Task.CompletedTask;
        });
    }
}
