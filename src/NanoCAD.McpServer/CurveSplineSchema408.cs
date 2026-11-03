using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class CurveSplineSchema408
{
    internal static JsonObject Entity() => new()
    {
        ["type"] = "object", ["additionalProperties"] = false,
        ["properties"] = new JsonObject
        {
            ["type"] = new JsonObject { ["type"] = "string", ["const"] = "Spline" },
            ["construction"] = new JsonObject { ["type"] = "string", ["const"] = "native_curve" },
            ["source_handle"] = new JsonObject { ["type"] = "string", ["pattern"] = "^[0-9A-Fa-f]{1,16}$" },
            ["layer"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 255, ["default"] = "0" }
        },
        ["required"] = new JsonArray("type", "construction", "source_handle"),
        ["description"] = "Derive a new native Spline representation of an existing exact Arc or Ellipse base curve in the requested drawing space. Uses the native curve representation getter, with no sampled refitting or invented controls. Source is retained unchanged; new geometry is independent and carries no source Fields, constraints, history or associations. Layer follows this request; source appearance/application data and extrusion surface thickness are not transferred. Geometry remains in source WCS placement: do not provide workplane, fit points or control points. The request's current expected_revision protects source context. Existing linked source curves may be read without editing their links. Nested/foreign definitions require their own route."
    };
}
