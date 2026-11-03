using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

public static class HatchAssociationSchema358
{
    static JsonObject Text()=>new(){["type"]="string"};
    static JsonObject Bool()=>new(){["type"]="boolean"};
    static JsonObject Array(JsonNode item)=>new(){["type"]="array",["items"]=item};
    static JsonObject Object(JsonObject properties,params string[] required)=>new(){["type"]="object",["additionalProperties"]=false,
        ["properties"]=properties,["required"]=new JsonArray(required.Select(value=>(JsonNode?)JsonValue.Create(value)).ToArray())};
    static JsonObject Handle()=>new(){["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"};
    public static JsonObject Operation()
    {
        JsonObject Group()=>new(){["type"]="array",["minItems"]=1,["uniqueItems"]=true,["items"]=Handle()};
        JsonObject Branch(string action,bool grouped=false)
        {
            var p=new JsonObject{["operation"]=new JsonObject{["const"]="set_hatch_association"},["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=Handle()},
                ["hatch_token"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{64}$"},["action"]=new JsonObject{["const"]=action}};
            var names=new List<string>{"operation","handles","hatch_token","action"};
            if(action=="rebind")
            {
                if(grouped){p["outer_boundary_handles"]=Group();p["hole_boundary_groups"]=Array(Group());names.AddRange(["outer_boundary_handles","hole_boundary_groups"]);}
                else{p["outer_boundary_handle"]=Handle();p["hole_boundary_handles"]=Array(Handle());names.AddRange(["outer_boundary_handle","hole_boundary_handles"]);}
            }
            return Object(p,names.ToArray());
        }
        return new(){["oneOf"]=new JsonArray(Branch("detach"),Branch("rebind"),Branch("rebind",true)),["description"]="Edit the existing native Hatch association lifecycle with its fresh hatch_token. detach removes only this Hatch's forward source IDs and reverse source reactors, preserving stored loops/fill and all source curves. rebind replaces its complete outer/holes. The existing outer_boundary_handle/hole_boundary_handles form uses one closed zero-width native Polyline (Line/Arc bulges), Circle or full Ellipse, or one actually closed native Spline per loop. Spline sources require WCS XY/Z0 controls; no projection/refit/chord approximation is performed. Existing inclined planes for other types remain. The outer_boundary_handles/hole_boundary_groups form supplies explicit source groups: each group is one of those closed curves or a connected closed chain of two or more native Line/Arc sources, ordered around the contour; member direction may differ. Forms cannot be mixed, and every source is distinct across all groups. Sources remain in the same requested space and retained Hatch OCS plane. Finite native curve intersections and detached Region face containment prove simple closed loops and strictly inside, separate, non-nested holes; no bbox inference or tessellation. Each native AppendLoop receives the full group of existing IDs; exact per-loop membership, roles, geometry and bidirectional links are verified. Native reversal/start offsets and equivalent NURBS basis/seam/split changes are allowed, assessed by native geometric equality/complete overlap with physical tolerance rather than identical controls/knots. Existing local shared Hatch links and unrelated readable metadata remain retained; fields, wide/annotative sources and foreign/unclassified reactors require their adapter. Model or active ordinary Paper. Patterns/island settings stay; native resident evaluation decides the fill; gradient conversion stays outside this operation. Dedicated batch of distinct Hatches with disjoint touched sources; actual bytes/cancellation bound admission. Started recovery/Undo is not guaranteed; unknown remains unresolved."};
    }
    public static JsonObject Read()=>Object(new(){["candidate"]=Bool(),["scope"]=Text(),["actions"]=Array(Text()),["required_inputs"]=Array(Text()),["boundary_scope"]=Text(),["boundary_types"]=Array(Text()),["topology_verification"]=Text(),
        ["source_loops"]=Array(Object(new(){["loop_index"]=new JsonObject{["type"]="integer",["minimum"]=0},["loop_type"]=new JsonObject{["type"]="integer"},["source_handles"]=Array(Handle())},"loop_index","loop_type","source_handles"))},"candidate","scope","actions","required_inputs","boundary_scope","boundary_types","topology_verification");
    public static JsonObject Change(JsonObject entityRef)=>Object(new(){["operation_index"]=new JsonObject{["type"]="integer",["minimum"]=0},["hatch_ref"]=entityRef.DeepClone(),["action"]=Text(),["hatch_token"]=Text(),["associative"]=Bool(),
        ["previous_sources"]=Array(entityRef.DeepClone()),["current_sources"]=Array(entityRef.DeepClone()),["loop_count"]=new JsonObject{["type"]="integer",["minimum"]=1},["association_scope"]=Text(),
        ["verified_boundaries"]=Array(Object(new(){["source_ref"]=entityRef.DeepClone(),["boundary_type"]=Text(),["role"]=new JsonObject{["enum"]=new JsonArray("outer","hole")}},"source_ref","boundary_type","role")),
        ["verified_loops"]=Array(Object(new(){["boundary_group_index"]=new JsonObject{["type"]="integer",["minimum"]=0},["role"]=new JsonObject{["enum"]=new JsonArray("outer","hole")},["source_refs"]=Array(entityRef.DeepClone()),["geometry_scope"]=Text()},"boundary_group_index","role","source_refs","geometry_scope"))},
        "operation_index","hatch_ref","action","hatch_token","associative","previous_sources","current_sources","loop_count","association_scope","verified_boundaries");
}
