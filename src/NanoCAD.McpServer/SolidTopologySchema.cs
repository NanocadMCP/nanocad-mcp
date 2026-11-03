using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class SolidTopologySchema
{
    internal static JsonElement Input()
    {
        var schema=JsonNode.Parse(ModelVariablesSchema.Input().GetRawText())!.AsObject();
        schema["properties"]!["include_native_model_faces"]=new JsonObject{
            ["type"]="boolean",["default"]=false,
            ["description"]="Also read current MultiCAD face IDs and planar/cylindrical support data for a native Mc3dSolid. These identities differ from B-rep native_id and must be reacquired after rebuild."};
        schema["properties"]!["include_native_model_edges"]=new JsonObject{["type"]="boolean",["default"]=false,["description"]="Also read current MultiCAD model edge IDs, geometry and edge_token for feature bindings. Distinct from B-rep IDs; reacquire after rebuild."};
        return JsonSerializer.SerializeToElement(schema);
    }
}
