using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class ModelVariablesSchema
{
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
