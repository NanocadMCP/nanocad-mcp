using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class SolidImprintSchema393
{
    internal const string Scope="Imprint retained native curves onto one existing direct Model Space Solid3d in their actual WCS positions. Use inspect_solid_topology for the target token and inspect_objects for each source curve_token. Exact native Line/Arc/Circle/Polyline/Ellipse/Spline/Helix curves are passed as independent native copies; original identities, geometry, appearance and observed root data remain. No projection, coordinate rebuilding, source erasure or persistent curve/feature association. The native kernel decides contact and applicability. Imprint marks topology and does not itself prescribe a volume change or guarantee a new selectable region. Returned before/after tokens and face/edge counts are observations, not a success oracle. Inspect the resulting part, then reacquire current topology and choose actual faces before local extrusion/presspull. Enabled solid history, fields and unknown target dependencies use their owning adapter; healthy existing drawing documentation follows its existing refresh route. This is the sole operation in its request. Started failures have unknown outcomes and no guaranteed rollback/Undo; inspect them before another write.";
    private static JsonObject Text()=>new(){["type"]="string"};
    private static JsonObject Integer()=>new(){["type"]="integer",["minimum"]=0};
    private static JsonObject Array(JsonObject item)=>new(){["type"]="array",["items"]=item};
    private static JsonObject Object(params (string Name,JsonObject Shape)[] fields)
    {
        var props=new JsonObject();foreach(var field in fields)props[field.Name]=field.Shape;
        return new(){["type"]="object",["properties"]=props,["required"]=new JsonArray(fields.Select(f=>(JsonNode?)JsonValue.Create(f.Name)).ToArray()),["additionalProperties"]=false};
    }
    private static JsonObject Token(){var value=Text();value["pattern"]="^[0-9A-F]{64}$";return value;}
    private static JsonObject Handle(){var value=Text();value["pattern"]="^[0-9A-Fa-f]{1,16}$";return value;}
    internal static JsonObject Operation()
    {
        var handles=Array(Handle());handles["minItems"]=1;handles["maxItems"]=1;
        var sources=Array(Object(("handle",Handle()),("curve_token",Token())));sources["minItems"]=1;
        sources["description"]="Distinct retained top-level current Model Space curves, already placed at their intended contact with the body. Use each actual current native-curve snapshot token; tokens and source identities are checked again before native mutation.";
        var result=Object(("operation",new(){["const"]="imprint_solid"}),("handles",handles),("topology_token",Token()),("source_curves",sources));
        result["description"]=Scope;return result;
    }
    internal static JsonObject Change(JsonObject reference)
    {
        JsonObject Topology()=>Object(("snapshot_token",Token()),("faces",Integer()),("edges",Integer()));
        var result=Object(("operation_index",Integer()),("target",reference),
            ("sources",Array(Object(("source",reference.DeepClone().AsObject()),("curve_token",Token())))),
            ("before",Topology()),("after",Topology()),("topology_token_changed",new(){["type"]="boolean"}),
            ("native_calls_completed",Integer()),("observation_scope",Text()));
        result["description"]="Actual native calls on the retained body and topology observed before and after them. No created model feature, durable face identity or prescribed topology/volume delta is claimed. The current after snapshot is observed again following commit and any documentation refresh; use a fresh inspect_solid_topology before choosing a later face.";return result;
    }
}