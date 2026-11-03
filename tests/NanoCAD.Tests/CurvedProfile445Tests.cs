using NanoCAD.Contracts;

internal static class CurvedProfile445Tests
{
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static CurveSplineGeometry445 Definition(double[][] points,double[]? knots=null,double[]? weights=null,double start=0,double end=1)=>
        new(3,weights is {Length:>0},false,false,true,false,points,knots??[0,0,0,0,1,1,1,1],weights??[],null,1e-9,
            start,end,points[0],points[^1],null,null,"pure_definition_fixture");
    static SketchConstraintCurve Curve(CurveSplineGeometry445 value)=>new("spline","spline",new(value.start_point[0],value.start_point[1]),new(value.end_point[0],value.end_point[1]),Spline:value);
    static SketchConstraintCurve Ellipse(double start=0,double end=2*Math.PI,bool reverseAxes=false)=>new("ellipse","ellipse",Ellipse:new(new(0,0,0),new(reverseAxes?-1:1,0,0),new(0,reverseAxes?-1:1,0),10,5,start,end));
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        await test("block445: complete curved profile coverage accepts reversed NURBS with an affine knot domain",()=>
        {
            double[][] points=[[0,0,0],[2,0,0],[5,4,0],[8,4,0]];
            var a=Curve(Definition(points));var b=Curve(Definition(points.Reverse().ToArray(),[4,4,4,4,9,9,9,9],start:4,end:9));
            Check(CurvedProfileBoundary445.Same(a,b),"Reversed full NURBS locus was lost.");
            Check(ModelProfileBoundary340.Same([a],[b])&&ModelProfileElementGeometry358.SameBoundary([a],[b],()=>{}),"Selected-profile preservation did not reuse complete native curve coverage.");
            return Task.CompletedTask;
        });
        await test("block445: curved profile coverage rejects changed spline interior even with unchanged endpoints",()=>
        {
            var a=Curve(Definition([[0,0,0],[2,0,0],[5,4,0],[8,4,0]]));
            var b=Curve(Definition([[0,0,0],[2,0,0],[5,7,0],[8,4,0]]));
            Check(!CurvedProfileBoundary445.Same(a,b)&&!ModelProfileBoundary340.Same([a],[b]),"An endpoint-only check hid a changed spline shape.");
            return Task.CompletedTask;
        });
        await test("block445: rational curve physical coverage permits common weight scale and rejects ratio changes",()=>
        {
            double[][] points=[[0,0,0],[2,0,0],[5,4,0],[8,4,0]];
            var a=Curve(Definition(points,weights:[1,2,2,1]));var scaled=Curve(Definition(points,weights:[3,6,6,3]));var changed=Curve(Definition(points,weights:[3,6,9,3]));
            Check(CurvedProfileBoundary445.Same(a,scaled),"Equivalent homogeneous weight scaling was rejected.");
            Check(!CurvedProfileBoundary445.Same(a,changed),"Changed rational curve weights were ignored.");
            return Task.CompletedTask;
        });
        await test("block445: complete ellipse boundary preserves native principal-axis reversal",()=>
        {
            var a=Ellipse();var reversed=Ellipse(reverseAxes:true);
            Check(CurvedProfileBoundary445.Same(a,reversed)&&ModelProfileBoundary340.Same([a],[reversed]),"Axis-sign representation changed a complete ellipse's physical boundary.");
            var resized=a with{Ellipse=a.Ellipse! with{MinorRadius=6}};
            Check(!CurvedProfileBoundary445.Same(a,resized),"Ellipse minor-radius change was hidden.");
            return Task.CompletedTask;
        });
        await test("block445: partial ellipse coverage distinguishes the selected arc from a same-size different arc",()=>
        {
            var a=Ellipse(0,Math.PI/2);var same=Ellipse(Math.PI,3*Math.PI/2,reverseAxes:true);var other=Ellipse(Math.PI/2,Math.PI);
            Check(CurvedProfileBoundary445.Same(a,same),"An equivalent shifted ellipse parameterization was rejected.");
            Check(!CurvedProfileBoundary445.Same(a,other),"Different ellipse quadrants were treated as the same boundary.");
            return Task.CompletedTask;
        });
        await test("block445: selected-profile coverage preserves both analytic and curved members",()=>
        {
            var spline=Curve(Definition([[0,0,0],[2,0,0],[5,4,0],[8,4,0]]));var line=new SketchConstraintCurve("line","line",new(8,4),new(0,0));
            Check(ModelProfileBoundary340.Same([spline,line],[line,spline])&&ModelProfileElementGeometry358.SameBoundary([spline,line],[line,spline],()=>{}),"Mixed profile reordered its complete members incorrectly.");
            Check(!ModelProfileBoundary340.Same([spline,line],[spline]),"Removing an analytic boundary member was ignored.");
            return Task.CompletedTask;
        });
    }
}
