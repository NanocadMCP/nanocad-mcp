using System.Text.Json.Nodes;
using NanoCAD.Contracts;

namespace NanoCAD.McpServer;

internal static class NativeStyleLifecycleSchema352
{
    static JsonObject Text()=>new(){["type"]="string"};
    static JsonObject Handle()=>new(){["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"};
    static JsonObject Token()=>new(){["type"]="string",["pattern"]="^[0-9A-F]{64}$"};
    static JsonObject Nullable(JsonObject item)=>new(){["anyOf"]=new JsonArray(item,new JsonObject{["type"]="null"})};
    static JsonObject Object(JsonObject fields,params string[] required)=>new(){["type"]="object",["additionalProperties"]=false,
        ["properties"]=fields,["required"]=new JsonArray(required.Select(key=>(JsonNode?)JsonValue.Create(key)).ToArray())};
    internal static IEnumerable<JsonObject> Operations()
    {
        foreach(string operation in NativeStyleLifecycleValidation352.Operations)
        {
            bool current=operation.StartsWith("set_current_",StringComparison.Ordinal);
            var fields=new JsonObject{["operation"]=new JsonObject{["const"]=operation},["handle"]=Handle(),
                ["expected_name"]=new JsonObject{["type"]="string",["minLength"]=1,["maxLength"]=255},["style_token"]=Token()};
            fields[current?"expected_current_handle":"name"]=current?Handle():new JsonObject{["type"]="string",["minLength"]=1,["maxLength"]=255};
            var result=Object(fields,fields.Select(field=>field.Key).ToArray());
            result["description"]="One ordinary local exact native TableStyle/MLeaderStyle in its actual owning dictionary, from Model or Paper. Read lifecycle.style_token and current_style_handle from get_structure table_styles/mleader_styles. "+
                (current?"Select this native record as the drawing's current style; exact expected_current_handle protects the previous selection. This does not assign or regenerate existing entities or promise to override an explicit style selected for future creation. ":
                    "Rename the shared native record under a distinct unused ordinary name through its actual owning dictionary (TableStyle) or native Name setter (MLeaderStyle). Existing consumers retain the same style ID, root/display/readable data and typed content/layout where observable; no clone, reassignment or defaults edit. ")+
                "Observed stored defaults/templates/owned metadata, dictionary membership and ordinary local native consumer identity/data remain checked after Commit/disposal. Field, block-content, annotation-context and richer ordinary content does not require a child-edit adapter for these identity operations. Opaque native/field-derived internals/layout and existing dynamic-column/override-mask boundaries are disclosed. External/custom consumers and unclassified style dependencies still need their owning route. Consumer layers need no unlocking for this identity/default-selection edit. The drawing's other current selections remain. One operation per request. Native rollback/Undo is not guaranteed; unknown is not success.";
            yield return result;
        }
    }
    internal static JsonObject Read()=>Object(new JsonObject{["candidate"]=new JsonObject{["type"]="boolean"},
        ["operations"]=new JsonObject{["type"]="array",["items"]=Text()},["style_token"]=Nullable(Token()),
        ["current_style_handle"]=Nullable(Handle()),["scope"]=Text(),["unavailable_reason"]=Text()},
        "candidate","operations","style_token","current_style_handle","scope");
}
