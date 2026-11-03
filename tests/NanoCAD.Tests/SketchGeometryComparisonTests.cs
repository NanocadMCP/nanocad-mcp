using NanoCAD.Contracts;
internal static class SketchGeometryComparisonTests
{
    internal static async Task Run(Func<string,Func<Task>,Task> test)
    {
        System.Text.Json.JsonElement Arc(double start,double end,double[]? x=null,double[]? n=null,double z=0)=>Wire.Element(new{kind="circular_arc",center=new[]{0d,0,z},normal=n??new[]{0d,0,1},reference_direction=x??new[]{1d,0,0},radius=10d,start_angle=start,end_angle=end,angle_unit="radians"});
        await test("sketch-shape-equality-arc-reference-and-angle-wrap",()=>{var upper=Arc(0,Math.PI);if(!SketchGeometryComparison.Same(upper,Arc(-Math.PI/2,Math.PI/2,new[]{0d,1,0}))||!SketchGeometryComparison.Same(upper,Arc(2*Math.PI,3*Math.PI)))throw new Exception("Equivalent arc rejected");return Task.CompletedTask;});
        await test("sketch-shape-equality-reversed-arc-normal",()=>{if(!SketchGeometryComparison.Same(Arc(0,Math.PI),Arc(Math.PI,2*Math.PI,n:new[]{0d,0,-1})))throw new Exception("Reversed equivalent arc rejected");return Task.CompletedTask;});
        await test("sketch-shape-equality-rejects-opposite-half-and-other-plane",()=>{if(SketchGeometryComparison.Same(Arc(0,Math.PI),Arc(Math.PI,2*Math.PI))||SketchGeometryComparison.Same(Arc(0,2*Math.PI),Arc(0,2*Math.PI,z:1)))throw new Exception("Different geometry accepted");return Task.CompletedTask;});
        await test("sketch-shape-equality-circle-frame-and-degenerate-sweep",()=>{if(!SketchGeometryComparison.Same(Arc(0,2*Math.PI),Arc(1,1+2*Math.PI,new[]{0d,1,0},new[]{0d,0,-1}))||SketchGeometryComparison.Same(Arc(0,0),Arc(0,0)))throw new Exception("Circle semantics invalid");return Task.CompletedTask;});
    }
}
