using System.Text.Json.Nodes;
using NanoCAD.Contracts;

namespace NanoCAD.McpServer;

internal static class TableFormulaSchema343
{
    private static JsonObject Object(JsonObject properties,params string[] required)=>new(){["type"]="object",["additionalProperties"]=false,
        ["properties"]=properties,["required"]=new JsonArray(required.Select(value=>(JsonNode?)JsonValue.Create(value)).ToArray())};
    internal static JsonObject Operation()
    {
        JsonObject Index()=>new(){["type"]="integer",["minimum"]=0,["maximum"]=int.MaxValue};
        var cell=Object(new JsonObject{
            ["row"]=Index(),["column"]=Index(),
            ["formula"]=new JsonObject{["type"]="string",["description"]="Native same-table A1 arithmetic using + - * / parentheses and finite literals, plus SUM/AVERAGE/COUNT with comma-separated individual cells and inclusive top-left:bottom-right rectangles. Optional leading = and $ anchors. A1 denotes native row0,column0. References must address visible merged anchors. Arithmetic leaves require numeric scalars/formulas; native aggregates ignore ordinary empty/String cells. No external/named references, nested aggregate arguments, fields or spreadsheet evaluator are inferred. The native kernel evaluates feasibility and values."},
            ["unit_type"]=new JsonObject{["type"]="string",["enum"]=new JsonArray(TableValueEditingValidation336.UnitTypes.Select(value=>(JsonNode?)JsonValue.Create(value)).ToArray()),["description"]="Explicit native result unit for Double storage, without implicit unit conversion."},
            ["data_format"]=new JsonObject{["type"]="string",["description"]="Optional native data format; omission preserves its stored format. Empty requests the native default and any resolved stored normalization is reported."}},"row","column","formula","unit_type");
        var operation=Object(new JsonObject{
            ["operation"]=new JsonObject{["type"]="string",["const"]=TableFormulaValidation343.Operation},
            ["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"}},
            ["table_token"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-F]{64}$",["description"]="Fresh geometry.editing.table_token, including stored formula and native evaluated value. Refresh after native reevaluation."},
            ["cells"]=new JsonObject{["type"]="array",["minItems"]=1,["items"]=cell}},"operation","handles","table_token","cells");
        operation["description"]="Set native formulas in selected anchors of one exact existing Table in Model or active ordinary Paper. Empty anchors acquire one native value; scalar anchors intentionally become formula contents. Each result is native Double with explicit unit_type; stored formula and native_value/display are read separately. The native table evaluates every result and dependent cell; no external evaluator or guessed value is substituted. Complete selected local reference closure, actual native range bounds and cycles are checked before writing. fill_table_formula_range fills a rectangle using explicit relative/repeated references while retaining $ row/column anchors. Native normalization of formula text retains arithmetic/reference association and is disclosed. Unselected expressions, independent raw values, units, formats, explicit cell/grid overrides, merged ranges, placement and owned metadata remain checked; evaluated caches and minimum layout sizes may reflow with observations/warnings. External/non-formula external/non-formula fields, data links, blocks and multiple contents require their adapters. Row/column and merge changes on formula tables remain outside this route. Ordinary scalar replacement, cell appearance/layout/grid and compatible TableStyle assignment can retain other formulas. One operation per request. Native rollback/Undo is not guaranteed; inspect the resulting table.";
        return operation;
    }
}
