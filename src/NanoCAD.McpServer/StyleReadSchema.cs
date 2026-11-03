using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;

namespace NanoCAD.McpServer;

// Text-style properties in the structural output, since candidate 0.8.
public static class StyleReadSchema
{
    private static JsonObject S(int max = 16384) => new() { ["type"] = "string", ["maxLength"] = max };
    private static JsonObject B() => new() { ["type"] = "boolean" };
    private static JsonObject N() => new() { ["type"] = "number" };
    private static JsonObject I() => new() { ["type"] = "integer", ["minimum"] = int.MinValue, ["maximum"] = int.MaxValue };
    private static JsonObject Const(string value) => new() { ["type"] = "string", ["const"] = value };
    private static JsonObject Null() => new() { ["type"] = "null" };
    private static JsonObject Nullable(JsonObject value) => new() { ["anyOf"] = new JsonArray(value, Null()) };
    private static JsonObject O(params (string Name, JsonObject Schema)[] fields)
    {
        var props = new JsonObject(); var required = new JsonArray();
        foreach (var field in fields) { props[field.Name] = field.Schema; required.Add(field.Name); }
        return new() { ["type"] = "object", ["additionalProperties"] = false, ["properties"] = props, ["required"] = required };
    }
    public static JsonElement TextStyleProperties()
    {
        var family = S(255); family["minLength"] = 1;
        var metadata = new JsonObject { ["oneOf"] = new JsonArray(
            O(("status", Const("empty")), ("family", Null()), ("flags", Null())),
            O(("status", Const("unsupported")), ("family", Null()), ("flags", Null())),
            O(("status", Const("acad_font")), ("family", family), ("flags", I()))) };
        var reasons = new JsonObject { ["type"] = "array", ["maxItems"] = TextStyleMetadata.ReasonCodes.Length, ["uniqueItems"] = true,
            ["items"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(TextStyleMetadata.ReasonCodes.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()) } };
        var noReasons = (JsonObject)reasons.DeepClone(); noReasons["maxItems"] = 0;
        reasons["minItems"] = 1;
        var eligibility = new JsonObject { ["oneOf"] = new JsonArray(
            O(("eligible", new JsonObject { ["const"] = true }), ("reasons", noReasons)),
            O(("eligible", new JsonObject { ["const"] = false }), ("reasons", reasons))) };
        var state = S(16); state["enum"] = new JsonArray("true", "false", "not_applicable", "unknown");
        var result = O(("is_dependent", B()), ("font_file", S()), ("big_font_file", S()), ("height", N()), ("width_factor", N()),
            ("is_vertical", B()), ("is_shape_file", B()),
            ("font_descriptor", O(("typeface", Nullable(S(255))), ("bold", B()), ("italic", B()), ("character_set", I()), ("pitch_and_family", I()))),
            ("oblique_angle", N()), ("generation_flags", I()), ("annotative_state", state), ("annotative_native_value", I()),
            ("has_extension_dictionary", B()), ("has_fields", B()), ("font_metadata", metadata), ("base_eligibility", eligibility));
        result["description"] = "Existing style metadata and conservative creation-base eligibility. Eligibility is a preflight hint, not a write reservation or proof of font rendering. Unknown/unsupported metadata remains readable without truncation or modification.";
        JsonObject StateCase(string name, JsonObject native) => new() { ["properties"] = new JsonObject
            { ["annotative_state"] = Const(name), ["annotative_native_value"] = native } };
        var unknown = I(); unknown["not"] = new JsonObject { ["enum"] = new JsonArray(0, 1, 2) };
        result["allOf"] = new JsonArray(new JsonObject { ["oneOf"] = new JsonArray(
            StateCase("true", new JsonObject { ["const"] = 0 }), StateCase("false", new JsonObject { ["const"] = 1 }),
            StateCase("not_applicable", new JsonObject { ["const"] = 2 }), StateCase("unknown", unknown)) });
        result["properties"]!["is_current"] = B();
        return JsonSerializer.SerializeToElement(result);
    }
}
