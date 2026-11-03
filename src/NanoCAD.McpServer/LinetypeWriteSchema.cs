using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

public static class LinetypeWriteSchema
{
    public static JsonElement Create() => JsonSerializer.SerializeToElement(new JsonObject
    {
        ["type"] = "object", ["additionalProperties"] = false,
        ["properties"] = new JsonObject
        {
            ["operation"] = new JsonObject { ["const"] = "create_linetype" },
            ["name"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 255 },
            ["description"] = new JsonObject { ["type"] = "string", ["maxLength"] = 1024 },
            ["pattern"] = new JsonObject { ["type"] = "array", ["minItems"] = 2, ["maxItems"] = NanoCAD.Contracts.LinetypeWriteValidation.MaxElements,
                ["items"] = new JsonObject { ["type"] = "number", ["minimum"] = -1e6, ["maximum"] = 1e6 },
                ["description"] = "Even number of alternating nonnegative dash/dot and negative gap lengths in mm. Zero denotes a dot. Nonzero magnitudes >=1e-9; sum of magnitudes <=1e6. Runtime verifies alternation and total; no normalization." }
        },
        ["required"] = new JsonArray("operation", "name", "description", "pattern"),
        ["description"] = "Create a new ordinary simple linetype. no ByLayer/ByBlock/Continuous, overwrite, external LIN/SHX, text/shape elements or current-type change. Use the new name in a subsequent committed Layer/Line request."
    });
}
