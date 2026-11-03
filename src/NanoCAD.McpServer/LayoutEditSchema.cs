using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class LayoutEditSchema
{
    public static JsonElement Edit(bool rename)
    {
        JsonObject Text() => new() { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 255 };
        var properties = new JsonObject
        {
            ["operation"] = new JsonObject { ["type"] = "string", ["const"] = rename ? "rename_layout" : "set_layout_properties" },
            ["handle"] = new JsonObject { ["type"] = "string", ["pattern"] = "^[0-9A-Fa-f]{1,16}$" }, ["expected_name"] = Text()
        };
        if (rename) properties["name"] = Text();
        else properties["properties"] = new JsonObject { ["type"] = "object", ["additionalProperties"] = false, ["minProperties"] = 1,
            ["properties"] = new JsonObject { ["tab_order"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1 } }, ["required"] = new JsonArray("tab_order") };
        return JsonSerializer.SerializeToElement(new JsonObject { ["type"] = "object", ["additionalProperties"] = false, ["properties"] = properties,
            ["required"] = new JsonArray("operation", "handle", "expected_name", rename ? "name" : "properties"),
            ["description"] = rename ? "Rename an existing native paper layout by handle and exact name from get_structure layouts. Preserve layout, paper-space and entity identities, plot settings and active space. New ordinary name must be unused, ignoring case; Model is protected. One layout/request; allowed from Model or Paper. Verify get_structure layouts after commit."
            : "Edit an existing native paper layout by handle and exact current name from get_structure layouts. tab_order moves this tab to the requested position1..paper-layout count and reorders neighboring paper tabs; Model remains0. Other metadata, limits, paper entities and active layout are preserved. One layout/request; verify get_structure layouts after commit. Printer/PDF settings are outside this operation." });
    }
}
