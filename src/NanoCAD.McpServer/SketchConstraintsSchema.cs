using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class SketchConstraintsSchema
{
    public static JsonElement Input()
    {
        var schema = JsonNode.Parse(ModelVariablesSchema.Input().GetRawText())!.AsObject();
        schema["required"]!.AsArray().Add("sketch_id");
        schema["properties"]!["measure_smooth"] = new JsonObject
        {
            ["type"] = "boolean",
            ["description"] = "Measure the current WCS endpoint gap and continuation angle of observed native GeomSmooth relations in this sketch, using their actual member and endpoint bindings."
        };
        schema["properties"]!["sketch_scope"] = new JsonObject
        {
            ["type"] = "string",
            ["enum"] = new JsonArray("body_history", "document"),
            ["description"] = "Default body_history. Use document to read an independent construction sketch by an exact document sketch ID with a selected body in the same native owner document."
        };
        schema["properties"]!["sketch_id"] = new JsonObject { ["type"] = "string", ["pattern"] = @"^\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}$" };
        return JsonSerializer.SerializeToElement(schema);
    }
}
