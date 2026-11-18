using System.Text.Json;
using NanoCAD.Contracts;

namespace NanoCAD.McpServer;

internal static class LayoutCreateSchema
{
    public static JsonElement Create() => Wire.Element(new
    {
        type = "object",
        additionalProperties = false,
        required = new[] { "operation", "name", "base_layout_handle" },
        properties = new
        {
            operation = new { type = "string", @const = "create_layout" },
            name = new { type = "string", minLength = 1, maxLength = 255 },
            base_layout_handle = new { type = "string", minLength = 1, maxLength = 16, pattern = "^[0-9A-Fa-f]{1,16}$" },
            decoration_handles = new { type = "array", uniqueItems = true, items = new { type = "string", pattern = "^[0-9A-Fa-f]{1,16}$" } }
        },
        description = "Create a named paper layout with the base layout's stored plot settings. Optionally select ordinary lines, polylines, circles, arcs, text and static local title-block insertions via decoration_handles. The result maps each selected root and its variable attributes to new handles in the new layout. The default is an empty layout. Read its initialized paper limits with get_structure. Use a separate layout creation batch in a millimeter drawing."
    });
}
