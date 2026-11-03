using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class RegionBooleanSchema
{
    private static JsonObject Handle()=>new(){["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"};
    private static JsonObject Point()=>new(){["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=new JsonObject{["type"]="number"}};
    private static JsonObject Object(params(string Name,JsonObject Schema)[] fields)
    {
        var properties=new JsonObject();var required=new JsonArray();
        foreach(var field in fields){properties[field.Name]=field.Schema;required.Add(field.Name);}
        return new(){["type"]="object",["additionalProperties"]=false,["properties"]=properties,["required"]=required};
    }
    internal static JsonObject Edit()=>Object(
        ("operation",new(){["type"]="string",["const"]="boolean_region"}),
        ("handles",new(){["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=Handle()}),
        ("operator",new(){["type"]="string",["enum"]=new JsonArray("union","intersection","subtract")}),
        ("tool_handles",new(){["type"]="array",["minItems"]=1,["uniqueItems"]=true,["items"]=Handle(),
            ["description"]="Ordered committed native Region operands in the same ordinary drawing space and geometric plane. Sources are read-only; native Boolean consumes only detached tool clones. The target cannot occur here."}))
        .Describe();
    private static JsonObject Describe(this JsonObject schema)
    {
        schema["description"]="Modify one existing exact native Region in place by ordered union/intersection/subtract, preserving its handle, geometric plane, native owner and stored appearance/metadata. Model or active ordinary Paper in drawing millimeters; tilted/elevated coplanar Regions are supported by their native plane, not projected to XY. Read current Region geometry/plane and use fresh bridge/document/revision references. This must be the sole operation. All tools stay read-only; holes and disconnected nonempty native results are retained without conversion to curves or manual reconstruction. Empty/invalid results are rejected on a detached target preview before resident mutation. Native B-rep validity and meaningful readback are checked; area values are observations, not an analytical result oracle. Target fields, persistent dependencies or native model ownership require their owning route. No Region count ceiling; actual serialized snapshot/transport-byte limits and cancellation apply. Undo/rollback is not guaranteed; pending/unknown is not success.";
        return schema;
    }
    internal static JsonObject Plane()=>Object(("point",Point()),("normal",Point()));
    internal static JsonObject Topology()=>Object(
        ("face_count",new(){["type"]="integer",["minimum"]=1}),
        ("boundary_loop_count",new(){["type"]="integer",["minimum"]=1}),
        ("edge_count",new(){["type"]="integer",["minimum"]=1,
            ["description"]="Native edge occurrences traversed through face boundary loops; not a persistent edge identity inventory."}),
        ("checked_valid",new(){["type"]="boolean"}));
    internal static JsonObject Read()=>Object(
        ("scope",new(){["type"]="string",["const"]="native_planar_region_brep"}),
        ("plane",Plane()),("perimeter",new(){["type"]="number",["exclusiveMinimum"]=0}),
        ("topology",Topology()));
    internal static JsonObject Change()=>Object(
        ("operation_index",new(){["type"]="integer"}),
        ("operator",new(){["type"]="string",["enum"]=new JsonArray("union","intersection","subtract")}),
        ("target",new(){["$ref"]="#/$defs/ref"}),
        ("tools",new(){["type"]="array",["items"]=new JsonObject{["$ref"]="#/$defs/ref"}}),
        ("area_before",new(){["type"]="number",["exclusiveMinimum"]=0}),
        ("area_after",new(){["type"]="number",["exclusiveMinimum"]=0}),
        ("plane",Plane()),("topology_after",Topology()));
}
