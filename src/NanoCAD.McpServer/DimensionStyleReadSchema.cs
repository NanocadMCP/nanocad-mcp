using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;

namespace NanoCAD.McpServer;

// Dimension-style properties used by StructureSchema, since candidate 0.9.
public static class DimensionStyleReadSchema
{
    private static JsonObject S(int limit = 16384) => new() { ["type"] = "string", ["maxLength"] = limit };
    private static JsonObject B() => new() { ["type"] = "boolean" };
    private static JsonObject N() => new() { ["type"] = "number" };
    private static JsonObject I(int min = int.MinValue, int max = int.MaxValue) => new() { ["type"] = "integer", ["minimum"] = min, ["maximum"] = max };
    private static JsonObject Null() => new() { ["type"] = "null" };
    private static JsonObject Const(object value) => new() { ["const"] = JsonSerializer.SerializeToNode(value) };
    private static JsonObject Enum<T>(IEnumerable<T> values) => new() { ["enum"] = JsonSerializer.SerializeToNode(values) };
    private static JsonObject Choice(params JsonObject[] values) => new() { ["oneOf"] = new JsonArray(values.Cast<JsonNode?>().ToArray()) };
    private static JsonObject O(params (string Name, JsonObject Schema)[] fields)
    {
        var props = new JsonObject(); var required = new JsonArray();
        foreach (var (name, schema) in fields) { props[name] = schema; required.Add(name); }
        return new() { ["type"] = "object", ["additionalProperties"] = false, ["properties"] = props, ["required"] = required };
    }
    private static JsonObject Identity()
    {
        JsonObject GuidValue() => new() { ["type"] = "string", ["pattern"] = "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$" };
        return O(("bridge_instance_id", GuidValue()), ("document_id", GuidValue()),
            ("handle", new JsonObject { ["type"] = "string", ["pattern"] = "^[1-9A-F][0-9A-F]{0,15}$" }));
    }
    private static JsonObject Dependency(string kind) => Choice(
        O(("target_kind", Const(kind)), ("state", Const("null")), ("ref", Null()), ("name", Null()), ("reason", Null())),
        O(("target_kind", Const(kind)), ("state", Const("unavailable")), ("ref", Null()), ("name", Null()), ("reason", Enum(DimensionStyleMetadata.DependencyReasons))),
        O(("target_kind", Const(kind)), ("state", Const("resolved")), ("ref", Identity()), ("name", S()), ("reason", Null())));
    private static JsonObject Color()
    {
        var variants = new List<JsonObject>();
        foreach (int native in Enumerable.Range(192, 9).Append(-1))
        {
            var nativeSchema = native == -1 ? I() : Const(native);
            if (native == -1) nativeSchema["not"] = Enum(Enumerable.Range(192, 9));
            variants.Add(O(("method", Const(DimensionStyleMetadata.ColorMethod(native))), ("native_value", nativeSchema),
                ("aci", native == 195 ? I(short.MinValue, short.MaxValue) : Null()),
                ("rgb", native == 194 ? O(("red", I(0, 255)), ("green", I(0, 255)), ("blue", I(0, 255))) : Null()),
                ("has_color_name", B()), ("color_name", S(1024)), ("book_name", S(1024))));
        }
        return Choice(variants.ToArray());
    }
    private static JsonObject Weight()
    {
        var unknown = I(); unknown["not"] = Enum(DimensionStyleMetadata.ExplicitLineweights.Concat(new[] { -1, -2, -3 }));
        return Choice(O(("mode", Const("by_layer")), ("native_value", Const(-1))),
            O(("mode", Const("by_block")), ("native_value", Const(-2))), O(("mode", Const("default")), ("native_value", Const(-3))),
            O(("mode", Const("explicit")), ("native_value", Enum(DimensionStyleMetadata.ExplicitLineweights))),
            O(("mode", Const("unknown")), ("native_value", unknown)));
    }
    public static JsonElement Properties()
    {
        JsonObject Scalar(System.Reflection.PropertyInfo property)
        {
            if (property.Name == "dimdsep") return I(0, 65535);
            if (property.PropertyType == typeof(bool)) return B();
            if (property.PropertyType == typeof(double)) return N();
            if (property.PropertyType == typeof(int)) return I();
            if (property.PropertyType == typeof(string))
            {
                var text = S(4096); text["description"] = "Exact text, no truncation. Runtime also enforces 4096 UTF-16 units and rejects unpaired surrogates.";
                return text;
            }
            throw new InvalidOperationException("Unreviewed dimension variable type.");
        }
        var variables = O(typeof(DimensionVariables).GetProperties().Select(p => (p.Name, Scalar(p))).ToArray());
        DimensionStyleDefaultsSchema364.DescribeReadVariables(variables);
        variables["description"] = "Native DIM variables without unit conversion or value normalization; DIMJOGANG units are unconfirmed and must not be assumed. DIMDSEP is a UTF-16 code unit, not a Unicode scalar.";
        var result = O(("is_dependent", B()), ("dimension_scale", N()), ("text_height", N()), ("arrow_size", N()),
            ("variables", variables), ("references", O(("arrow", Dependency("block")), ("first_arrow", Dependency("block")),
                ("second_arrow", Dependency("block")), ("leader_arrow", Dependency("block")), ("extension1_linetype", Dependency("linetype")),
                ("extension2_linetype", Dependency("linetype")), ("dimension_linetype", Dependency("linetype")), ("text_style", Dependency("text_style")))),
            ("colors", O(("dimension", Color()), ("extension", Color()), ("text", Color()), ("text_fill", Color()))),
            ("lineweights", O(("dimension", Weight()), ("extension", Weight()))),
            ("annotative_state", Enum(new[] { "true", "false", "not_applicable", "unknown" })), ("annotative_native_value", I()),
            ("has_extension_dictionary", B()), ("has_fields", B()), ("has_xdata", B()), ("family_name_candidate", B()));
        JsonObject State(string label, JsonObject native) => new() { ["properties"] = new JsonObject {
            ["annotative_state"] = Const(label), ["annotative_native_value"] = native } };
        var unknownState = I(); unknownState["not"] = Enum(new[] { 0, 1, 2 });
        result["allOf"] = new JsonArray(Choice(State("true", Const(0)), State("false", Const(1)), State("not_applicable", Const(2)), State("unknown", unknownState)));
        result["description"] = "Prepared dimension-style read metadata. Resolved refs require native exact class/table ownership plus the same lifetime as the enclosing record. Null arrow is not interpreted as a particular arrow shape. Family-name candidate is only an ASCII numeric name suffix, not proven inheritance. No creation eligibility is claimed.";
        result["properties"]!["is_current"] = B();
        return JsonSerializer.SerializeToElement(result);
    }
}
