namespace NanoCAD.Contracts;

/// <summary>Finite tangent construction for the selected interior of two straight rays.</summary>
public static class IndependentFilletGeometry349
{
    public sealed record Plan(double[] FirstPoint, double[] SecondPoint, double[] Center,
        double[] Normal, double[] ReferenceDirection, double Sweep, double Setback);

    public static Plan BetweenLines(double[] corner, double[] firstOpposite, double[] secondOpposite,
        double radius, double pointTolerance, double vectorTolerance)
    {
        if (corner.Length != 3 || firstOpposite.Length != 3 || secondOpposite.Length != 3 ||
            corner.Concat(firstOpposite).Concat(secondOpposite).Any(n => !double.IsFinite(n)) ||
            !double.IsFinite(radius) || radius <= 0 || pointTolerance <= 0 || vectorTolerance <= 0)
            throw new DomainException("INVALID_GEOMETRY", "The selected straight fillet requires finite points and a positive radius.");
        double[] Difference(double[] a, double[] b) => a.Zip(b, (x, y) => x - y).ToArray();
        double Dot(double[] a, double[] b) => a.Zip(b, (x, y) => x * y).Sum();
        double Length(double[] a) => Math.Sqrt(Dot(a, a));
        double[] Scale(double[] a, double k) => a.Select(x => x * k).ToArray();
        double[] Add(double[] a, double[] b) => a.Zip(b, (x, y) => x + y).ToArray();
        double[] Cross(double[] a, double[] b) => [a[1] * b[2] - a[2] * b[1],
            a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
        var v1 = Difference(firstOpposite, corner);
        var v2 = Difference(secondOpposite, corner);
        double l1 = Length(v1), l2 = Length(v2);
        if (!double.IsFinite(l1) || !double.IsFinite(l2) || l1 <= pointTolerance || l2 <= pointTolerance)
            throw new DomainException("INVALID_GEOMETRY", "A selected straight source has no finite retained length.");
        var u1 = Scale(v1, 1 / l1); var u2 = Scale(v2, 1 / l2);
        double cosine = Math.Clamp(Dot(u1, u2), -1, 1);
        if (Length(Cross(u1, u2)) <= vectorTolerance)
            throw new DomainException("AMBIGUOUS_CORNER", "Collinear selected rays have no unique finite fillet plane.");
        double halfSin = Math.Sqrt((1 - cosine) / 2), halfCos = Math.Sqrt((1 + cosine) / 2);
        double setback = radius * halfCos / halfSin;
        if (!double.IsFinite(setback) || setback <= pointTolerance ||
            l1 - setback <= pointTolerance || l2 - setback <= pointTolerance)
            throw new DomainException("INVALID_CORNER_SETBACK", "The requested radius would consume a selected finite source; no extension or collapsed remainder is allowed.");
        var sum = Add(u1, u2);
        var center = Add(corner, Scale(sum, radius / (halfSin * Length(sum))));
        var first = Add(corner, Scale(u1, setback));
        var second = Add(corner, Scale(u2, setback));
        var r1 = Difference(first, center); var r2 = Difference(second, center);
        var cross = Cross(r1, r2);
        var normal = Scale(cross, 1 / Length(cross));
        double sweep = Math.Atan2(Length(cross), Dot(r1, r2));
        if (center.Concat(first).Concat(second).Concat(normal).Any(n => !double.IsFinite(n)) ||
            !double.IsFinite(sweep) || sweep <= vectorTolerance || sweep >= Math.PI)
            throw new DomainException("INVALID_GEOMETRY", "The requested tangent construction is not a finite minor arc.");
        return new(first, second, center, normal, Scale(r1, 1 / Length(r1)), sweep, setback);
    }
}
