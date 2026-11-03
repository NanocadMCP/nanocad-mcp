using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;

namespace NanoCAD.McpServer;

// Static block operation schemas exposed by modify_structure since 0.7.
public static class BlockWriteSchema
{
    private const string TagPattern = "^[A-Z_][A-Z0-9_]{0,63}$";
    private static JsonObject Text(int max, int min = 1) => new() { ["type"] = "string", ["minLength"] = min, ["maxLength"] = max };
    private static JsonObject PlainText(int min = 0)
    {
        var result = Text(4096, min); result["pattern"] = "^(?!.*(?:%<|%%))[^\\u0000-\\u001f\\u007f-\\u009f]*$"; return result;
    }
    private static JsonObject Number(double min = -1e9, double max = 1e9) => new() { ["type"] = "number", ["minimum"] = min, ["maximum"] = max };
    private static JsonObject Point(string context) => new()
    { ["type"] = "array", ["minItems"] = 2, ["maxItems"] = 2, ["items"] = Number(), ["description"] = context + " XY in millimeters; Z=0." };
    private static JsonObject Object(JsonObject properties, params string[] required) => new()
    { ["type"] = "object", ["additionalProperties"] = false, ["properties"] = properties, ["required"] = new JsonArray(required.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()) };
    private static JsonObject Kind(string name) => new() { ["type"] = "string", ["const"] = name };
    public static JsonElement Define()
    {
        var creation = JsonNode.Parse(CreateSchema.Input().GetRawText())!.AsObject();
        var entities = creation["properties"]!["entities"]!.DeepClone().AsObject();
        var primitiveVariants = entities["items"]!["oneOf"]!.AsArray();
        for (int index = primitiveVariants.Count - 1; index >= 0; index--)
        {
            var properties=primitiveVariants[index]!["properties"]!.AsObject();
            if (properties["type"]!["const"]!.GetValue<string>() is not ("Line" or "Circle" or "Arc" or "Ellipse" or "Spline" or "Polyline" or "Text" or "MText" or "Hatch")||
                properties["type"]!["const"]!.GetValue<string>() is "Spline" or "Polyline"&&properties["construction"]!=null)
                primitiveVariants.RemoveAt(index);
        }
        foreach (var variant in entities["items"]!["oneOf"]!.AsArray())
        {
            var properties = variant!["properties"]!.AsObject();properties.Remove("workplane");
            properties["layer"] = new JsonObject { ["type"] = "string", ["const"] = "0", ["default"] = "0" };
            if (properties["type"]!["const"]!.GetValue<string>() == "Text") properties["text"] = PlainText(1);
        }
        void LocalCoordinates(JsonNode? node)
        {
            if (node is JsonObject obj)
                foreach (var pair in obj.ToArray())
                {
                    if (pair.Key == "description" && pair.Value?.ToString().Contains("WCS XY", StringComparison.Ordinal) == true)
                        obj[pair.Key] = pair.Value!.ToString().Replace("WCS XY","Block-definition local XY",StringComparison.Ordinal);
                    else LocalCoordinates(pair.Value);
                }
            else if (node is JsonArray array) foreach (var item in array) LocalCoordinates(item);
        }
        LocalCoordinates(entities);
        primitiveVariants.Add(BlockTemplateSchema391.Reference());
        // Inline block fills own their local loops and never bind drawing roots.
        foreach(var variant in primitiveVariants.Where(v=>v?["properties"]?["type"]?["const"]?.GetValue<string>()=="Hatch").ToArray())
            if(variant?["properties"]?["boundary_handle"]!=null||variant?["properties"]?["boundary_groups"]!=null)primitiveVariants.Remove(variant);
        var tag = Text(64); tag["pattern"] = TagPattern;
        var attribute = Object(new JsonObject
        {
            ["tag"] = tag, ["default_text"] = PlainText(), ["position"] = Point("Block-definition local"), ["height"] = Number(1e-9),
            ["text_style"] = Text(255), ["rotation"] = Number(-1e6, 1e6)
        }, "tag", "default_text", "position", "height");
        attribute["description"] = "Variable single-line attribute only. Unique uppercase tag; left/baseline, layer 0, normal +Z. Defaults: Standard style, zero rotation; no constant, field, MText or hidden flags.";
        var result = Object(new JsonObject
        {
            ["operation"] = Kind("define_block"), ["name"] = Text(255), ["base_point"] = Point("Block-definition local"),
            ["entities"] = entities, ["attributes"] = new JsonObject { ["type"] = "array", ["minItems"] = 0, ["items"] = attribute }
        }, "operation", "name", "base_point", "entities");
        result["description"] = "Create a new ordinary static Millimeters definition, never overwrite. Local coordinates are stored as supplied; base_point is not subtracted from member values. Layer-0 ordinary curves/Text, literal MText, independent polygon/circle-hole SOLID or ANSI31 Hatch and existing ordinary static BlockReference members with positive uniform scale are supported. Nested definition handles/styles must already exist; no Fields/dynamic/xref/custom graph or assembly mates. Local base_point is retained. Use its returned handle in a later insertion request; inspect_block exposes local hierarchy, MText and Hatch loops.";
        return JsonSerializer.SerializeToElement(result);
    }
    public static JsonElement Insert()
    {
        var handle = Text(16); handle["pattern"] = "^[0-9A-Fa-f]{1,16}$";
        var result = Object(new JsonObject
        {
            ["operation"] = Kind("insert_block"), ["definition_handle"] = handle, ["position"] = Point("Requested Model or paper space"), ["layer"] = Text(255),
            ["rotation"] = Number(-1e6, 1e6), ["scale"] = Number(1e-6, 1e6),
            ["attribute_values"] = new JsonObject { ["type"] = "object",                 ["propertyNames"] = new JsonObject { ["pattern"] = TagPattern }, ["additionalProperties"] = PlainText() }
        }, "operation", "definition_handle", "position", "layer");
        result["description"] = "Insert an existing supported ordinary static Millimeters definition in the requested active space. modify_structure target_space defaults to model; paper requires the active sheet outside its model viewport. position uses the selected space's XY millimeters. Preserve the shared definition; positive uniform scale(default1), radians rotation(default0). Missing attribute values use read definition defaults; unknown tags fail the batch. Ordinary nested static references, literal MText and independent native Hatch leaves remain shared and are supported. Dynamic/xref/Field/custom/associative graphs retain their owning routes. Only the requested root variable attributes are initialized; existing nested occurrences keep their values. Inspect the resulting Model/Paper hierarchy and useful view.";
        return JsonSerializer.SerializeToElement(result);
    }
}
