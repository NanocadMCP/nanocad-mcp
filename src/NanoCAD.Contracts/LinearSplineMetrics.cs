namespace NanoCAD.Contracts;

public sealed record LinearSplineMetric(double Length,double[] Min,double[] Max,double? Area);

public static class LinearSplineMetrics
{
    // On each nonzero knot interval, positive degree-one NURBS weights give
    // a monotone traversal of the straight segment between adjacent controls.
    // Thus the full closed clamped curve has exact segment-sum length and
    // coordinate extrema at the controls, regardless of knot normalization.
    public static LinearSplineMetric? TryCalculate(SplineGeometry spline)
    {
        SplineVerification.Verify(spline);
        var points=spline.control_points;int count=points.Length;
        if(spline.degree!=1||!spline.closed||!points[0].SequenceEqual(points[^1])||
            spline.knots[0]!=spline.start_parameter||spline.knots[1]!=spline.start_parameter||
            spline.knots[count]!=spline.end_parameter||spline.knots[count+1]!=spline.end_parameter)return null;
        for(int i=1;i<count;i++)if(spline.knots[i+1]<=spline.knots[i])return null;
        double length=0,correction=0;
        for(int i=1;i<count;i++)
        {
            double squared=0;for(int axis=0;axis<3;axis++){double d=points[i][axis]-points[i-1][axis];squared+=d*d;}
            double term=Math.Sqrt(squared)-correction;double sum=length+term;correction=(sum-length)-term;length=sum;
        }
        if(!double.IsFinite(length)||length<=0)return null;
        return new(length,Enumerable.Range(0,3).Select(axis=>points.Min(p=>p[axis])).ToArray(),
            Enumerable.Range(0,3).Select(axis=>points.Max(p=>p[axis])).ToArray(),SimpleXyArea(spline));
    }
    private static double? SimpleXyArea(SplineGeometry spline)
    {
        // Positive degree-one weights only reparameterize each segment. The
        // polygon is therefore the actual curve, not a smooth control hull.
        var source=spline.control_points;var anchor=source[0];
        if(!spline.planar||source.Any(p=>p[2]!=anchor[2]))return null;
        // Subtract an anchor before products: a small contour far from the
        // origin must not lose its area through large-coordinate cancellation.
        var polygon=source.Take(source.Length-1).Select(p=>new Point2(p[0]-anchor[0],p[1]-anchor[1])).ToArray();
        try{SimplePolygonValidation.Validate(polygon);}
        catch(DomainException error)when(error.Error.code=="INVALID_GEOMETRY"){return null;}
        double sum=0,correction=0;
        for(int i=0;i<polygon.Length;i++)
        {
            var a=polygon[i];var b=polygon[(i+1)%polygon.Length];
            double term=(a.X*b.Y-a.Y*b.X)-correction;
            double next=sum+term;correction=(next-sum)-term;sum=next;
        }
        double area=Math.Abs(sum)*0.5;
        return double.IsFinite(area)&&area>0?area:null;
    }
}
