namespace NanoCAD.Contracts;

// The minimum on two finite parameter intervals is either on an endpoint,
// at an intersection, or at an interior stationary pair. For a segment and
// circle, a nonzero stationary separation is normal to the segment and radial
// to the circle. Both normals must be checked for a restricted arc.
public static class FiniteArcDistance
{
    public static (double[] First,double[] Second,double Distance) Minimum(
        double[][] vertices,double[] center,double radius,double startAngle,double sweep,Action check)
    {
        if(vertices.Length<2||vertices.Any(p=>!Point(p))||!Point(center)||
            !double.IsFinite(radius)||radius<=0||radius>1e9||!double.IsFinite(startAngle)||Math.Abs(startAngle)>2*Math.PI||
            !double.IsFinite(sweep)||Math.Abs(sweep)<=0||Math.Abs(sweep)>=2*Math.PI)
            throw new DomainException("UNSUPPORTED_SPLINE","Finite arc distance requires bounded WCS XY/Z0 geometry and a finite nonzero sweep smaller than a full circle.");
        static bool Point(double[] p)=>p.Length==3&&p.All(v=>double.IsFinite(v)&&Math.Abs(v)<=1e9)&&p[2]==0;
        double[] AtAngle(double angle)=>new[]{center[0]+radius*Math.Cos(angle),center[1]+radius*Math.Sin(angle),0d};
        bool Contains(double angle)
        {
            double delta=(sweep>0?angle-startAngle:startAngle-angle)%(2*Math.PI);
            if(delta<0)delta+=2*Math.PI;
            return delta<=Math.Abs(sweep);
        }
        var start=AtAngle(startAngle);var end=AtAngle(startAngle+sweep);
        double best=double.PositiveInfinity;double[]? pa=null,pb=null;
        void Candidate(double[] p,double[] q)
        {
            double d=CurveDistanceVerification.Distance(p,q);
            if(d<best){best=d;pa=p.ToArray();pb=q.ToArray();}
        }
        void Endpoint(double[] p)
        {
            Candidate(p,start);Candidate(p,end);
            double x=p[0]-center[0],y=p[1]-center[1];
            if(x==0&&y==0)return; // Every arc point is equally far from its center.
            double angle=Math.Atan2(y,x);
            if(Contains(angle))Candidate(p,AtAngle(angle));
        }
        for(int i=1;i<vertices.Length;i++)
        {
            check();var a=vertices[i-1];var b=vertices[i];double dx=b[0]-a[0],dy=b[1]-a[1],length2=dx*dx+dy*dy;
            double[] At(double t)=>new[]{a[0]+t*dx,a[1]+t*dy,0d};
            double[] Project(double[] q)=>At(Math.Clamp(((q[0]-a[0])*dx+(q[1]-a[1])*dy)/length2,0,1));
            Endpoint(a);Endpoint(b);if(length2==0)continue;
            Candidate(Project(start),start);Candidate(Project(end),end);
            double normalAngle=Math.Atan2(dx,-dy);
            foreach(double angle in new[]{normalAngle,normalAngle+Math.PI})
                if(Contains(angle)){var q=AtAngle(angle);Candidate(Project(q),q);}
            double t=((center[0]-a[0])*dx+(center[1]-a[1])*dy)/length2;
            var foot=At(t);double x=foot[0]-center[0],y=foot[1]-center[1],remaining=radius*radius-x*x-y*y;
            if(remaining<0)continue;
            double delta=Math.Sqrt(remaining/length2);
            foreach(double parameter in new[]{t-delta,t+delta})
                if(parameter>=0&&parameter<=1)
                {
                    var p=At(parameter);double angle=Math.Atan2(p[1]-center[1],p[0]-center[0]);
                    if(Contains(angle))Candidate(p,AtAngle(angle));
                }
        }
        if(pa==null||pb==null||!double.IsFinite(best))throw new DomainException("NATIVE_DISTANCE_MISMATCH","No finite segment-arc distance found.");
        return (pa,pb,best);
    }
}
