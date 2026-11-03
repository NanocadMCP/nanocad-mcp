using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class BlockTemplateSchema391
{
    internal static JsonObject Reference()
    {
        var input=JsonNode.Parse(BlockWriteSchema.Insert().GetRawText())!.AsObject();
        var properties=input["properties"]!.AsObject();properties.Remove("operation");
        properties["type"]=new JsonObject{["type"]="string",["const"]="BlockReference"};
        properties["layer"]=new JsonObject{["type"]="string",["const"]="0",["default"]="0"};
        properties["position"]!["description"]="Position in the new parent definition's local XY millimeters; the existing child definition's base_point is handled by native BlockTransform.";
        input["required"]=new JsonArray("type","definition_handle","position");
        input["description"]="Ordinary nested static symbol using an already existing native Millimeters definition. Positive uniform scale(default1), radians rotation(default0), visible variable root attributes from attribute_values or their actual definition defaults. Preserve the child graph and all its existing occurrence values. Dependencies cannot refer to handles created in this request; no dynamic/xref/Field/custom/associative graph or assembly mates.";
        return input;
    }
}
