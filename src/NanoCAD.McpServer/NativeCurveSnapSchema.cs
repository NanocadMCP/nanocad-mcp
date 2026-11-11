using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class NativeCurveSnapSchema
{
    private static JsonObject Text(string pattern) => new() { ["type"] = "string", ["pattern"] = pattern };

    internal static JsonObject Input()
    {
        var fields = new JsonObject
        {
            ["handle"] = Text("^[0-9A-Fa-f]{1,16}$"),
            ["expected_curve_token"] = Text("^[0-9A-Fa-f]{64}$"),
            ["snap"] = new JsonObject { ["enum"] = new JsonArray("start", "end", "mid", "center", "nearest", "intersection") },
            ["reference_point_wcs"] = new JsonObject
            {
                ["type"] = "array",
                ["minItems"] = 3,
                ["maxItems"] = 3,
                ["items"] = new JsonObject { ["type"] = "number" }
            },
            ["tolerance"] = new JsonObject { ["type"] = "number", ["exclusiveMinimum"] = 0 },
            ["other_handle"] = Text("^[0-9A-Fa-f]{1,16}$"),
            ["other_expected_curve_token"] = Text("^[0-9A-Fa-f]{64}$")
        };
        JsonObject When(JsonObject mode, params string[] required)
        {
            var forbidden = new JsonObject();
            foreach (string name in required) forbidden[name] = false;
            return new JsonObject
            {
                ["if"] = new JsonObject { ["properties"] = new JsonObject { ["snap"] = mode } },
                ["then"] = new JsonObject { ["required"] = new JsonArray(required.Select(name => (JsonNode?)JsonValue.Create(name)).ToArray()) },
                ["else"] = new JsonObject { ["properties"] = forbidden }
            };
        }
        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = fields,
            ["required"] = new JsonArray("handle", "expected_curve_token", "snap"),
            ["allOf"] = new JsonArray(
                When(new JsonObject { ["enum"] = new JsonArray("nearest", "intersection") }, "reference_point_wcs", "tolerance"),
                When(new JsonObject { ["const"] = "intersection" }, "other_handle", "other_expected_curve_token")),
            ["description"] = "Observe a token-checked existing native curve in the selected space. Start/end are stored endpoints, mid is half native length, center requires Arc/Circle/Ellipse. Nearest and unique finite intersection require a WCS pick and positive millimeter tolerance. This resolves a coordinate once."
        };
    }

    internal static JsonObject WithPointAlternative(JsonObject entity, string field)
    {
        var properties = entity["properties"]!.AsObject();
        properties[field + "_snap"] = Input();
        var required = entity["required"]!.AsArray();
        for (int i = required.Count - 1; i >= 0; i--)
            if (required[i]!.GetValue<string>() == field) required.RemoveAt(i);
        var constraints = entity["allOf"] as JsonArray ?? new JsonArray();
        constraints.Add(new JsonObject
        {
            ["oneOf"] = new JsonArray(
                new JsonObject { ["required"] = new JsonArray(field) },
                new JsonObject { ["required"] = new JsonArray(field + "_snap") })
        });
        if (entity["allOf"] is null) entity["allOf"] = constraints;
        return entity;
    }
}
