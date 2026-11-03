namespace NanoCAD.Contracts;

// Exact straight/circular regions: no polygonal approximation of round holes.
public static class HatchRegionValidation
{
    private static DomainException Bad()=>new("INVALID_GEOMETRY","Hatch holes must lie strictly inside the outer boundary, without touching, overlap or nesting.");
    private static double Cross(Point2 a,Point2 b,Point2 p)=>(b.X-a.X)*(p.Y-a.Y)-(b.Y-a.Y)*(p.X-a.X);
    private static double Distance(Point2 p,Point2 a,Point2 b)
    {
        double x=b.X-a.X,y=b.Y-a.Y,t=Math.Clamp(((p.X-a.X)*x+(p.Y-a.Y)*y)/(x*x+y*y),0,1);
        return Math.Sqrt(Math.Pow(p.X-a.X-t*x,2)+Math.Pow(p.Y-a.Y-t*y,2));
    }
    private static bool Inside(Point2 p,Point2[] polygon,Action? cancel=null)
    {
        bool inside=false;
        for(int i=0,j=polygon.Length-1;i<polygon.Length;j=i++)
        {
            cancel?.Invoke();var a=polygon[j];var b=polygon[i];
            if(Distance(p,a,b)<=1e-9)return false;
            if((a.Y>p.Y)!=(b.Y>p.Y)&&p.X<(b.X-a.X)*(p.Y-a.Y)/(b.Y-a.Y)+a.X)inside=!inside;
        }
        return inside;
    }
    private static bool Crosses(Point2[] a,Point2[] b,Action? cancel=null)
    {
        for(int i=0;i<a.Length;i++)for(int j=0;j<b.Length;j++)
        {
            cancel?.Invoke();var p=a[i];var q=a[(i+1)%a.Length];var r=b[j];var s=b[(j+1)%b.Length];
            if(Distance(p,r,s)<=1e-9||Distance(q,r,s)<=1e-9||Distance(r,p,q)<=1e-9||Distance(s,p,q)<=1e-9)return true;
            if(Cross(p,q,r)*Cross(p,q,s)<0&&Cross(r,s,p)*Cross(r,s,q)<0)return true;
        }
        return false;
    }
    private static double Clearance(Point2 p,Point2[] polygon,Action? cancel=null)=>Enumerable.Range(0,polygon.Length).Min(i=>{cancel?.Invoke();return Distance(p,polygon[i],polygon[(i+1)%polygon.Length]);});
    public static void Validate(Point2[] outer,HatchHole[] holes,Action? cancel=null)
    {
        SimplePolygonValidation.Validate(outer,cancel:cancel);
        SerializedCapacity357.Check(new{outer,holes},cancel:cancel);
        foreach(var h in holes)
        {
            if(h.Vertices is {} p)
            {SimplePolygonValidation.Validate(p,cancel:cancel);if(!p.All(v=>Inside(v,outer,cancel))||Crosses(p,outer,cancel))throw Bad();}
            else if(h.Center is not {} c||!double.IsFinite(c.X)||!double.IsFinite(c.Y)||!double.IsFinite(h.Radius)||h.Radius<=0||!Inside(c,outer,cancel)||Clearance(c,outer,cancel)<=h.Radius+1e-9)throw Bad();
        }
        for(int i=0;i<holes.Length;i++)for(int j=i+1;j<holes.Length;j++)
        {
            cancel?.Invoke();var a=holes[i];var b=holes[j];
            if(a.Vertices is {} pa&&b.Vertices is {} pb)
            {if(Crosses(pa,pb,cancel)||Inside(pa[0],pb,cancel)||Inside(pb[0],pa,cancel))throw Bad();}
            else if(a.Vertices is null&&b.Vertices is null)
            {var ca=a.Center!;var cb=b.Center!;if(Math.Sqrt(Math.Pow(ca.X-cb.X,2)+Math.Pow(ca.Y-cb.Y,2))<=a.Radius+b.Radius+1e-9)throw Bad();}
            else
            {var p=a.Vertices??b.Vertices!;var circle=a.Vertices is null?a:b;var c=circle.Center!;if(Inside(c,p,cancel)||Clearance(c,p,cancel)<=circle.Radius+1e-9)throw Bad();}
        }
    }
}

