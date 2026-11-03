using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class ViewportLifecycleSchema
{
    internal static JsonObject[] Operations()
    {
        JsonObject Handle()=>new(){["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"};
        JsonObject Operation(string name)
        {
            var fields=new JsonObject{["operation"]=new JsonObject{["const"]=name},["layout_handle"]=Handle(),["handle"]=Handle()};
            var required=new JsonArray("operation","layout_handle","handle");
            if(name=="copy_viewport")
            {
                fields["center"]=new JsonObject{["type"]="array",["minItems"]=2,["maxItems"]=2,
                    ["items"]=new JsonObject{["type"]="number",["minimum"]=-1e9,["maximum"]=1e9}};
                fields["is_on"]=new JsonObject{["type"]="boolean"};required.Add("center");
            }
            return new(){["type"]="object",["additionalProperties"]=false,["properties"]=fields,["required"]=required,
                ["description"]=name=="copy_viewport"
                    ?"Copy an existing ordinary native rectangular viewport within its explicit active Paper layout to center (paper millimeters). Native DeepClone returns a distinct handle and retained-source provenance. Model camera, frame size/scale, frozen layers, locked state and appearance are preserved. Optional is_on sets the copy; omitted preserves source On. Source stays read-only. Requires Paper CVPORT1; viewport1, perspective/clipping, fields, persistent reactors, unknown extension/application data and named-view associations require their owning route. Separate copy group, bounded by actual request/result bytes. No implicit layout switch/save; Undo/rollback is not guaranteed."
                    :"Delete only the explicit existing ordinary native rectangular viewport from its active Paper layout. Model geometry and sheet entities remain; result reports the erased handle. Requires Paper CVPORT1; viewport1, perspective/clipping, fields, persistent reactors, unknown extension/application data and named-view associations are protected. Separate delete group, bounded by actual request/result bytes. Native viewport numbers may be reassigned; retained handles and settings remain. No implicit layout switch/save; Undo/rollback is not guaranteed."};
        }
        return new[]{Operation("copy_viewport"),Operation("delete_viewport")};
    }
    internal static JsonObject Output()
    {
        JsonObject Ref()=>new(){["$ref"]="#/$defs/ref"};
        return BasicModelingSchema.Obj(new(){["operation_index"]=new JsonObject{["type"]="integer"},
            ["operation"]=new JsonObject{["type"]="string",["enum"]=new JsonArray("copy_viewport","delete_viewport")},
            ["layout"]=Ref(),["source"]=Ref(),["created"]=new JsonObject{["anyOf"]=new JsonArray(Ref(),new JsonObject{["type"]="null"})},
            ["erased"]=new JsonObject{["type"]="boolean"}});
    }
}
