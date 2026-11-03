using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;

namespace NanoCAD.McpServer;

public static class LinetypeReadSchema
{
    private static JsonObject S(int max = LinetypeMetadata.MaxDescription) => new() { ["type"] = "string", ["maxLength"] = max };
    private static JsonObject I(int min = int.MinValue, int max = int.MaxValue) => new() { ["type"] = "integer", ["minimum"] = min, ["maximum"] = max };
    private static JsonObject N() => new() { ["type"] = "number" };
    private static JsonObject B() => new() { ["type"] = "boolean" };
    private static JsonObject Null() => new() { ["type"] = "null" };
    private static JsonObject Const(string value) => new() { ["const"] = value };
    private static JsonObject O(params (string Key, JsonObject Value)[] fields)
    {
        var properties = new JsonObject(); var required = new JsonArray();
        foreach (var (key, value) in fields) { properties[key] = value; required.Add(key); }
        return new() { ["type"] = "object", ["additionalProperties"] = false, ["properties"] = properties, ["required"] = required };
    }
    public static JsonElement Properties()
    {
        JsonObject Id() => new() { ["type"] = "string", ["pattern"] = "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$" };
        var reference = O(("bridge_instance_id", Id()), ("document_id", Id()), ("handle", new JsonObject { ["type"] = "string", ["pattern"] = "^[1-9A-F][0-9A-F]{0,15}$" }));
        var dependency = new JsonObject { ["oneOf"] = new JsonArray(
            O(("target_kind", Const("text_style")), ("state", Const("null")), ("ref", Null()), ("name", Null()), ("reason", Null())),
            O(("target_kind", Const("text_style")), ("state", Const("unavailable")), ("ref", Null()), ("name", Null()),
                ("reason", new JsonObject { ["enum"] = JsonSerializer.SerializeToNode(LinetypeMetadata.DependencyReasons) })),
            O(("target_kind", Const("text_style")), ("state", Const("resolved")), ("ref", reference), ("name", S()), ("reason", Null()))) };
        var element = O(("index", I(0, int.MaxValue)), ("length", N()), ("text", S(LinetypeMetadata.MaxElementText)),
            ("shape_number", I()), ("style", dependency), ("offset", new JsonObject { ["type"] = "array", ["minItems"] = 2, ["maxItems"] = 2, ["items"] = N() }),
            ("rotation", N()), ("scale", N()), ("is_ucs_oriented", B()));
        var result = O(("is_dependent", B()), ("description", S()), ("pattern_length", N()), ("dash_count", I(0, int.MaxValue)),
            ("is_scaled_to_fit", B()), ("elements", new JsonObject { ["type"] = "array", ["items"] = element }),
            ("has_extension_dictionary", B()), ("has_fields", B()), ("has_xdata", B()));
        result["description"] = "Complete bounded native linetype pattern. Lengths, offsets, scale and rotation are raw native values without normalization. Style refs are exact native text-style table records in the enclosing document lifetime. Null refs do not imply a particular visual appearance. No font/LIN loading or rendering proof. Runtime checks ordered indices/count and UTF-16 limits.";
        return JsonSerializer.SerializeToElement(result);
    }
}
