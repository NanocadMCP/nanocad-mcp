namespace NanoCAD.Contracts;

public static class LinearSplineSegments
{
    // Positive degree1 weights only change speed along each actual segment.
    // Clamped endpoints and strictly increasing internal spans are required;
    // trimmed/discontinuous curves cannot use their control polygon directly.
    public static double[][]? TryVertices(SplineGeometry spline)
    {
        SplineVerification.Verify(spline);int n=spline.control_points.Length;
        if(spline.degree!=1||!SplineVerification.LiesInWcsXy(spline)||
            spline.knots[0]!=spline.start_parameter||spline.knots[1]!=spline.start_parameter||
            spline.knots[n]!=spline.end_parameter||spline.knots[n+1]!=spline.end_parameter||
            spline.closed&&!spline.control_points[0].SequenceEqual(spline.control_points[^1]))return null;
        for(int i=1;i<n;i++)if(spline.knots[i+1]<=spline.knots[i])return null;
        return spline.control_points;
    }
}
