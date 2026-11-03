using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class DrawingViewsSchema
{
    public static JsonElement Input()
    {
        var schema=JsonNode.Parse(ModelVariablesSchema.Input().GetRawText())!.AsObject();
        var properties=schema["properties"]!.AsObject();var handle=properties["handle"]!.DeepClone();
        properties.Remove("handle");properties["layout_handle"]=handle;
        properties["geometry_view_handle"]=handle.DeepClone();
        properties["source_identity_view_handle"]=handle.DeepClone();
        properties["source_identity_view_handle"]!["description"]="Optional exact current view in this layout. Read native simple sources and complete complex insertion chains separately, plus sliced-body IDs and stored native source-generation settings for a Section view, without regeneration or traversal. Stored settings and actual projection participation are separate observations. Default omits this inventory.";
        properties["geometry_start_index"]=new JsonObject{["type"]="integer",["minimum"]=0,["maximum"]=int.MaxValue,["default"]=0};
        properties["geometry_page_size"]=new JsonObject{["type"]="integer",["minimum"]=1,["maximum"]=int.MaxValue,["default"]=100};
        schema["dependentRequired"]=new JsonObject{["geometry_start_index"]=new JsonArray("geometry_view_handle"),["geometry_page_size"]=new JsonArray("geometry_view_handle")};
        schema["required"]=new JsonArray("bridge_instance_id","document_id","expected_revision","layout_handle");
        return JsonSerializer.SerializeToElement(schema);
    }
}
