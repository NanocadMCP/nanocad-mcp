using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class Basic340IntegrationTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        await test("block340 published profile routes preserve actual IDs and recovery classification",()=>
        {
            static void Need(bool ok){if(!ok)throw new Exception("340 shared integration contract failed.");}
            var args=ModifyValidation.Parse(Wire.Element(new{
                bridge_instance_id="00000000-0000-0000-0000-000000000011",document_id="00000000-0000-0000-0000-000000000012",
                operation_id="00000000-0000-0000-0000-000000000013",expected_revision="0",units="Millimeters",
                operations=new[]{new{operation="create_model_profile",handles=new[]{"A1"},source_handles=new[]{"B1","B2","B3","B4"},
                    expected_last_item_id="{00000000-0000-0000-0000-000000000014}",
                    plane=new{origin=new[]{0.0,0.0,7.0},x_axis=new[]{1.0,0.0,0.0},y_axis=new[]{0.0,1.0,0.0}}}}}));
            Need(args.Operations[0] is ModelProfileCreate340&&WriteRecovery.NotGuaranteed(args));
            Need(EditReferences.All(args.Operations[0]).SequenceEqual(new[]{"A1","B1","B2","B3","B4"}));
            var tool=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_entities");
            var branches=tool.InputSchema.GetProperty("properties").GetProperty("operations").GetProperty("items").GetProperty("oneOf").EnumerateArray().ToArray();
            JsonElement Branch(string name)=>branches.Single(v=>v.GetProperty("properties").GetProperty("operation").TryGetProperty("const",out var operation)&&operation.GetString()==name);
            Need(Branch("create_model_profile").GetProperty("required").EnumerateArray().Any(v=>v.GetString()=="plane"));
            Need(Branch("add_model_extrusion_from_profile").GetProperty("required").EnumerateArray().Any(v=>v.GetString()=="expected_profile_token"));
            Need(Branch("mirror").GetProperty("description").GetString()!.Contains("Helix"));
            Need(Branch("mirror").GetProperty("properties").GetProperty("handles").TryGetProperty("maxItems",out _)==false);
            return Task.CompletedTask;
        });
    }
}
