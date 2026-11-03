using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class MLeaderTopologySchema431
{
    static JsonObject Index()=>new(){["type"]="integer",["minimum"]=0,["maximum"]=int.MaxValue};
    static JsonObject Point()=>new(){["type"]="array",["minItems"]=3,["maxItems"]=3,["prefixItems"]=new JsonArray(new JsonObject{["type"]="number",["minimum"]=-1e6,["maximum"]=1e6},new JsonObject{["type"]="number",["minimum"]=-1e6,["maximum"]=1e6},new JsonObject{["const"]=0}),["items"]=false};
    static JsonObject Action(string action)
    {
        var p=new JsonObject{["action"]=new JsonObject{["const"]=action}};var required=new JsonArray("action");
        if(action=="add_line")
        {p["leader_index"]=new JsonObject{["anyOf"]=new JsonArray(Index(),new JsonObject{["type"]="null"}),["description"]="Existing branch index, or null/omitted to create a new native branch."};p["vertices_wcs"]=new JsonObject{["type"]="array",["minItems"]=2,["items"]=Point()};required.Add("vertices_wcs");}
        else if(action=="remove_branch"){p["leader_index"]=Index();required.Add("leader_index");}
        else{p["leader_line_index"]=Index();required.Add("leader_line_index");if(action!="remove_line"){p["vertex_index"]=Index();required.Add("vertex_index");if(action=="insert_vertex"){p["point_wcs"]=Point();required.Add("point_wcs");}}}
        return new(){["type"]="object",["additionalProperties"]=false,["properties"]=p,["required"]=required};
    }
    internal static JsonObject Operation()=>new(){["type"]="object",["additionalProperties"]=false,["properties"]=new JsonObject{
        ["operation"]=new JsonObject{["const"]="edit_mleader_topology"},["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"}},
        ["expected_geometry_token"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{64}$"},["edits"]=new JsonObject{["type"]="array",["minItems"]=1,["items"]=new JsonObject{["oneOf"]=new JsonArray(Action("add_line"),Action("remove_line"),Action("remove_branch"),Action("insert_vertex"),Action("remove_vertex"))}}},
        ["required"]=new JsonArray("operation","handles","expected_geometry_token","edits"),["description"]="Edit native branch/line/vertex topology of one existing ordinary straight XY MText-content MLeader; retain its real object, raw content, style and owned data. Read complete geometry.leader_editing.geometry_token and actual native indices first. Edits are ordered; existing indices refer to the current state at that point. add_line accepts arrow-first ordered vertices and an existing leader_index, or creates a new branch when omitted/null; inspect the resulting actual IDs before referring to the new line in another request. insert_vertex inserts before vertex_index (count appends); remove_vertex removes the actual indexed point. Native line IDs are retained for vertex edits. At least one line and two vertices per surviving line are necessary; erase removes the complete callout. Native text/dogleg/landing may reflow; inspect actual result. No artificial branch/vertex-count ceiling. Sole operation; Undo/recovery is not guaranteed and does not require extra consent."};
}
