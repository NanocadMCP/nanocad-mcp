using NanoCAD.Contracts;

internal static class DrawingArcRevolutionTests
{
    internal static async Task Run(Func<string,Func<Task>,Task> test)
    {
        SketchConstraintCurve Line(string id,Point2 a,Point2 b)=>new(id,"line",a,b);
        SketchConstraintCurve Arc(string id,Point2 center,double radius,double angle,double sweep)=>new(id,"arc",new(center.X+radius*Math.Cos(angle),center.Y+radius*Math.Sin(angle)),new(center.X+radius*Math.Cos(angle+sweep),center.Y+radius*Math.Sin(angle+sweep)),center,radius,sweep);
        var axis=Line("F",new(0,0),new(1,0));
        void Near(double a,double b){if(Math.Abs(a-b)>1e-8*Math.Max(1,Math.Abs(b)))throw new Exception($"{a} != {b}");}
        void Point(Point3 a,Point3 b){Near(a.X,b.X);Near(a.Y,b.Y);Near(a.Z,b.Z);}
        void Deny(Action a){try{a();}catch(DomainException){return;}throw new Exception("Cross-axis arc interior accepted");}
        await test("arc revolution exact sphere and hemisphere mixed moments",()=>{
            double r=4;var half=Arc("1",new(0,0),r,0,Math.PI);var sphere=DrawingRevolutionGeometry.Measure(new[]{half,Line("2",half.End!,half.Start!)},axis);
            Near(sphere.Volume,4*Math.PI*r*r*r/3);Point(sphere.Centroid,new(0,0,0));Point(sphere.Min,new(-r,-r,-r));Point(sphere.Max,new(r,r,r));
            var quarter=Arc("1",new(0,0),r,0,Math.PI/2);var hemi=DrawingRevolutionGeometry.Measure(new[]{quarter,Line("2",quarter.End!,new(0,0)),Line("3",new(0,0),quarter.Start!)},axis);
            Near(hemi.Volume,2*Math.PI*r*r*r/3);Point(hemi.Centroid,new(3*r/8,0,0));Point(hemi.Min,new(0,-r,-r));Point(hemi.Max,new(r,r,r));return Task.CompletedTask;
        });
        await test("arc revolution uses interior extrema along a translated oblique axis",()=>{
            Point2 W(Point2 p)=>new(100+.6*p.X-.8*p.Y,200+.8*p.X+.6*p.Y);
            var c=Arc("1",new(0,0),8,0,Math.PI/2);var source=new[]{c,Line("2",c.End!,new(0,0)),Line("3",new(0,0),c.Start!)};
            var transformed=source.Select(v=>v with{Start=W(v.Start!),End=W(v.End!),Center=v.Center is {} o?W(o):null}).ToArray();
            var result=DrawingRevolutionGeometry.Measure(transformed,Line("F",W(new(0,0)),W(new(10,0))));
            Near(result.Volume,1024*Math.PI/3);Point(result.Centroid,new(101.8,202.4,0));Point(result.Min,new(93.6,195.2,-8));Point(result.Max,new(108,208,8));
            var reverse=DrawingRevolutionGeometry.Measure(transformed.Reverse().ToArray(),Line("F",W(new(10,0)),W(new(0,0))));
            Near(reverse.Volume,result.Volume);Point(reverse.Centroid,result.Centroid);Point(reverse.Min,result.Min);Point(reverse.Max,result.Max);return Task.CompletedTask;
        });
        await test("arc revolution two semicircles agree with a full circle torus on either axis side",()=>{
            foreach(double side in new[]{-1d,1d})
            {
                var center=new Point2(12,side*10);var arcs=new[]{Arc("1",center,3,0,Math.PI),Arc("2",center,3,Math.PI,Math.PI)};
                var a=DrawingRevolutionGeometry.Measure(arcs,axis);var b=DrawingRevolutionGeometry.Measure(new[]{new SketchConstraintCurve("1","circle",Center:center,Radius:3)},axis);
                Near(a.Volume,b.Volume);Point(a.Centroid,b.Centroid);Point(a.Min,b.Min);Point(a.Max,b.Max);
            }
            return Task.CompletedTask;
        });
        await test("arc revolution refuses a crossing hidden between legal chord endpoints",()=>{
            var c=Arc("1",new(0,1),2,Math.PI,Math.PI);Deny(()=>DrawingRevolutionGeometry.Measure(new[]{c,Line("2",c.End!,c.Start!)},axis));
            var major=Arc("1",new(0,.5),2,0,1.75*Math.PI);Deny(()=>DrawingRevolutionGeometry.Measure(new[]{major,Line("2",major.End!,major.Start!)},axis));return Task.CompletedTask;
        });
    }
}
