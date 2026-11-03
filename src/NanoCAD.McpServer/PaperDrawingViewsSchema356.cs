using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class PaperDrawingViewsSchema356
{
    private static JsonObject Text()=>new(){["type"]="string"};
    private static JsonObject Bool()=>new(){["type"]="boolean"};
    private static JsonObject Array(JsonObject item)=>new(){["type"]="array",["items"]=item};
    private static JsonObject Nullable(JsonObject item)=>new(){["anyOf"]=new JsonArray(item,new JsonObject{["type"]="null"})};
    private static JsonObject Object(params(string Key,JsonObject Schema)[] fields)
    {
        var properties=new JsonObject();var required=new JsonArray();
        foreach(var field in fields){properties[field.Key]=field.Schema;required.Add(field.Key);}
        return new(){["type"]="object",["additionalProperties"]=false,["properties"]=properties,["required"]=required};
    }
    internal static JsonObject Creation(JsonObject reference)=>Object(("view_ref",(JsonObject)reference.DeepClone()),("layout_ref",(JsonObject)reference.DeepClone()),
        ("native_document_id",Text()),("view_token",Nullable(Text())),("view_token_unavailable_reason",Nullable(Text())),("source_refs",Array((JsonObject)reference.DeepClone())),("top_source_ids",Array(Text())),
        ("parent_view_id",Nullable(Text())),("root_view_id",Text()),("child_view_ids",Array(Text())),("projection_observation",Text()));
    internal static JsonObject Sources()=>Object(("top_source_ids",Array(Text())),("complex_sources",Nullable(Array(Object(
        ("object_ids",Array(Text())),("top_id",Text()),("leaf_id",Text()),("target_id",Text()),("simple",Bool()),("xref",Bool()),("subentity",Bool()))))),
        ("complex_unavailable_reason",Nullable(Text())),("sliced_source_ids",Nullable(Array(Text()))),("native_owner_document_id",Text()));
    internal static JsonObject SelectedSources(JsonObject reference)=>Object(("view_ref",(JsonObject)reference.DeepClone()),("identity",Sources()),("section_source_selection",Nullable(SectionSourceSelectionSchema357.Read())));
}
