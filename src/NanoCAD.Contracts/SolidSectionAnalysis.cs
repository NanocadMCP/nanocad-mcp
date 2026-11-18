using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record SolidSectionRequest(SketchPlaneSpec Plane, Point2[]? Segment, Point2[]? Window);

public static class SolidSectionAnalysis
{
    public const string Section = "solid_section";
    public const string Intervals = "section_material_intervals";

    public static bool Handles(string? mode) => mode is Section or Intervals;

    public static GeometryAnalysisArguments Parse(JsonElement input)
    {
        var read = new Dictionary<string, JsonElement>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in input.EnumerateObject())
        {
            if (!seen.Add(field.Name))
                throw Invalid("Duplicate section argument: " + field.Name);
            switch (field.Name)
            {
                case "bridge_instance_id":
                case "document_id":
                case "expected_revision":
                case "handles":
                    read.Add(field.Name, field.Value);
                    break;
                case "workplane": case "mode": case "segment": case "window": break;
                default: throw Invalid("Unknown section argument: " + field.Name);
            }
        }
        var args = Validation.Parse("inspect_objects", Wire.Element(read));
        if (args.BridgeId == null || args.DocumentId == null || args.ExpectedRevision == null || args.Handles is not { Length: 1 })
            throw Invalid("Section analysis requires one body handle, bridge, document and expected revision.");
        if (!input.TryGetProperty("workplane", out var workplane)) throw Invalid("An explicit section workplane is required.");
        var frame = DrawingWorkplane.Parse(workplane);
        string mode = input.GetProperty("mode").GetString()!;
        Point2[]? segment = input.TryGetProperty("segment", out var span) ? Pair(span, "segment") : null;
        Point2[]? window = input.TryGetProperty("window", out var bounds) ? Pair(bounds, "window") : null;
        if ((mode == Intervals) != (segment != null) || segment != null && segment[0] == segment[1])
            throw Invalid("section_material_intervals requires two distinct local endpoints; solid_section omits segment.");
        if (window != null && (mode != Section || window[0].X >= window[1].X || window[0].Y >= window[1].Y))
            throw Invalid("A solid_section window must have strictly ordered local XY minimum and maximum corners.");
        return new(args, mode, null, frame) { Section = new(frame, segment, window) };
    }

    private static Point2[] Pair(JsonElement points, string field)
    {
        if (points.ValueKind != JsonValueKind.Array || points.GetArrayLength() != 2)
            throw Invalid(field + " requires two [x,y] points in section coordinates.");
        return points.EnumerateArray().Select(point =>
        {
            if (point.ValueKind != JsonValueKind.Array || point.GetArrayLength() != 2 ||
                point.EnumerateArray().Any(n => n.ValueKind != JsonValueKind.Number || !n.TryGetDouble(out var value) || !double.IsFinite(value)))
                throw Invalid(field + " requires finite section coordinates.");
            return new Point2(point[0].GetDouble(), point[1].GetDouble());
        }).ToArray();
    }

    private static DomainException Invalid(string message) => new("INVALID_ARGUMENT", message);
}
