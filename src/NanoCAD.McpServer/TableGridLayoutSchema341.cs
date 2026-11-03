using System.Text.Json.Nodes;
using NanoCAD.Contracts;

namespace NanoCAD.McpServer;

internal static class TableGridLayoutSchema341
{
    private static JsonObject Object(JsonObject properties,params string[] required)=>new(){["type"]="object",["additionalProperties"]=false,["properties"]=properties,["required"]=new JsonArray(required.Select(value=>(JsonNode?)JsonValue.Create(value)).ToArray())};
    internal static JsonObject Operation()
    {
        var grid=Object(new JsonObject{
            ["line"]=new JsonObject{["type"]="string",["enum"]=new JsonArray(TableGridLayoutValidation341.Lines.Select(value=>(JsonNode?)JsonValue.Create(value)).ToArray())},
            ["line_style"]=new JsonObject{["type"]="string",["enum"]=new JsonArray("Single","Double")},
            ["double_spacing"]=new JsonObject{["type"]="number",["minimum"]=0,["maximum"]=1e9,["description"]="Stored native separation of double grid lines in drawing millimeters. Zero is accepted as native input; feasibility is decided by the native setter. In Single style this remains a dormant stored setting."}},"line");
        grid["anyOf"]=new JsonArray(new JsonObject{["required"]=new JsonArray("line_style")},new JsonObject{["required"]=new JsonArray("double_spacing")});
        JsonObject Index()=>new(){["type"]="integer",["minimum"]=0,["maximum"]=int.MaxValue};
        var cell=Object(new JsonObject{["row"]=Index(),["column"]=Index(),["grids"]=new JsonObject{["type"]="array",["minItems"]=1,["items"]=grid}},"row","column","grids");
        var result=Object(new JsonObject{
            ["operation"]=new JsonObject{["type"]="string",["const"]=TableGridLayoutValidation341.Operation},
            ["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"}},
            ["table_token"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-F]{64}$"},
            ["cells"]=new JsonObject{["type"]="array",["minItems"]=1,["items"]=cell}},"operation","handles","table_token","cells");
        result["description"]="Edit native Single/Double grid line style and stored double-line spacing on selected anchors of one existing Table in Model or active ordinary Paper. Read geometry.editing.table_token and resolve appearance_id in appearance_formats for current native grid fields. Use distinct single grid slots; select merged ranges by their top-left anchor. Conflicting requests for the same physical adjacent border fail before writing. Native shared-border normalization is allowed only for corresponding requested grid fields/masks; other grid appearance, raw scalar content, explicit overrides, table identity/plane and metadata remain checked. HorizontalInside/VerticalInside address native stored inside-grid slots; no visible interior line is promised for a single cell or merged range. Empty and single String/Long/Double scalar anchors, including retained formatted strings, are supported without content creation. Retained single-content formulas keep their stored expressions while native caches/reflow are observed. External/non-formula external/non-formula fields, data links, blocks and multiple contents need their owning adapter. No font/content/style reassignment. One operation per request. Native minimum sizes and automatic content scale can reflow; inspect the actual table. Native rollback/Undo is not guaranteed.";
        return result;
    }
}
