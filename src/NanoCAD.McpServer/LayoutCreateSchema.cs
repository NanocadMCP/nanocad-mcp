using System.Text.Json;
using NanoCAD.Contracts;

namespace NanoCAD.McpServer;

internal static class LayoutCreateSchema
{
    public static JsonElement Create() => Wire.Element(new
    {
        type = "object", additionalProperties = false,
        required = new[] { "operation", "name", "base_layout_handle" },
        properties = new
        {
            operation = new { type = "string", @const = "create_layout" },
            name = new { type = "string", minLength = 1, maxLength = 255 },
            base_layout_handle = new { type = "string", minLength = 1, maxLength = 16, pattern = "^[0-9A-Fa-f]{1,16}$" }
        },
        description = "Create an empty paper layout, copying stored plot settings from an explicit existing paper layout. New layout limits are initialized by the SDK and remain readable through get_structure. Separate batch of in a millimeter drawing; available from Model or Paper. New unique ordinary name, appended tab order; existing layouts and their entities are preserved without an arbitrary total layout count cap. No viewport/entity copy, activation, printer validation or save."
    });
}
