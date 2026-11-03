using System.Text.Json.Nodes;
using NanoCAD.Contracts;

namespace NanoCAD.McpServer;

internal static class MLeaderInstanceSchema442
{
    internal const string Guidance="MLeader content and geometry have separate native editors. replace_mleader_text updates one retained literal MText note with all existing branches/arrows preserved; edit_mleader_topology manages actual lines/vertices. set_mleader_instance uses the fresh geometry.instance_editing.instance_token for explicit content_position_wcs, useful per-callout text/arrow/landing settings or block balloon attribute values. An explicit content position moves native content/dogleg points while arrow targets stay fixed; native text/block/landing layout may reflow, so inspect actual state and a meaningful group image. It does not attach arrows to source geometry. Create a block-content balloon with type=MLeader, content_type=block, an existing local static block_handle and existing mleader_style; attributes use real AttributeDefinition handles, not tags or occurrence attribute handles. Omitted attribute values use definition defaults. Existing attribute changes include expected_value and retain the definition and other values. Fields, annotation contexts and unsupported block member graphs retain their owning guards. Native Undo/recovery is uncertain and does not require extra consent by itself; unknown is neither success nor rollback.";
    static JsonObject O(JsonObject properties,params string[] required)=>new(){["type"]="object",["additionalProperties"]=false,["properties"]=properties,["required"]=new JsonArray(required.Select(s=>(JsonNode?)JsonValue.Create(s)).ToArray())};
    static JsonObject Handle()=>new(){["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"};
    static JsonObject Number(bool positive=false)=>positive?new(){["type"]="number",["exclusiveMinimum"]=0,["maximum"]=1e9}:new(){["type"]="number",["minimum"]=-1e9,["maximum"]=1e9};
    static JsonObject Choice(string[] choices)=>new(){["enum"]=new JsonArray(choices.Select(s=>(JsonNode?)JsonValue.Create(s)).ToArray())};
    static JsonObject Point(bool triple)=>new(){["type"]="array",["minItems"]=triple?3:2,["maxItems"]=triple?3:2,
        ["prefixItems"]=triple?new JsonArray(Number(),Number(),new JsonObject{["const"]=0}):new JsonArray(Number(),Number()),["items"]=false,["description"]="WCS XY/Z0 drawing millimeters; no coordinate-frame inference."};
    static JsonObject Literal()=>new(){["type"]="string",["maxLength"]=4096,["description"]="Single-line native literal without Fields, formatting or CAD percent codes; empty is permitted."};
    static JsonObject Attributes(bool editing)
    {
        var p=new JsonObject{["attribute_definition_handle"]=Handle(),["value"]=Literal()};if(editing)p["expected_value"]=Literal();
        return new(){["type"]="array",["items"]=O(p,editing?["attribute_definition_handle","expected_value","value"]:["attribute_definition_handle","value"]),
            ["description"]=editing?"Sparse actual variable AttributeDefinition identities and exact current expected_value. No definition or tag editing.":"Sparse actual variable AttributeDefinition identities; omitted values use native definition defaults. [] is permitted."};
    }
    internal static JsonObject Create()
    {
        var p=new JsonObject{["type"]=new JsonObject{["const"]="MLeader"},["content_type"]=new JsonObject{["const"]="block"},["layer"]=new JsonObject{["type"]="string",["minLength"]=1,["maxLength"]=255},
            ["vertices"]=new JsonObject{["type"]="array",["minItems"]=2,["items"]=Point(false),["description"]="Ordered native line vertices, arrow first; XY millimeters."},
            ["block_position"]=Point(false),["block_handle"]=Handle(),["block_scale"]=Number(true),["block_rotation"]=Number(),["block_connection_type"]=Choice(MLeaderInstanceValidation442.BlockConnections),
            ["mleader_style"]=new JsonObject{["type"]="string",["minLength"]=1,["maxLength"]=255},["attributes"]=Attributes(false)};
        var result=O(p,"type","content_type","vertices","block_position","block_handle","mleader_style","attributes");result["description"]="Create a real native BlockContent MLeader balloon using a local ordinary static symbol containing curves/Text/MText/single-line AttributeDefinition members, without Fields or annotation contexts. Unitless/Millimeters source only, no implicit unit conversion. Existing definition and source geometry are retained. block_scale is positive uniform native scale (default1), block_rotation is radians (default0), block_connection_type defaults ConnectExtents. One initial native line; block position/scale/rotation and attribute values use set_mleader_instance with actual read state. Existing branch/vertex topology editors retain their MText-content scope. "+Guidance;return result;
    }
    internal static JsonObject Operation()
    {
        var p=new JsonObject{["content_position_wcs"]=Point(true),["text_height"]=Number(true),["text_style_handle"]=Handle(),["arrow_size"]=Number(),["arrow_symbol_handle"]=Handle(),["landing_gap"]=Number(),["dogleg_length"]=Number(),
            ["enable_landing"]=new JsonObject{["type"]="boolean"},["enable_dogleg"]=new JsonObject{["type"]="boolean"},["enable_frame_text"]=new JsonObject{["type"]="boolean"},
            ["text_alignment"]=Choice(MLeaderStyleValidation337.TextAlignments),["text_angle_type"]=Choice(MLeaderStyleValidation337.TextAngles),["text_attachment_left"]=Choice(MLeaderStyleValidation337.HorizontalAttachments),["text_attachment_right"]=Choice(MLeaderStyleValidation337.HorizontalAttachments),
            ["block_scale"]=Number(true),["block_rotation"]=Number(),["block_connection_type"]=Choice(MLeaderInstanceValidation442.BlockConnections)};
        foreach(string name in new[]{"arrow_size","landing_gap","dogleg_length"})p[name]!["minimum"]=0;
        var result=O(new(){["operation"]=new JsonObject{["const"]="set_mleader_instance"},["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=Handle()},
            ["instance_token"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{64}$"},["properties"]=O(p),["block_attributes"]=Attributes(true)},"operation","handles","instance_token");
        result["anyOf"]=new JsonArray(new JsonObject{["required"]=new JsonArray("properties"),["properties"]=new JsonObject{["properties"]=new JsonObject{["minProperties"]=1}}},
            new JsonObject{["required"]=new JsonArray("block_attributes"),["properties"]=new JsonObject{["block_attributes"]=new JsonObject{["minItems"]=1}}});
        result["description"]="Edit one retained native MText/block-content MLeader in Model or active ordinary Paper. Sole token-bound operation; combine sparse properties and explicit block attribute values. Read geometry.instance_editing first. All native branches, line identities and arrow targets remain. Text-only properties require MText content; block-only properties/attributes require BlockContent. Every requested stored value is checked; unrequested source definition/raw text/metadata are retained. Width/landing/content layout may reflow through native setters; no analytic layout oracle. "+Guidance;return result;
    }
    internal static JsonObject Read()=>new(){["type"]="object",["additionalProperties"]=false,["properties"]=new JsonObject{
        ["instance_token"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-F]{64}$"},["complete"]=new JsonObject{["const"]=true},
        ["properties"]=new JsonObject{["type"]="object"},["lines"]=new JsonObject{["type"]="array",["items"]=new JsonObject{["type"]="object"}},
        ["branches"]=new JsonObject{["type"]="array",["items"]=new JsonObject{["type"]="object"}},["content"]=new JsonObject{["type"]="object"},["scope"]=new JsonObject{["type"]="string"}},
        ["required"]=new JsonArray("instance_token","complete","properties","lines","branches","content","scope")};
}
