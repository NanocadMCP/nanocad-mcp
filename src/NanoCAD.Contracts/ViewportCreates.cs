using System.Text.Json;
using static NanoCAD.Contracts.StructureWriteValidation;

namespace NanoCAD.Contracts;

public sealed record CreateViewportEdit(string LayoutHandle, string LayerHandle, double[] Center, double Width, double Height,
    double[] ViewCenter, double ViewHeight, double TwistAngle, bool IsOn, bool IsLocked) : StructureEdit("create_viewport");

public static class ViewportCreateValidation
{
    public static CreateViewportEdit Parse(JsonElement input, string path = "$")
    {
        Shape(input, path, "operation", "layout_handle", "layer_handle", "center", "width", "height", "view_center", "view_height", "twist_angle", "is_on", "is_locked");
        if (Text(input, "operation", path, 32) != "create_viewport") throw Invalid(path, "Expected create_viewport.");
        double Number(JsonElement value, string name)
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double n) || !double.IsFinite(n)) throw Invalid(name, "Expected a finite number.");
            if (n == 0)
            {
                string raw = value.GetRawText(); int exponent = raw.IndexOfAny(new[] { 'e', 'E' });
                if (raw.Take(exponent < 0 ? raw.Length : exponent).Any(c => c is >= '1' and <= '9')) throw Invalid(name, "Nonzero number underflows to zero.");
            }
            return n == 0 ? 0 : n;
        }
        double Scalar(string name) => Number(Required(input, name, path), name);
        double[] Point(string name)
        {
            var value = Required(input, name, path);
            if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 2) throw Invalid(name, "Expected exactly two coordinates.");
            return value.EnumerateArray().Select(v => Number(v, name)).ToArray();
        }
        bool Flag(string name)
        {
            var value = Required(input, name, path);
            if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw Invalid(name, "Expected boolean.");
            return value.GetBoolean();
        }
        var result = new CreateViewportEdit(Validation.NormalizeHandle(Text(input, "layout_handle", path, 16)),
            Validation.NormalizeHandle(Text(input, "layer_handle", path, 16)), Point("center"), Scalar("width"), Scalar("height"),
            Point("view_center"), Scalar("view_height"), Scalar("twist_angle"), Flag("is_on"), Flag("is_locked"));
        Validate(result); return result;
    }
    public static void Validate(CreateViewportEdit edit)
    {
        foreach (string handle in new[] { edit.LayoutHandle, edit.LayerHandle })
            if (Validation.NormalizeHandle(handle) != handle) throw Invalid("handle", "Expected normalized nonzero handle.");
        if (edit.LayoutHandle == edit.LayerHandle) throw Invalid("layer_handle", "Layout and layer identities must differ.");
        foreach (var point in new[] { edit.Center, edit.ViewCenter })
            if (point is null || point.Length != 2 || point.Any(n => !double.IsFinite(n) || Math.Abs(n) > 1e9)) throw Invalid("center", "Expected two finite coordinates within ±1e9 mm.");
        foreach (double size in new[] { edit.Width, edit.Height, edit.ViewHeight })
            if (!double.IsFinite(size) || size is < 1e-6 or > 1e9) throw Invalid("size", "Expected a size in 1e-6..1e9 mm.");
        if (!double.IsFinite(edit.TwistAngle) || edit.TwistAngle < 0 || edit.TwistAngle >= 2 * Math.PI) throw Invalid("twist_angle", "Expected radians in [0, 2*pi).");
        double scale = edit.Height / edit.ViewHeight;
        if (scale is < 1e-9 or > 1e9 || Math.Abs(edit.Center[0]) + edit.Width / 2 > 1e9 || Math.Abs(edit.Center[1]) + edit.Height / 2 > 1e9)
            throw Invalid("size", "Viewport frame or scale exceeds its bound.");
    }
    public static int ValidateBatch(IEnumerable<StructureEdit> operations)
    {
        var batch = operations.ToArray(); var additions = batch.OfType<CreateViewportEdit>().ToArray();
        if (additions.Length > 0)
        {
            if (additions.Length != batch.Length) throw Invalid("operations", "Viewport creation requires a separate batch.", "INVALID_OPERATION_ORDER");
            foreach (var edit in additions) Validate(edit);
        }
        int budget = LayoutCreateValidation.ValidateBatch(batch) + additions.Length;
        BlockWriteValidation.CheckObjectBudget(budget); return budget;
    }
    public static void RequireLayout(LayoutProperties layout, IEnumerable<int> numbers, int additions)
    {
        var existing = numbers.ToArray();
        if (layout.is_model || layout.plot.paper_units_native_value != 1)
            throw new DomainException("UNSUPPORTED_LAYOUT", "Viewport creation requires a paper layout using millimeters.");
        if (existing.Count(n => n == 1) != 1)
            throw new DomainException("LAYOUT_NOT_INITIALIZED", "Paper layout must already contain one overall viewport; unsupported Layout.Initialize is never called.");
        if (additions < 1)
            throw Invalid("operations", "At least one new viewport is required.");
    }
    public static void RequireDesired(CreateViewportEdit edit, ViewportProperties actual, bool resident)
    {
        double scale = edit.Height / edit.ViewHeight;
        if (actual.number == 1 || !actual.center.SequenceEqual(new[] { edit.Center[0], edit.Center[1], 0d }) ||
            actual.width != edit.Width || actual.height != edit.Height || !actual.view_center.SequenceEqual(edit.ViewCenter) ||
            !actual.view_target.SequenceEqual(new[] { 0d, 0, 0 }) || !actual.view_direction.SequenceEqual(new[] { 0d, 0, 1 }) ||
            actual.view_height != edit.ViewHeight || actual.twist_angle != edit.TwistAngle || actual.is_locked != edit.IsLocked ||
            resident && actual.is_on != edit.IsOn || actual.is_perspective || actual.non_rect_clip_on || actual.clip_boundary.state != "null" ||
            actual.front_clip_on || actual.back_clip_on || actual.frozen_layers.Length != 0 ||
            !double.IsFinite(actual.custom_scale) || Math.Abs(actual.custom_scale - scale) > Math.Max(scale * 1e-12, double.Epsilon) ||
            actual.standard_scale_native_value is < 1 or > 34)
            throw new DomainException("API_EXCEPTION", "New viewport does not match the requested rectangular XY view; batch cannot commit.", false, new { resident, actual });
    }
}
