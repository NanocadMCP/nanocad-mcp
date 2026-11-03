using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class FitSplineSchema408
{
    internal static JsonObject Entity()
    {
        JsonObject Pair(string description) => new()
        {
            ["type"] = "array", ["minItems"] = 2, ["maxItems"] = 2,
            ["items"] = new JsonObject { ["type"] = "number", ["minimum"] = -1e9, ["maximum"] = 1e9 },
            ["description"] = description
        };
        string tangent = "Endpoint tangent vector along the curve's forward parameter direction, in local workplane coordinates (WCS XY by default). Supply both endpoint tangents or omit both for native automatic tangents. Nonzero, with a component of at least 1e-9; passed to the native constructor without normalization.";
        return new()
        {
            ["type"] = "object", ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["type"] = new JsonObject { ["type"] = "string", ["const"] = "Spline" },
                ["construction"] = new JsonObject { ["type"] = "string", ["const"] = "fit_points" },
                ["layer"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 255, ["default"] = "0" },
                ["workplane"] = WorkplaneSchema.Input(),
                ["fit_points"] = new JsonObject
                {
                    ["type"] = "array", ["minItems"] = 2,
                    ["items"] = Pair("Ordered local XY fit point in millimeters; these are points on the requested curve, not NURBS control points."),
                    ["description"] = "At least two ordered points. Consecutive points and open endpoints must differ. No fixed point-count cap; actual request bytes govern transport capacity."
                },
                ["fit_tolerance"] = new JsonObject
                {
                    ["type"] = "number", ["minimum"] = 0, ["maximum"] = 1e9,
                    ["description"] = "Requested native fitting tolerance in millimeters. Zero requests interpolation; positive values allow this deviation from supplied points, with ordinary numeric roundoff. Choose it from the task's dimensional requirements."
                },
                ["start_tangent"] = Pair(tangent), ["end_tangent"] = Pair(tangent)
            },
            ["required"] = new JsonArray("type", "construction", "fit_points", "fit_tolerance"),
            ["dependentRequired"] = new JsonObject
            {
                ["start_tangent"] = new JsonArray("end_tangent"), ["end_tangent"] = new JsonArray("start_tangent")
            },
            ["description"] = "Create an open native cubic (degree 3) fitted Spline from ordered points, with optional paired endpoint tangents and explicit fit tolerance. Local coordinate pairs use the optional explicit workplane; otherwise WCS XY/Z=0. Retains native fitted/NURBS data. This is a smooth Spline representation, not a tangent-arc Polyline, an associative constraint network or a segmented mesh. Existing control-point Spline creation remains a separate grammar; do not mix fit and control-point fields."
        };
    }
}
