using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

public static class DimensionStyleWriteSchema
{
    public static JsonElement SetProperties()
    {
        var schema = (JsonObject)JsonNode.Parse(Create().GetRawText())!;
        var properties = (JsonObject)schema["properties"]!;
        properties["operation"]!["const"] = "set_dimension_style_properties";
        properties.Remove("name"); properties.Remove("base_style_handle");
        properties["handle"] = new JsonObject { ["type"] = "string", ["pattern"] = "^[0-9A-Fa-f]{1,16}$" };
        properties["expected_name"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 255 };
        schema["required"] = new JsonArray("operation", "handle", "expected_name", "properties");
        schema["description"] = "Edit existing ordinary non-annotative native dimension style by handle and exact expected_name. Native reusable primary/angular precision, unit display, rounding/zero suppression, signed literal tolerance modes/deviations and extension/text spacing. Sizes and line spacing use the drawing length convention; precision and modes are native values. These annotation defaults do not convert or drive source geometry. Omitted values inherit unchanged. Read complete get_structure dimension_styles properties first. The new defaults route retains all other observed style settings/dependencies and native identity. For existing ordinary independent dimension consumers, measured definitions, explicit placement, stored text, appearance, XData and readable owned dictionaries/Xrecords remain checked; automatic native text placement may reflow. Associated/field/context/dynamic-owner consumers require their owning route. Effective consumer changes are limited to requested fields taking either the retained override or new default; effective values alone do not prove inheritance when an explicit override equals its old default. Read affected inspect_objects geometry.format/display and review rendered notation. No current style selection or SetDimstyleData is invoked; editing the current record may affect its inherited drawing defaults, while set_current_dimension_style deliberately replaces document DIM overrides. One style per request; Model or Paper. Native recovery/Undo is not guaranteed.";
        return JsonSerializer.SerializeToElement(schema);
    }
    public static JsonElement Create()
    {
        JsonObject Number() => new() { ["type"] = "number", ["minimum"] = 1e-9, ["maximum"] = 1e6 };
        var defaults = new JsonObject { ["dimension_scale"] = Number(), ["text_height"] = Number(), ["arrow_size"] = Number() };
        DimensionStyleDefaultsSchema364.Add(defaults);
        var propertiesObject = new JsonObject { ["type"] = "object", ["additionalProperties"] = false, ["minProperties"] = 1,
            ["properties"] = defaults, ["description"] = "Native reusable primary/angular precision, unit display, rounding/zero suppression, signed literal tolerance modes/deviations and extension/text spacing. Sizes and line spacing use the drawing length convention; precision and modes are native values. These annotation defaults do not convert or drive source geometry. Omitted values inherit unchanged. Supply both signed deviations and their mode together; use only specified engineering tolerances." };
        DimensionStyleDefaultsSchema364.Deviations(propertiesObject);
        return JsonSerializer.SerializeToElement(new JsonObject
        {
            ["type"] = "object", ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["operation"] = new JsonObject { ["type"] = "string", ["const"] = "create_dimension_style" },
                ["name"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 255,
                    ["description"] = "New ordinary unqualified name without a $digits family suffix; creation never overwrites." },
                ["base_style_handle"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 16, ["pattern"] = "^[0-9A-Fa-f]{1,16}$" },
                ["properties"] = propertiesObject
            },
            ["required"] = new JsonArray("operation", "name", "base_style_handle"),
            ["description"] = "Create a reusable ordinary non-annotative dimension style from an existing base_style_handle with resolved local dependencies and no fields, XData or extension dictionary. Native reusable primary/angular precision, unit display, rounding/zero suppression, signed literal tolerance modes/deviations and extension/text spacing. Sizes and line spacing use the drawing length convention; precision and modes are native values. These annotation defaults do not convert or drive source geometry. Omitted values inherit unchanged. Read complete get_structure dimension_styles properties first. The new defaults route retains all other observed style settings/dependencies and native identity. For existing ordinary independent dimension consumers, measured definitions, explicit placement, stored text, appearance, XData and readable owned dictionaries/Xrecords remain checked; automatic native text placement may reflow. Associated/field/context/dynamic-owner consumers require their owning route. Effective consumer changes are limited to requested fields taking either the retained override or new default; effective values alone do not prove inheritance when an explicit override equals its old default. Read affected inspect_objects geometry.format/display and review rendered notation. No current style selection or SetDimstyleData is invoked; editing the current record may affect its inherited drawing defaults, while set_current_dimension_style deliberately replaces document DIM overrides. Creation never replaces the base or changes its consumers. Zero/annotative dimension_scale and zero text_height/arrow_size remain outside this existing route. No Dimension entities are created."
        });
    }
}
