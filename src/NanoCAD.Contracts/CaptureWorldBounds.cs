using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record CaptureWorldBounds(double MinX, double MinY, double MaxX, double MaxY, double Z)
{
    public object ToWire() => new { min = new[] { MinX, MinY }, max = new[] { MaxX, MaxY }, z = Z };

    public static CaptureWorldBounds Parse(JsonElement input)
    {
        static DomainException Invalid(string field, string rule) =>
            new("INVALID_ARGUMENT", rule, false, new { path = "$.bounds" + field });
        if (input.ValueKind != JsonValueKind.Object) throw Invalid("", "bounds must be a WCS rectangle object.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in input.EnumerateObject())
        {
            if (property.Name is not ("min" or "max" or "z")) throw Invalid("." + property.Name, "Unknown bounds field.");
            if (!seen.Add(property.Name)) throw Invalid("." + property.Name, "Duplicate bounds field.");
        }
        double[] Point(string name)
        {
            if (!input.TryGetProperty(name, out var point) || point.ValueKind != JsonValueKind.Array || point.GetArrayLength() != 2)
                throw Invalid("." + name, "Expected exactly two finite WCS coordinates [x,y].");
            return point.EnumerateArray().Select((value, index) => Number(value, $".{name}[{index}]")).ToArray();
        }
        static double Number(JsonElement value, string field) =>
            value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number)
                ? number : throw Invalid(field, "Expected a finite WCS coordinate.");
        var min = Point("min");
        var max = Point("max");
        for (int axis = 0; axis < 2; axis++)
            if (min[axis] >= max[axis]) throw Invalid($".max[{axis}]", "Each maximum must be greater than its corresponding minimum.");
        return new(min[0], min[1], max[0], max[1], input.TryGetProperty("z", out var z) ? Number(z, ".z") : 0);
    }
}

public readonly record struct CaptureViewportPixels(int Width, int Height, double Scale)
{
    public static CaptureViewportPixels Bind(int viewportWidth, int viewportHeight, int logicalWidth, int logicalHeight,
        int physicalWidth, int physicalHeight)
    {
        DomainException Unavailable() => new("CAPTURE_BOUNDS_UNAVAILABLE",
            "The native viewport and its logical-to-physical window mapping could not be verified.");
        // The renderer occupies the full width; the client can include layout
        // tabs below it. A partial-width viewport needs its own window origin.
        if (viewportWidth < 1 || viewportHeight < 1 || viewportWidth != logicalWidth || viewportHeight > logicalHeight ||
            physicalWidth < 1 || physicalHeight < 1) throw Unavailable();
        double scale = (double)physicalWidth / logicalWidth;
        // Integer rectangles may round either edge by one device pixel.
        if (Math.Abs(logicalHeight * scale - physicalHeight) > 2) throw Unavailable();
        int height = (int)Math.Min(physicalHeight, Math.Floor(viewportHeight * scale));
        if (height < 1) throw Unavailable();
        return new(physicalWidth, height, scale);
    }
}

// Right and bottom are exclusive. The crop conservatively encloses all four
// projected corners in viewport-local physical pixels, before PNG scaling.
public sealed record CapturePixelRect(int left, int top, int right, int bottom)
{
    public bool Fits(int width, int height) => left >= 0 && top >= 0 && left < right && top < bottom && right <= width && bottom <= height;

    public static CapturePixelRect? Read(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object) return null;
        bool Edge(string name, out int value)
        {
            value = 0;
            return input.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.Number && field.TryGetInt32(out value);
        }
        return Edge("left", out int left) && Edge("top", out int top) && Edge("right", out int right) && Edge("bottom", out int bottom)
            ? new(left, top, right, bottom) : null;
    }

    public static CapturePixelRect Enclose((double X, double Y) origin, (double X, double Y) xCorner,
        (double X, double Y) yCorner, (double X, double Y) farCorner, int width, int height)
    {
        var corners = new[] { origin, xCorner, yCorner, farCorner };
        if (corners.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
            throw new DomainException("CAPTURE_BOUNDS_UNAVAILABLE", "The native projection returned nonfinite pixels.");
        if (corners.Any(point => point.X < 0 || point.X > width || point.Y < 0 || point.Y > height))
            throw new DomainException("CAPTURE_BOUNDS_OUTSIDE_VIEWPORT", "Part of the requested region is outside the current viewport.");
        double area = (xCorner.X - origin.X) * (yCorner.Y - origin.Y) - (xCorner.Y - origin.Y) * (yCorner.X - origin.X);
        if (area == 0)
            throw new DomainException("CAPTURE_BOUNDS_UNAVAILABLE", "The requested WCS rectangle projects edge-on or collapses in the current view.");
        var crop = new CapturePixelRect((int)Math.Floor(corners.Min(point => point.X)), (int)Math.Floor(corners.Min(point => point.Y)),
            (int)Math.Ceiling(corners.Max(point => point.X)), (int)Math.Ceiling(corners.Max(point => point.Y)));
        if (!crop.Fits(width, height))
            throw new DomainException("CAPTURE_BOUNDS_UNAVAILABLE", "The projection has no usable viewport pixels.");
        return crop;
    }
}
