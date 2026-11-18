using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class ModelEdgeFeatureSchema
{
    internal static JsonObject Operation(string kind)
    {
        var size = kind == "add_model_fillet" ? "radius" : "distance";
        var modelId = new JsonObject { ["type"] = "string", ["pattern"] = @"^\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}$" };
        var token = new JsonObject { ["type"] = "string", ["pattern"] = "^[0-9A-Fa-f]{64}$" };
        static JsonObject Selection(string first, string second, string otherFirst, string otherSecond) => new()
        {
            ["required"] = new JsonArray(first, second),
            ["not"] = new JsonObject
            {
                ["anyOf"] = new JsonArray(
                new JsonObject { ["required"] = new JsonArray(otherFirst) },
                new JsonObject { ["required"] = new JsonArray(otherSecond) })
            }
        };
        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("operation", "handles", "expected_last_item_id", size),
            ["oneOf"] = new JsonArray(Selection("model_edge_ids", "edge_token", "edges", "topology_token"), Selection("edges", "topology_token", "model_edge_ids", "edge_token")),
            ["properties"] = new JsonObject
            {
                ["operation"] = new JsonObject { ["const"] = kind },
                ["handles"] = new JsonObject { ["type"] = "array", ["minItems"] = 1, ["maxItems"] = 1, ["items"] = new JsonObject { ["type"] = "string", ["pattern"] = "^[0-9A-Fa-f]{1,16}$" } },
                ["expected_last_item_id"] = modelId.DeepClone(),
                ["topology_token"] = token.DeepClone(),
                ["edge_token"] = token,
                ["model_edge_ids"] = new JsonObject { ["type"] = "array", ["minItems"] = 1, ["uniqueItems"] = true, ["items"] = modelId },
                ["edges"] = new JsonObject
                {
                    ["type"] = "array",
                    ["minItems"] = 1,
                    ["uniqueItems"] = true,
                    ["items"] = new JsonObject { ["type"] = "string", ["pattern"] = "^[1-9][0-9]{0,18}$" }
                },
                [size] = new JsonObject { ["type"] = "number", ["minimum"] = .001, ["maximum"] = 1e6 }
            },
            ["description"] = "Append a native parametric " + (kind == "add_model_fillet" ? "FilletFeature" : "ChamferFeature") + " to one existing Mc3dSolid. Select model_edge_ids from inspect_solid_topology(edge_query) with the same edge_token and last_item_id. The bridge checks each native model edge on the selected body before editing. The existing B-rep edges plus topology_token route supports straight edges and full circles with a unique native match. The feature belongs to the model and exposes its own native radius/distance variable. Supported terminal associative views are preserved by inserting before their history chain. Sole operation in the request. Select a nonempty set of unique current edges; complete request and native verification inventories use actual byte budgets. Size must fit each edge. The bridge checks the rebuilt solid, feature owner and status, actual selected edges and size variable. ROLLBACK_NOT_GUARANTEED: a started failure may leave partial history; inspect before further writes. Reacquire body handle/history/topology and verify useful visual checkpoints."
        };
    }
}
