using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class SolidReplacementSchema409
{
    internal static JsonObject Operation() => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["properties"] = new JsonObject
        {
            ["operation"] = new JsonObject { ["const"] = "replace_solid" },
            ["handles"] = new JsonObject
            {
                ["type"] = "array",
                ["minItems"] = 1,
                ["maxItems"] = 1,
                ["items"] = new JsonObject { ["type"] = "string", ["pattern"] = "^[0-9A-Fa-f]{1,16}$" }
            },
            ["source_handle"] = new JsonObject { ["type"] = "string", ["pattern"] = "^[0-9A-Fa-f]{1,16}$" }
        },
        ["required"] = new JsonArray("operation", "handles", "source_handle"),
        ["description"] = "Replace one direct native Solid3d with a native copy of source_handle, retaining the target root handle, owner, display, XData and extension dictionary. Native identity transfer and owning object cleanup must be verified before success. Both bodies must be top-level Model Space direct bodies. Replacement source must be a plain direct shape without Fields, application data, extension dictionary or documentation; it remains unchanged. A retained target AssocManager has only its resident anchor and readable owner/data/reactor identities observed; its private dependency network is not completely serialized or evaluated. Healthy target-owned drawing views/sections are refreshed once and rechecked after command completion. Face/edge identities can change: reacquire topology and do not claim feature-history replacement or retained face constraints. This must be the sole operation. Native rollback/Undo is not guaranteed; inspect any started unresolved result."
    };
}
