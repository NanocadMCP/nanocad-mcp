using System.Text.Json;
using static NanoCAD.Contracts.StructureWriteValidation;

namespace NanoCAD.Contracts;

public sealed record ViewportViewChanges(double[]? ViewCenter, double? ViewHeight, double? TwistAngle, bool? IsLocked,
    double[]? Center = null, double? Width = null, double? Height = null, bool? IsOn = null,
    double[]? ViewDirection = null, double[]? ViewTarget = null, string[]? FrozenLayerHandles = null);
public sealed record SetViewportViewEdit(string LayoutHandle, string Handle, ViewportViewChanges Properties) : StructureEdit("set_viewport_view");

public static class ViewportWriteValidation
{
    public static SetViewportViewEdit Parse(JsonElement input, string path = "$")
    {
        Shape(input, path, "operation", "layout_handle", "handle", "properties");
        if (Text(input, "operation", path, 32) != "set_viewport_view") throw Invalid(path, "Expected set_viewport_view.");
        var properties = Required(input, "properties", path);
        Shape(properties, path + ".properties", "view_center", "view_height", "twist_angle", "is_locked", "center", "width", "height", "is_on", "view_direction", "view_target", "frozen_layer_handles");
        double Number(JsonElement value, string name)
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double number) || !double.IsFinite(number)) throw Invalid(name, "Expected a finite number.");
            if (number == 0)
            {
                string raw = value.GetRawText(); int exponent = raw.IndexOfAny(new[] { 'e', 'E' });
                if (raw.Take(exponent < 0 ? raw.Length : exponent).Any(c => c is >= '1' and <= '9')) throw Invalid(name, "Nonzero number underflows to zero.");
            }
            return number == 0 ? 0 : number;
        }
        double? Optional(string name) => properties.TryGetProperty(name, out var value) ? Number(value, path + ".properties." + name) : null;
        double[]? center = null;
        if (properties.TryGetProperty("view_center", out var point))
        {
            if (point.ValueKind != JsonValueKind.Array || point.GetArrayLength() != 2) throw Invalid(path, "view_center requires exactly two view coordinates.");
            center = point.EnumerateArray().Select(v => Number(v, "view_center")).ToArray();
        }
        bool? Flag(string name)
        {
            if (!properties.TryGetProperty(name, out var flag)) return null;
            if (flag.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw Invalid(path, name + " requires a boolean.");
            return flag.GetBoolean();
        }
        double[]? Triple(string name)
        {
            if (!properties.TryGetProperty(name, out var value)) return null;
            if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 3) throw Invalid(path, name + " requires exactly three WCS coordinates.");
            return value.EnumerateArray().Select(v => Number(v, name)).ToArray();
        }
        string[]? frozen = null;
        if (properties.TryGetProperty("frozen_layer_handles", out var handles))
        {
            if (handles.ValueKind != JsonValueKind.Array) throw Invalid(path, "Expected existing layer handles; empty thaws all viewport layers. Requests are bounded by actual serialized bytes.");
            frozen = handles.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.String ? Validation.NormalizeHandle(v.GetString()!) : throw Invalid(path, "Frozen layer handles must be strings.")).ToArray();
        }
        double[]? paperCenter=null;
        if(properties.TryGetProperty("center",out var pc))
        {if(pc.ValueKind!=JsonValueKind.Array||pc.GetArrayLength()!=2)throw Invalid(path,"center requires two paper coordinates.");paperCenter=pc.EnumerateArray().Select(v=>Number(v,"center")).ToArray();}
        var operation = new SetViewportViewEdit(Validation.NormalizeHandle(Text(input, "layout_handle", path, 16)), Validation.NormalizeHandle(Text(input, "handle", path, 16)),
            new(center, Optional("view_height"), Optional("twist_angle"), Flag("is_locked"), paperCenter, Optional("width"), Optional("height"), Flag("is_on"), Triple("view_direction"), Triple("view_target"), frozen));
        Validate(operation); return operation;
    }
    public static void Validate(SetViewportViewEdit operation)
    {
        var p = operation.Properties;
        if (p is null || p.ViewCenter is null && p.ViewHeight is null && p.TwistAngle is null && p.IsLocked is null && p.Center is null && p.Width is null && p.Height is null && p.IsOn is null && p.ViewDirection is null && p.ViewTarget is null && p.FrozenLayerHandles is null) throw Invalid("properties", "At least one viewport property is required.");
        foreach (var point in new[] { p.ViewDirection, p.ViewTarget })
            if (point is not null && (point.Length != 3 || point.Any(n => !double.IsFinite(n) || Math.Abs(n) > 1e9))) throw Invalid("view_direction/view_target", "Expected three finite WCS coordinates within ±1e9.");
        if (p.ViewDirection is { } direction && direction.All(n => n == 0)) throw Invalid("view_direction", "View direction must be nonzero.");
        if (p.FrozenLayerHandles is { } frozen && frozen.Distinct(StringComparer.Ordinal).Count() != frozen.Length) throw Invalid("frozen_layer_handles", "Expected distinct existing layer handles.");
        if(p.Center is {} c&&(c.Length!=2||c.Any(n=>!double.IsFinite(n)||Math.Abs(n)>1e9)))throw Invalid("center","Expected two finite paper coordinates within ±1e9.");
        foreach(var size in new[]{p.Width,p.Height})if(size.HasValue&&(!double.IsFinite(size.Value)||size.Value is <1e-6 or >1e9))throw Invalid("width/height","Paper dimensions must be1e-6..1e9 mm.");
        if (operation.LayoutHandle == operation.Handle) throw Invalid("handle", "Layout and viewport identities must differ.");
        if (p.ViewCenter is not null && (p.ViewCenter.Length != 2 || p.ViewCenter.Any(n => !double.IsFinite(n) || Math.Abs(n) > 1e9)))
            throw Invalid("view_center", "Expected two finite view coordinates within ±1e9 model mm.");
        if (p.ViewHeight is { } height && (!double.IsFinite(height) || height is < 1e-9 or > 1e9)) throw Invalid("view_height", "Expected 1e-9..1e9 model mm.");
        if (p.TwistAngle is { } angle && (!double.IsFinite(angle) || angle < 0 || angle >= 2 * Math.PI)) throw Invalid("twist_angle", "Expected radians in [0, 2*pi).");
    }
    public static int ValidateBatch(IEnumerable<StructureEdit> operations)
    {
        var batch = operations.ToArray(); var edits = batch.OfType<SetViewportViewEdit>().ToArray();
        if (edits.Length > 0)
        {
            if (edits.Length != batch.Length) throw Invalid("operations", "Viewport view edits cannot mix with other structural operations.", "INVALID_OPERATION_ORDER");
            var handles = new HashSet<string>(StringComparer.Ordinal);
            foreach (var edit in edits)
            { Validate(edit); if (!handles.Add(edit.Handle)) throw Invalid("operations", "Each viewport may occur only once per batch.", "INVALID_OPERATION_ORDER"); }
        }
        return LinetypeWriteValidation.ValidateBatch(batch);
    }
    public static void RequireWritable(ViewportProperties before, SetViewportViewEdit operation)
    {
        Validate(operation);
        if (before.number == 1 || before.is_perspective || before.non_rect_clip_on || before.clip_boundary.state != "null" || before.front_clip_on || before.back_clip_on ||
            before.view_direction.Length != 3 || before.view_direction.All(n => n == 0) ||
            before.height <= 0 || before.width <= 0 || before.view_height <= 0 || before.frozen_layers.Any(l => l.state != "resolved"))
            throw new DomainException("UNSUPPORTED_VIEWPORT", "Expected an ordinary rectangular viewport without perspective or clipping; the paper overview viewport is protected.");
        var p = operation.Properties;
        if (before.is_locked && (p.ViewCenter is not null || p.ViewHeight is not null || p.TwistAngle is not null || p.Center is not null || p.Width.HasValue || p.Height.HasValue || p.ViewDirection is not null || p.ViewTarget is not null || p.FrozenLayerHandles is not null) && p.IsLocked != false)
            throw new DomainException("VIEWPORT_LOCKED", "Explicitly include is_locked=false to change a locked viewport view.");
    }
    public static void RequireDesired(ViewportProperties before, ViewportProperties actual, SetViewportViewEdit operation)
    {
        RequireWritable(before, operation); var p = operation.Properties;
        var expected = before with { view_center = p.ViewCenter ?? before.view_center, view_height = p.ViewHeight ?? before.view_height,
            twist_angle = p.TwistAngle ?? before.twist_angle, is_locked = p.IsLocked ?? before.is_locked, is_on = p.IsOn ?? before.is_on,
            view_direction = p.ViewDirection ?? before.view_direction, view_target = p.ViewTarget ?? before.view_target,
            center=p.Center is {} c?new[]{c[0],c[1],before.center[2]}:before.center,width=p.Width??before.width,height=p.Height??before.height };
        if (p.ViewHeight.HasValue||p.Height.HasValue)
        {
            double ratio = expected.height / expected.view_height;
            if (!double.IsFinite(actual.custom_scale) || !double.IsFinite(ratio) || ratio <= 0 ||
                Math.Abs(actual.custom_scale - ratio) > Math.Max(Math.Abs(ratio) * 1e-12, double.Epsilon) || actual.standard_scale_native_value is < 1 or > 34)
                throw new DomainException("API_EXCEPTION", "Native viewport scale is inconsistent with the requested view height.");
            // StandardScale is the SDK-derived classification; it is not a separate write input.
            expected = expected with { custom_scale = actual.custom_scale, standard_scale_native_value = actual.standard_scale_native_value };
        }
        if (p.FrozenLayerHandles is { } frozen)
        {
            if (actual.frozen_layers.Any(l => l.state != "resolved") || !actual.frozen_layers.Select(l => l.@ref!.handle).OrderBy(h => h, StringComparer.Ordinal).SequenceEqual(frozen.OrderBy(h => h, StringComparer.Ordinal)))
                throw new DomainException("API_EXCEPTION", "Viewport frozen layers differ from the requested layer handles.");
            expected = expected with { frozen_layers = actual.frozen_layers };
        }
        RequireSame(expected, actual);
    }
    public static void RequireSame(ViewportProperties expected, ViewportProperties actual)
    {
        // A camera direction represents orientation; SDK normalization is harmless.
        if (SameDirection(expected.view_direction, actual.view_direction)) expected = expected with { view_direction = actual.view_direction };
        // Frozen layers are a set. Native enumeration order is not an engineering setting.
        expected = expected with { frozen_layers = expected.frozen_layers.OrderBy(l => l.@ref?.handle, StringComparer.Ordinal).ToArray() };
        actual = actual with { frozen_layers = actual.frozen_layers.OrderBy(l => l.@ref?.handle, StringComparer.Ordinal).ToArray() };
        if (!JsonSerializer.SerializeToUtf8Bytes(expected, Wire.Json).AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(actual, Wire.Json)))
        {
            var expectedFields = Wire.Element(expected); var actualFields = Wire.Element(actual);
            string field = expectedFields.EnumerateObject().First(p => p.Value.GetRawText() != actualFields.GetProperty(p.Name).GetRawText()).Name;
            throw new DomainException("API_EXCEPTION", "Viewport edit changed unexpected metadata or did not preserve the requested view; batch cannot commit.", false, new { property = field });
        }
    }
    public static bool SameDirection(double[] expected, double[] actual)
    {
        if (expected.Length != 3 || actual.Length != 3 || expected.Concat(actual).Any(n => !double.IsFinite(n))) return false;
        double[]? Unit(double[] value)
        {
            double scale = value.Max(Math.Abs); if (scale == 0) return null;
            var scaled = value.Select(n => n / scale).ToArray(); double length = Math.Sqrt(scaled.Sum(n => n * n));
            return scaled.Select(n => n / length).ToArray();
        }
        var left = Unit(expected); var right = Unit(actual);
        return left is not null && right is not null && Enumerable.Range(0, 3).All(i => Math.Abs(left[i] - right[i]) <= 1e-12);
    }
}
