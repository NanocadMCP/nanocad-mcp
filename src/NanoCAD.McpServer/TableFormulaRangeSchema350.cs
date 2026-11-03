using System.Text.Json.Nodes;
using NanoCAD.Contracts;

namespace NanoCAD.McpServer;

internal static class TableFormulaRangeSchema350
{
    static JsonObject Text(string description)=>new(){["type"]="string",["description"]=description};
    static JsonObject Object(JsonObject fields,params string[] required)=>new(){["type"]="object",["additionalProperties"]=false,
        ["properties"]=fields,["required"]=new JsonArray(required.Select(key=>(JsonNode?)JsonValue.Create(key)).ToArray())};
    static JsonObject Index()=>new(){["type"]="integer",["minimum"]=0,["maximum"]=int.MaxValue};
    internal static JsonObject Operation()
    {
        var range=Object(new JsonObject{["first_row"]=Index(),["last_row"]=Index(),["first_column"]=Index(),["last_column"]=Index()},
            "first_row","last_row","first_column","last_column");
        var formula=Text("Formula for the top-left destination anchor: same-table A1 arithmetic or documented SUM/AVERAGE/COUNT with comma-separated individual cells and top-left:bottom-right rectangles. $ anchors retain their row/column in relative filling. Native kernel evaluates every result.");
        var result=Object(new JsonObject{["operation"]=new JsonObject{["const"]=TableFormulaRangeValidation350.Operation},
            ["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"}},
            ["table_token"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-F]{64}$"},["range"]=range,["formula"]=formula,
            ["relative_references"]=new JsonObject{["type"]="boolean",["description"]="true shifts unanchored references by each cell's row/column displacement from the rectangle top-left; false writes the same exact expression in every selected anchor."},
            ["unit_type"]=new JsonObject{["type"]="string",["enum"]=new JsonArray(TableValueEditingValidation336.UnitTypes.Select(item=>(JsonNode?)JsonValue.Create(item)).ToArray())},
            ["data_format"]=Text("Optional native result format; omission retains each cell's current stored format.")},
            "operation","handles","table_token","range","formula","relative_references","unit_type");
        result["description"]="Fill one existing native Table rectangle with stored formulas in Model or active ordinary Paper. Select only complete editable anchors; covered merged cells are refused. Fresh complete table_token, native identity/owners/metadata, untouched inputs/formulas/appearance/topology and local cycles remain checked. Aggregates ignore ordinary empty or nonnumeric String cells; arithmetic references require numeric scalars/formulas. No external links, fields, named references, topology changes or spreadsheet evaluator. Result Double units are explicit and not converted. Native cache/layout can reflow; inspect actual stored expressions, values and readability. Recovery and Undo are not guaranteed.";return result;
    }
    internal static JsonObject Read()=>Object(new JsonObject{["operations"]=new JsonObject{["type"]="array",["items"]=new JsonObject{["type"]="string"}},
        ["aggregate_functions"]=new JsonObject{["type"]="array",["items"]=new JsonObject{["type"]="string"}},
        ["range_scope"]=Text("Native supported same-table syntax."),["relative_fill"]=Text("Explicit reference shift semantics."),
        ["evaluated_values"]=Text("Actual native evaluation observation boundary.")},"operations","aggregate_functions","range_scope","relative_fill","evaluated_values");
}
