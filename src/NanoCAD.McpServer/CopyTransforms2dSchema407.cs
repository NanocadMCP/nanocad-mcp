using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class CopyTransforms2dSchema407
{
    static JsonObject Number()=>new(){["type"]="number",["minimum"]=-1e9,["maximum"]=1e9};
    static JsonObject Point()=>new(){["type"]="array",["minItems"]=2,["maxItems"]=2,["items"]=Number()};
    static JsonObject Branch(string operation,JsonObject additional,params string[] names)
    {
        var properties=new JsonObject{["operation"]=new JsonObject{["const"]=operation},
            ["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["uniqueItems"]=true,["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"}}};
        foreach(var pair in additional)properties[pair.Key]=pair.Value!.DeepClone();
        var required=new JsonArray("operation","handles");foreach(string name in names)required.Add(name);
        return new(){["type"]="object",["additionalProperties"]=false,["properties"]=properties,["required"]=required};
    }
    internal static IEnumerable<JsonObject> Operations()
    {
        var copy=Branch("copy_transform_2d",new(){["center"]=Point(),["translation"]=Point(),["rotation"]=Number(),
            ["scale"]=new JsonObject{["type"]="number",["minimum"]=1e-9,["maximum"]=1e9,["default"]=1}});
        copy["description"]="Create native transformed copies of selected independent WCS XY curves, ordinary text or supported static block/MInsert insertions; sources remain. Apply positive uniform scale about center, then rotation about center in radians, then XY translation. Defaults center/translation [0,0], rotation0, scale1. Copies preserve supported source ownership/content and reuse block definitions. This is placement, not an associative array or constraint. Compatible ordered batches are allowed; read returned new handles before later requests use them.";
        yield return copy;
        var polar=Branch("polar_repeat",new(){["center"]=Point(),["count"]=new JsonObject{["type"]="integer",["minimum"]=1,["maximum"]=int.MaxValue},
            ["angle_step"]=Number(),["rotate_items"]=new JsonObject{["type"]="boolean",["default"]=true},["base_point"]=Point()},"center","count","angle_step");
        polar["allOf"]=new JsonArray(new JsonObject{["if"]=new JsonObject{["required"]=new JsonArray("rotate_items"),["properties"]=new JsonObject{["rotate_items"]=new JsonObject{["const"]=false}}},["then"]=new JsonObject{["required"]=new JsonArray("base_point")}});
        polar["description"]="Create native circular repetition of a selected independent2D group. count includes the retained original seed; angle_step is radians, for a full ring normally2pi/count. rotate_items=true rotates whole source geometry around center; false retains orientation and requires the group's explicit base_point to calculate each displacement. Output states associative=false: these are native copies, not an AssocArray network. Prefer a supported retained pattern when that future-change behavior is required. Actual output bytes/cancellation govern batch capacity; no fixed object-count ceiling.";
        yield return polar;
    }
    internal static JsonObject Result(JsonObject reference)=>new(){["type"]="object",["additionalProperties"]=false,
        ["properties"]=new JsonObject{["operation_index"]=new JsonObject{["type"]="integer"},["operation"]=new JsonObject{["type"]="string"},
            ["associative"]=new JsonObject{["const"]=false},["instance_count_including_seed"]=new JsonObject{["type"]="integer",["minimum"]=1},
            ["source_refs"]=new JsonObject{["type"]="array",["items"]=reference.DeepClone()},["created_refs"]=new JsonObject{["type"]="array",["items"]=reference.DeepClone()}},
        ["required"]=new JsonArray("operation_index","operation","associative","instance_count_including_seed","source_refs","created_refs")};
}
