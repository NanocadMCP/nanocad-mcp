using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;

namespace NanoCAD.McpServer;

internal static class CameraChangeSchema
{
    internal static Tool Tool()
    {
        var input = JsonNode.Parse("""
        {"type":"object","additionalProperties":false,"required":["bridge_instance_id","document_id","expected_revision","operation_id","expected_camera_token","action"],"properties":{
        "bridge_instance_id":{"type":"string","format":"uuid"},"document_id":{"type":"string","format":"uuid"},"operation_id":{"type":"string","format":"uuid"},
        "expected_revision":{"type":"string","pattern":"^[0-9]{1,20}$"},"expected_camera_token":{"type":"string","pattern":"^[0-9A-F]{64}$"},
        "action":{"type":"string","enum":["preset","restore"]},"preset":{"type":"string","enum":["top","bottom","front","back","left","right","isometric"]},
        "restore_token":{"type":"string","format":"uuid"},"display_style":{"type":"string","enum":["preserve","shaded_edges","wireframe_2d"],"default":"preserve"},
        "frame_handles":{"type":"array","minItems":1,"uniqueItems":true,"items":{"type":"string","pattern":"^[0-9A-Fa-f]{1,16}$"}},
        "frame_padding":{"type":"number","minimum":0,"maximum":1,"default":0.1}},"dependentRequired":{"frame_padding":["frame_handles"]},
        "oneOf":[{"properties":{"action":{"const":"preset"}},"required":["preset"],"not":{"required":["restore_token"]}},
        {"properties":{"action":{"const":"restore"}},"required":["restore_token"],"not":{"anyOf":[{"required":["preset"]},{"required":["display_style"]},{"required":["frame_handles"]},{"required":["frame_padding"]}]}}]}
        """)!;
        return new()
        {
            Name = "nanocad.set_view",
            Description = "Set a named WCS camera direction or restore a previous view on the active unclipped orthographic Model tab or Paper sheet. Paper supports preset top with display_style=preserve and framing of the sheet's own objects; restore stays scoped to that same native space. Use set_viewport_view for a model camera inside a Paper viewport. Read expected_camera_token from get_document(include_view=true). Presets reset twist to zero and keep target/center/size unless frame_handles is supplied: top +Z, bottom -Z, front -Y, back +Y, left -X, right +X, isometric (1,-1,1). Optional display_style=shaded_edges requests native Gouraud shading with edges for 3D inspection; display_style=wireframe_2d requests the native Optimized2d edge-only mode for 2D Model documentation; default preserve keeps the existing style. In Millimeters Model, the exact existing MultiCAD view camera is applied and its orthographic centre/direction/pixel scale read back separately from the editor camera. Before/after renderer observations report the actual native view identity and camera, without guaranteeing that captured pixels have painted that state. Other unit/Paper contexts retain the editor camera route with explicit unobserved native renderer scope. With frame_handles, fit visible top-level native objects in the active Model/Paper space using their display bounds, preserve viewport aspect, and apply optional per-side frame_padding0–1 (default0.1). This includes ordinary Hatch, Region, Helix, native rectangular block arrays, displayed RasterImage/DWF/DGN/PDF input references, all seven supported Dimension kinds, and ordinary Paper viewports. A reserved viewport1 is not a sheet object to frame. Bounds are for display framing, not exact engineering measurements; no objects are silently skipped or made visible. Without frame_handles, no zoom/centering occurs. No geometry edits, commands, mouse/keyboard, selection, save, new styles, layouts, perspective or clipping. Returns before/after camera metadata and a restore_token scoped to this document/viewport/bridge lifetime, without a snapshot-count ceiling. Restore also restores the style. Native readback reports the actual view and any metadata difference; an uncertain change requires a fresh view read or capture. Object framing can fall back to the requested preset. Exact replay retrieves the historical result without reapplying a view; it does not describe the current view. After uncertain transport use only the identical operation_id/payload in the same bridge lifetime. Capture a fresh image after a meaningful change to inspect the rendered result.",
            InputSchema = JsonSerializer.SerializeToElement(input),
            OutputSchema = OutputSchemas.For("set_view"),
            Annotations = new() { ReadOnlyHint = false, DestructiveHint = false, IdempotentHint = true, OpenWorldHint = false }
        };
    }
}
