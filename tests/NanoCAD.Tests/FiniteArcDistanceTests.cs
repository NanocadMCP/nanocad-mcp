using NanoCAD.Contracts;

internal static class FiniteArcDistanceTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        double[][] P(params (double X,double Y)[] points)=>points.Select(p=>new[]{p.X,p.Y,0d}).ToArray();
        void Distance(double[][] p,double start,double sweep,double expected,double radius=1)
        {
            var r=FiniteArcDistance.Minimum(p,new[]{0d,0d,0d},radius,start,sweep,()=>{});
            double angle=Math.Atan2(r.Second[1],r.Second[0]),delta=(sweep>0?angle-start:start-angle)%(2*Math.PI);
            if(delta<0)delta+=2*Math.PI;
            if(Math.Abs(r.Distance-expected)>1e-9||Math.Abs(CurveDistanceVerification.Distance(r.First,r.Second)-expected)>1e-9||
                Math.Abs(Math.Sqrt(r.Second[0]*r.Second[0]+r.Second[1]*r.Second[1])-radius)>1e-9||
                (delta>Math.Abs(sweep)+1e-12&&2*Math.PI-delta>1e-12))throw new Exception("Wrong minimum or witness outside finite arc.");
        }
        await test("finite arc distance checks interior stationary pairs and clipped circle minima",()=>{
            Distance(P((-2,3),(2,3)),0,Math.PI,2);
            Distance(P((-2,3),(2,3)),Math.PI,Math.PI,3);
            Distance(P((1,0),(3,0)),Math.PI/2,Math.PI,Math.Sqrt(2));
            Distance(P((-.2,0),(.2,0)),Math.PI/3,Math.PI/3,Math.Sqrt(.84));
            Distance(P((-2,0),(2,0)),Math.PI/4,Math.PI/2,Math.Sqrt(.5));return Task.CompletedTask;
        });
        await test("finite arc distance handles wraparound reverse sweep intersections and tangency",()=>{
            Distance(P((2,-2),(2,2)),11*Math.PI/6,Math.PI/3,1);
            Distance(P((2,-2),(2,2)),Math.PI/6,-Math.PI/3,1);
            Distance(P((-2,1),(2,1)),Math.PI/4,Math.PI/2,0);
            Distance(P((-2,.5),(2,.5)),0,Math.PI,0);
            Distance(P((-2,.5),(2,.5)),Math.PI,-Math.PI,0);return Task.CompletedTask;
        });
        await test("finite arc distance handles degenerate segments and restricted endpoint witnesses",()=>{
            Distance(P((0,0),(0,0)),.2,.3,2,2);
            Distance(P((3,0),(3,0)),Math.PI/4,Math.PI/2,Math.Sqrt(10-3*Math.Sqrt(2)));
            Distance(P((0,0),(0,0)),Math.PI,-Math.PI,1);return Task.CompletedTask;
        });
        await test("finite arc distance checks all actual segments against independent angular samples",()=>{
            // Every sampled arc point gives an upper bound. The arc-length
            // sampling step bounds the gap to the continuous global minimum.
            var random=new Random(124);
            for(int trial=0;trial<24;trial++)
            {
                var p=P((random.NextDouble()*8-4,random.NextDouble()*8-4),(random.NextDouble()*8-4,random.NextDouble()*8-4),(random.NextDouble()*8-4,random.NextDouble()*8-4));
                double start=random.NextDouble()*2*Math.PI-Math.PI,sweep=(.1+random.NextDouble()*6)*(trial%2==0?1:-1),radius=.2+random.NextDouble()*3;
                var result=FiniteArcDistance.Minimum(p,new[]{0d,0d,0d},radius,start,sweep,()=>{});double sampled=double.PositiveInfinity;
                for(int k=0;k<=4096;k++)
                {
                    double angle=start+sweep*k/4096,x=radius*Math.Cos(angle),y=radius*Math.Sin(angle);
                    for(int i=1;i<p.Length;i++)
                    {
                        double ux=p[i][0]-p[i-1][0],uy=p[i][1]-p[i-1][1];
                        double t=Math.Clamp(((x-p[i-1][0])*ux+(y-p[i-1][1])*uy)/(ux*ux+uy*uy),0,1);
                        double dx=x-p[i-1][0]-t*ux,dy=y-p[i-1][1]-t*uy;
                        sampled=Math.Min(sampled,Math.Sqrt(dx*dx+dy*dy));
                    }
                }
                if(result.Distance>sampled+1e-10||sampled-result.Distance>radius*Math.Abs(sweep)/4096+1e-10)throw new Exception("Analytic result disagrees with independent sample bounds.");
            }
            return Task.CompletedTask;
        });
        await test("finite arc distance enforces bounds sweep and cancellation",()=>{
            var p=P((0,0),(1,0));var c=new[]{0d,0d,0d};
            foreach(double sweep in new[]{0d,2*Math.PI,-2*Math.PI,double.NaN,double.PositiveInfinity})
            {try{FiniteArcDistance.Minimum(p,c,1,0,sweep,()=>{});throw new Exception("Invalid sweep accepted.");}catch(DomainException){}}
            foreach(double radius in new[]{0d,-1d,1e9+1,double.NaN})
            {try{FiniteArcDistance.Minimum(p,c,radius,0,1,()=>{});throw new Exception("Invalid radius accepted.");}catch(DomainException){}}
            try{FiniteArcDistance.Minimum(p,new[]{0d,0d,1e-12},1,0,1,()=>{});throw new Exception("Nonplanar input accepted.");}catch(DomainException){}
            try{FiniteArcDistance.Minimum(p,c,1,0,1,()=>throw new OperationCanceledException());throw new Exception("Cancellation ignored.");}catch(OperationCanceledException){}
            return Task.CompletedTask;
        });
    }
}
