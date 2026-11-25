using System.Text.Json;

namespace NanoCAD.Contracts;

public readonly record struct SheetBox(double Left, double Bottom, double Right, double Top)
{
    public bool Valid => double.IsFinite(Left) && double.IsFinite(Bottom) && double.IsFinite(Right) && double.IsFinite(Top)
        && Left <= Right && Bottom <= Top;
    public bool HasArea => Valid && Left < Right && Bottom < Top;
    public double[] Coordinates() => [Left, Bottom, Right, Top];
    public SheetBox Union(SheetBox b) => new(Math.Min(Left, b.Left), Math.Min(Bottom, b.Bottom), Math.Max(Right, b.Right), Math.Max(Top, b.Top));
    public SheetBox Expand(double margin) => new(Left - margin, Bottom - margin, Right + margin, Top + margin);
    public SheetBox Shift(double dx, double dy) => new(Left + dx, Bottom + dy, Right + dx, Top + dy);
    // Returning an absolute origin and reading it back adds two roundings to a solved translation.
    // Treat adjoining representable doubles at an envelope boundary as contact.
    private static bool Before(double a, double b) => a < b && Math.BitIncrement(Math.BitIncrement(a)) < b;
    public bool Contains(SheetBox b) => Valid && b.Valid && !Before(b.Left, Left) && !Before(b.Bottom, Bottom)
        && !Before(Right, b.Right) && !Before(Top, b.Top);
    public bool Conflicts(SheetBox b, double gap) => Before(Left, b.Right + gap) && Before(b.Left - gap, Right)
        && Before(Bottom, b.Top + gap) && Before(b.Bottom - gap, Top);
}

public sealed record SheetPlacementRequest(string View, Point2? Proposed, SheetBox? Area, double Clearance, int Start, int Size)
{
    public bool CompactSpacing { get; init; }
    public string? FrameHandle { get; init; }
    public bool IncludeAlignedGroup { get; init; }
    public SheetBox[] ReservedAreas { get; init; } = [];
    public double AnnotationMargin { get; init; }
    public Dictionary<string, string> MemberSides { get; init; } = new(StringComparer.Ordinal);

    public static SheetPlacementRequest Parse(JsonElement input)
    {
        DomainException Invalid(string reason) => new("INVALID_ARGUMENT", "sheet_placement: " + reason);
        if (input.ValueKind != JsonValueKind.Object) throw Invalid("expected an object.");
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var item in input.EnumerateObject())
            if (item.Name is not ("view_handle" or "proposed_origin" or "available_area" or "clearance" or "start_index" or "page_size" or "include_aligned_group" or "annotation_margin" or "member_sides" or "reserved_areas" or "compact_spacing" or "frame_handle")
                || !fields.TryAdd(item.Name, item.Value)) throw Invalid("unknown or duplicate field " + item.Name);
        if (!fields.TryGetValue("view_handle", out var handle) || handle.ValueKind != JsonValueKind.String)
            throw Invalid("view_handle must identify a view on the selected paper layout.");
        double Number(JsonElement n) => n.ValueKind == JsonValueKind.Number && n.TryGetDouble(out var v) && double.IsFinite(v)
            ? v : throw Invalid("coordinates and clearance must be finite numbers.");
        double[]? ArrayField(string key, int count)
        {
            if (!fields.TryGetValue(key, out var val)) return null;
            if (val.ValueKind != JsonValueKind.Array || val.GetArrayLength() != count) throw Invalid(key + " has the wrong coordinate count.");
            return val.EnumerateArray().Select(Number).ToArray();
        }
        int Index(string key, int fallback, int minimum) => !fields.TryGetValue(key, out var val) ? fallback
            : val.ValueKind == JsonValueKind.Number && val.TryGetInt32(out int n) && n >= minimum ? n : throw Invalid("invalid " + key);
        var xy = ArrayField("proposed_origin", 2);
        var bounds = ArrayField("available_area", 4);
        SheetBox? area = bounds == null ? null : new(bounds[0], bounds[1], bounds[2], bounds[3]);
        if (area is { HasArea: false }) throw Invalid("available_area must have positive width and height.");
        double gap = fields.TryGetValue("clearance", out var requested) ? Number(requested) : 0;
        if (gap < 0) throw Invalid("clearance must be nonnegative.");
        bool group = fields.TryGetValue("include_aligned_group", out var mode) && mode.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw Invalid("include_aligned_group must be a boolean.")
        };
        double margin = fields.TryGetValue("annotation_margin", out var reserve) ? Number(reserve) : 0;
        if (margin < 0 || !group && (fields.ContainsKey("annotation_margin") || fields.ContainsKey("member_sides")))
            throw Invalid("annotation_margin and member_sides require group mode and a nonnegative margin.");
        bool compact = false;
        string? frame = null;
        if (fields.TryGetValue("compact_spacing", out var spacing))
        {
            if (!group || spacing.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw Invalid("compact_spacing requires group mode and a boolean.");
            compact = spacing.GetBoolean();
        }
        if (fields.TryGetValue("frame_handle", out var frameId))
        {
            if (!group || frameId.ValueKind != JsonValueKind.String) throw Invalid("frame_handle requires group mode and the sheet border block handle.");
            frame = Validation.NormalizeHandle(frameId.GetString()!);
        }
        var reserved = new List<SheetBox>();
        if (fields.TryGetValue("reserved_areas", out var exclusions))
        {
            if (!group || exclusions.ValueKind != JsonValueKind.Array) throw Invalid("reserved_areas is an array of fixed paper rectangles in group mode.");
            foreach (var corners in exclusions.EnumerateArray())
            {
                if (corners.ValueKind != JsonValueKind.Array || corners.GetArrayLength() != 4) throw Invalid("A reserved area needs four coordinates.");
                var box = new SheetBox(Number(corners[0]), Number(corners[1]), Number(corners[2]), Number(corners[3]));
                if (!box.HasArea) throw Invalid("A reserved area must have positive width and height.");
                reserved.Add(box);
            }
        }
        var sides = new Dictionary<string, string>(StringComparer.Ordinal);
        if (fields.TryGetValue("member_sides", out var directions))
        {
            if (directions.ValueKind != JsonValueKind.Object) throw Invalid("member_sides must map child handles to paper sides.");
            foreach (var item in directions.EnumerateObject())
            {
                if (item.Value.ValueKind != JsonValueKind.String || item.Value.GetString() is not ("left" or "right" or "above" or "below") ||
                    !sides.TryAdd(Validation.NormalizeHandle(item.Name), item.Value.GetString()!))
                    throw Invalid("Each child handle must specify one of left, right, above or below.");
            }
        }
        return new(Validation.NormalizeHandle(handle.GetString()!), xy == null ? null : new(xy[0], xy[1]),
            area, gap, Index("start_index", 0, 0), Index("page_size", 100, 1))
        { IncludeAlignedGroup = group, AnnotationMargin = margin, CompactSpacing = compact, FrameHandle = frame, MemberSides = sides, ReservedAreas = reserved.ToArray() };
    }
}

