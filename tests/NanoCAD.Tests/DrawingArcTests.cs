using NanoCAD.Contracts;

internal static class DrawingArcTests
{
    internal static async Task Run(Func<string,Func<Task>,Task> test)
    {
        SketchConstraintCurve Arc(string id,double x,double y,double radius,double start,double sweep)=>new(id,"arc",new(x+radius*Math.Cos(start),y+radius*Math.Sin(start)),new(x+radius*Math.Cos(start+sweep),y+radius*Math.Sin(start+sweep)),new(x,y),radius,sweep);
        SketchConstraintCurve Line(string id,Point2 a,Point2 b)=>new(id,"line",a,b);
        void Near(double a,double b){if(Math.Abs(a-b)>Math.Max(1e-8,Math.Abs(b)*1e-10))throw new Exception($"Expected {b}, observed {a}");}
        void Deny(Action f){try{f();}catch(DomainException){return;}throw new Exception("Invalid curved profile accepted");}
        await test("drawing arc semicircle moments preserve curvature and reversed traversal",()=>{
            var arc=Arc("A",100,-30,12,0,Math.PI);var line=Line("B",arc.Start!,arc.End!);
            foreach(var curves in new[]{new[]{arc,line},new[]{line,arc}})
            {var v=DrawingProfileGeometry.Measure(curves,-8);Near(v.Area,72*Math.PI);Near(v.Volume,576*Math.PI);Near(v.Centroid.X,100);Near(v.Centroid.Y,-30+16/Math.PI);Near(v.Centroid.Z,-4);Near(v.Min.X,88);Near(v.Max.X,112);Near(v.Max.Y,-18);}
            var major=Arc("C",0,0,10,0,3*Math.PI/2);var v2=DrawingProfileGeometry.Measure(new[]{major,Line("D",major.End!,major.Start!)},1);Near(v2.Area,50*(3*Math.PI/2+1));return Task.CompletedTask;
        });
        await test("drawing arc capsule and complementary arcs use exact area bounds centroid",()=>{
            var left=Arc("A",0,0,10,Math.PI/2,Math.PI);var right=Arc("B",40,0,10,-Math.PI/2,Math.PI);
            var curves=new[]{left,Line("C",right.End!,left.Start!),right,Line("D",left.End!,right.Start!)};
            var v=DrawingProfileGeometry.Measure(curves,6);Near(v.Area,800+100*Math.PI);Near(v.Centroid.X,20);Near(v.Centroid.Y,0);Near(v.Min.X,-10);Near(v.Max.X,50);
            var other=Arc("E",0,0,10,-Math.PI/2,Math.PI);v=DrawingProfileGeometry.Measure(new[]{left,other},2);Near(v.Volume,200*Math.PI);Near(v.Centroid.X,0);Near(v.Centroid.Y,0);return Task.CompletedTask;
        });
        await test("drawing arc lens resolves circle intersections and wrapped sweeps",()=>{
            var a=Arc("A",0,0,10,5*Math.PI/3,2*Math.PI/3);var b=Arc("B",10,0,10,2*Math.PI/3,2*Math.PI/3);
            var v=DrawingProfileGeometry.Measure(new[]{a,b},2);double area=200*Math.PI/3-50*Math.Sqrt(3);
            Near(v.Area,area);Near(v.Centroid.X,5);Near(v.Centroid.Y,0);Near(v.Min.X,0);Near(v.Max.X,10);Near(v.Min.Y,-5*Math.Sqrt(3));Near(v.Max.Y,5*Math.Sqrt(3));return Task.CompletedTask;
        });
        await test("drawing arc contour rejects crossings overlaps branches gaps and inconsistent sweep",()=>{
            var arc=Arc("A",0,0,10,0,Math.PI);
            Deny(()=>DrawingProfileGeometry.Measure(new[]{arc,Line("B",arc.End!,new(0,-5)),Line("C",new(0,-5),new(0,15)),Line("D",new(0,15),arc.Start!)},1));
            Deny(()=>DrawingProfileGeometry.Measure(new[]{arc,arc with{Id="B"}},1));
            Deny(()=>DrawingProfileGeometry.Measure(new[]{arc,Line("B",arc.End!,new(9,0))},1));
            Deny(()=>DrawingArcContours.ValidateArc(arc with{Sweep=Math.PI/2}));
            Deny(()=>DrawingProfileGeometry.Measure(new[]{arc,Line("B",arc.End!,arc.Start!),Line("C",arc.Start!,new(20,0))},1));return Task.CompletedTask;
        });
        await test("drawing arc constraints bind endpoints center radius and actual tangent sweep",()=>{
            var arc=Arc("A",0,0,10,0,Math.PI);var chord=Line("B",arc.Start!,arc.End!);var tangent=Line("C",new(-20,10),new(20,10));var circle=new SketchConstraintCurve("D","circle",Center:new(0,0),Radius:10);
            var curves=new[]{arc,chord,tangent,circle};
            DrawingProfileGeometry.Validate(new[]{new SketchConstraintSpec("coincident","A","B","start","start",null),new("fix_point","A",null,"center",null,null),new("radius","A",null,null,null,10),new("diameter","A",null,null,null,20),new("equal","A","D",null,null,null),new("concentric","A","D",null,null,null),new("tangent","A","C",null,null,null)},curves);
            Deny(()=>DrawingProfileGeometry.Validate(new[]{new SketchConstraintSpec("horizontal","A",null,null,null,null)},curves));
            Deny(()=>DrawingProfileGeometry.Validate(new[]{new SketchConstraintSpec("tangent","A","C",null,null,null)},new[]{arc,tangent with{Start=new(-20,-10),End=new(20,-10)}}));
            if(DrawingProfileGeometry.Same(arc,arc with{Sweep=3*Math.PI/2}))throw new Exception("Arc sweep missing from snapshot");return Task.CompletedTask;
        });
    }
}
