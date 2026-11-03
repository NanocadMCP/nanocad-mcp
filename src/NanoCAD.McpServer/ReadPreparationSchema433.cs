using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;

namespace NanoCAD.McpServer;

internal static class ReadPreparationSchema433
{
    internal const string Description = " Optional prepare_for={operation,variant} adds observed bindings/current values and explicit unit/frame facts for the selected write. No extra CAD calls or eligibility guarantee; omitted by default.";
    internal static JsonElement Input(string reader, JsonElement original)
    {
        if (!ReadPreparation433.Readers.Contains(reader, StringComparer.Ordinal)) return original;
        var copy = JsonNode.Parse(original.GetRawText())!.AsObject();
        copy["properties"]!["prepare_for"] = new JsonObject
        {
            ["type"] = "object", ["additionalProperties"] = false, ["required"] = new JsonArray("operation"),
            ["properties"] = new JsonObject
            {
                ["operation"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(Wire.WriteOperations.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()), ["description"] = "Existing write tool without nanocad. prefix. Preparation is a read, never submission of that write." },
                ["variant"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 128, ["description"] = "Exact existing create/modify/structure variant, required for those three batched tools. Other tools omit variant. Obtain names from focused schema help." }
            },
            ["allOf"] = new JsonArray(new JsonObject
            {
                ["if"] = new JsonObject { ["properties"] = new JsonObject { ["operation"] = new JsonObject { ["enum"] = new JsonArray("create_entities", "modify_entities", "modify_structure") } } },
                ["then"] = new JsonObject { ["required"] = new JsonArray("variant") },
                ["else"] = new JsonObject { ["not"] = new JsonObject { ["required"] = new JsonArray("variant") } }
            })
        };
        return JsonSerializer.SerializeToElement(copy);
    }

    internal static JsonObject Output() => new()
    {
        ["type"] = "object", ["additionalProperties"] = false,
        ["required"] = new JsonArray("operation", "variant", "status", "detail"),
        ["properties"] = new JsonObject
        {
            ["operation"] = Text(), ["variant"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
            ["status"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("observed", "source_unavailable", "details_omitted") },
            ["detail"] = Text(), ["details_omitted"] = new JsonObject { ["type"] = "boolean" }, ["source"] = Object(), ["request_bindings"] = Array(Object()),
            ["candidates"] = Array(Object()), ["input_fields"] = Array(Object()), ["unit_facts"] = Object()
        },
        ["description"] = "Optional stateless observations for one selected operation. Missing preparation means not requested or no remaining transport bytes, never unsupported. Each binding points to the exact returned read or a declared schema constant. Current values are observations, not chosen new values."
    };
    private static JsonObject Text() => new() { ["type"] = "string" };
    private static JsonObject Object() => new() { ["type"] = "object" };
    private static JsonObject Array(JsonObject items) => new() { ["type"] = "array", ["items"] = items };
}
