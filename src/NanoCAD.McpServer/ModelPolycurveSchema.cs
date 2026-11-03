using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class ModelPolycurveSchema
{
    internal static JsonObject Variant()=>new()
    {
        ["type"]="object",["additionalProperties"]=false,["required"]=new JsonArray("kind","vertices","bulges"),
        ["properties"]=new JsonObject
        {
            ["kind"]=new JsonObject{["const"]="polycurve"},
            ["vertices"]=new JsonObject{["type"]="array",["minItems"]=2,["items"]=new JsonObject{["type"]="array",["minItems"]=2,["maxItems"]=2,["items"]=new JsonObject{["type"]="number",["minimum"]=-1e6,["maximum"]=1e6}}},
            ["bulges"]=new JsonObject{["type"]="array",["minItems"]=2,["items"]=new JsonObject{["type"]="number",["minimum"]=-1000,["maximum"]=1000}}
        },
        ["description"]="Exact closed Line/Arc profile in local plane coordinates. One outgoing bulge per vertex, including the last-to-first edge:0=line, tan(signed_sweep_radians/4)=arc; positive is counterclockwise about x_axis cross y_axis. Nonzero magnitude1e-5..1000; derived radius0.001..1000000mm. Example semicircle plus diameter: vertices[[10,0],[-10,0]],bulges[1,0]. Do not repeat the first vertex. No gaps, overlapping edges, self-intersections or holes. Arcs stay exact, not tessellated. Profiles and native sketch geometry are distinct from ordinary ACAD drawing constraints."
    };
}
