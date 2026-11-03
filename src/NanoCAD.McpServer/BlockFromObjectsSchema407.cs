using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class BlockFromObjectsSchema407
{
    internal static JsonObject Input()=>new()
    {
        ["type"]="object",["additionalProperties"]=false,
        ["properties"]=new JsonObject
        {
            ["operation"]=new JsonObject{["type"]="string",["const"]="create_block_from_objects"},
            ["name"]=new JsonObject{["type"]="string",["minLength"]=1,["maxLength"]=255},
            ["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["uniqueItems"]=true,["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"}},
            ["base_point"]=new JsonObject{["type"]="array",["minItems"]=2,["maxItems"]=2,["items"]=new JsonObject{["type"]="number",["minimum"]=-1e9,["maximum"]=1e9}}
        },
        ["required"]=new JsonArray("operation","name","handles","base_point"),
        ["description"]="Create a reusable ordinary local block from actual existing independent planar native curves, literal Text/MText, independent Hatch, or static BlockReference/MInsert occurrences in the requested active Model/Paper space. Native DeepClone preserves their real geometry/display and owned attributes, without reconstruction from entity specifications. Definition coordinates remain the original WCS XY coordinates; its origin is base_point. Creates a definition only and retains all selected originals and shared definitions. Read actual handles first. Fields, constraints, model-owned or persistent associations require their owning route; annotative/dynamic/external/custom entities are protected. Use a new block name and one separate structural request. The result identifies actual cloned members; owned attributes retain native ownership. Insert the reusable definition separately with insert_block when needed. Native user metadata cloning follows its documented adapter, without an exhaustive opaque payload guarantee. Undo/rollback is not guaranteed."
    };
}
