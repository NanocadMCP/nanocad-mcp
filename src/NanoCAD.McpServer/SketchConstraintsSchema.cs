using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class SketchConstraintsSchema
{
    public static JsonElement Input()
    {
        var schema=JsonNode.Parse(ModelVariablesSchema.Input().GetRawText())!.AsObject();
        schema["required"]!.AsArray().Add("sketch_id");
        schema["properties"]!["sketch_id"]=new JsonObject { ["type"]="string",["pattern"]=@"^\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}$" };
        return JsonSerializer.SerializeToElement(schema);
    }
}
