namespace NanoCAD.Contracts;

public static class SimplePolygonValidation
{
    public static void Validate(Point2[] polygon,string path="boundary",Action? cancel=null)
    {
        DomainException Invalid(string message)=>new("INVALID_GEOMETRY",path+": "+message,false,new{path});
        if(polygon.Length<3||polygon.Any(p=>!double.IsFinite(p.X)||!double.IsFinite(p.Y)||Math.Abs(p.X)>1e9||Math.Abs(p.Y)>1e9))
            throw Invalid("Expected at least three finite vertices within ±1e9.");
        SerializedCapacity357.Check(polygon,cancel:cancel);
        double Cross(Point2 a,Point2 b,Point2 c)=>(b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);
        bool On(Point2 a,Point2 b,Point2 p)=>p.X>=Math.Min(a.X,b.X)&&p.X<=Math.Max(a.X,b.X)&&p.Y>=Math.Min(a.Y,b.Y)&&p.Y<=Math.Max(a.Y,b.Y);
        double area=0;
        for(int i=0;i<polygon.Length;i++)
        {
            cancel?.Invoke();var a=polygon[i];var b=polygon[(i+1)%polygon.Length];
            if(Math.Abs(a.X-b.X)<1e-9&&Math.Abs(a.Y-b.Y)<1e-9)throw Invalid("Repeated polygon vertex.");
            area+=(a.X-polygon[0].X)*(b.Y-polygon[0].Y)-(a.Y-polygon[0].Y)*(b.X-polygon[0].X);
            for(int j=i+1;j<polygon.Length;j++)
            {
                cancel?.Invoke();if(j==i+1||i==0&&j==polygon.Length-1)continue;
                var c=polygon[j];var d=polygon[(j+1)%polygon.Length];
                double x=Cross(a,b,c),y=Cross(a,b,d),z=Cross(c,d,a),w=Cross(c,d,b);
                if(x*y<0&&z*w<0||x==0&&On(a,b,c)||y==0&&On(a,b,d)||z==0&&On(c,d,a)||w==0&&On(c,d,b))
                    throw Invalid("Self-intersecting polygon boundary.");
            }
        }
        if(Math.Abs(area)<1e-12)throw Invalid("Zero-area polygon boundary.");
    }
}
