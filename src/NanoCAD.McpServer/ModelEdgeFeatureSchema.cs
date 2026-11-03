using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class ModelEdgeFeatureSchema
{
    internal static JsonObject Operation(string kind)
    {
        var size=kind=="add_model_fillet"?"radius":"distance";
        return new JsonObject
        {
            ["type"]="object",["additionalProperties"]=false,
            ["required"]=new JsonArray("operation","handles","expected_last_item_id","topology_token","edges",size),
            ["properties"]=new JsonObject
            {
                ["operation"]=new JsonObject{["const"]=kind},
                ["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"}},
                ["expected_last_item_id"]=new JsonObject{["type"]="string",["pattern"]="^\\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\\}$"},
                ["topology_token"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{64}$"},
                ["edges"]=new JsonObject{["type"]="array",["minItems"]=1,["uniqueItems"]=true,
                    ["items"]=new JsonObject{["type"]="string",["pattern"]="^[1-9][0-9]{0,18}$"}},
                [size]=new JsonObject{["type"]="number",["minimum"]=.001,["maximum"]=1e6}
            },
            ["description"]="Append a native parametric "+(kind=="add_model_fillet"?"FilletFeature":"ChamferFeature")+" to one existing Mc3dSolid. Supply current last_item_id from inspect_model_history plus B-rep edge IDs and snapshot_token from inspect_solid_topology. Inside the write, current MultiCAD straight edges are matched by unique endpoint and length geometry; full circular edges by center, radius and normal independently of the seam vertex. Ambiguous or missing mapping is refused. The feature belongs to the model and exposes its own native radius/distance variable. Supported terminal associative views are preserved by inserting before their history chain. Sole operation in the request. Select a nonempty set of unique current edges; complete request and native verification inventories use actual byte budgets. Size must fit each edge. Native rebuild, feature ownership/status, edge binding and nonempty subtractive body are checked. ROLLBACK_NOT_GUARANTEED: a started failure may leave partial history; inspect before further writes. Reacquire body handle/history/topology and verify useful visual checkpoints."
        };
    }
}
