using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

// create_text_style operation schema exposed by modify_structure since 0.8.
public static class StyleWriteSchema
{
    public static JsonElement SetProperties()
    {
        var schema = (JsonObject)JsonNode.Parse(CreateTextStyle().GetRawText())!;
        var properties = (JsonObject)schema["properties"]!;
        properties["operation"]!["const"] = "set_text_style_properties";
        properties.Remove("name"); properties.Remove("base_style_handle");
        properties["handle"] = new JsonObject { ["type"] = "string", ["pattern"] = "^[0-9A-Fa-f]{1,16}$" };
        properties["expected_name"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 255 };
        schema["required"] = new JsonArray("operation", "handle", "expected_name", "properties");
        schema["description"] = "Edit the existing ordinary native text style height/width_factor by handle and exact current name from get_structure text_styles. Preserve identity, font and other metadata. Height zero means variable-height style; stored entity text heights and widths are not automatically replaced. Changes affect style defaults and rendering according to native inheritance. Read the style and dependent annotations after commit. One style per request; allowed from Model or Paper.";
        return JsonSerializer.SerializeToElement(schema);
    }
    public static JsonElement SetFont() => JsonSerializer.SerializeToElement(new {
        type="object", additionalProperties=false,
        properties=new {
            operation=new {type="string",@const="set_text_style_font"},
            handle=new {type="string",pattern="^[0-9A-Fa-f]{1,16}$"},
            expected_typeface=new {type=new[]{"string","null"},minLength=1,maxLength=255,description="Exact font_descriptor.typeface from the current text-style read; explicit null binds a native descriptor with no family. The field is required; never guess a family from the font filename."},
            typeface=new {type="string",minLength=1,maxLength=31},
            required_text=new {type="string",minLength=1,maxLength=4096}
        },
        required=new[]{"operation","handle","expected_typeface","typeface","required_text"},
        description="Change an existing ordinary text style to an installed TrueType family. Affects all text using that style, including dimensions; text can reflow. Supply required literal characters in required_text; existing text is checked too. Missing glyphs or a substituted family are rejected before writing. Use a separate request before annotation creation. Font size and width remain unchanged."
    });
    public static JsonElement CreateTextStyle()
    {
        var properties = new JsonObject
        {
            ["type"] = "object", ["additionalProperties"] = false, ["minProperties"] = 1,
            ["properties"] = new JsonObject
            {
                ["height"] = new JsonObject { ["oneOf"] = new JsonArray(new JsonObject { ["type"] = "number", ["const"] = 0 },
                    new JsonObject { ["type"] = "number", ["minimum"] = 1e-9, ["maximum"] = 1e6 }) },
                ["width_factor"] = new JsonObject { ["type"] = "number", ["minimum"] = 0.01, ["maximum"] = 100 }
            },
            ["description"] = "Absent fields inherit from base. Height zero means variable height. No font/flags/current-style mutation."
        };
        return JsonSerializer.SerializeToElement(new JsonObject
        {
            ["type"] = "object", ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["operation"] = new JsonObject { ["type"] = "string", ["const"] = "create_text_style" },
                ["name"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 255,
                    ["description"] = "New ordinary unqualified name; never overwrite or change an existing style." },
                ["base_style_handle"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 16, ["pattern"] = "^[0-9A-Fa-f]{1,16}$" },
                ["properties"] = properties
            },
            ["required"] = new JsonArray("operation", "name", "base_style_handle"),
            ["description"] = "Derive a new ordinary horizontal text style from an existing committed native style. Fonts are inherited; no paths supplied. Source must satisfy the bounded metadata policy."
        });
    }
}
