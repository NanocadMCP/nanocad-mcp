using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class NativeHatchCreateSchema407
{
    internal static JsonObject Entity(bool grouped)
    {
        JsonObject Handle() => new() { ["type"] = "string", ["pattern"] = "^[0-9A-Fa-f]{1,16}$" };
        var properties = new JsonObject
        {
            ["type"] = new JsonObject { ["const"] = "Hatch" },
            ["layer"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 255 },
            ["pattern_name"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 255, ["default"] = "SOLID", ["description"] = "Installed native predefined PAT name; not a file path. No fixed pattern-name catalogue is imposed." },
            ["pattern_angle"] = new JsonObject { ["type"] = "number", ["minimum"] = 0, ["exclusiveMaximum"] = 2 * Math.PI, ["default"] = 0, ["description"] = "Radians for line patterns; has no rendering effect on SOLID." },
            ["pattern_scale"] = new JsonObject { ["type"] = "number", ["exclusiveMinimum"] = 0, ["maximum"] = 1e9, ["default"] = 1, ["description"] = "Native line-pattern scale; has no rendering effect on SOLID." },
            ["origin_ocs"] = new JsonObject { ["type"] = "array", ["minItems"] = 2, ["maxItems"] = 2, ["items"] = new JsonObject { ["type"] = "number", ["minimum"] = -1e9, ["maximum"] = 1e9 } },
            ["hatch_style"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("normal", "outer", "ignore"), ["default"] = "normal" }
        };
        string boundary;
        if (grouped)
        {
            boundary = "boundary_groups"; properties[boundary] = new JsonObject
            {
                ["type"] = "array",
                ["minItems"] = 1,
                ["items"] = new JsonObject { ["type"] = "array", ["minItems"] = 1, ["uniqueItems"] = true, ["items"] = Handle() },
                ["description"] = "First group is the outer loop; later groups are separate nonnested holes. Each group is one closed zero-width Polyline (native bulges retained), Circle or full Ellipse, one actually closed native Spline in WCS XY/Z0, or a connected native Line/Arc chain. Handles are distinct across all loops and reference already committed sources."
            };
        }
        else { boundary = "boundary_handle"; properties[boundary] = Handle(); }
        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = properties,
            ["required"] = new JsonArray("type", boundary),
            ["description"] = "Create the final native associative Hatch directly from existing curved boundary sources and optional hole groups, with chosen predefined fill. Source geometry and other classified local Hatch links are retained. Plane is inferred from the outer closed native boundary; straight Line-only chains use WCS XY at their existing elevation. Use Model or active ordinary Paper sources in the requested space. Sources must be unlocked and local. A Spline is one closed source per outer/hole group, with all controls in WCS XY/Z0 and no projection; existing planes for other source types remain. Native NURBS loop direction/seam/basis may change while the actual source curve and association remain. No temporary rectangle/rebind workflow or chord approximation is needed. ROLLBACK_NOT_GUARANTEED: A started failure can leave native boundary links that need inspection."
        };
    }
}
