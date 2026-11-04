using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class NativeCurveSnapSchema
{
    private static JsonObject Handle() => new() { ["type"] = "string", ["pattern"] = "^[0-9A-Fa-f]{1,16}$" };
    private static JsonObject Token() => new()
    {
        ["type"] = "string", ["pattern"] = "^[0-9A-Fa-f]{64}$",
        ["description"] = "Current geometry.native_curve.curve_token for the corresponding handle."
    };

    internal static JsonObject Input()
    {
        JsonObject Branch(JsonArray modes, bool picked, bool intersection)
        {
            var properties = new JsonObject
            {
                ["handle"] = Handle(), ["expected_curve_token"] = Token(), ["snap"] = new JsonObject { ["enum"] = modes }
            };
            if (picked)
            {
                properties["reference_point_wcs"] = new JsonObject
                {
                    ["type"] = "array", ["minItems"] = 3, ["maxItems"] = 3, ["items"] = new JsonObject { ["type"] = "number" }
                };
                properties["tolerance"] = new JsonObject { ["type"] = "number", ["exclusiveMinimum"] = 0 };
            }
            if (intersection)
            {
                properties["other_handle"] = Handle();
                properties["other_expected_curve_token"] = Token();
            }
            return new JsonObject
            {
                ["type"] = "object", ["additionalProperties"] = false, ["properties"] = properties,
                ["required"] = new JsonArray(properties.Select(p => (JsonNode?)JsonValue.Create(p.Key)).ToArray())
            };
        }
        var intersection = Branch(new JsonArray("intersection"), true, true);
        var fields = intersection["properties"]!.DeepClone().AsObject();
        fields["snap"] = new JsonObject { ["enum"] = new JsonArray("start", "end", "mid", "center", "nearest", "intersection") };
        return new JsonObject
        {
            ["type"] = "object", ["properties"] = fields,
            ["oneOf"] = new JsonArray(Branch(new JsonArray("start", "end", "mid", "center"), false, false),
                Branch(new JsonArray("nearest"), true, false), intersection),
            ["description"] = "Resolves a current native Line, Arc, Circle, Polyline, Ellipse or Spline in the requested active space. Start and end are stored endpoints, mid is at half native curve length, center requires Arc/Circle/Ellipse. Nearest uses the finite native closest point. Intersection uses two distinct finite native operands and requires one unique hit within tolerance. Pick and result use actual WCS XYZ, and tolerance is in drawing millimeters. Resolution is a one-time coordinate observation."
        };
    }
}
