using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class ModelVariablesSchema
{
    public static JsonElement FeatureInput()
    {
        var input = JsonNode.Parse(Input().GetRawText())!.AsObject();
        input["properties"]!["feature_id"] = ModelDatumPlaneSchema353.Id();
        input["properties"]!["feature_id"]!["description"] = "Limit the variable network to variables owned by this current feature and their native upstream formula inputs. Returned expressions retain their units and current direct consumers.";
        return JsonSerializer.SerializeToElement(input);
    }
    public static JsonElement Input() => JsonSerializer.SerializeToElement(JsonNode.Parse("""
    {"type":"object","additionalProperties":false,
     "required":["bridge_instance_id","document_id","expected_revision","handle"],
     "properties":{
       "bridge_instance_id":{"type":"string","format":"uuid"},
       "document_id":{"type":"string","format":"uuid"},
       "expected_revision":{"type":"string","pattern":"^[0-9]{1,20}$"},
       "handle":{"type":"string","pattern":"^[0-9A-Fa-f]{1,16}$"}
     }}
    """)!);
}
