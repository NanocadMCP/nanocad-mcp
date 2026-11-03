namespace NanoCAD.Contracts;

// Physical coverage for a complete native NURBS definition or one analytic
// ellipse arc. This is deliberately not an arbitrary NURBS equivalence solver:
// split/degree-elevated curves need native region verification, not faceting.
public static class CurvedProfileBoundary445
{
    static bool Near(double a,double b)=>DrawingProfileGeometry.Near(a,b);
    static bool Point(double[] a,double[] b)=>a.Length==3&&b.Length==3&&a.Zip(b).All(pair=>Near(pair.First,pair.Second));
    static bool Point(Point3 a,Point3 b)=>Near(a.X,b.X)&&Near(a.Y,b.Y)&&Near(a.Z,b.Z);
    static double Dot(Point3 a,Point3 b)=>a.X*b.X+a.Y*b.Y+a.Z*b.Z;
    static double Norm(double angle){angle%=2*Math.PI;return angle<0?angle+2*Math.PI:angle;}
    static bool Angle(double a,double b)=>Math.Min(Norm(a-b),Norm(b-a))<=1e-8;
    public static bool Same(SketchConstraintCurve a,SketchConstraintCurve b,Action? cancel=null)
    {
        cancel?.Invoke();if(a.Kind!=b.Kind)return false;
        if(a.Kind=="spline")return a.Spline!=null&&b.Spline!=null&&Spline(a.Spline,b.Spline,cancel);
        return a.Kind=="ellipse"&&a.Ellipse!=null&&b.Ellipse!=null&&Ellipse(a.Ellipse,b.Ellipse);
    }
    static bool Spline(CurveSplineGeometry445 a,CurveSplineGeometry445 b,Action? cancel)
    {
        if(a.degree!=b.degree||a.periodic!=b.periodic||a.closed!=b.closed||a.control_points.Length!=b.control_points.Length||a.knots.Length!=b.knots.Length)return false;
        int count=a.control_points.Length;if(count==0||a.knots.Length==0)return false;
        double spanA=a.end_parameter-a.start_parameter,spanB=b.end_parameter-b.start_parameter;
        if(!double.IsFinite(spanA)||!double.IsFinite(spanB)||spanA<=0||spanB<=0)return false;
        bool Weights(CurveSplineGeometry445 value)=>value.weights.Length==0||value.weights.Length==count&&value.weights.All(w=>double.IsFinite(w)&&w>0);
        if(!Weights(a)||!Weights(b))return false;
        double Weight(CurveSplineGeometry445 value,int index)=>value.weights.Length==0?1:value.weights[index];
        bool Match(bool reverse)
        {
            double scaleA=Weight(a,0),scaleB=Weight(b,reverse?count-1:0);
            for(int i=0;i<count;i++)
            {
                cancel?.Invoke();int j=reverse?count-1-i:i;
                if(!Point(a.control_points[i],b.control_points[j])||!Near(Weight(a,i)/scaleA,Weight(b,j)/scaleB))return false;
            }
            for(int i=0;i<a.knots.Length;i++)
            {
                cancel?.Invoke();double u=(a.knots[i]-a.start_parameter)/spanA;
                double v=reverse?(b.end_parameter-b.knots[^(i+1)])/spanB:(b.knots[i]-b.start_parameter)/spanB;
                if(!Near(u,v))return false;
            }
            return true;
        }
        return Match(false)||Match(true);
    }
    static bool Ellipse(NativeSketchEllipseGeometry445 a,NativeSketchEllipseGeometry445 b)
    {
        if(!Point(a.Center,b.Center)||!Near(a.MajorRadius,b.MajorRadius)||!Near(a.MinorRadius,b.MinorRadius))return false;
        double uu=Dot(a.MajorAxis,b.MajorAxis),uv=Dot(a.MajorAxis,b.MinorAxis),vu=Dot(a.MinorAxis,b.MajorAxis),vv=Dot(a.MinorAxis,b.MinorAxis);
        if(!Near(uu*uu+vu*vu,1)||!Near(uv*uv+vv*vv,1)||!Near(uu*uv+vu*vv,0))return false;
        // For unequal radii the principal axes may reverse, but cannot rotate
        // into an arbitrary orientation without changing the ellipse itself.
        if(!Near(a.MajorRadius,a.MinorRadius)&&(!Near(Math.Abs(uu),1)||!Near(Math.Abs(vv),1)||!Near(uv,0)||!Near(vu,0)))return false;
        double direction=uu*vv-uv*vu;if(!Near(Math.Abs(direction),1))return false;
        double spanA=a.EndAngle-a.StartAngle,spanB=b.EndAngle-b.StartAngle;
        if(!Near(spanA,spanB)||spanA<=0)return false;
        if(Near(spanA,2*Math.PI))return true;
        double offset=Math.Atan2(vu,uu);
        double firstB=direction>0?b.StartAngle:b.EndAngle;
        return Angle(a.StartAngle,offset+direction*firstB);
    }
}
