namespace NanoCAD.Contracts;

// Complete enumeration of actual straight segments, with endpoint projections
// and interior intersections. This never tessellates a curved spline.
public static class FiniteSegmentDistance
{
    public static (double[] First,double[] Second,double Distance) ToCircle(double[][] vertices,double[] center,double radius,Action check)
    {
        // Distance to a circle is |distance-to-center - radius|. Along a
        // finite segment, radial extrema occur at endpoints or the center's
        // projection; if the radius lies between them, solve the intersections.
        if(vertices.Length<2||vertices.Any(p=>p.Length!=3||p.Any(v=>!double.IsFinite(v)||Math.Abs(v)>1e9)||p[2]!=0)||
            center.Length!=3||center.Any(v=>!double.IsFinite(v)||Math.Abs(v)>1e9)||center[2]!=0||!double.IsFinite(radius)||radius<=0||radius>1e9)
            throw new DomainException("UNSUPPORTED_SPLINE","Segment-circle distance requires bounded WCS XY/Z0 geometry.");
        double best=double.PositiveInfinity;double[]? pa=null,pb=null;
        void Candidate(double[] p)
        {
            double x=p[0]-center[0],y=p[1]-center[1],length=Math.Sqrt(x*x+y*y);
            var q=length==0?new[]{center[0]+radius,center[1],0d}:new[]{center[0]+radius*(x/length),center[1]+radius*(y/length),0d};
            double distance=CurveDistanceVerification.Distance(p,q);
            if(distance<best){best=distance;pa=p.ToArray();pb=q;}
        }
        for(int i=1;i<vertices.Length;i++)
        {
            check();var a=vertices[i-1];var b=vertices[i];double dx=b[0]-a[0],dy=b[1]-a[1],length2=dx*dx+dy*dy;
            double[] At(double t)=>new[]{a[0]+t*dx,a[1]+t*dy,0d};
            Candidate(a);Candidate(b);if(length2==0)continue;
            double t=((center[0]-a[0])*dx+(center[1]-a[1])*dy)/length2;
            Candidate(At(Math.Clamp(t,0,1)));
            var foot=At(t);double x=foot[0]-center[0],y=foot[1]-center[1],remaining=radius*radius-x*x-y*y;
            if(remaining<0)continue;
            double delta=Math.Sqrt(remaining/length2);
            foreach(double parameter in new[]{t-delta,t+delta})if(parameter>=0&&parameter<=1)Candidate(At(parameter));
        }
        if(pa==null||pb==null||!double.IsFinite(best))throw new DomainException("NATIVE_DISTANCE_MISMATCH","No finite segment-circle distance found.");
        return (pa,pb,best);
    }
    public static (double[] First,double[] Second,double Distance) Minimum(double[][] first,double[][] second,Action check)
    {
        foreach(var vertices in new[]{first,second})
            if(vertices.Length<2||vertices.Any(p=>p.Length!=3||p.Any(v=>!double.IsFinite(v)||Math.Abs(v)>1e9)||p[2]!=0))
                throw new DomainException("UNSUPPORTED_SPLINE","Exact segment distance requires bounded WCS XY/Z0 points within +/-1e9.");
        double best=double.PositiveInfinity;double[]? pa=null,pb=null;
        void Candidate(double[] a,double[] b)
        {
            double distance=CurveDistanceVerification.Distance(a,b);
            if(distance<best){best=distance;pa=a.ToArray();pb=b.ToArray();}
        }
        static double[] At(double[] a,double[] b,double t)=>new[]{a[0]+t*(b[0]-a[0]),a[1]+t*(b[1]-a[1]),0d};
        static double[] Project(double[] p,double[] a,double[] b)
        {
            double x=b[0]-a[0],y=b[1]-a[1],length=x*x+y*y;
            double t=length==0?0:Math.Clamp(((p[0]-a[0])*x+(p[1]-a[1])*y)/length,0,1);
            return At(a,b,t);
        }
        for(int i=1;i<first.Length;i++)for(int j=1;j<second.Length;j++)
        {
            check();var a=first[i-1];var b=first[i];var c=second[j-1];var d=second[j];
            Candidate(a,Project(a,c,d));Candidate(b,Project(b,c,d));
            Candidate(Project(c,a,b),c);Candidate(Project(d,a,b),d);
            double x=b[0]-a[0],y=b[1]-a[1],u=d[0]-c[0],v=d[1]-c[1],qx=c[0]-a[0],qy=c[1]-a[1];
            double cross=x*v-y*u;
            if(cross!=0)
            {
                double t=(qx*v-qy*u)/cross,s=(qx*y-qy*x)/cross;
                if(t>=0&&t<=1&&s>=0&&s<=1)Candidate(At(a,b,t),At(c,d,s));
            }
        }
        if(pa==null||pb==null||!double.IsFinite(best))throw new DomainException("NATIVE_DISTANCE_MISMATCH","No finite segment distance was found.");
        return (pa,pb,best);
    }
}
