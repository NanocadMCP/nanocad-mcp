using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class NestedAttributeSchema393
{
    static JsonObject Text()=>new(){["type"]="string"};
    static JsonObject Bool()=>new(){["type"]="boolean"};
    static JsonObject Nullable(JsonObject value)=>new(){["anyOf"]=new JsonArray(value,new JsonObject{["type"]="null"})};
    static JsonObject Array(JsonObject value)=>new(){["type"]="array",["items"]=value};
    static JsonObject Object(params(string Name,JsonObject Schema)[] fields)
    {
        var properties=new JsonObject();var required=new JsonArray();foreach(var field in fields){properties[field.Name]=field.Schema;required.Add(field.Name);}
        return new(){["type"]="object",["properties"]=properties,["required"]=required,["additionalProperties"]=false};
    }
    internal static JsonObject Read(JsonObject reference)
    {
        JsonObject Ref()=>(JsonObject)reference.DeepClone();
        var value=Object(("definition_ref",Ref()),("definition_token",Nullable(Text())),("coordinate_system",Text()),("can_edit",Bool()),("unavailable_reason",Nullable(Text())),
            ("members",Array(Object(("block_ref",Ref()),("attribute_ref",Ref()),("tag",Text()),("content_type",Text()),("contents",Text()),("can_edit",Bool()),("unavailable_reason",Nullable(Text()))))),
            ("referencing_insertions",Array(Ref())));
        value["description"]="Actual direct nested owned attribute values of this shared definition. Token covers full membership/source/consumer observations and is independent of hierarchy depth or selected attribute subset. Use owner_definition_handle and expected_definition_token in set_attribute_text; every actual Model/Paper/nested consumer shares the result. Ordinary literal variable single-line or existing literal multiline values only; no AttributeDefinition/default synchronization, kind conversion or occurrence flattening.";
        return value;
    }
}
