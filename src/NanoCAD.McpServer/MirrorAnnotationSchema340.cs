using System.Text.Json.Nodes;
namespace NanoCAD.McpServer;

public static class MirrorAnnotationSchema340
{
    public static JsonObject Operation()
    {
        JsonObject Point()=>new(){["type"]="array",["minItems"]=2,["maxItems"]=2,["items"]=new JsonObject{["type"]="number",["minimum"]=-1e9,["maximum"]=1e9}};
        return new(){["type"]="object",["additionalProperties"]=false,
            ["required"]=new JsonArray("operation","handles","axis_start","axis_end","copy"),
            ["properties"]=new JsonObject{["operation"]=new JsonObject{["const"]="mirror"},
                ["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["uniqueItems"]=true,["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"}},
                ["axis_start"]=Point(),["axis_end"]=Point(),["copy"]=new JsonObject{["type"]="boolean"}},
            ["description"]="Mirror about the vertical WCS plane through two distinct XY axis points, preserving Z. Existing independent planar curves retain their route. Exact ordinary native Table, MLeader, seven Dimension types, nonassociative Hatch, Region and Helix use native definition reflection; copy=true retains the source and reports exact copies, false edits in place. Model or active ordinary Paper owner only. Stored content/style/format and ordinary owned metadata are retained; native world XData follows the reflection. Fields, external reactors, annotative contexts, linked Table cells and associated Hatch boundaries need their owning adapter. Native text orientation/automatic landing follow the object; this does not emulate command-level MIRRTEXT readability. No guaranteed recovery/Undo. Standalone Text/MText and BlockReference mirror are outside this route; bodies use transform_solid mirror."};
    }
}
