using System.Text;
using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record StructureDependency(string target_kind, string state, EntityRef? @ref, string? label, string? reason);
public sealed record PlotMetadata(string configuration_name, string canonical_media_name, string style_sheet, string settings_name, string view_name,
    int paper_units_native_value, int rotation_native_value, int type_native_value, double[] paper_size, double[] origin,
    double[] paper_margins_min, double[] paper_margins_max, double[] window_min, double[] window_max,
    bool use_standard_scale, int standard_scale_type_native_value, double standard_scale, double custom_scale_numerator,
    double custom_scale_denominator, bool centered, bool print_lineweights, bool scale_lineweights, bool plot_plot_styles);
public sealed record LayoutProperties(int tab_order, bool is_model, EntityRef block_record, bool tab_selected,
    double[] limits_min, double[] limits_max, PlotMetadata plot);
public sealed record ViewportProperties(EntityRef layout, int number, double[] center, double width, double height, double[] view_center,
    double[] view_target, double[] view_direction, double view_height, double twist_angle, bool is_on, bool is_locked, bool is_perspective,
    double custom_scale, int standard_scale_native_value, double lens_length, bool front_clip_on, bool back_clip_on,
    bool front_clip_at_eye, double front_clip_distance, double back_clip_distance, bool non_rect_clip_on,
    StructureDependency clip_boundary, StructureDependency[] frozen_layers);

public static class LayoutMetadata
{
    public const int MaxText = 4096;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    public static readonly string[] Reasons = { "invalid_id", "erased", "wrong_type", "wrong_owner", "read_failed" };
    private static void Fail(string text) => throw new DomainException("OBJECT_READ_FAILED", text);
    public static string Text(string? value)
    {
        if (value is null) throw new DomainException("OBJECT_READ_FAILED", "Missing layout/viewport metadata text.");
        if (value.Length > MaxText) throw new DomainException("ENTITY_TOO_LARGE", "Layout/viewport text exceeds 4096 UTF-16 units; no truncation.");
        try { StrictUtf8.GetByteCount(value); } catch (EncoderFallbackException) { Fail("Invalid layout/viewport Unicode."); }
        return value;
    }
    public static double Number(double value)
    { if (!double.IsFinite(value)) Fail("Non-finite layout/viewport metadata."); return value; }
    private static void Point(double[]? value, int count)
    { if (value is null || value.Length != count) Fail("Invalid layout/viewport coordinate count."); foreach (double v in value!) Number(v); }
    public static void Identity(EntityRef? value, EntityRef? owner = null)
    {
        if (value is null || !Guid.TryParseExact(value.bridge_instance_id, "D", out var b) || b == Guid.Empty ||
            !Guid.TryParseExact(value.document_id, "D", out var d) || d == Guid.Empty) Fail("Invalid structure reference lifetime.");
        try { if (value!.handle is null || Validation.NormalizeHandle(value.handle) != value.handle) Fail("Invalid structure handle."); }
        catch (DomainException) { Fail("Invalid structure handle."); }
        if (owner is not null && (value!.bridge_instance_id != owner.bridge_instance_id || value.document_id != owner.document_id)) Fail("Foreign structure reference lifetime.");
    }
    public static int FrozenCount(int count)
    {
        if (count < 0) Fail("Negative frozen layer count.");
        return count;
    }
    public static void Dependency(EntityRef owner, StructureDependency? value, string kind)
    {
        if (value is null || value.target_kind != kind) Fail("Invalid viewport dependency type.");
        if (value!.state == "resolved")
        { Identity(value.@ref, owner); Text(value.label); if (value.reason is not null) Fail("Resolved dependency has a failure reason."); }
        else if (value.state is "null" or "unavailable")
        {
            if (value.@ref is not null || value.label is not null || (value.state == "null" ? value.reason is not null : value.reason is null || !Reasons.Contains(value.reason)))
                Fail("Unresolved dependency claims an identity or invalid reason.");
        }
        else Fail("Unknown viewport dependency state.");
    }
    public static LayoutProperties Validate(EntityRef owner, LayoutProperties value)
    {
        Identity(owner); Identity(value.block_record, owner); Point(value.limits_min, 2); Point(value.limits_max, 2);
        var p = value.plot ?? throw new DomainException("OBJECT_READ_FAILED", "Missing plot metadata.");
        foreach (var text in new[] { p.configuration_name, p.canonical_media_name, p.style_sheet, p.settings_name, p.view_name }) Text(text);
        foreach (var point in new[] { p.paper_size, p.origin, p.paper_margins_min, p.paper_margins_max, p.window_min, p.window_max }) Point(point, 2);
        Number(p.standard_scale); Number(p.custom_scale_numerator); Number(p.custom_scale_denominator);
        return value;
    }
    public static ViewportProperties Validate(EntityRef owner, ViewportProperties value,Action? check=null)
    {
        check?.Invoke();
        Identity(owner); Identity(value.layout, owner);
        foreach (var point in new[] { value.center, value.view_target, value.view_direction }) Point(point, 3); Point(value.view_center, 2);
        foreach (double number in new[] { value.width, value.height, value.view_height, value.twist_angle, value.custom_scale, value.lens_length, value.front_clip_distance, value.back_clip_distance }) Number(number);
        Dependency(owner, value.clip_boundary, "entity");
        if (value.frozen_layers is null) Fail("Missing viewport frozen layer list."); FrozenCount(value.frozen_layers!.Length);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var layer in value.frozen_layers)
        { check?.Invoke();Dependency(owner, layer, "layer"); if (layer.state == "null" || (layer.@ref is not null && !seen.Add(layer.@ref.handle))) Fail("Null or repeated frozen layer."); }
        if(JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json).Length>Wire.PayloadBudget-8192)
            throw new DomainException("RESPONSE_LIMIT_EXCEEDED","Viewport metadata exceeds the actual serialized payload budget; no frozen-layer entries were truncated.");
        check?.Invoke();
        return value;
    }
}
