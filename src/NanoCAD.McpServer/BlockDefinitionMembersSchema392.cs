using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class BlockDefinitionMembersSchema392
{
    static JsonObject Text()=>new(){["type"]="string"};
    static JsonObject Handle()=>new(){["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$",["minLength"]=1,["maxLength"]=16};
    static JsonObject Token()=>new(){["type"]="string",["pattern"]="^[0-9A-F]{64}$"};
    static JsonObject Object(params(string Key,JsonObject Value)[] fields)
    {var properties=new JsonObject();var required=new JsonArray();foreach(var f in fields){properties[f.Key]=f.Value;required.Add(f.Key);}return new(){["type"]="object",["additionalProperties"]=false,["properties"]=properties,["required"]=required};}
    internal static JsonElement Input()
    {
        var define=JsonNode.Parse(BlockWriteSchema.Define().GetRawText())!.AsObject();
        var added=JsonNode.Parse(define["properties"]!["entities"]!.ToJsonString())!.AsObject();added.Remove("minItems");
        var result=Object(("operation",new JsonObject{["type"]="string",["const"]="set_block_definition_members"}),
            ("definition_handle",Handle()),("expected_definition_token",Token()));
        result["properties"]!["add_entities"]=added;
        result["properties"]!["remove_handles"]=new JsonObject{["type"]="array",["uniqueItems"]=true,["items"]=Handle()};
        result["anyOf"]=new JsonArray(new JsonObject{["required"]=new JsonArray("add_entities"),["properties"]=new JsonObject{["add_entities"]=new JsonObject{["minItems"]=1}}},
            new JsonObject{["required"]=new JsonArray("remove_handles"),["properties"]=new JsonObject{["remove_handles"]=new JsonObject{["minItems"]=1}}});
        result["description"]="Change one ordinary local shared static definition in-place, using inspect_block.definition_membership's fresh token and actual direct member handles. Append native curve/Text/MText/independent Hatch/existing nested static BlockReference specs from define_block; new members use layer0/ByLayer and definition-local XY millimeters. Dependencies must already exist and must not introduce a nesting cycle. Retire explicitly selected independent ordinary direct members with native Erase; a retired nested occurrence's owned attributes retire with it, while its shared child definition remains. AttributeDefinition additions/removals and attribute synchronization require their owning lifecycle; existing definition defaults and all retained occurrence values remain. The definition, retained native members and every actual Model/Paper/nested consumer keep their identities and placement. No redefine, explode, flatten or occurrence replacement. Read eligibility for each removal and can_add/add_unavailable_reason. Additions require a Millimeters drawing and Unitless/Millimeters target; retirement alone does not convert drawing units. Selected members and actual consumer layers must be writable. Proven Field/reactor/native-model links, dynamic/annotative/custom/external graphs remain protected. One operation in a separate request; at least one addition/removal, no artificial member-count ceiling. Receipt children identifies added members/nested attributes, retired members/owned attributes and retained consumers notified for redraw. Refresh inspection and assess the useful shared symbol/titleblock view. Undo/rollback is not guaranteed.";
        return JsonSerializer.SerializeToElement(result);
    }
    internal static JsonObject Read(JsonObject reference)
    {
        JsonObject Ref()=>JsonNode.Parse(reference.ToJsonString())!.AsObject();
        JsonObject Nullable(JsonObject value)=>new(){["anyOf"]=new JsonArray(value,new JsonObject{["type"]="null"})};
        var row=Object(("ref",Ref()),("type",Text()),("can_remove",new JsonObject{["type"]="boolean"}),("unavailable_reason",Nullable(Text())));
        return Object(("definition_ref",Ref()),("definition_name",Text()),("coordinate_system",new JsonObject{["const"]="parent_definition"}),
            ("definition_token",Nullable(Token())),("can_edit",new JsonObject{["type"]="boolean"}),("unavailable_reason",Nullable(Text())),
            ("can_add",new JsonObject{["type"]="boolean"}),("add_unavailable_reason",Nullable(Text())),
            ("members",new JsonObject{["type"]="array",["items"]=row}),("referencing_insertions",new JsonObject{["type"]="array",["items"]=Ref()}));
    }
}
