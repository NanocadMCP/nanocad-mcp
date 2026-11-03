using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class BlockDefinitionTextSchema391
{
    static JsonObject Text()=>new(){["type"]="string"};
    static JsonObject Handle()=>new(){["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$",["minLength"]=1,["maxLength"]=16};
    static JsonObject Token()=>new(){["type"]="string",["pattern"]="^[0-9A-F]{64}$"};
    static JsonObject Object(params(string Key,JsonObject Value)[] fields)
    {var properties=new JsonObject();var required=new JsonArray();foreach(var f in fields){properties[f.Key]=f.Value;required.Add(f.Key);}return new(){["type"]="object",["additionalProperties"]=false,["properties"]=properties,["required"]=required};}
    static JsonObject Content(string kind)=>new(){["type"]="string",["maxLength"]=kind=="Text"?4096:8192,
        ["description"]=kind=="Text"?"Literal single-line stored TextString; empty allowed. No control characters, Fields (%<) or CAD percent codes (%%).":
            "Exact canonical literal native MText Contents; empty allowed. Paragraphs use \\P, literal backslash/braces/caret use the existing native reversible codec. Up to4096 decoded/8192 encoded characters and2048 per paragraph. No Fields, percent codes, controls or inline formatting."};
    static JsonObject Member(string kind)=>Object(("handle",Handle()),("type",new JsonObject{["const"]=kind}),("expected_text",Content(kind)),("text",Content(kind)));
    internal static JsonElement Input()
    {
        var result=Object(("operation",new JsonObject{["type"]="string",["const"]="set_block_definition_text"}),
            ("definition_handle",Handle()),("expected_definition_token",Token()),
            ("members",new JsonObject{["type"]="array",["minItems"]=1,["items"]=new JsonObject{["oneOf"]=new JsonArray(Member("Text"),Member("MText"))}}));
        result["description"]="Replace literal static labels in existing direct native Text/MText members of one ordinary local shared block definition. Read inspect_block.definition_text_editing by definition or insertion handle for its current token, exact content and readiness. Changes affect every actual Model/Paper/nested insertion, retaining native members, insertion identity/placement and literal occurrence attributes. Text retains its left/center/right alignment anchor and formatting; native glyph origin for nondefault justification may change. MText retains its stored Location anchor, frame/style/explicit column settings and appearance while native layout reflows the replacement Contents. NoColumns Width stays fixed; column envelopes and dynamic count/heights can change without resetting the stored column widths/gutter/height-mode/flow. Selected and related definitions must be observed ordinary XY2D primitive/text/local static nested graphs; native model/3D/custom/Field-bearing data require their owning route. No AttributeDefinition/default edits, occurrence-attribute synchronization, kind conversion, explode or redefine. Select actual direct member handles once, with exact expected_text and one operation in a separate request. Selected labels and actual referring insertion layers must be writable for native graphics notification; dependent/dynamic/annotative sources and persistent label associations are protected. Shared stored-text editing does not change definition units or insertion scale, and is available from Model or Paper. Result changes.children reports the edited labels and actual insertions marked for redraw. Refresh inspect_block and inspect the useful titleblock/symbol view. Undo/rollback is not guaranteed.";
        return JsonSerializer.SerializeToElement(result);
    }
    internal static JsonObject Read(JsonObject reference)
    {
        JsonObject Ref()=>JsonNode.Parse(reference.ToJsonString())!.AsObject();
        JsonObject Nullable(JsonObject value)=>new(){["anyOf"]=new JsonArray(value,new JsonObject{["type"]="null"})};
        var member=Object(("ref",Ref()),("type",new JsonObject{["type"]="string",["enum"]=new JsonArray("Text","MText")}),
            ("contents",Text()),("plain_text",Nullable(Text())),("eligible",new JsonObject{["type"]="boolean"}),("unavailable_reason",Nullable(Text())));
        return Object(("definition_ref",Ref()),("definition_name",Text()),("coordinate_system",new JsonObject{["const"]="parent_definition"}),
            ("definition_token",Nullable(Token())),("can_edit",new JsonObject{["type"]="boolean"}),
            ("unavailable_reason",Nullable(Text())),("members",new JsonObject{["type"]="array",["items"]=member}),
            ("referencing_insertions",new JsonObject{["type"]="array",["items"]=Ref()}));
    }
}
