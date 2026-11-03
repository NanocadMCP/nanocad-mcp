using System.Text.Json.Nodes;
using NanoCAD.Contracts;

namespace NanoCAD.McpServer;

internal static class TableCellLayoutSchema340
{
    private static JsonObject Number(double? minimum=null)=>new(){["type"]="number",["maximum"]=1e9,["minimum"]=minimum??-1e9};
    private static JsonObject Object(JsonObject properties,params string[] required)=>new(){["type"]="object",["additionalProperties"]=false,["properties"]=properties,["required"]=new JsonArray(required.Select(v=>(JsonNode?)JsonValue.Create(v)).ToArray())};
    internal static JsonObject Operation()
    {
        var margins=Object(new JsonObject{["top"]=Number(0),["left"]=Number(0),["bottom"]=Number(0),["right"]=Number(0)});margins["minProperties"]=1;
        margins["description"]="Stored native margins in drawing millimeters; each requested side is independent. Unrequested explicit overrides remain. Native minimum sizes/layout can reflow; derived bounds are observed rather than an equality oracle.";
        var scale=Number(0);scale.Remove("minimum");scale["exclusiveMinimum"]=0;
        var properties=Object(new JsonObject{["margins"]=margins,["rotation"]=Number(),["scale"]=scale,["auto_scale"]=new JsonObject{["type"]="boolean"},
            ["cell_style"]=new JsonObject{["type"]="string",["minLength"]=1,["description"]="Exact existing native cell-style name in the current TableStyle. Use get_structure table_styles properties.table_style.cell_style_names; no TableStyle reassignment or new style creation. Explicit native cell/content/grid overrides remain; inherited effective defaults may follow this cell style."},
            ["data_format"]=new JsonObject{["type"]="string",["description"]="Native scalar data-format string; no value/type/unit conversion. Empty requests native default, whose resolved nonempty stored string may be reported with a warning."}});
        properties["minProperties"]=1;
        properties["allOf"]=new JsonArray(new JsonObject{["if"]=new JsonObject{["required"]=new JsonArray("scale")},["then"]=new JsonObject{["required"]=new JsonArray("auto_scale"),["properties"]=new JsonObject{["auto_scale"]=new JsonObject{["const"]=false}}}});
        JsonObject Index()=>new(){["type"]="integer",["minimum"]=0,["maximum"]=int.MaxValue};
        var cell=Object(new JsonObject{["row"]=Index(),["column"]=Index(),["properties"]=properties},"row","column","properties");
        var result=Object(new JsonObject{["operation"]=new JsonObject{["type"]="string",["const"]=TableCellLayoutValidation340.Operation},
            ["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"}},
            ["table_token"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-F]{64}$"},["cells"]=new JsonObject{["type"]="array",["minItems"]=1,["items"]=cell}},"operation","handles","table_token","cells");
        result["description"]="Edit native margins, cell-style defaults and scalar data format in one exact existing Table in Model or active ordinary Paper, retaining its native identity/owner/plane and exact raw scalar contents. Read geometry.editing first and use its fresh complete table_token. Select merged cells by their top-left anchors. Rotation is native radians; content scale is dimensionless and strictly positive with explicit auto_scale:false; rotation/scale/auto_scale require an existing single scalar. Margins/style/data-format also support empty anchors without creating contents. One operation per request. Retained single-content formulas are supported; their stored expressions remain and actual native results/reflow are observed. No external/non-formula fields, external data links, blocks or multiple contents. Native font coverage is checked for changed base fonts; inline or unavailable coverage requires visual review. Native regeneration may legitimately reflow table minimum sizes and automatic content scale. No replacement primitives or override clearing; native rollback/Undo is not guaranteed.";
        return result;
    }
    internal static JsonObject ReadContentLayout()=>Object(new JsonObject{["rotation"]=new JsonObject{["type"]="number"},["scale"]=new JsonObject{["type"]="number"},["auto_scale"]=new JsonObject{["type"]="boolean"}},"rotation","scale","auto_scale");
}
