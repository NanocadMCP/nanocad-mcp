using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class ViewportWriteSchema
{
    public static JsonElement Edit()
    {
        JsonObject Handle() => new() { ["type"] = "string", ["pattern"] = "^[0-9A-Fa-f]{1,16}$" };
        JsonObject Number(double min, double max) => new() { ["type"] = "number", ["minimum"] = min, ["maximum"] = max };
        var angle = Number(0, 2 * Math.PI); angle.Remove("maximum"); angle["exclusiveMaximum"] = 2 * Math.PI;
        var properties = new JsonObject { ["type"] = "object", ["additionalProperties"] = false, ["minProperties"] = 1, ["properties"] = new JsonObject {
            ["view_center"] = new JsonObject { ["type"] = "array", ["minItems"] = 2, ["maxItems"] = 2, ["items"] = Number(-1e9, 1e9) },
            ["center"] = new JsonObject { ["type"] = "array", ["minItems"] = 2, ["maxItems"] = 2, ["items"] = Number(-1e9, 1e9) },
            ["width"]=Number(1e-6,1e9),["height"]=Number(1e-6,1e9),
            ["view_height"] = Number(1e-9, 1e9), ["twist_angle"] = angle, ["is_locked"] = new JsonObject { ["type"] = "boolean" } } };
        var fields = (JsonObject)properties["properties"]!;
        fields["is_on"] = new JsonObject { ["type"] = "boolean" };
        fields["view_direction"] = new JsonObject { ["type"] = "array", ["minItems"] = 3, ["maxItems"] = 3, ["items"] = Number(-1e9, 1e9), ["description"] = "Nonzero WCS camera direction vector. Target and view_center are preserved unless supplied." };
        fields["view_target"] = new JsonObject { ["type"] = "array", ["minItems"] = 3, ["maxItems"] = 3, ["items"] = Number(-1e9, 1e9) };
        fields["frozen_layer_handles"] = new JsonObject { ["type"] = "array", ["uniqueItems"] = true, ["items"] = Handle(), ["description"] = "Replace this viewport's frozen layer set using existing local layer handles; empty thaws all. Other viewports and global layer states are preserved. The request and complete readback use actual serialized byte budgets; no layer-count ceiling." };
        return JsonSerializer.SerializeToElement(new JsonObject { ["type"] = "object", ["additionalProperties"] = false,
            ["properties"] = new JsonObject { ["operation"] = new JsonObject { ["const"] = "set_viewport_view" }, ["layout_handle"] = Handle(), ["handle"] = Handle(), ["properties"] = properties },
            ["required"] = new JsonArray("operation", "layout_handle", "handle", "properties"),
            ["description"] = "Existing native rectangular paper viewport belonging to the explicit layout; no perspective/clipping/proxy/fields/extension data; viewport number1 is protected. Optional center/width/height change the paper frame in millimeters. view_center and view_height use model millimeters, view_target is WCS XYZ, view_direction is a nonzero WCS vector; twist_angle radians. Unspecified model framing is preserved when resizing the frame; height/view_height determines scale. Read get_structure viewports after commit to verify. On/off and viewport frozen layers are editable; other settings are preserved. A locked viewport requires explicit is_locked=false for camera/frame/frozen-layer edits. Enabling requires the explicit paper layout already active. ROLLBACK_NOT_GUARANTEED: native viewport activation may survive a failed transaction; inspect actual state before further writes. At most16 distinct handles; separate structural family; no implicit layout switching or save." });
    }
}
