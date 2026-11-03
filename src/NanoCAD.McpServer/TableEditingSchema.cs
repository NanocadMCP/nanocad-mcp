using System.Text.Json.Nodes;
using NanoCAD.Contracts;

namespace NanoCAD.McpServer;

internal static class TableEditingSchema
{
    private static JsonObject Text()=>new(){["type"]="string"};
    private static JsonObject Integer()=>new(){["type"]="integer"};
    private static JsonObject Number()=>new(){["type"]="number"};
    private static JsonObject Boolean()=>new(){["type"]="boolean"};
    private static JsonObject Nullable(JsonObject schema)=>new(){["anyOf"]=new JsonArray(schema,new JsonObject{["type"]="null"})};
    private static JsonObject Array(JsonObject item)=>new(){["type"]="array",["items"]=item};
    private static JsonObject Object(params (string Name,JsonObject Schema)[] fields)
    {
        var properties=new JsonObject();var required=new JsonArray();foreach(var field in fields){properties[field.Name]=field.Schema;required.Add(field.Name);}
        return new(){["type"]="object",["additionalProperties"]=false,["properties"]=properties,["required"]=required};
    }
    private static JsonObject Handle()=>new(){["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"};
    private static JsonObject Index(bool positive=false)=>new(){["type"]="integer",["minimum"]=positive?1:0,["maximum"]=int.MaxValue};
    private static JsonObject Size()=>new(){["type"]="number",["minimum"]=0.001,["maximum"]=1e6};
    private static JsonObject Range()=>Object(("top_row",Index()),("left_column",Index()),("bottom_row",Index()),("right_column",Index()));
    private static JsonObject Aci()=>new(){["type"]="integer",["minimum"]=0,["maximum"]=256,
        ["description"]="Native ACI:0 ByBlock,256 ByLayer. Explicit assignment replaces prior true color."};
    private static JsonObject Rgb()=>new(){["type"]="array",["minItems"]=3,["maxItems"]=3,
        ["items"]=new JsonObject{["type"]="integer",["minimum"]=0,["maximum"]=255},["description"]="Native true color [red,green,blue]. Choose color_rgb or color_aci."};
    private static JsonObject NoTwoColors()=>new(){["required"]=new JsonArray("color_aci","color_rgb")};
    private static JsonObject Background()=>new(){["anyOf"]=new JsonArray(
        Object(("mode",new JsonObject{["type"]="string",["const"]="none"})),
        Object(("mode",new JsonObject{["type"]="string",["const"]="color"}),("color_aci",Aci())),
        Object(("mode",new JsonObject{["type"]="string",["const"]="color"}),("color_rgb",Rgb()))),
        ["description"]="None disables the native cell fill; its dormant stored color may normalize. Color enables an explicitly selected native ACI or RGB background."};
    private static JsonObject Grids()
    {
        var grid=new JsonObject{["type"]="object",["additionalProperties"]=false,["minProperties"]=2,["required"]=new JsonArray("line"),["not"]=NoTwoColors(),
            ["properties"]=new JsonObject{
                ["line"]=new JsonObject{["type"]="string",["enum"]=new JsonArray(TableEditingValidation.GridLines.Select(v=>(JsonNode?)JsonValue.Create(v)).ToArray())},
                ["visible"]=Boolean(),["color_aci"]=Aci(),["color_rgb"]=Rgb(),
                ["lineweight_hundredth_mm"]=new JsonObject{["type"]="integer",["enum"]=new JsonArray(ModifyValidation.Lineweights.Select(v=>(JsonNode?)JsonValue.Create(v)).ToArray()),
                    ["description"]="Native lineweight in hundredths of a millimeter; -1 ByLayer, -2 ByBlock, -3 Default."},
                ["linetype_handle"]=Handle()}};
        var grids=Array(grid);grids["minItems"]=1;
        grids["description"]="Distinct single native grid-line slots. Set any requested visibility/color/lineweight/linetype fields; unrequested fields remain stored. Native shared borders can be reflected in the exactly adjoining opposite side. Linetype must be an existing local record from get_structure linetypes; combined grid masks are not accepted.";
        return grids;
    }
    internal static IEnumerable<JsonObject> Operations()
    {
        foreach(var operation in TableEditingValidation.Operations)
        {
            var handles=Array(Handle());handles["minItems"]=1;handles["maxItems"]=1;
            var properties=new JsonObject{["operation"]=new JsonObject{["type"]="string",["const"]=operation},["handles"]=handles,
                ["table_token"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-F]{64}$",["description"]="Exact current inspect_objects geometry.editing.table_token. The token covers this native table's dimensions, literal contents, merged ranges, stored formatting and root identity; reacquire after any edit."}};
            bool insert=operation.StartsWith("insert_",StringComparison.Ordinal),delete=operation.StartsWith("delete_",StringComparison.Ordinal);
            if(insert||delete)
            {
                properties["index"]=Index();properties["count"]=Index(true);
                properties["formula_replacements"]=TableTopologySchema354.Replacements();
                if(insert)
                {
                    properties[operation.EndsWith("_rows",StringComparison.Ordinal)?"height":"width"]=Size();
                    var literal=Text();literal["maxLength"]=4096;
                    var row=Array(literal);row["minItems"]=1;var values=Array(row);values["minItems"]=1;
                    values["description"]="Literal contents for every inserted cell, as a rectangular row-major matrix. Rows insertion requires count rows and current column count; columns insertion requires current row count and count columns. Use empty strings for blank cells. No fields, formulas, CAD formatting or control codes.";properties["values"]=values;
                }
            }
            else if(operation=="set_table_cell_text")
            {
                var literal=Text();literal["maxLength"]=4096;
                var cells=Array(Object(("row",Index()),("column",Index()),("text",literal)));cells["minItems"]=1;properties["cells"]=cells;
            }
            else if(operation=="set_table_cell_format")
            {
                var format=new JsonObject{["type"]="object",["additionalProperties"]=false,["minProperties"]=1,["not"]=NoTwoColors(),["properties"]=new JsonObject{
                    ["alignment"]=new JsonObject{["type"]="string",["enum"]=new JsonArray(TableEditingValidation.Alignments.Select(a=>(JsonNode?)JsonValue.Create(a)).ToArray())},
                    ["text_height"]=Size(),["text_style_handle"]=Handle(),["color_aci"]=Aci(),["color_rgb"]=Rgb(),["background"]=Background(),["grids"]=Grids()}};
                var cells=Array(Object(("row",Index()),("column",Index()),("properties",format)));cells["minItems"]=1;properties["cells"]=cells;
            }
            else properties["range"]=Range();
            var result=new JsonObject{["type"]="object",["additionalProperties"]=false,["properties"]=properties,["required"]=new JsonArray(properties.Where(p=>p.Key!="formula_replacements").Select(p=>(JsonNode?)JsonValue.Create(p.Key)).ToArray()),
                ["description"]="Edit an existing exact native Table in Model or the active Paper sheet, retaining its entity handle/style/placement and unselected stored content/observed formatting. Read geometry.editing first. Row/column changes retain ordinary native String/Long/Double cells, formatted text and numeric formulas with their storage units, raw independent inputs, cell styles/overrides, grids, merged ranges and layout. Supported same-table arithmetic and SUM/AVERAGE/COUNT references are structurally remapped, including dollar anchors; copy/fill is a different rule. Explicit formula_replacements select retained BEFORE-operation anchors but use AFTER-operation coordinates. Removed single references or whole aggregate ranges require replacements before any write; partial ranges contract. Native Table evaluates results and its cache/display/automatic scale are observed separately. Merge and literal text lifecycle keeps the plain String/Unitless scope. Cell appearance admits supported native numeric scalars and retained formulas. Formula content uses set_table_cell_formula; unsupported/external formulas, links/fields/block/multiple contents need their owning adapter or an explicit supported replacement for a retained formula. Cell formatting preserves existing stored string formatting codes and any native plane; row/column keeps WCS XY/Z0 but retains supported existing formatted contents; merge and literal text retain plain-text scope. Legacy Table RowType datatype/unit getters are explicitly unavailable; actual per-cell types/units and all other observed defaults remain in the complete token. Native Table operations are used, never replacement line/text primitives. This operation must be the only operation in the request; inspect again for the next token. Source must be on an unlocked local visible/thawed layer. Sizes use drawing millimeters; no implicit unit conversion. " +(insert||delete?
                    "Zero-based index; insertion is before that index and may append at the current count. Deletion must leave at least one row/column. A change cannot cut a merged span; delete its full axis span or unmerge first. Existing complete merged ranges outside the changed span shift with their native cells. Inserted cells inherit native style and receive the explicit literal matrix.":
                    operation=="merge_table_cells"?"Range bounds are inclusive; at least two unmerged cells. Only the top-left anchor may contain nonempty text, preventing hidden/lost secondary content. Native merge grid representation may adapt within the requested range.":
                    operation=="unmerge_table_cells"?"Range bounds must exactly match one complete current native merged range. Hidden secondary text must be blank; anchor content/text formatting is retained; native secondary cell formatting is exposed after unmerge.":
                    operation=="set_table_cell_text"?"Replace selected literal cell text using the exact current table_token. Select a merged cell only by its top-left anchor. Existing cell formatting, sizes, native merge ranges and other content are preserved. Glyph coverage is checked before resident edits; no fields, formulas or formatting codes.":
                    "Set selected cells' alignment/text height/text style/content ACI or RGB, background fill and native grid visibility/color/lineweight/linetype. Supported String/Long/Double values retain their exact raw datatype/unit/value and unrequested content flags. Empty cells retain zero contents using native cell-level formatting. Select a merged cell only by its top-left anchor. Existing local text style handle comes from get_structure text_styles; changed fonts are checked against native visible text. Retained inline font runs or unavailable font coverage produce a warning requiring rendered-text review; known missing glyphs in a changed ordinary base font remain an error. Native grid override flags may adapt only for requested properties, and exactly adjoining shared borders may reflect the requested change. Scalar content/data-format and TableStyle assignment use their dedicated operations. Native rollback/Undo is not guaranteed; preserve the returned actual outcome.")};
            yield return result;
        }
    }
    internal static JsonObject Read()
    {
        JsonObject Color()=>Object(("method",Text()),("aci",Nullable(Integer())),("rgb",Nullable(Array(Integer()))));
        var grid=Object(("line",Text()),("visibility",Text()),("lineweight",Integer()),("linetype_handle",Nullable(Text())),
            ("line_style",Text()),("double_spacing",Number()),("overrides",Integer()),("color",Color()));
        grid["description"]="Stored native grid slots. Lineweight is in hundredths of a millimeter; overrides is the native GridProperties bit mask (the installed managed getter labels it CellProperties). Use the dedicated set_table_grid_layout branch with a fresh complete table_token for supported native Single/Double line style and double spacing; inside slots are stored defaults, not a single-cell visible-line promise.";
        var appearance=Object(("id",Integer()),("cell_style",Text()),("data_format",Text()),("background_none",Boolean()),("background_color",Color()),("margins",Array(Number())),("grids",Array(grid)));
        appearance["description"]="Lossless shared observed native background/grid formatting. Margins are [top,left,bottom,right]. Cell format.appearance_id refers to this response-local id; reacquire IDs after every inspect. No regeneration or data-link refresh occurs during reading.";
        var format=Object(("alignment",Text()),("text_height",Number()),("text_style_handle",Text()),("content_color",Color()),("appearance_id",Integer()));
        // Accepted older bridges expose the original four fields. Rich read
        // metadata is additive; the current reader always emits it, while its
        // absence must not invalidate otherwise usable stored table content.
        format["required"]=new JsonArray("alignment","text_height","text_style_handle","content_color");
        format["description"]="Observed native anchor/empty-cell text formatting plus a response-local reference to appearance_formats; the shared records prevent repeated grids from exhausting a normal table's byte budget.";
        var cell=Object(("row",Integer()),("column",Integer()),("text",Text()),("content_count",Integer()),("state",Integer()),
            ("content_editable",Boolean()),("format_editable",Boolean()),("merged_range",Nullable(Range())),("format",Nullable(format)),
            ("data_type",Nullable(Text())),("unit_type",Nullable(Text())),("native_value",new JsonObject{["anyOf"]=new JsonArray(Text(),Number(),new JsonObject{["type"]="null"})}),
            ("content_layout",Nullable(TableCellLayoutSchema340.ReadContentLayout())),
            ("formula",Nullable(Text())),("evaluation_unavailable_reason",Nullable(Text())),
            ("native_content_type",Text()),("native_formula_field",Nullable(TableFormulaFieldSchema358.Read())),("native_value_source",Nullable(Text())),("text_unavailable_reason",Nullable(Text())));
        cell["required"]=new JsonArray("row","column","text","content_count","state","content_editable","format_editable","merged_range","format");
        cell["description"]="Stored native scalar or supported same-table formula content. Optional data_type/unit_type/native_value distinguish native String/Long(Int32)/Double values from formatted display text; for a bound Field, native_value reads its existing native cache without requesting expression evaluation, and native_value_source identifies that route. Cached values do not imply fresh evaluation. text_unavailable_reason discloses a missing formatted cache; the empty placeholder is not observed literal/display text. Optional formula preserves the stored native expression. A null value with evaluation_unavailable_reason does not establish successful evaluation. Optional content_layout reports native rotation in radians, dimensionless scale and automatic scaling; empty cells have null scalar/layout fields. Values retain native storage units. Optional native_content_type and native_formula_field retain a native formula-backed local Field identity, actual ownership and readable definition/options. Private Field data/child-field internals are unobserved; formula flag/expression/reference meaning remain checked. External/non-formula fields, data links, block and multiple contents require their owning adapters.";
        var cells=Array(cell);
        var result=Object(("table_token",new JsonObject{["type"]="string",["pattern"]="^[0-9A-F]{64}$"}),("table_style_handle",Text()),("cells",cells),("appearance_formats",Array(appearance)),("formula_workflows",TableFormulaRangeSchema350.Read()),("topology_workflows",TableTopologySchema354.Workflow()),("layout_workflow",TableNativeLayoutSchema356.Read()));
        result["required"]=new JsonArray("table_token","table_style_handle","cells");
        return result;
    }
}
