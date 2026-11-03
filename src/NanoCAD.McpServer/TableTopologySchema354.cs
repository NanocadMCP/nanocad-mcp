using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class TableTopologySchema354
{
    static JsonObject Text()=>new(){["type"]="string"};
    static JsonObject Integer()=>new(){["type"]="integer",["minimum"]=0,["maximum"]=int.MaxValue};
    static JsonObject Array(JsonNode item)=>new(){["type"]="array",["items"]=item};
    static JsonObject Object(params (string Name,JsonNode Schema)[] fields)
    {var p=new JsonObject();var r=new JsonArray();foreach(var item in fields){p[item.Name]=item.Schema;r.Add(item.Name);}return new(){["type"]="object",["additionalProperties"]=false,["properties"]=p,["required"]=r};}
    internal static JsonObject Replacements()
    {
        var value=Array(Object(("row",Integer()),("column",Integer()),("formula",Text())));value["minItems"]=1;
        value["description"]="Optional explicit replacement for an existing retained formula selected by BEFORE-operation row/column. Formula uses AFTER-operation Table coordinates. Required if deleting an individual referenced cell or an entire referenced aggregate range; no invented #REF or dropped term. Can replace an unsupported stored same-table formula explicitly. Deleted formula cells cannot receive replacements. Existing datatype/unit/format/layout remain.";return value;
    }
    internal static JsonObject Workflow()=>Object(("operations",Array(Text())),("structural_references",Text()),("inserted_ranges",Text()),
        ("removed_references",Text()),("formula_replacements",Text()),("inserted_values",Text()),("row_default_type_observation",Text()));
    internal static JsonObject Change(JsonNode reference)
    {
        var scalar=new JsonObject{["anyOf"]=new JsonArray(new JsonObject{["type"]="number"},new JsonObject{["type"]="null"})};
        var formula=Object(("before_row",Integer()),("before_column",Integer()),("row",Integer()),("column",Integer()),("action",Text()),
            ("before_formula",Text()),("planned_formula",Text()),("formula",Text()),("data_type",Text()),("unit_type",Text()),("native_value",scalar),
            ("evaluation_unavailable_reason",new JsonObject{["type"]=new JsonArray("string","null")}));
        return Object(("operation_index",Integer()),("source",reference.DeepClone()),("operation",Text()),("index",Integer()),("count",Integer()),
            ("before_rows",Integer()),("before_columns",Integer()),("rows",Integer()),("columns",Integer()),
            ("table_token",new JsonObject{["type"]="string",["pattern"]="^[0-9A-F]{64}$"}),("formulas",Array(formula)),("reference_policy",Text()),("evaluated_values",Text()));
    }
}
