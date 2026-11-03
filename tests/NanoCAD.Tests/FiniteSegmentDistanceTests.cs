using NanoCAD.Contracts;

internal static class FiniteSegmentDistanceTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        double[][] P(params (double X,double Y)[] values)=>values.Select(p=>new[]{p.X,p.Y,0d}).ToArray();
        void Distance(double[][] a,double[][] b,double expected)
        {
            var r=FiniteSegmentDistance.Minimum(a,b,()=>{});var reverse=FiniteSegmentDistance.Minimum(b,a,()=>{});
            if(Math.Abs(r.Distance-expected)>1e-9||Math.Abs(reverse.Distance-expected)>1e-9||Math.Abs(CurveDistanceVerification.Distance(r.First,r.Second)-expected)>1e-9)throw new Exception("Wrong segment minimum.");
        }
        await test("linear spline distance checks missed vertex counterexample and ordered witnesses",()=>{
            var a=P((50,0),(70,0),(70,15),(50,0));var b=P((60,30),(70,30));Distance(a,b,15);
            var r=FiniteSegmentDistance.Minimum(a,b,()=>{});
            if(!r.First.SequenceEqual(new[]{70d,15d,0d})||!r.Second.SequenceEqual(new[]{70d,30d,0d}))throw new Exception("Wrong closest vertex.");return Task.CompletedTask;
        });
        await test("linear spline distance intersects interiors and handles parallel overlap endpoints",()=>{
            Distance(P((0,0),(10,10)),P((0,10),(10,0)),0);
            Distance(P((0,0),(10,0)),P((3,0),(15,0)),0);
            Distance(P((0,0),(10,0)),P((15,0),(20,0)),5);
            Distance(P((0,0),(10,0)),P((2,3),(8,3)),3);
            Distance(P((1,1),(1,1)),P((4,5),(4,5)),5);return Task.CompletedTask;
        });
        await test("linear spline distance checks every closing segment and translated coordinates",()=>{
            Distance(P((0,0),(20,0),(20,15),(0,0)),P((30,0),(40,0),(30,10),(30,0)),10);
            Distance(P((0,0),(20,0),(20,15),(0,0)),P((8,7),(8,8)),.8);
            Distance(P((999999990,999999990),(1000000000,999999990)),P((999999995,999999995),(999999999,999999995)),5);return Task.CompletedTask;
        });
        await test("linear spline distance preserves open weighted segments and excludes gaps and trim",()=>{
            var points=P((0,0),(20,0),(20,15));
            var spline=new SplineGeometry(1,true,false,false,true,false,points,new[]{0d,0d,.3,1d,1d},new[]{1d,3d,2d},1e-7,1e-7,0,1,points[0],points[^1]);
            var actual=LinearSplineSegments.TryVertices(spline)??throw new Exception("Open weighted linear spline excluded.");
            Distance(actual,P((8,7),(8,8)),7);
            if(actual.Length!=3||LinearSplineSegments.TryVertices(spline with{knots=new[]{-1d,0d,.3,1d,2d}})!=null)throw new Exception("Trimmed control polygon used or artificial closing segment added.");
            var closed=P((0,0),(10,0),(10,10),(0,0));
            var gap=new SplineGeometry(1,false,true,true,true,false,closed,new[]{0d,0d,.5,.5,1d,1d},Array.Empty<double>(),1e-7,1e-7,0,1,closed[0],closed[^1]);
            if(LinearSplineSegments.TryVertices(gap)!=null)throw new Exception("Discontinuous spans joined artificially.");
            return Task.CompletedTask;
        });
        await test("linear spline distance to exact circle handles exterior interior tangency and secants",()=>{
            var triangle=P((50,0),(70,0),(70,15),(50,0));
            foreach(var c in new[]{(70d,30d,3d,12d),(65d,5d,1d,4d),(73d,8d,3d,0d),(73d,8d,4d,0d),(70d,8d,3d,0d),(60d,5d,30d,30-Math.Sqrt(200))})
            {
                var center=new[]{c.Item1,c.Item2,0d};var r=FiniteSegmentDistance.ToCircle(triangle,center,c.Item3,()=>{});
                if(Math.Abs(r.Distance-c.Item4)>1e-9||Math.Abs(CurveDistanceVerification.Distance(r.Second,center)-c.Item3)>1e-9)throw new Exception("Wrong exact circle distance or witness.");
            }
            return Task.CompletedTask;
        });
        await test("linear spline distance to circle handles center witnesses and bounded inputs",()=>{
            var center=new[]{0d,0d,0d};var r=FiniteSegmentDistance.ToCircle(P((0,0),(0,0)),center,3,()=>{});
            if(r.Distance!=3)throw new Exception("Center-to-circle distance undefined.");
            foreach(double radius in new[]{0d,-1d,double.NaN,1e9+1})
            {try{FiniteSegmentDistance.ToCircle(P((0,0),(1,0)),center,radius,()=>{});throw new Exception("Invalid circle accepted.");}catch(DomainException){}}
            return Task.CompletedTask;
        });
        await test("linear spline distance rejects unsupported coordinates and observes cancellation",()=>{
            var a=P((0,0),(1,0));
            foreach(var b in new[]{new[]{new[]{0d,0d,1e-12},new[]{1d,0d,0d}},P((0,0),(double.NaN,0)),P((0,0),(1e9+1,0))})
            {try{FiniteSegmentDistance.Minimum(a,b,()=>{});throw new Exception("Invalid points accepted.");}catch(DomainException){}}
            try{FiniteSegmentDistance.Minimum(a,a,()=>throw new OperationCanceledException());throw new Exception("Cancellation ignored.");}catch(OperationCanceledException){}
            return Task.CompletedTask;
        });
    }
}