public static class SheetPlacementSolver
{
    // Coordinates below are translations of the target's current footprint.
    // For a fixed X, each obstacle forbids an open interval of Y translations.
    // The nearest feasible point occurs at a requested, area-edge or obstacle-edge X.
    public static Point2? Nearest(SheetBox target, IReadOnlyList<SheetBox> fixedBoxes, SheetBox translations,
        Point2 preferred, double gap, Action check)
    {
        if (!translations.Valid) return null;
        double x0 = Math.Clamp(preferred.X, translations.Left, translations.Right);
        double y0 = Math.Clamp(preferred.Y, translations.Bottom, translations.Top);
        var xs = new HashSet<double> { x0 };
        foreach (var box in fixedBoxes)
        {
            check();
            xs.Add(Math.Clamp(box.Left - gap - target.Right, translations.Left, translations.Right));
            xs.Add(Math.Clamp(box.Right + gap - target.Left, translations.Left, translations.Right));
        }
        Point2? best = null;
        double bestDistance = double.PositiveInfinity;
        foreach (var x in xs.OrderBy(value => Math.Abs(value - preferred.X)).ThenBy(value => value))
        {
            check();
            double dx = x - preferred.X;
            if (dx * dx > bestDistance) break;
            var blocked = new List<(double Lower, double Upper)>();
            foreach (var box in fixedBoxes)
            {
                check();
                if (x > box.Left - gap - target.Right && x < box.Right + gap - target.Left)
                    blocked.Add((box.Bottom - gap - target.Top, box.Top + gap - target.Bottom));
            }
            blocked.Sort();
            double nearest = y0;
            for (int i = 0; i < blocked.Count; i++)
            {
                check();
                var (low, high) = blocked[i];
                while (i + 1 < blocked.Count && blocked[i + 1].Lower < high)
                    high = Math.Max(high, blocked[++i].Upper);
                if (!(low < y0 && y0 < high)) continue;
                bool down = low >= translations.Bottom;
                bool up = high <= translations.Top;
                if (!down && !up) nearest = double.NaN;
                else nearest = down && (!up || y0 - low <= high - y0) ? low : high;
                break;
            }
            if (!double.IsFinite(nearest)) continue;
            double dy = nearest - preferred.Y;
            double distance = dx * dx + dy * dy;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = new(x, nearest);
                if (distance == 0) break;
            }
        }
        return best;
    }
}
