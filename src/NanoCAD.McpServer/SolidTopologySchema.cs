using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class SolidTopologySchema
{
    internal static JsonElement Input()
    {
        var schema = JsonNode.Parse(ModelVariablesSchema.Input().GetRawText())!.AsObject();
        schema["properties"]!["include_native_model_faces"] = new JsonObject
        {
            ["type"] = "boolean",
            ["default"] = false,
            ["description"] = "Also read current MultiCAD face IDs and planar/cylindrical support data for a native Mc3dSolid. These identities differ from B-rep native_id and must be reacquired after rebuild."
        };
        schema["properties"]!["include_native_model_edges"] = new JsonObject { ["type"] = "boolean", ["default"] = false, ["description"] = "Also read current MultiCAD model edge IDs, geometry and edge_token for feature bindings. Distinct from B-rep IDs; reacquire after rebuild." };
        schema["properties"]!["edge_query"] = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("boundary_of_face_id"),
            ["description"] = "Read the verified native boundary edges of one current model_face_id for this body. Omit curve for all types. Reuse edge_token and model_edge_id for supported native features. Continue with next_index at the same revision. A circle is a full ring.",
            ["properties"] = new JsonObject
            {
                ["boundary_of_face_id"] = new JsonObject { ["type"] = "string", ["minLength"] = 1 },
                ["curve"] = new JsonObject { ["enum"] = new JsonArray("line", "circle", "circular_arc") },
                ["start_index"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0 },
                ["page_size"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1 }
            }
        };
        JsonObject Triple() => new() { ["type"] = "array", ["minItems"] = 3, ["maxItems"] = 3, ["items"] = new JsonObject { ["type"] = "number" } };
        JsonObject Minimum(double value) => new() { ["type"] = "number", ["minimum"] = value };
        schema["properties"]!["face_query"] = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("surface"),
            ["description"] = "Find current native planar or cylindrical supports. Direction matches either axis sign. Datum distance is perpendicular to the infinite plane or axis in drawing units. Supply datum_point and max_distance together. adjacent_to_face_id selects faces sharing a native boundary edge. Advance start_index with the same handle, revision and filters. Returns current feature face IDs and token.",
            ["properties"] = new JsonObject
            {
                ["surface"] = new JsonObject { ["enum"] = new JsonArray("plane", "cylinder") },
                ["direction"] = Triple(),
                ["angle_tolerance_degrees"] = new JsonObject { ["type"] = "number", ["minimum"] = 0, ["maximum"] = 90 },
                ["datum_point"] = Triple(),
                ["max_distance"] = Minimum(0),
                ["min_radius"] = Minimum(0),
                ["max_radius"] = Minimum(0),
                ["adjacent_to_face_id"] = new JsonObject { ["type"] = "string", ["minLength"] = 1 },
                ["start_index"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0 },
                ["page_size"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1 }
            }
        };
        return JsonSerializer.SerializeToElement(schema);
    }
}
