using System.Text.Json.Nodes;
using NanoCAD.Contracts;

namespace NanoCAD.McpServer;

internal static class TableValueEditingSchema336
{
    private static JsonObject Text()=>new(){["type"]="string"};
    private static JsonObject Enum(params string[] values)=>new(){["type"]="string",["enum"]=new JsonArray(values.Select(v=>(JsonNode?)JsonValue.Create(v)).ToArray())};
    private static JsonObject Handle()=>new(){["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"};
    private static JsonObject Object(params (string Name,JsonObject Schema)[] fields)
    {
        var properties=new JsonObject();foreach(var field in fields)properties[field.Name]=field.Schema;
        return new(){["type"]="object",["additionalProperties"]=false,["properties"]=properties,["required"]=new JsonArray(fields.Select(f=>(JsonNode?)JsonValue.Create(f.Name)).ToArray())};
    }
    internal static IEnumerable<JsonObject> Operations()
    {
        foreach(string operation in TableValueEditingValidation336.Operations)
        {
            var handles=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=Handle()};
            var token=new JsonObject{["type"]="string",["pattern"]="^[0-9A-F]{64}$",["description"]="Fresh complete inspect_objects geometry.editing.table_token; includes raw values/types/units, stored format, merge/cell state, sizes, style and resident identity. Reacquire after each native edit."};
            JsonObject result;
            if(operation=="set_table_style")result=Object(("operation",new JsonObject{["type"]="string",["const"]=operation}),("handles",handles),("table_token",token),("table_style_handle",Handle()));
            else
            {
                JsonObject Index()=>new(){["type"]="integer",["minimum"]=0,["maximum"]=int.MaxValue};
                var format=Text();format["description"]="Native scalar data-format string. Empty requests the native empty/default format; a resolved nonempty stored default is reported with a warning and remains readable. Nonempty explicit format must retain the requested stored string. The resident SDK resolves syntax; this is not a formula, display text or an implicit unit conversion.";
                var cell=Object(("row",Index()),("column",Index()),("data_type",Enum(TableValueEditingValidation336.DataTypes)),("unit_type",Enum(TableValueEditingValidation336.UnitTypes)),("value",new JsonObject{["anyOf"]=new JsonArray(new JsonObject{["type"]="number"},Text())}),("data_format",format));
                cell["allOf"]=new JsonArray(
                    new JsonObject{["if"]=new JsonObject{["properties"]=new JsonObject{["data_type"]=new JsonObject{["const"]="Long"}}},["then"]=new JsonObject{["properties"]=new JsonObject{["value"]=new JsonObject{["type"]="integer",["minimum"]=int.MinValue,["maximum"]=int.MaxValue}}}},
                    new JsonObject{["if"]=new JsonObject{["properties"]=new JsonObject{["data_type"]=new JsonObject{["const"]="Double"}}},["then"]=new JsonObject{["properties"]=new JsonObject{["value"]=new JsonObject{["type"]="number"}}}},
                    new JsonObject{["if"]=new JsonObject{["properties"]=new JsonObject{["data_type"]=new JsonObject{["const"]="String"}}},["then"]=new JsonObject{["properties"]=new JsonObject{["value"]=new JsonObject{["type"]="string",["maxLength"]=4096},["unit_type"]=new JsonObject{["const"]="Unitless"}}}});
                result=Object(("operation",new JsonObject{["type"]="string",["const"]=operation}),("handles",handles),("table_token",token),("cells",new JsonObject{["type"]="array",["minItems"]=1,["items"]=cell}));
            }
            result["description"]="Existing exact native Table in Model or active ordinary Paper, one operation per request, with fresh complete table_token. Supported content is zero or one native scalar String/Long/Double or retained formula per anchor, without fields/links/multiple content. Setting a scalar explicitly replaces only the selected old formula; unselected stored expressions remain, while dependent native results/text may recalculate. Use set_table_cell_formula to change a supported expression. No table reconstruction or geometry replacement. Existing plane, owner, row/column topology, merge ranges and unselected raw contents remain. Native GenerateLayout can change observed row heights/column widths when content or inherited style needs space; this is reported with a warning and current sizes stay readable. Inspect actual readability/spacing; use resize_table for an explicit size change. Native recovery/Undo is not guaranteed; unknown is not success. "+(operation=="set_table_style"?
                "Assign an existing ordinary local native TableStyle from get_structure table_styles, can_reference=true, with compatible inherited native cell-style names. Existing explicit table/cell/content/grid overrides remain; inherited effective fonts, colors, margins and grids may follow the new style. Visible text glyph coverage is checked for changed inherited fonts; retained inline font runs or unavailable font coverage produce a warning requiring visual review, not a claim of verified glyph coverage. Known missing glyphs in the changed ordinary base font remain an error. No override clearing or forced old-default overrides; raw values/types/units, merge ranges and placement remain.":
                "Set typed native scalar literals with explicitly declared scalar native unit and exact native format. Long is signed Int32; Double is finite. Plain String/Unitless values let labels in an existing numeric table remain editable, with the same4096-character literal policy. Values are passed unchanged in native storage conventions, with no assumed degree/percentage/currency conversion. Read raw native_value/data_type/unit_type separately from displayed text. A selected empty anchor creates one value; other empty cells stay empty. Select merged cells by their top-left anchor. Format/content protection remains enforced.");
            yield return result;
        }
    }
}
