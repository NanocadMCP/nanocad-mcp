using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

public static class HatchBoundarySchema349
{
    static JsonObject Text()=>new(){["type"]="string"};
    static JsonObject Integer()=>new(){["type"]="integer",["minimum"]=0};
    static JsonObject Point()=>new(){["type"]="array",["minItems"]=2,["maxItems"]=2,["items"]=new JsonObject{["type"]="number",["minimum"]=-1e9,["maximum"]=1e9}};
    static JsonObject Object(JsonObject properties,params string[] required)=>new(){["type"]="object",["additionalProperties"]=false,["properties"]=properties,["required"]=new JsonArray(required.Select(value=>(JsonNode?)JsonValue.Create(value)).ToArray())};
    static JsonObject Array(JsonObject item)=>new(){["type"]="array",["items"]=item};
    static JsonObject Loop()=>new(){["oneOf"]=new JsonArray(
        Object(new(){["kind"]=new JsonObject{["const"]="polygon"},["vertices"]=new JsonObject{["type"]="array",["minItems"]=3,["items"]=Point()}},"kind","vertices"),
        Object(new(){["kind"]=new JsonObject{["const"]="circle"},["center"]=Point(),["radius"]=new JsonObject{["type"]="number",["exclusiveMinimum"]=0,["maximum"]=1e9}},"kind","center","radius"))};
    public static JsonObject Operation()
    {
        var value=Object(new(){["operation"]=new JsonObject{["const"]="set_hatch_boundary"},
            ["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"}},
            ["hatch_token"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{64}$"},["outer_loop"]=Loop(),["holes"]=Array(Loop())},
            "operation","handles","hatch_token","outer_loop","holes");
        value["description"]="Replace the closed boundary of an existing independent native Hatch, retaining its identity, stored plane/pattern/island style and ordinary root/owned metadata. Read the fresh geometry.editing.hatch_token and normal/elevation; explicit polygon/circle outer and polygon/circle holes use existing Hatch OCS XY in millimeters. Holes must lie strictly inside, without touching, overlap or nesting; empty holes removes all holes. Native circles remain exact circular bulges, never tessellated. This edits the Hatch's stored loops, creates no source curves and does not attach/detach associations; associative/annotative/gradient/field/network cases use their owning route. Model or active ordinary Paper; dedicated batch of distinct Hatches. Native resident evaluation governs the fill, without a density/line-count oracle. Retained outer/ignore island styles can hide stored holes; inspect the engineering result. Recovery/Undo not guaranteed, pending/unknown is unresolved.";
        return value;
    }
    public static JsonObject Read()=>Object(new(){["candidate"]=new JsonObject{["type"]="boolean"},["scope"]=Text(),["required_inputs"]=Array(Text()),["coordinate_system"]=Text()},"candidate","scope","required_inputs","coordinate_system");
    public static JsonObject Change(JsonObject entityRef)=>Object(new(){["operation_index"]=Integer(),["hatch_ref"]=entityRef.DeepClone(),
        ["hatch_token"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-F]{64}$"},["previous_loop_count"]=Integer(),["loop_count"]=Integer(),["hole_count"]=Integer(),["boundary_scope"]=Text(),["warnings"]=Array(Text())},
        "operation_index","hatch_ref","hatch_token","previous_loop_count","loop_count","hole_count","boundary_scope","warnings");
}
