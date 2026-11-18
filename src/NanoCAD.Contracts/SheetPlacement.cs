using System.Text.Json;

namespace NanoCAD.Contracts;

public readonly record struct SheetBox(double Left, double Bottom, double Right, double Top)
{
    public bool Valid => double.IsFinite(Left) && double.IsFinite(Bottom) && double.IsFinite(Right) && double.IsFinite(Top)
        && Left <= Right && Bottom <= Top;
    public bool HasArea => Valid && Left < Right && Bottom < Top;
    public double[] Coordinates() => [Left, Bottom, Right, Top];
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
    public static SheetPlacementRequest Parse(JsonElement input)
    {
        DomainException Invalid(string reason) => new("INVALID_ARGUMENT", "sheet_placement: " + reason);
        if (input.ValueKind != JsonValueKind.Object) throw Invalid("expected an object.");
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var item in input.EnumerateObject())
            if (item.Name is not ("view_handle" or "proposed_origin" or "available_area" or "clearance" or "start_index" or "page_size")
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
        return new(Validation.NormalizeHandle(handle.GetString()!), xy == null ? null : new(xy[0], xy[1]),
            area, gap, Index("start_index", 0, 0), Index("page_size", 100, 1));
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
