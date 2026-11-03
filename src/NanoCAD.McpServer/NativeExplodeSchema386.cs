using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class NativeExplodeSchema386
{
    static JsonObject Text()=>new(){["type"]="string"};
    static JsonObject Array(JsonNode item)=>new(){["type"]="array",["items"]=item};
    static JsonObject Object(params(string Name,JsonNode Value)[] fields)
    {
        var properties=new JsonObject();var required=new JsonArray();foreach(var field in fields){properties[field.Name]=field.Value;required.Add(field.Name);}
        return new(){["type"]="object",["additionalProperties"]=false,["properties"]=properties,["required"]=required};
    }
    internal static JsonObject Operation()
    {
        var handles=Array(new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"});handles["minItems"]=1;handles["uniqueItems"]=true;
        var result=Object(("operation",new JsonObject{["const"]="explode_native"}),("handles",handles),
            ("source_mode",new JsonObject{["type"]="string",["enum"]=new JsonArray("retain","replace")}));
        result["description"]="Native Entity.Explode for existing top-level ordinary local static attribute-free BlockReference, MInsertBlock or Region in requested Model/active ordinary Paper space. Explicit retain keeps the selected source; replace erases it after native outputs are appended. Nested static definitions remain shared and unchanged; this is a single native explosion, not recursive primitive reconstruction. The tested MInsert native factory returned NotApplicable before producing objects; MInsert explosion is not a confirmed working route on this host. Its owning placement/copy and array-parameter edits remain usable. Do not infer that admission or an inherited SDK declaration guarantees explosion. Generated native objects keep the native exploder's geometry/properties, not a promise to migrate source-owned XData/extension data or constraints. Read the selected source and block definition first. Dynamic/anonymous/Xref/other-array/annotative or attribute-bearing block graphs need their own adapter. replace requires no Fields/persistent associations/native model source dependency and intentionally retires the selected source's own data; retain is available without erasing its dependencies. Distinct sources, dedicated explode-only batch, actual response bytes rather than a count ceiling. Returned native_explode_changes maps every source to actual generated refs/types/owners/layers. No purge of definitions or source files. Undo/rollback is uncertain; inspect generated objects and one useful group view after commit. Unknown never means success or rollback.";
        return result;
    }
    internal static JsonObject Change(JsonNode reference)
    {
        // Each reference is an independent schema node; JSON nodes have one parent.
        var generated=Object(("ref",reference.DeepClone()),("runtime_type",Text()),("owner_handle",Text()),("layer",Text()));
        return Object(("operation_index",new JsonObject{["type"]="integer"}),("source",reference.DeepClone()),("source_mode",new JsonObject{["enum"]=new JsonArray("retain","replace")}),
            ("source_erased",new JsonObject{["type"]="boolean"}),("generated",Array(generated)),("warnings",Array(Text())));
    }
}
