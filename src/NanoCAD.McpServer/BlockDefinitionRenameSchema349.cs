using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class BlockDefinitionRenameSchema349
{
    internal static JsonElement Input()
    {
        JsonObject Text(int max)=>new(){["type"]="string",["minLength"]=1,["maxLength"]=max};
        var handle=Text(16);handle["pattern"]="^[0-9A-Fa-f]{1,16}$";
        var result=new JsonObject{["type"]="object",["additionalProperties"]=false,
            ["properties"]=new JsonObject{["operation"]=new JsonObject{["type"]="string",["const"]="rename_block_definition"},
                ["handle"]=handle,["expected_name"]=Text(255),["name"]=Text(255)},
            ["required"]=new JsonArray("operation","handle","expected_name","name"),
            ["description"]="Rename one ordinary local static shared block definition, using its handle and exact name from get_structure blocks. Choose an unused name ignoring case; no case-only rename or overwrite. This changes the shared name seen by every insertion, using only the native Name setter; it does not redefine content or reassign insertions/attributes. Actual native definition/member/reference identities, owners, appearance and readable stored state are retained; typed geometry is additionally checked where an adapter is available. Field-derived layout and opaque native content/owned internals are disclosed as unobserved rather than requiring child-edit support. Stored application data/external text containing the old name are not rewritten. Target dynamic/anonymous/xref/dependent definitions are protected. One operation in a separate request, allowed from Model or Paper without switching layouts. Recovery/Undo is not guaranteed."};
        return JsonSerializer.SerializeToElement(result);
    }
}
