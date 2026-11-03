namespace NanoCAD.Contracts;

// Preflight of one bounded region: separated/nested circles or one complete
// simple Line/Arc loop. Native selection remains the writer; this only proves
// the requested point and expected physical boundary before creating a profile.
public static class ModelSketchSelection362
{
    const double Epsilon=1e-7,Tau=2*Math.PI;
    static void Need(bool value,string message){if(!value)throw new DomainException("PROFILE_REGION_AMBIGUOUS",message);}
    public static SketchConstraintCurve[] Select(SketchConstraintCurve[] curves,Point2 point,Action cancel)
    {
        Need(curves.Length>0&&double.IsFinite(point.X)&&double.IsFinite(point.Y),"Select a finite interior point in nonempty sketch geometry.");
        if(curves.All(curve=>curve.Kind=="circle"))
        {
            var selected=ModelCircleRegions350.Select(curves.Select(curve=>new ModelCircleLoop350(curve.Id,curve.Center!,curve.Radius,curve.Id)).ToArray(),[point],cancel).Single();
            Need(selected.hole_element_ids.Length==0,"This typed sweep profile requires one simple selected area without holes.");
            return [curves.Single(curve=>curve.Id==selected.outer_element_id)];
        }
        Need(curves.All(curve=>curve.Kind is "line" or "arc"),"Select separated circles or one complete simple Line/Arc loop.");
        DrawingProfileGeometry.Measure(curves,1,cancel);Need(Inside(curves,point,cancel),"Choose an unambiguous point inside the complete closed Line/Arc region.");return curves;
    }
    public static bool Inside(SketchConstraintCurve[] curves,Point2 point,Action cancel)
    {
        Need(double.IsFinite(point.X)&&double.IsFinite(point.Y),"Choose a finite region point.");
        // A fixed horizontal ray can hit two native arc endpoints whose
        // independently computed ordinates differ by roundoff. Choose a ray
        // away from every vertex bearing, then rigidly express the same
        // curves/query in that ray frame. The analytic crossings and boundary
        // checks below keep their existing roots, sweeps and tolerances.
        var bearings=new List<double>();
        foreach(var curve in curves)
        {
            cancel();
            foreach(var vertex in new[]{curve.Start!,curve.End!})
            {
                double angle=Math.Atan2(vertex.Y-point.Y,vertex.X-point.X)%Math.PI;
                Need(double.IsFinite(angle),"A native boundary vertex is not finite.");
                bearings.Add(angle<0?angle+Math.PI:angle);
            }
        }
        if(bearings.Count==0)return false;
        bearings.Sort();double largest=-1,ray=0;
        for(int i=0;i<bearings.Count;i++)
        {
            cancel();double left=bearings[i],right=i+1<bearings.Count?bearings[i+1]:bearings[0]+Math.PI;
            if(right-left>largest){largest=right-left;ray=(left+right)/2;}
        }
        double cosine=Math.Cos(ray),sine=Math.Sin(ray);
        Point2 Local(Point2 value)
        {double x=value.X-point.X,y=value.Y-point.Y;return new(cosine*x+sine*y,-sine*x+cosine*y);}
        var rotated=new SketchConstraintCurve[curves.Length];
        for(int i=0;i<curves.Length;i++)
        {
            cancel();var curve=curves[i];rotated[i]=curve with
            {Start=Local(curve.Start!),End=Local(curve.End!),Center=curve.Center==null?null:Local(curve.Center)};
        }
        return InsideHorizontal(rotated,new(0,0),cancel);
    }
    static bool InsideHorizontal(SketchConstraintCurve[] curves,Point2 point,Action cancel)
    {
        int crossings=0;
        foreach(var curve in curves)
        {
            cancel();var a=curve.Start!;var b=curve.End!;
            if(curve.Kind=="line")
            {
                double dx=b.X-a.X,dy=b.Y-a.Y,length=dx*dx+dy*dy,u=((point.X-a.X)*dx+(point.Y-a.Y)*dy)/length;
                double closest=Math.Clamp(u,0,1),ex=point.X-(a.X+closest*dx),ey=point.Y-(a.Y+closest*dy);
                Need(ex*ex+ey*ey>Epsilon*Epsilon,"Region point lies on a boundary.");
                if((a.Y>point.Y)!=(b.Y>point.Y)&&a.X+(point.Y-a.Y)*dx/dy>point.X)crossings++;
                continue;
            }
            var center=curve.Center!;double radius=curve.Radius,distance=DrawingProfileGeometry.Distance(point,center);
            Need(Math.Abs(distance-radius)>Epsilon||!DrawingArcContours.OnArc(curve,point),"Region point lies on a circular boundary.");
            double start=Math.Atan2(a.Y-center.Y,a.X-center.X),end=start+curve.Sweep;var breaks=new List<double>{start,end};
            for(int k=-4;k<=4;k++){double extremum=Math.PI/2+k*Math.PI;if(extremum>start&&extremum<end)breaks.Add(extremum);}breaks.Sort();
            double ordinate=(point.Y-center.Y)/radius;if(Math.Abs(ordinate)>1)continue;
            double first=Math.Asin(Math.Clamp(ordinate,-1,1));
            for(int i=0;i<breaks.Count-1;i++)
            {
                double left=breaks[i],right=breaks[i+1],ya=i==0?a.Y:center.Y+radius*Math.Sin(left),yb=i==breaks.Count-2?b.Y:center.Y+radius*Math.Sin(right);
                if((ya>point.Y)==(yb>point.Y))continue;bool found=false;
                foreach(double root in new[]{first,Math.PI-first})
                {
                    for(int turn=-2;turn<=2;turn++)
                    {double angle=root+turn*Tau;if(angle>=left-1e-12&&angle<=right+1e-12){if(center.X+radius*Math.Cos(angle)>point.X)crossings++;found=true;break;}}
                    if(found)break;
                }
                Need(found,"A circular boundary intersection could not be proved.");
            }
        }
        return crossings%2==1;
    }
}
