using System.Text.Json;
namespace NanoCAD.Contracts;

// Geometry equality must not depend on the chosen arc reference vector or a
// 2*pi shift of its angle interval. No coordinate transform is guessed here.
public static class SketchGeometryComparison
{
    public static bool Same(JsonElement a,JsonElement b)
    {
        try
        {
            string kind=a.GetProperty("kind").GetString()!;if(kind!=b.GetProperty("kind").GetString())return false;
            double[] P(JsonElement g,string k)=>g.GetProperty(k).EnumerateArray().Select(v=>v.GetDouble()).ToArray();
            bool Point(double[] x,double[] y,double tol=1e-7)=>x.Length==3&&y.Length==3&&x.Zip(y).All(p=>double.IsFinite(p.First)&&double.IsFinite(p.Second)&&Math.Abs(p.First-p.Second)<=tol);
            if(kind=="line")return Point(P(a,"start"),P(b,"start"))&&Point(P(a,"end"),P(b,"end"))||Point(P(a,"start"),P(b,"end"))&&Point(P(a,"end"),P(b,"start"));
            if(kind!="circular_arc"||a.GetProperty("angle_unit").GetString()!="radians"||b.GetProperty("angle_unit").GetString()!="radians")return false;
            double ra=a.GetProperty("radius").GetDouble(),rb=b.GetProperty("radius").GetDouble(),tol=Math.Max(1e-7,Math.Max(ra,rb)*1e-10);
            if(!double.IsFinite(ra)||!double.IsFinite(rb)||ra<=0||rb<=0||Math.Abs(ra-rb)>tol||!Point(P(a,"center"),P(b,"center")))return false;
            var na=P(a,"normal");var nb=P(b,"normal");var xa=P(a,"reference_direction");var xb=P(b,"reference_direction");
            double Dot(double[] x,double[] y)=>x.Zip(y).Sum(v=>v.First*v.Second);
            bool Frame(double[] n,double[] x)=>n.Length==3&&x.Length==3&&n.Concat(x).All(double.IsFinite)&&Math.Abs(Dot(n,n)-1)<1e-9&&Math.Abs(Dot(x,x)-1)<1e-9&&Math.Abs(Dot(n,x))<1e-9;
            if(!Frame(na,xa)||!Frame(nb,xb)||Math.Abs(Math.Abs(Dot(na,nb))-1)>1e-9)return false;
            double Sweep(JsonElement g)
            {
                double d=g.GetProperty("end_angle").GetDouble()-g.GetProperty("start_angle").GetDouble();
                if(!double.IsFinite(d)||Math.Abs(d)<1e-10||Math.Abs(d)>2*Math.PI+1e-9)return double.NaN;
                if(Math.Abs(Math.Abs(d)-2*Math.PI)<1e-9)return 2*Math.PI;
                return d<0?d+2*Math.PI:d;
            }
            double sa=Sweep(a),sb=Sweep(b);if(!double.IsFinite(sa)||!double.IsFinite(sb)||Math.Abs(sa-sb)>1e-9)return false;
            if(Math.Abs(sa-2*Math.PI)<1e-9)return true;
            double[] At(JsonElement g,double fraction,double sweep)
            {
                var c=P(g,"center");var n=P(g,"normal");var x=P(g,"reference_direction");var y=new[]{n[1]*x[2]-n[2]*x[1],n[2]*x[0]-n[0]*x[2],n[0]*x[1]-n[1]*x[0]};
                double angle=g.GetProperty("start_angle").GetDouble()+fraction*sweep,r=g.GetProperty("radius").GetDouble();
                return Enumerable.Range(0,3).Select(i=>c[i]+r*(x[i]*Math.Cos(angle)+y[i]*Math.Sin(angle))).ToArray();
            }
            return Point(At(a,.5,sa),At(b,.5,sb),tol)&&
                (Point(At(a,0,sa),At(b,0,sb),tol)&&Point(At(a,1,sa),At(b,1,sb),tol)||Point(At(a,0,sa),At(b,1,sb),tol)&&Point(At(a,1,sa),At(b,0,sb),tol));
        }
        catch(Exception e)when(e is InvalidOperationException or KeyNotFoundException or FormatException){return false;}
    }
}
