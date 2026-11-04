using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class ViewportCreateSchema
{
    public static JsonElement Create()
    {
        JsonObject Number(double min, double max) => new() { ["type"] = "number", ["minimum"] = min, ["maximum"] = max };
        JsonObject Handle() => new() { ["type"] = "string", ["pattern"] = "^[0-9A-Fa-f]{1,16}$" };
        JsonObject Point() => new() { ["type"] = "array", ["minItems"] = 2, ["maxItems"] = 2, ["items"] = Number(-1e9, 1e9) };
        var angle = Number(0, 2 * Math.PI); angle.Remove("maximum"); angle["exclusiveMaximum"] = 2 * Math.PI;
        return JsonSerializer.SerializeToElement(new JsonObject { ["type"] = "object", ["additionalProperties"] = false,
            ["required"] = new JsonArray("operation", "layout_handle", "layer_handle", "center", "width", "height", "view_center", "view_height", "twist_angle", "is_on", "is_locked"),
            ["properties"] = new JsonObject { ["operation"] = new JsonObject { ["const"] = "create_viewport" },
                ["layout_handle"] = Handle(), ["layer_handle"] = Handle(), ["center"] = Point(), ["width"] = Number(1e-6, 1e9), ["height"] = Number(1e-6, 1e9),
                ["view_center"] = Point(), ["view_height"] = Number(1e-6, 1e9), ["twist_angle"] = angle,
                ["is_on"] = new JsonObject { ["type"] = "boolean" }, ["is_locked"] = new JsonObject { ["type"] = "boolean" } },
            ["description"] = "ROLLBACK_NOT_GUARANTEED: A started failure can leave viewport changes that need inspection. On/Number are verified after Commit/disposal; a post-commit verification warning means creation was committed but final state still needs inspection. Create an ordinary rectangular XY viewport in an existing initialized paper layout using millimeters. Explicit local visible unlocked layer handle. center/width/height are paper mm; view_center/view_height are model mm; radians for twist. View target is WCS origin, direction +Z, no clipping/perspective/frozen layers. Explicit on/off and lock flags. New viewports per separate batch; frame within ±1e9 mm and scale1e-9..1e9. Existing model/layout inventory has no artificial count cap. Overall viewport, existing geometry and plot settings are preserved. Uninitialized/imperial layouts are refused before writes. Enabled creation requires the requested paper layout already active via manage_document activate_layout. This operation retains that context; no implicit activation, save or printer calls." });
    }
}
