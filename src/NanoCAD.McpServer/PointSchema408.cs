using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class PointSchema408
{
    internal const string Scope = "Native Point is an actual DBPoint. Create Points singly or together through create_entities. Position uses three finite millimeter coordinates: WCS by default; with workplane, local XYZ uses x_axis, y_axis and their cross-product normal. Use set_point_position for an exact coordinate change, preserving the native identity and other geometry/display properties; expected_position uses the same declared frame. Batch distinct position edits in one modify_entities request. Ordinary move/copy/rotate/scale/mirror and grouped copies operate on the complete native Point geometry, including stored Z, normal, thickness and ECS rotation, with actual created identity mappings. Their displacement/center/axis pairs remain WCS XY unless workplane is declared. Native uniform scale also scales the normal-coordinate distance from its center; no point is projected onto a plane. Independent top-level native Points in the requested space are supported; model-owned, constrained, Field-bearing or otherwise dependent points require their owning adapter. set_properties and erase retain their existing meanings. Point appearance follows the drawing's current point display settings.";

    private static JsonObject Triple() => new()
    {
        ["type"] = "array", ["minItems"] = 3, ["maxItems"] = 3,
        ["items"] = new JsonObject { ["type"] = "number" },
        ["description"] = "Finite XYZ coordinates in millimeters; WCS by default, local XYZ with workplane (local Z follows x_axis cross y_axis)."
    };
    internal static JsonObject Create() => new()
    {
        ["type"] = "object", ["additionalProperties"] = false,
        ["properties"] = new JsonObject
        {
            ["type"] = new JsonObject { ["type"] = "string", ["const"] = "Point" },
            ["layer"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 255, ["default"] = "0" },
            ["position"] = Triple(), ["workplane"] = WorkplaneSchema.Input()
        },
        ["required"] = new JsonArray("type", "position"), ["description"] = Scope
    };
    internal static JsonObject PositionEdit() => new()
    {
        ["type"] = "object", ["additionalProperties"] = false,
        ["properties"] = new JsonObject
        {
            ["operation"] = new JsonObject { ["type"] = "string", ["const"] = "set_point_position" },
            ["handles"] = new JsonObject { ["type"] = "array", ["minItems"] = 1, ["maxItems"] = 1,
                ["items"] = new JsonObject { ["type"] = "string", ["pattern"] = "^[0-9A-Fa-f]{1,16}$" } },
            ["expected_position"] = Triple(), ["position"] = Triple(), ["workplane"] = WorkplaneSchema.Input()
        },
        ["required"] = new JsonArray("operation", "handles", "expected_position", "position"), ["description"] = Scope
    };
}
