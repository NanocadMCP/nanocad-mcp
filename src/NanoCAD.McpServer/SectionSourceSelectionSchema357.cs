using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class SectionSourceSelectionSchema357
{
    static JsonObject Text()=>new(){["type"]="string"};
    static JsonObject Array(JsonObject value)=>new(){["type"]="array",["items"]=value};
    static JsonObject Nullable(JsonObject value)=>new(){["anyOf"]=new JsonArray(value,new JsonObject{["type"]="null"})};
    static JsonObject Object(params(string Name,JsonObject Value)[] fields)
    {
        var properties=new JsonObject();var required=new JsonArray();
        foreach(var field in fields){properties[field.Name]=field.Value;required.Add(field.Name);}
        return new(){["type"]="object",["additionalProperties"]=false,["properties"]=properties,["required"]=required};
    }
    internal static JsonObject Read()=>Object(("section_id",Text()),("section_handle",Text()),("settings_handle",Text()),("settings_owner_handle",Nullable(Text())),
        ("current_section_type",Text()),("modes",Array(Object(("kind",Text()),("generation_flags",new JsonObject{["type"]="integer"}),
            ("generation_options",Text()),("source_handles",Array(Text()))))),("scope",Text()));
}
