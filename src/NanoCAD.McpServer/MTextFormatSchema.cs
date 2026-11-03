using System.Text.Json.Nodes;
namespace NanoCAD.McpServer;

internal static class MTextFormatSchema
{
    internal static JsonObject Operation()
    {
        JsonObject Handle()=>new(){["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"};
        JsonObject Positive()=>new(){["type"]="number",["exclusiveMinimum"]=0,["maximum"]=1e6};
        var properties=new JsonObject{["type"]="object",["additionalProperties"]=false,["minProperties"]=1,["properties"]=new JsonObject{
            ["text_style_handle"]=Handle(),["text_height"]=Positive(),
            ["width"]=new JsonObject{["type"]="number",["minimum"]=0,["maximum"]=1e6},
            ["attachment"]=new JsonObject{["enum"]=new JsonArray("top_left","top_center","top_right","middle_left","middle_center","middle_right","bottom_left","bottom_center","bottom_right")},
            ["line_spacing_style"]=new JsonObject{["enum"]=new JsonArray("at_least","exactly")},["line_spacing_factor"]=Positive(),
            ["direction_wcs"]=new JsonObject{["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=new JsonObject{["type"]="number",["minimum"]=-1,["maximum"]=1}}}};
        return new(){["type"]="object",["additionalProperties"]=false,["required"]=new JsonArray("operation","handles","mtext_token","properties"),
            ["properties"]=new JsonObject{["operation"]=new JsonObject{["const"]="set_mtext_format"},
                ["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=Handle()},
                ["mtext_token"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{64}$"},["properties"]=properties},
            ["description"]="Edit stored format/layout of one existing native top-level MText in Model or active ordinary Paper. Read fresh geometry.formatting.editing.mtext_token. Select an existing local text style by stable handle; native plain Text supplies required glyphs checked in the changed base-style font. Installed font availability is required. Raw Contents and inline font overrides remain exact; unverified retained inline runs or unsupported glyph analysis produce explicit warnings for visual review, not a claim of lost glyphs. Height and wrap width use drawing units; width0 uses the native unbounded wrapping mode. direction_wcs is an explicit XYZ unit vector in the retained native text plane, not an XY angle. Attachment changes text around the retained Location anchor. Native spacing style/factor setters validate feasibility. Unspecified entity format and source placement, raw inline content, background, flow, explicit column settings and ordinary owned metadata remain retained. Columns are observed/preserved, not edited; width changes on column MText require its column-layout adapter. Dynamic column count/heights and overall column envelope can reflow; explicit per-column width/gutter/height-mode/fixed height/flow remain retained. No analytic bbox oracle. Field, annotative-context and unclassified persistent dependency adapters are separate; optional read editing metadata explains unavailability without failing the base MText read. One format edit per target in a batch; combine its fields. Returns mtext_format_changes with actual stored values/new token and optional warnings. ROLLBACK_NOT_GUARANTEED; started failure remains unknown. Inspect readable notes and placement in MCP after the meaningful group; no content replacement or automatic Undo."};
    }
}
