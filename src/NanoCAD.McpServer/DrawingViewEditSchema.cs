using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class DrawingViewEditSchema
{
    private static JsonObject Handle() => new() { ["type"] = "string", ["pattern"] = "^[0-9A-Fa-f]{1,16}$" };
    private static JsonObject TargetProperties() => new()
    {
        ["layout_handle"] = Handle(),
        ["handle"] = Handle(),
        ["expected_name"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 4096 }
    };
    public static JsonElement Properties()
    {
        var properties = TargetProperties(); properties["operation"] = new JsonObject { ["type"] = "string", ["const"] = "set_drawing_view_properties" };
        properties["origin"] = new JsonObject
        {
            ["type"] = "array",
            ["minItems"] = 2,
            ["maxItems"] = 2,
            ["items"] = new JsonObject { ["type"] = "number" },
            ["description"] = "WCS XY millimeters in the owning Model/Paper space; preserve native plane axes and Z. Native parent alignment remains enabled as stored."
        };
        properties["model_scale"] = new JsonObject
        {
            ["type"] = "number",
            ["exclusiveMinimum"] = 0,
            ["description"] = "Native DScale denominator (2 draws at one-half size). Preserve design_scale. Include affected aligned descendants with the requested placement/scale in this same group."
        };
        foreach (string key in new[] { "show_hidden_lines", "show_hatches", "show_tangent_edges" }) properties[key] = new JsonObject { ["type"] = "boolean" };
        properties["preview_mode"] = new JsonObject
        {
            ["type"] = "boolean",
            ["description"] = "Native DrawingView preview: true displays its projection bounding box; false requests full view graphics. Omit to preserve the stored mode."
        };
        return JsonSerializer.SerializeToElement(new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("operation", "layout_handle", "handle", "expected_name"),
            ["properties"] = properties,
            ["anyOf"] = new JsonArray(new JsonObject { ["required"] = new JsonArray("origin") }, new JsonObject { ["required"] = new JsonArray("model_scale") },
                new JsonObject { ["required"] = new JsonArray("show_hidden_lines") }, new JsonObject { ["required"] = new JsonArray("show_hatches") }, new JsonObject { ["required"] = new JsonArray("show_tangent_edges") },
                new JsonObject { ["required"] = new JsonArray("preview_mode") }),
            ["description"] = "Edit an existing exact native DrawingView in an ordinary Model/Paper layout in place. Views with attached local or outer cuts are excluded. Use get_drawing_views for layout/view identity, names, parent/child and alignment. Group only these property operations in one request; each view appears once. Preserve 3D source, parent/root, section and break identities; no source reattachment, projection-plane change, view reconstruction or manual geometry. Placement/scale with breaks requires its owning adapter. Native update/Undo is not guaranteed atomic; failures after starting can be unknown."
        });
    }
    public static JsonElement Refresh() => JsonSerializer.SerializeToElement(new JsonObject
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray("operation", "targets"),
        ["properties"] = new JsonObject
        {
            ["operation"] = new JsonObject { ["type"] = "string", ["const"] = "refresh_drawing_views" },
            ["targets"] = new JsonObject
            {
                ["type"] = "array",
                ["minItems"] = 1,
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["required"] = new JsonArray("layout_handle", "handle", "expected_name"),
                    ["properties"] = TargetProperties()
                }
            }
        },
        ["description"] = "Sole operation. Explicitly refresh selected current native DrawingViews, preserving their sources, placement, preview mode and section/break links. Views with attached local or outer cuts are excluded. Read current layout/view handles and names with get_drawing_views; no model edits or view reconstruction."
    });
    public static JsonElement Delete() => JsonSerializer.SerializeToElement(new JsonObject
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray("operation", "targets"),
        ["properties"] = new JsonObject
        {
            ["operation"] = new JsonObject { ["type"] = "string", ["const"] = "delete_drawing_views" },
            ["targets"] = new JsonObject
            {
                ["type"] = "array",
                ["minItems"] = 1,
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["required"] = new JsonArray("layout_handle", "handle", "expected_name"),
                    ["properties"] = TargetProperties()
                }
            },
            ["section_targets"] = new JsonObject
            {
                ["type"] = "array",
                ["minItems"] = 0,
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["required"] = new JsonArray("section_id", "section_token"),
                    ["properties"] = new JsonObject
                    {
                        ["section_id"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["pattern"] = "^\\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\\}$",
                            ["not"] = new JsonObject { ["const"] = "{00000000-0000-0000-0000-000000000000}" }
                        },
                        ["section_token"] = new JsonObject { ["type"] = "string", ["pattern"] = "^[0-9A-Fa-f]{64}$" }
                    }
                }
            }
        },
        ["description"] = "Sole operation. Native erase of an explicit view leaf/subtree across Model/Paper layouts, retaining 3D sources and unselected views. Views with attached local or outer cuts are excluded. Select every descendant by its current layout/view handle and name. Section/slice view deletion may retain its Sections. Optional section_targets explicitly retires selected independent non-live Plane Sections using fresh section_cut tokens and every associated view/descendant. Foreign/shared inputs and unselected dependents remain protected. Returned drawing_view changes refer to erased identities. Native Undo/rollback is not guaranteed; inspect unknown outcomes without duplicating requests."
    });
}
