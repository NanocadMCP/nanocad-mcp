using System.Text.Json;

namespace NanoCAD.Contracts;

// Native sign is intentionally explicit: it is not a guessed left/inward
// interpretation of every kernel curve family and orientation.
public sealed record NativeOffsetCurveSpec341(string[] Targets, double Distance, string Side,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] SketchPlaneSpec? Workplane = null) : EditSpec("offset_curve", Targets)
{
    public double SignedDistance => Side == "native_positive" ? Distance : -Distance;
}
public static class NativeContourOffsetValidation341
{
    public static bool HandlesSide(string side) => side is "native_positive" or "native_negative";
    private static DomainException Invalid(string path, string message) => new("INVALID_ARGUMENT", path + ": " + message, false, new { path });
    private static string Text(JsonElement value, string path)
    { if (value.ValueKind != JsonValueKind.String) throw Invalid(path, "Expected string."); try { return value.GetString()!; } catch (InvalidOperationException) { throw Invalid(path, "Invalid Unicode scalar text."); } }
    public static bool NativeMode(JsonElement input, string path)
    {
        if (input.TryGetProperty("mode", out var mode))
            return Text(mode, path + ".mode") switch
            {
                "native" => true,
                "legacy" => false,
                _ => throw Invalid(path + ".mode", "Use native or legacy.")
            };
        return input.TryGetProperty("signed_distance", out _) ||
            input.TryGetProperty("side", out var side) && HandlesSide(Text(side, path + ".side"));
    }
    public static NativeOffsetCurveSpec341 Parse(JsonElement input, string path, string[] handles)
    {
        if (handles.Length != 1) throw Invalid(path + ".handles", "Select one existing planar native curve.");
        if (input.ValueKind != JsonValueKind.Object) throw Invalid(path, "Expected object.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in input.EnumerateObject()) if (!names.Add(property.Name) || property.Name is not ("operation" or "handles" or "distance" or "side" or "workplane" or "signed_distance" or "mode")) throw Invalid(path + "." + property.Name, "Unknown or duplicate field.");
        if (!input.TryGetProperty("operation", out var operation) || Text(operation, path + ".operation") != "offset_curve" || !NativeMode(input, path)) throw Invalid(path + ".operation", "Expected native offset_curve.");
        string side;
        double magnitude;
        if (input.TryGetProperty("signed_distance", out var signed))
        {
            if (names.Contains("side") || names.Contains("distance"))
                throw Invalid(path + ".signed_distance", "Choose signed_distance or the native side and distance pair.");
            if (signed.ValueKind != JsonValueKind.Number || !signed.TryGetDouble(out double value) || !double.IsFinite(value) || value == 0 || Math.Abs(value) > 1e6)
                throw Invalid(path + ".signed_distance", "Expected a nonzero signed native distance within +/-1000000 drawing millimeters.");
            side = value > 0 ? "native_positive" : "native_negative";
            magnitude = Math.Abs(value);
        }
        else
        {
            if (!input.TryGetProperty("side", out var direction)) throw Invalid(path + ".side", "Expected native_positive or native_negative.");
            side = Text(direction, path + ".side");
            if (!HandlesSide(side)) throw Invalid(path + ".side", "Expected explicit native_positive or native_negative; no left/inward sign mapping is inferred.");
            if (!input.TryGetProperty("distance", out var distance) || distance.ValueKind != JsonValueKind.Number || !distance.TryGetDouble(out magnitude) || !double.IsFinite(magnitude) || magnitude <= 0 || magnitude > 1e6)
                throw Invalid(path + ".distance", "Expected finite strictly positive native distance up to1000000 drawing millimeters.");
        }
        var frame = input.TryGetProperty("workplane", out var workplane) ? DrawingWorkplane.Parse(workplane) : null;
        return new(handles, magnitude, side, frame);
    }
    public static void Batch(IReadOnlyList<EditSpec> operations)
    {
        if (operations.Any(edit => edit is NativeOffsetCurveSpec341) && operations.Count != 1)
            throw new DomainException("INVALID_OPERATION_ORDER", "Native offset must be the sole operation: its read-only source and complete generated roots are verified together.");
    }
}
