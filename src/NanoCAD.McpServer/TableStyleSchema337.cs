using System.Text.Json.Nodes;
using NanoCAD.Contracts;

namespace NanoCAD.McpServer;

internal static class TableStyleSchema337
{
    private static JsonObject Handle()=>new(){["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"};
    private static JsonObject Token()=>new(){["type"]="string",["pattern"]="^[0-9A-Fa-f]{64}$"};
    private static JsonObject Enum(string[] values)=>new(){["type"]="string",["enum"]=new JsonArray(values.Select(value=>(JsonNode?)JsonValue.Create(value)).ToArray())};
    private static JsonObject Length(bool zero)=>new(){["type"]="number",[zero?"minimum":"exclusiveMinimum"]=0,["maximum"]=1e6};
    private static JsonObject Rgb()=>new(){["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=new JsonObject{["type"]="integer",["minimum"]=0,["maximum"]=255}};
    internal static JsonObject RoleBackground()
    {
        var color=new JsonObject{["type"]="object",["additionalProperties"]=false,["required"]=new JsonArray("mode"),
            ["properties"]=new JsonObject{["mode"]=new JsonObject{["const"]="color"},["color_aci"]=new JsonObject{["type"]="integer",["minimum"]=0,["maximum"]=256},["color_rgb"]=Rgb()},
            ["oneOf"]=new JsonArray(new JsonObject{["required"]=new JsonArray("color_aci")},new JsonObject{["required"]=new JsonArray("color_rgb")})};
        return new(){["oneOf"]=new JsonArray(new JsonObject{["type"]="object",["additionalProperties"]=false,["properties"]=new JsonObject{["mode"]=new JsonObject{["const"]="none"}},["required"]=new JsonArray("mode")},color)};
    }
    internal static JsonObject RoleGrids()=>new(){["type"]="array",["minItems"]=1,["items"]=new JsonObject{["type"]="object",["additionalProperties"]=false,["minProperties"]=2,["required"]=new JsonArray("line"),
        ["properties"]=new JsonObject{["line"]=Enum(TableEditingValidation.GridLines),["visible"]=new JsonObject{["type"]="boolean"},["color_aci"]=new JsonObject{["type"]="integer",["minimum"]=0,["maximum"]=256},["color_rgb"]=Rgb(),
            ["lineweight_hundredth_mm"]=new JsonObject{["type"]="integer",["enum"]=new JsonArray(ModifyValidation.Lineweights.Select(value=>(JsonNode?)JsonValue.Create(value)).ToArray())}},
        ["not"]=new JsonObject{["required"]=new JsonArray("color_aci","color_rgb")}}};
    private static JsonObject Properties()
    {
        var row=new JsonObject{["type"]="object",["additionalProperties"]=false,["minProperties"]=2,["required"]=new JsonArray("row_type"),
            ["properties"]=new JsonObject{["row_type"]=Enum(TableStyleValidation337.RowTypes),["text_height"]=Length(false),["alignment"]=Enum(TableStyleValidation337.Alignments),
                ["text_style_handle"]=Handle(),["color_aci"]=new JsonObject{["type"]="integer",["minimum"]=1,["maximum"]=255},["color_rgb"]=Rgb(),["background"]=RoleBackground(),["grids"]=RoleGrids()},
            ["not"]=new JsonObject{["required"]=new JsonArray("color_aci","color_rgb")}};
        return new JsonObject{["type"]="object",["additionalProperties"]=false,["minProperties"]=1,["properties"]=new JsonObject{
            ["description"]=new JsonObject{["type"]="string",["description"]="Native Unicode description; empty string clears it. Actual request/readback bytes apply."},
            ["horizontal_margin"]=Length(true),["vertical_margin"]=Length(true),["rows"]=new JsonObject{["type"]="array",["minItems"]=1,["items"]=row}}};
    }
    internal static IEnumerable<JsonObject> Operations()
    {
        foreach(string operation in TableStyleValidation337.Operations)
        {
            bool creating=operation=="create_table_style";
            var fields=new JsonObject{["operation"]=new JsonObject{["const"]=operation,["type"]="string"},[creating?"base_style_handle":"handle"]=Handle(),
                [creating?"base_style_token":"table_style_token"]=Token(),[creating?"name":"expected_name"]=new JsonObject{["type"]="string",["minLength"]=1,["maxLength"]=255},["properties"]=Properties()};
            var required=new JsonArray("operation",creating?"base_style_handle":"handle",creating?"base_style_token":"table_style_token",creating?"name":"expected_name");
            if(!creating)required.Add("properties");
            yield return new JsonObject{["type"]="object",["additionalProperties"]=false,["properties"]=fields,["required"]=required,
                ["description"]="One ordinary local reusable native TableStyle per request, from Model or Paper. Read a fresh properties.table_style.editing.table_style_token from get_structure table_styles. "+
                (creating?"Clone an existing native style and post it under a distinct unused name; source/default identity stays. Ordinary native Table consumers can remain linked to the original style and are not refreshed or reassigned by creation. Actual borrowed source reference headers/ownership/membership are preserved without consumer edit eligibility or cell reads; opaque consumer contents are unobserved and no consumer is edited/refreshed. Source-owned extension/template/dependency graphs still require native owner remapping. ":
                    "Edit the existing style by identity. Rendered defaults inspect every referring native table before setters. Actual multiple-content cells support ordinary String/Long/Double scalar slots with exact current order/value/type/unit/raw bits/data formats/layout and per-cell/per-slot overrides retained. Field/block/formula slots within a multiple-content consumer, links and unproved critical ownership still require their owning propagation adapter. Existing single-content adapters retain their separate scopes. Native row-role and named font/height observations govern inherited defaults; no locale-name aliases or reset of explicit overrides are inferred. Description-only edits do not touch referring tables. ")+
                "Selected TitleRow/HeaderRow/DataRow defaults include dormant/suppressed roles. Text heights/margins are drawing lengths and require an actual Millimeters drawing; description/alignment/fonts/colors alone are settings. Raw table contents, merge/placement and explicit overrides stay; inherited layout may reflow. Detached cloning proves source-copy defaults, metadata and borrowed-edge isolation; requested defaults are applied/verified in the actual owning drawing. Actual resident row/global/named defaults and font coverage are checked before explicit Table layout, with bounded field diagnostics for mismatches. Existing local font identity is resolved and actual visible text checked honestly; retained inline runs/unavailable coverage produce warnings. Use the separate native lifecycle branch for rename/current selection. Selected role backgrounds and six distinct grid defaults support visibility/color/native lineweight; explicit cell/grid overrides remain. No delete, suppression, arbitrary named-cell-style lifecycle or template assignment. Native Undo/rollback is not guaranteed."};
        }
    }
    private static JsonObject Object(JsonObject fields,params string[] required)=>new(){["type"]="object",["additionalProperties"]=false,
        ["properties"]=fields,["required"]=new JsonArray(required.Select(value=>(JsonNode?)JsonValue.Create(value)).ToArray())};
    private static JsonObject Nullable(JsonObject value)=>new(){["anyOf"]=new JsonArray(value,new JsonObject{["type"]="null"})};
    internal static JsonObject ReadProperties()
    {
        var color=Object(new(){["method"]=new JsonObject{["type"]="string"},["aci"]=Nullable(new JsonObject{["type"]="integer"}),["rgb"]=Nullable(Rgb())},"method","aci","rgb");
        var grid=Object(new(){["line"]=new JsonObject{["type"]="string"},["color"]=(JsonObject)color.DeepClone(),["lineweight"]=new JsonObject{["type"]="integer"},["visible"]=new JsonObject{["type"]="boolean"}},"line","color","lineweight","visible");
        var row=Object(new(){["row_type"]=Enum(TableStyleValidation337.RowTypes),["text_height"]=new JsonObject{["type"]="number"},["alignment"]=new JsonObject{["type"]="string"},
            ["text_style_handle"]=Nullable(Handle()),["content_color"]=color,["background_none"]=new JsonObject{["type"]="boolean"},["background_color"]=(JsonObject)color.DeepClone(),["grids"]=new JsonObject{["type"]="array",["items"]=grid}},"row_type","text_height","alignment","text_style_handle","content_color","background_none","background_color","grids");
        var editing=Object(new(){["editable"]=new JsonObject{["type"]="boolean"},["table_style_token"]=Token(),["unavailable_reason"]=new JsonObject{["type"]="string"}},"editable");
        return Object(new(){["description"]=new JsonObject{["type"]="string"},["horizontal_margin"]=new JsonObject{["type"]="number"},["vertical_margin"]=new JsonObject{["type"]="number"},
            ["rows"]=new JsonObject{["type"]="array",["items"]=row},["editing"]=editing,["lifecycle"]=NativeStyleLifecycleSchema352.Read(),
            ["cell_style_names"]=new JsonObject{["type"]="array",["items"]=new JsonObject{["type"]="string"},
                ["description"]="Observed exact native named CellStyles. Cell layout may select these names only from the table's current local TableStyle; this list alone does not establish editability of the style record."}},"description","horizontal_margin","vertical_margin","rows","editing");
    }
    internal static JsonObject ChangeOutput()
    {
        JsonObject Ref()=>new(){["$ref"]="#/$defs/ref"};
        JsonObject Strings()=>new(){["type"]="array",["items"]=new JsonObject{["type"]="string"}};
        return Object(new(){["operation_index"]=new JsonObject{["type"]="integer"},["style_ref"]=Ref(),["source_ref"]=Ref(),["table_style_token"]=Token(),
            ["affected_table_count"]=new JsonObject{["type"]="integer",["minimum"]=0},["changed_named_cell_styles"]=Strings(),["warnings"]=Strings()},
            "operation_index","style_ref","source_ref","table_style_token","affected_table_count");
    }
}
