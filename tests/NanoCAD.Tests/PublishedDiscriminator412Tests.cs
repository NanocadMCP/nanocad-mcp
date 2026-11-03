using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class PublishedDiscriminator412Tests
{
    static void Need(bool value,string reason){if(!value)throw new Exception(reason);}
    static JsonElement Request(string collection,JsonObject item)=>Wire.Element(new JsonObject
    {
        ["bridge_instance_id"]="00000000-0000-0000-0000-000000000011",
        ["document_id"]="00000000-0000-0000-0000-000000000012",
        ["operation_id"]="00000000-0000-0000-0000-000000000013",
        ["expected_revision"]="0",["units"]="Millimeters",[collection]=new JsonArray(item)
    });
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        await test("discriminator412 all published creation and edit names reach their semantic parser",()=>
        {
            foreach(var (tool,collection,field) in new[]{("create_entities","entities","type"),("modify_entities","operations","operation"),("modify_structure","operations","operation")})
            {
                var schema=ToolCatalog.Tools.Single(value=>value.Name=="nanocad."+tool).InputSchema;
                var variants=schema.GetProperty("properties").GetProperty(collection).GetProperty("items").GetProperty("oneOf");
                var names=variants.EnumerateArray().Select(value=>value.GetProperty("properties").GetProperty(field).GetProperty("const").GetString()!).Distinct(StringComparer.Ordinal).ToArray();
                Need(names.Length>0,"No actual published discriminators: "+tool);
                foreach(string name in names)
                {
                    var item=new JsonObject{[field]=name};if(field=="operation")item["handles"]=new JsonArray("A1");
                    try{WriteValidation.Parse(tool,Request(collection,item));}
                    catch(DomainException error)
                    {
                        // Minimal branch inputs intentionally omit geometry or
                        // role-specific fields. A failure AT the discriminator
                        // exposes schema/dispatcher drift before any CAD work.
                        var details=error.Error.details is {} evidence?Wire.Element(evidence):default;
                        string? path=details.ValueKind==JsonValueKind.Object&&details.TryGetProperty("path",out var location)?location.GetString():null;
                        Need(path!="$."+collection+"[0]."+field,$"Published {tool}/{name} was rejected at its discriminator: {error.Message}");
                    }
                }
            }
            return Task.CompletedTask;
        });
        await test("discriminator412 same-profile revolution accepts its full published operation name",()=>
        {
            const string name="add_model_revolution_from_profile";
            var item=new JsonObject{["operation"]=name,["handles"]=new JsonArray("A1"),
                ["expected_last_item_id"]="{00000000-0000-0000-0000-000000000014}",
                ["profile_id"]="{00000000-0000-0000-0000-000000000015}",
                ["expected_sketch_id"]="{00000000-0000-0000-0000-000000000016}",
                ["expected_profile_token"]=new string('A',64),["axis_handle"]="B1",["angle_degrees"]=60,["part_operation"]="join"};
            var parsed=(ModifyArguments)WriteValidation.Parse("modify_entities",Request("operations",item));
            Need(parsed.Operations.Single() is ModelProfileRevolution411,"Full revolution branch was not dispatched.");
            var unknown=new JsonObject{["operation"]=new string('x',128),["handles"]=new JsonArray("A1")};
            try{WriteValidation.Parse("modify_entities",Request("operations",unknown));throw new Exception("Unknown discriminator accepted.");}
            catch(DomainException error){Need(error.Error.code=="INVALID_ARGUMENT"&&error.Message.Contains("Unknown edit operation",StringComparison.Ordinal),"Unknown names must still fail the closed dispatcher.");}
            return Task.CompletedTask;
        });
    }
}
