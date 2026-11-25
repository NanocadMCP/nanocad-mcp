namespace NanoCAD.Contracts;

public sealed record CylindricalFit(double axial_overlap, double axial_gap, double? minimum_radial_clearance, string fit_status)
{
    // All stations use the shaft axis and drawing units. A negative margin is
    // meaningful only for a longitudinally engaged shaft with its axis inside the bore.
    public static CylindricalFit Measure(double shaftStart, double shaftEnd, double boreStart, double boreEnd,
        double shaftRadius, double boreRadius, double offset, double tolerance)
    {
        if (!new[] { shaftStart, shaftEnd, boreStart, boreEnd, shaftRadius, boreRadius, offset, tolerance }.All(double.IsFinite) ||
            shaftStart >= shaftEnd || boreStart >= boreEnd || shaftRadius <= 0 || boreRadius <= 0 || offset < 0 || tolerance < 0)
            throw new DomainException("INVALID_GEOMETRY", "Cylindrical fit requires finite ordered bands, positive radii and a nonnegative offset and tolerance.");
        double overlap = Math.Min(shaftEnd, boreEnd) - Math.Max(shaftStart, boreStart);
        if (overlap <= tolerance)
            return new(Math.Max(0, overlap), Math.Max(0, -overlap), null, overlap < -tolerance ? "axially_separated" : "end_contact_within_tolerance");
        if (offset >= boreRadius)
            return new(overlap, 0, null, "shaft_axis_outside_bore");
        double margin = boreRadius - shaftRadius - offset;
        return new(overlap, 0, margin, margin > tolerance ? "clearance" : margin < -tolerance ? "interference" : "contact_within_tolerance");
    }

    // Distinct circular trim edges must tile the complete rim exactly once.
    public static bool CoversRim(IEnumerable<(double Start, double Sweep)> spans, double angularTolerance)
    {
        var intervals = new List<(double Low, double High)>();
        foreach (var (start, sweep) in spans)
        {
            if (!double.IsFinite(start) || !double.IsFinite(sweep) || sweep == 0 || Math.Abs(sweep) > Math.Tau + angularTolerance) return false;
            double lower = (sweep < 0 ? start + sweep : start) % Math.Tau;
            if (lower < 0) lower += Math.Tau;
            double upper = lower + Math.Abs(sweep);
            intervals.Add((lower, Math.Min(upper, Math.Tau)));
            if (upper > Math.Tau) intervals.Add((0, upper - Math.Tau));
        }
        double end = 0;
        foreach (var interval in intervals.OrderBy(i => i.Low))
        {
            if (Math.Abs(interval.Low - end) > angularTolerance) return false;
            end = Math.Max(end, interval.High);
        }
        return intervals.Count > 0 && Math.Abs(end - Math.Tau) <= angularTolerance;
    }
}
