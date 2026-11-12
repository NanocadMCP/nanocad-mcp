using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class DrawingViewLifecycleSchema345
{
    private static JsonObject Handle() => new() { ["type"] = "string", ["pattern"] = "^[0-9A-Fa-f]{1,16}$" };
    private static JsonObject Name() => new() { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 4096 };
    private static JsonObject Target() => new()
    {
        ["layout_handle"] = Handle(),
        ["handle"] = Handle(),
        ["expected_name"] = Name(),
        ["view_token"] = new JsonObject { ["type"] = "string", ["pattern"] = "^[0-9A-Fa-f]{64}$", ["description"] = "Current get_drawing_views token for this exact view and owning layout." }
    };
    private static JsonElement Branch(string operation, JsonObject properties, JsonArray required, string description)
    {
        properties["operation"] = new JsonObject { ["type"] = "string", ["const"] = operation }; required.Insert(0, "operation");
        return JsonSerializer.SerializeToElement(new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = required,
            ["properties"] = properties,
            ["description"] = description
        });
    }
    public static JsonElement Sources() => Branch("set_drawing_view_sources", new JsonObject
    {
        ["targets"] = new JsonObject
        {
            ["type"] = "array",
            ["minItems"] = 1,
            ["items"] = new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = new JsonArray("layout_handle", "handle", "expected_name", "view_token"),
                ["properties"] = Target()
            }
        },
        ["source_handles"] = new JsonObject { ["type"] = "array", ["minItems"] = 1, ["uniqueItems"] = true, ["items"] = Handle() }
    },
        new JsonArray("targets", "source_handles"),
        "Sole operation. Change the native source set of an explicit complete ordinary root-view/descendant group in Model or Paper. Views with attached local or outer cuts are excluded. Sources are current addressable top-level Model solids. Include every existing descendant and retain any native history host among the new sources. Keep source bodies, view identities, ownership, placement, scales and display. Section/slice/break sources require their owning adapter. Native reattachment updates reactors; recovery/Undo is not guaranteed.");
    public static JsonElement Rename()
    {
        var properties = Target(); properties["name"] = Name();
        return Branch("rename_drawing_view", properties, new JsonArray("layout_handle", "handle", "expected_name", "view_token", "name"),
            "Sole operation. Rename one current native DrawingView in place using its existing layout/ref/token. Views with attached local or outer cuts are excluded. Use an unused view name; preserve sources, tree, section and history. No reconstruction or source edit. Native recovery/Undo is not guaranteed.");
    }
    public static JsonElement Child()
    {
        var properties = Target(); properties["name"] = Name(); properties["direction"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("top", "bottom", "left", "right", "iso_top_left", "iso_top_right", "iso_bottom_left", "iso_bottom_right") };
        properties["origin"] = new JsonObject { ["type"] = "array", ["minItems"] = 2, ["maxItems"] = 2, ["items"] = new JsonObject { ["type"] = "number" }, ["description"] = "WCS XY millimeters in the parent's active Model/Paper space." };
        properties["model_scale"] = new JsonObject { ["type"] = "number", ["exclusiveMinimum"] = 0, ["description"] = "Native DScale denominator; 2 draws at half size. New child design scale is1." };
        return Branch("add_drawing_view_child", properties, new JsonArray("layout_handle", "handle", "expected_name", "view_token", "name", "direction", "origin", "model_scale"),
            "Sole operation. Add one native child to an ordinary healthy root projection in its active owning Model/Paper space. Parents with attached local or outer cuts are excluded. Copy its actual sources/projection basis and preserve the retained tree/history. Placement is explicit; parent alignment for this child is disabled so the requested origin stays independent. Section/slice/break parents require their owning adapter. Return both retained parent and created child refs; recovery/Undo is not guaranteed.");
    }
}
