using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class DrawingGroupSchema431
{
    static JsonObject Text(int max=255,int min=1)=>new(){["type"]="string",["minLength"]=min,["maxLength"]=max};
    static JsonObject Handles()=>new(){["type"]="array",["uniqueItems"]=true,["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"}};
    internal static JsonObject[] Input()=>new[]{"create_group","set_group","dissolve_group"}.Select(action=>
    {
        var fields=new JsonObject{["operation"]=new JsonObject{["type"]="string",["const"]=action}};
        var required=new JsonArray("operation");
        if(action=="create_group")
        {fields["name"]=Text();var handles=Handles();handles["minItems"]=1;fields["handles"]=handles;required.Add("name");required.Add("handles");}
        else
        {fields["handle"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"};fields["expected_name"]=Text();fields["expected_group_token"]=new JsonObject{["type"]="string",["pattern"]="^[A-Fa-f0-9]{64}$"};required.Add("handle");required.Add("expected_name");required.Add("expected_group_token");}
        if(action!="dissolve_group")
        {fields["description"]=Text(4096,0);fields["selectable"]=new JsonObject{["type"]="boolean"};}
        if(action=="set_group"){fields["name"]=Text();fields["add_handles"]=Handles();fields["remove_handles"]=Handles();}
        return new JsonObject{["type"]="object",["additionalProperties"]=false,["properties"]=fields,["required"]=required,
            ["description"]=action=="dissolve_group"?"Remove the native named Group only, retaining every member entity. Read get_structure(section=groups) for current identity and token; Undo is not guaranteed.":"Create or sparsely revise a native named Group. Members retain their original identity, geometry and ownership; grouping is selection organization, not a block, pattern or geometric constraint. Read groups for current membership/token. Added entities must be top-level in the requested Model/Paper space. Separate group-only batches; Undo is not guaranteed."};
    }).ToArray();
}
