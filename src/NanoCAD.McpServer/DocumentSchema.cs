using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class DocumentSchema
{
    private static JsonObject Text(int maximum = 240) => new() { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = maximum };
    private static JsonObject Id() { var value = Text(36); value["format"] = "uuid"; return value; }
    private static JsonObject Flag() => new() { ["type"] = "boolean", ["default"] = false };
    public static JsonElement Input()
    {
        var variants = new JsonArray();
        foreach (string action in new[] { "new", "open", "activate", "save", "export_dxf", "activate_layout" })
        {
            var properties = new JsonObject
            {
                ["bridge_instance_id"] = Id(), ["operation_id"] = Id(),
                ["action"] = new JsonObject { ["type"] = "string", ["const"] = action }
            };
            var required = new JsonArray("bridge_instance_id", "operation_id", "action");
            if (action is not ("new" or "open"))
            {
                properties["document_id"] = Id(); required.Add("document_id");
                properties["expected_revision"] = new JsonObject { ["type"] = "string", ["pattern"] = "^[0-9]{1,20}$" }; required.Add("expected_revision");
            }
            if (action is not ("new" or "activate" or "activate_layout"))
            {
                properties["path"] = Text();
                if (action is "open" or "export_dxf") required.Add("path");
            }
            if (action == "open") properties["read_only"] = Flag();
            if (action == "activate_layout")
            {
                properties["layout_handle"] = new JsonObject { ["type"] = "string", ["pattern"] = "^[0-9A-Fa-f]{1,16}$" };
                required.Add("layout_handle");
            }
            if (action is "save" or "export_dxf") properties["overwrite"] = Flag();
            variants.Add(new JsonObject { ["type"] = "object", ["additionalProperties"] = false, ["properties"] = properties, ["required"] = required });
        }
        return JsonSerializer.SerializeToElement(new JsonObject { ["type"] = "object", ["oneOf"] = variants });
    }
}
