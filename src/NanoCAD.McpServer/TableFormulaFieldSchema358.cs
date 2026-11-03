using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class TableFormulaFieldSchema358
{
    static JsonObject Text()=>new(){["type"]="string"};
    static JsonObject Boolean()=>new(){["type"]="boolean"};
    static JsonObject Object(params(string Name,JsonNode Value)[] fields)=>new(){["type"]="object",["additionalProperties"]=false,
        ["properties"]=new JsonObject(fields.Select(field=>new KeyValuePair<string,JsonNode?>(field.Name,field.Value))),
        ["required"]=new JsonArray(fields.Select(field=>(JsonNode?)JsonValue.Create(field.Name)).ToArray())};
    internal static JsonObject Read()
    {
        var result=Object(("handle",Text()),("owner_handle",Text()),("runtime_type",Text()),
            ("field_code",Text()),("object_reference_code",Text()),("evaluator_id",Text()),("format",Text()),
            ("evaluation_options",Text()),("filing_options",Text()),("is_text_field",Boolean()),("has_fields",Boolean()),
            ("private_state_complete",Boolean()),("scope",Text()));
        result["description"]="Actual local Field bound by this native Table cell's nonempty stored formula and native HasFormula flag. Its identity, actual owner, readable unevaluated code/evaluator/format/options are observed. Private Field data and child-field internals have no complete public inventory and remain explicitly unobserved. This never treats an external Field as a scalar, evaluates a Field, or supplies a substitute value.";
        return result;
    }
}
