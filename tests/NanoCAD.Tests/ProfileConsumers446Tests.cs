using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class ProfileConsumers446Tests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        static void Need(bool ok){if(!ok)throw new Exception("Existing-profile Loft446 contract failed.");}
        static void Reject(Action act){try{act();}catch(DomainException){return;}throw new Exception("Unsafe retained-profile Loft request was accepted.");}
        static JsonObject Selection(int index)=>new(){["profile_id"]=$"{{00000000-0000-0000-0000-{index:000000000000}}}",["expected_sketch_id"]=$"{{10000000-0000-0000-0000-{index:000000000000}}}",["expected_profile_token"]=new string('A',64)};
        static JsonObject Input()=>new(){["operation"]="add_model_loft_from_profiles",["handles"]=new JsonArray("A1"),["expected_last_item_id"]="{20000000-0000-0000-0000-000000000001}",["profiles"]=new JsonArray(Selection(2),Selection(1)),["part_operation"]="join"};
        await test("block446 retained Loft exact ordered bindings and exclusive request",()=>
        {
            var input=Input();var parsed=(ModelProfileLoft446)ModelProfileLoftValidation446.Parse(Wire.Element(input),"$",["A1"]);
            Need(parsed.Profiles.Length==2&&parsed.Profiles[0].ProfileId.EndsWith("000000000002}")&&parsed.Profiles[1].ProfileId.EndsWith("000000000001}"));
            Need(WriteRecovery.NotGuaranteed(new ModifyArguments("b","d","r","o","Millimeters",[parsed])));
            Reject(()=>ModelSketchLifecycleValidation362.Batch([parsed,new MoveSpec(["B1"],new(1,2))]));
            input["profiles"]![1]!["profile_id"]=input["profiles"]![0]!["profile_id"]!.DeepClone();Reject(()=>ModelProfileLoftValidation446.Parse(Wire.Element(input),"$",["A1"]));
            input=Input();input["profiles"]![1]!["expected_profile_token"]="stale";Reject(()=>ModelProfileLoftValidation446.Parse(Wire.Element(input),"$",["A1"]));
            input=Input();input["path_handle"]="B2";input["guide_handles"]=new JsonArray(new JsonArray("B3"));Reject(()=>ModelProfileLoftValidation446.Parse(Wire.Element(input),"$",["A1"]));
            return Task.CompletedTask;
        });
        await test("block446 callable Loft schema exposes every section identity and returned freshness",()=>
        {
            var tool=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_entities");
            var branch=tool.InputSchema.GetProperty("properties").GetProperty("operations").GetProperty("items").GetProperty("oneOf").EnumerateArray()
                .Single(v=>v.GetProperty("properties").GetProperty("operation").GetProperty("const").GetString()=="add_model_loft_from_profiles");
            var profiles=branch.GetProperty("properties").GetProperty("profiles");Need(profiles.GetProperty("minItems").GetInt32()==2&&!profiles.TryGetProperty("maxItems",out _));
            var fields=profiles.GetProperty("items").GetProperty("properties");Need(fields.TryGetProperty("profile_id",out _)&&fields.TryGetProperty("expected_sketch_id",out _)&&fields.TryGetProperty("expected_profile_token",out _));
            Need(tool.OutputSchema!.Value.GetRawText().Contains("consumed_profiles"));return Task.CompletedTask;
        });
    }
}
