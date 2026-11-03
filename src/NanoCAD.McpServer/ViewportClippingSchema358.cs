using System.Text.Json.Nodes;
using NanoCAD.Contracts;

namespace NanoCAD.McpServer;
internal static class ViewportClippingSchema358
{
    static JsonObject Text()=>new(){["type"]="string"};
    static JsonObject Bool()=>new(){["type"]="boolean"};
    static JsonObject Handle()=>new(){["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"};
    static JsonObject Token()=>new(){["type"]="string",["pattern"]="^[0-9A-F]{64}$"};
    static JsonObject Nullable(JsonObject item)=>new(){["anyOf"]=new JsonArray(item,new JsonObject{["type"]="null"})};
    static JsonObject Object(params(string Name,JsonObject Schema)[] fields)=>new(){["type"]="object",["additionalProperties"]=false,
        ["properties"]=new JsonObject(fields.Select(f=>KeyValuePair.Create<string,JsonNode?>(f.Name,f.Schema))),
        ["required"]=new JsonArray(fields.Select(f=>(JsonNode?)JsonValue.Create(f.Name)).ToArray())};
    internal static IEnumerable<JsonObject> Operations()
    {
        foreach(string action in new[]{"set_boundary","enable","disable","clear"})
        {
            var fields=new List<(string,JsonObject)>{("operation",new(){["const"]="set_viewport_clipping"}),
                ("layout_handle",Handle()),("handle",Handle()),("clipping_token",Token()),("action",new(){["const"]=action})};
            if(action=="set_boundary")fields.AddRange([("boundary_handle",Handle()),("boundary_curve_token",Token()),("enabled",Bool())]);
            var result=Object(fields.ToArray());result["description"]="Read get_structure section viewport_clipping for the current viewport clipping_token; read the target boundary's geometry.native_curve.curve_token from inspect_objects. Activate its exact ordinary Paper sheet outside a model viewport (CVPORT1), and use target_space paper. set_boundary binds an existing native closed planar Circle, Ellipse or Polyline in this same sheet; the boundary cannot belong to another viewport. Plan the crop against the current projected detail: its native Paper frame may change to the boundary extents, while scale and Model-to-Paper placement are retained. enable/disable retain the existing boundary identity; clear disables clipping and clears the native link while preserving the boundary entity. Reserved viewport1, perspective/front/back-clipped or foreign/unresolved links require their owning route. "+ViewportClippingValidation358.Scope+" Actual response/source bytes and cancellation govern admission. Uncertain native rollback/Undo is disclosed before writing; unknown is not success. Read fresh state and inspect the rendered detail after the related group.";
            yield return result;
        }
    }
    internal static JsonObject Read(JsonObject reference)=>Object(("viewport",JsonNode.Parse(LayoutReadSchema.Viewport().GetRawText())!.AsObject()),
        ("candidate",Bool()),("clipping_token",Nullable(Token())),("boundary_ref",Nullable(reference.DeepClone().AsObject())),
        ("boundary_curve_token",Nullable(Token())),("reverse_viewport_ref",Nullable(reference.DeepClone().AsObject())),("scope",Text()));
    internal static JsonObject ReadWithReason(JsonObject reference)
    {var result=Read(reference);result["properties"]!["unavailable_reason"]=Text();return result;}
    internal static JsonObject Change(JsonObject reference)=>Object(("operation_index",new(){["type"]="integer"}),
        ("viewport_ref",reference.DeepClone().AsObject()),("layout_ref",reference.DeepClone().AsObject()),("action",Text()),
        ("boundary_ref",Nullable(reference.DeepClone().AsObject())),("enabled",Bool()),("preservation_scope",Text()));
}
