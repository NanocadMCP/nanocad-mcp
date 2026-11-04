using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record LineFromNativeSnapSpec(Point2? Start, Point2? End, NativeCurveSnap? StartSnap,
    NativeCurveSnap? EndSnap, string LayerName) : EntitySpec("Line", LayerName);

public sealed record ResolvedNativeSnapLineSpec(Point3 Start, Point3 End, string LayerName) : EntitySpec("Line", LayerName);

public sealed record NativeCurveSnap(string Handle, string ExpectedCurveToken, string Snap,
    Point3? ReferencePointWcs, double? Tolerance, string? OtherHandle, string? OtherExpectedCurveToken)
{
    internal static NativeCurveSnap Parse(JsonElement value, string path)
    {
        DomainException Bad(string field, string message) =>
            new("INVALID_ARGUMENT", path + "." + field + ": " + message, false, new { path = path + "." + field });
        string Text(string field)
        {
            if (!value.TryGetProperty(field, out var item) || item.ValueKind != JsonValueKind.String)
                throw Bad(field, "Expected a string.");
            try { return item.GetString()!; }
            catch (InvalidOperationException) { throw Bad(field, "Invalid Unicode scalar text."); }
        }
        string Token(string field)
        {
            var token = Text(field);
            if (token.Length != 64 || token.Any(c => !Uri.IsHexDigit(c)))
                throw Bad(field, "Expected the current geometry.native_curve.curve_token.");
            return token.ToUpperInvariant();
        }
        if (value.ValueKind != JsonValueKind.Object)
            throw new DomainException("INVALID_ARGUMENT", path + ": Expected a native curve snap object.", false, new { path });
        var snap = Text("snap");
        if (snap is not ("start" or "end" or "mid" or "center" or "nearest" or "intersection"))
            throw Bad("snap", "Use start, end, mid, center, nearest or intersection.");
        bool picked = snap is "nearest" or "intersection";
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in value.EnumerateObject())
        {
            bool allowed = field.Name is "handle" or "expected_curve_token" or "snap" ||
                (picked && field.Name is ("reference_point_wcs" or "tolerance")) ||
                (snap == "intersection" && field.Name is ("other_handle" or "other_expected_curve_token"));
            if (!seen.Add(field.Name) || !allowed) throw Bad(field.Name, "Unknown or duplicate field for this snap.");
        }
        var handle = Validation.NormalizeHandle(Text("handle"));
        var token = Token("expected_curve_token");
        Point3? reference = null;
        double? tolerance = null;
        if (picked)
        {
            if (!value.TryGetProperty("reference_point_wcs", out var point))
                throw Bad("reference_point_wcs", "Supply the WCS XYZ pick.");
            reference = PointCoordinates408.Parse(point, path + ".reference_point_wcs");
            if (!value.TryGetProperty("tolerance", out var number) || number.ValueKind != JsonValueKind.Number ||
                !number.TryGetDouble(out double distance) || !double.IsFinite(distance) || distance <= 0)
                throw Bad("tolerance", "Supply a strictly positive finite pick distance in drawing millimeters.");
            tolerance = distance;
        }
        if (snap != "intersection") return new(handle, token, snap, reference, tolerance, null, null);
        var other = Validation.NormalizeHandle(Text("other_handle"));
        if (handle == other) throw Bad("other_handle", "Use a second distinct curve.");
        return new(handle, token, snap, reference, tolerance, other, Token("other_expected_curve_token"));
    }
}

public static partial class CreateValidation
{
    private static LineFromNativeSnapSpec ParseSnappedLine(JsonElement item, string path, string layer)
    {
        Shape(item, path, "type", "layer", "start", "end", "start_snap", "end_snap");
        (Point2?, NativeCurveSnap?) EndPoint(string field)
        {
            string snapField = field + "_snap";
            bool literal = item.TryGetProperty(field, out _);
            bool snapped = item.TryGetProperty(snapField, out var snap);
            if (literal == snapped) throw Invalid(path, "Choose one " + field + " or " + snapField + ".");
            return literal ? (Point(item, field, path), null) : (null, NativeCurveSnap.Parse(snap, path + "." + snapField));
        }
        var (start, startSnap) = EndPoint("start");
        var (end, endSnap) = EndPoint("end");
        return new(start, end, startSnap, endSnap, layer);
    }
}
