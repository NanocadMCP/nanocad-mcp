using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class WorkplaneSchema
{
    internal static JsonObject Input()
    {
        JsonObject Triple()=>new(){["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=new JsonObject{["type"]="number",["minimum"]=-1e6,["maximum"]=1e6}};
        return new(){["type"]="object",["additionalProperties"]=false,["properties"]=new JsonObject{["origin"]=Triple(),["x_axis"]=Triple(),["y_axis"]=Triple()},["required"]=new JsonArray("origin","x_axis","y_axis"),
            ["description"]="Explicit WCS workplane: origin in millimeters and perpendicular unit x_axis/y_axis. Planar coordinates, horizontal/vertical relations and positive extrusion direction refer to this frame (normal=x_axis cross y_axis). No normalization, UCS mutation or implicit coordinate conversion. Omit for the existing WCSXY/Z0 behavior. Supply the same frame for related source/constraint/model operations."};
    }
}
