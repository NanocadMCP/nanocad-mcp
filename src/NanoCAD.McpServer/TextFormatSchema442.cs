using System.Text.Json.Nodes;
using NanoCAD.Contracts;
namespace NanoCAD.McpServer;

internal static class TextFormatSchema442
{
    static JsonObject Handle()=>new(){["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"};
    static JsonObject Number(bool positive=false)=>positive?new(){["type"]="number",["exclusiveMinimum"]=0}:new(){["type"]="number"};
    static JsonObject Point(int size=3)=>new(){["type"]="array",["minItems"]=size,["maxItems"]=size,["items"]=Number()};
    static JsonObject Enum(string[] values)=>new(){["enum"]=new JsonArray(values.Select(v=>(JsonNode?)JsonValue.Create(v)).ToArray())};
    static JsonObject Object(JsonObject properties,params string[] required)=>new(){["type"]="object",["additionalProperties"]=false,["properties"]=properties,["required"]=new JsonArray(required.Select(v=>(JsonNode?)JsonValue.Create(v)).ToArray())};
    static JsonObject Fields()=>new(){["width_factor"]=Number(true),["rotation"]=Number(),["oblique"]=new JsonObject{["type"]="number",["exclusiveMinimum"]=-Math.PI/2,["exclusiveMaximum"]=Math.PI/2},
        ["horizontal_mode"]=Enum(TextFormatValidation442.Horizontal),["vertical_mode"]=Enum(TextFormatValidation442.Vertical)};
    internal static JsonObject CreateFormat()
    {var p=Fields();p["second_anchor"]=Point(2);var value=Object(p);value["description"]="Optional native TEXT typography at creation. position is the authoritative local XY anchor; center/right/top/bottom retain it as AlignmentPoint. Aligned/Fit require baseline and explicit distinct second_anchor; native fitting derives rotation/width and Aligned height. Do not also supply width_factor or rotation for fitted modes. Radians; no manual glyph-width or text-bound approximation. Omission retains the existing left-baseline creation.";return value;}
    internal static JsonObject Operation()
    {
        var p=Fields();p["text_style_handle"]=Handle();p["text_height"]=Number(true);p["anchor_wcs"]=Point();p["second_anchor_wcs"]=Point();var properties=Object(p);properties["minProperties"]=1;
        var result=Object(new(){["operation"]=new JsonObject{["const"]="set_text_format"},["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=Handle()},
            ["text_token"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{64}$"},["properties"]=properties},"operation","handles","text_token","properties");
        result["description"]="Format native TEXT using fresh geometry.formatting.editing.text_token. Change existing local text_style_handle, text_height, width_factor, radians rotation/oblique, native horizontal/vertical modes or authoritative anchor_wcs in the retained text plane. Keep exact contents, unrelated display/user data and identity. Omitted anchor retains the current meaningful insertion/alignment anchor. Entering Aligned/Fit requires an explicit second_anchor_wcs; these modes derive rotation/width, and Aligned derives height (use Fit for explicit fixed height). Do not set width_factor/rotation in fitted modes or text_height in Aligned. Native middle horizontal mode uses baseline vertical. Multiple distinct notes can be formatted in one TEXT-format batch; combine each target's properties and read tokens once. Model or active ordinary Paper. Field/annotative contexts require their owning adapter. Inspect returned actual values and readable layout; started recovery/Undo is not guaranteed and unknown must not be duplicated.";return result;
    }
    internal static JsonObject Editing()=>Object(new(){["text_token"]=new JsonObject{["type"]=new JsonArray("string","null")},["editable"]=new JsonObject{["type"]="boolean"},["unavailable_reason"]=new JsonObject{["type"]="string"}},"text_token","editable");
    internal static JsonObject ExtendRead(JsonObject existing)
    {existing["properties"]!["editing"]=Editing();return existing;}
    internal static JsonObject Change()
    {
        var reference=Object(new(){["bridge_instance_id"]=new JsonObject{["type"]="string"},["document_id"]=new JsonObject{["type"]="string"},["handle"]=Handle()},"bridge_instance_id","document_id","handle");
        return Object(new(){["source_ref"]=reference,["text_token"]=new JsonObject{["type"]="string"},["text_style_ref"]=reference.DeepClone(),["text_height"]=Number(true),["width_factor"]=Number(true),["rotation"]=Number(),["oblique"]=Number(),
            ["horizontal_mode"]=Enum(TextFormatValidation442.Horizontal),["vertical_mode"]=Enum(TextFormatValidation442.Vertical),["anchor_wcs"]=Point(),["second_anchor_wcs"]=new JsonObject{["oneOf"]=new JsonArray(Point(),new JsonObject{["type"]="null"})},
            ["warnings"]=new JsonObject{["type"]="array",["items"]=new JsonObject{["type"]="string"}}},"source_ref","text_token","text_style_ref","text_height","width_factor","rotation","oblique","horizontal_mode","vertical_mode","anchor_wcs","second_anchor_wcs","warnings");
    }
}
