using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;

namespace NanoCAD.McpServer;

public static class LayoutReadSchema
{
    private static JsonObject S() => new() { ["type"] = "string", ["maxLength"] = LayoutMetadata.MaxText };
    private static JsonObject I() => new() { ["type"] = "integer", ["minimum"] = int.MinValue, ["maximum"] = int.MaxValue };
    private static JsonObject N() => new() { ["type"] = "number" };
    private static JsonObject B() => new() { ["type"] = "boolean" };
    private static JsonObject Null() => new() { ["type"] = "null" };
    private static JsonObject Const(string value) => new() { ["const"] = value };
    private static JsonObject P(int size = 2) => new() { ["type"] = "array", ["minItems"] = size, ["maxItems"] = size, ["items"] = N() };
    private static JsonObject O(params (string Key, JsonObject Value)[] fields)
    {
        var properties = new JsonObject(); var required = new JsonArray();
        foreach (var (key, value) in fields) { properties[key] = value; required.Add(key); }
        return new() { ["type"] = "object", ["additionalProperties"] = false, ["properties"] = properties, ["required"] = required };
    }
    private static JsonObject Ref()
    {
        JsonObject Id() => new() { ["type"] = "string", ["pattern"] = "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$" };
        return O(("bridge_instance_id", Id()), ("document_id", Id()), ("handle", new JsonObject { ["type"] = "string", ["pattern"] = "^[1-9A-F][0-9A-F]{0,15}$" }));
    }
    private static JsonObject Dependency(string kind) => new() { ["oneOf"] = new JsonArray(
        O(("target_kind", Const(kind)), ("state", Const("null")), ("ref", Null()), ("label", Null()), ("reason", Null())),
        O(("target_kind", Const(kind)), ("state", Const("unavailable")), ("ref", Null()), ("label", Null()),
            ("reason", new JsonObject { ["enum"] = JsonSerializer.SerializeToNode(LayoutMetadata.Reasons) })),
        O(("target_kind", Const(kind)), ("state", Const("resolved")), ("ref", Ref()), ("label", S()), ("reason", Null()))) };
    public static JsonElement Layout()
    {
        var plot = O(("configuration_name", S()), ("canonical_media_name", S()), ("style_sheet", S()), ("settings_name", S()), ("view_name", S()),
            ("paper_units_native_value", I()), ("rotation_native_value", I()), ("type_native_value", I()), ("paper_size", P()), ("origin", P()),
            ("paper_margins_min", P()), ("paper_margins_max", P()), ("window_min", P()), ("window_max", P()),
            ("use_standard_scale", B()), ("standard_scale_type_native_value", I()), ("standard_scale", N()),
            ("custom_scale_numerator", N()), ("custom_scale_denominator", N()), ("centered", B()), ("print_lineweights", B()),
            ("scale_lineweights", B()), ("plot_plot_styles", B()));
        var result = O(("tab_order", I()), ("is_model", B()), ("block_record", Ref()), ("tab_selected", B()), ("limits_min", P()), ("limits_max", P()), ("plot", plot));
        result["description"] = "Stored native layout/plot metadata only. Raw enum integers, coordinates and scales are not normalized. Configuration/media/style strings do not prove installed devices or files and are never opened. Paper size and margins follow the native millimeter convention; paper_units describes plot settings, not drawing units. No layout activation, print validation, rendering or printing.";
        return JsonSerializer.SerializeToElement(result);
    }
    public static JsonElement Viewport()
    {
        var result = O(("layout", Ref()), ("number", I()), ("center", P(3)), ("width", N()), ("height", N()), ("view_center", P()),
            ("view_target", P(3)), ("view_direction", P(3)), ("view_height", N()), ("twist_angle", N()), ("is_on", B()), ("is_locked", B()), ("is_perspective", B()),
            ("custom_scale", N()), ("standard_scale_native_value", I()), ("lens_length", N()), ("front_clip_on", B()), ("back_clip_on", B()),
            ("front_clip_at_eye", B()), ("front_clip_distance", N()), ("back_clip_distance", N()), ("non_rect_clip_on", B()),
            ("clip_boundary", Dependency("entity")), ("frozen_layers", new JsonObject { ["type"] = "array", ["items"] = Dependency("layer") }));
        result["description"] = "Native paper viewport parameters, including the special paper-space viewport when present. Center/width/height are in paper space, view_center in view coordinates, target/direction in WCS, angles in radians. Custom scale is raw native paper/model ratio, not a complete plot scale. Clip boundary ref is checked against the same layout; its label is a managed native entity type, not geometry or rendered visibility. Frozen layer refs are checked against this database's layer table; labels are names. All refs share the document lifetime. No clipping calculation or visibility/rendering proof. Frozen-layer entries are bounded by actual response bytes with cancellation; no fixed layer-count ceiling.";
        return JsonSerializer.SerializeToElement(result);
    }
}
