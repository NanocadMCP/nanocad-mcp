using NanoCAD.Contracts;
using NanoCAD.McpServer;
internal static class PartialRevolutionTests
{
    private static SketchConstraintCurve Axis(Point2? a=null,Point2? b=null)=>new("F","line",a??new(0,0),b??new(1,0));
    private static SketchConstraintCurve[] Polygon(params Point2[] p)=>p.Select((v,i)=>new SketchConstraintCurve((i+1).ToString("X"),"line",v,p[(i+1)%p.Length])).ToArray();
    private static void Near(double a,double b){if(Math.Abs(a-b)>1e-7*Math.Max(1,Math.Abs(b)))throw new Exception($"{a} != {b}");}
    private static void Point(Point3 a,Point3 b){Near(a.X,b.X);Near(a.Y,b.Y);Near(a.Z,b.Z);}
    internal static async Task Run(Func<string,Func<Task>,Task> test)
    {
        await test("partial revolution independent cylinder sector mass and angular bounds",()=>{
            var p=Polygon(new(0,0),new(12,0),new(12,6),new(0,6));
            var quarter=DrawingPartialRevolution.Measure(p,Axis(),Math.PI/2);Near(quarter.Volume,108*Math.PI);Point(quarter.Centroid,new(6,8/Math.PI,8/Math.PI));Point(quarter.Min,new(0,0,0));Point(quarter.Max,new(12,6,6));
            var major=DrawingPartialRevolution.Measure(p,Axis(),3*Math.PI/2);Near(major.Volume,324*Math.PI);Point(major.Centroid,new(6,-8/(3*Math.PI),8/(3*Math.PI)));Point(major.Min,new(0,-6,-6));Point(major.Max,new(12,6,6));
            var full=DrawingPartialRevolution.Measure(p,Axis(),2*Math.PI);if(full!=DrawingRevolutionGeometry.Measure(p,Axis()))throw new Exception("Full-turn path changed");
            return Task.CompletedTask;
        });
        await test("partial revolution cone and torus have weighted radial centroid",()=>{
            var cone=DrawingPartialRevolution.Measure(Polygon(new(0,0),new(12,0),new(12,6)),Axis(),Math.PI/2);
            Near(cone.Volume,36*Math.PI);Point(cone.Centroid,new(9,6/Math.PI,6/Math.PI));
            var torus=DrawingPartialRevolution.Measure([new("A","circle",Center:new(4,10),Radius:2)],Axis(),Math.PI);
            Near(torus.Volume,40*Math.PI*Math.PI);Point(torus.Centroid,new(4,0,20.2/Math.PI));Point(torus.Min,new(2,-12,0));Point(torus.Max,new(6,12,12));
            return Task.CompletedTask;
        });
        await test("partial revolution curved profile matches hemisphere closed form and winding",()=>{
            SketchConstraintCurve[] p=[new("1","line",new(-4,0),new(4,0)),new("2","arc",new(4,0),new(-4,0),new(0,0),4,Math.PI)];
            var half=DrawingPartialRevolution.Measure(p,Axis(),Math.PI);Near(half.Volume,128*Math.PI/3);Point(half.Centroid,new(0,0,1.5));Point(half.Min,new(-4,-4,0));Point(half.Max,new(4,4,4));
            var reverse=DrawingPartialRevolution.Measure(p.Reverse().ToArray(),Axis(),Math.PI);Point(reverse.Centroid,half.Centroid);Near(reverse.Volume,half.Volume);
            return Task.CompletedTask;
        });
        await test("partial revolution negative radial side and tilted axis preserve handedness",()=>{
            var negative=DrawingPartialRevolution.Measure(Polygon(new(0,-6),new(12,-6),new(12,0),new(0,0)),Axis(),Math.PI/2);
            Point(negative.Centroid,new(6,-8/Math.PI,-8/Math.PI));Point(negative.Min,new(0,-6,-6));Point(negative.Max,new(12,0,0));
            Point2 W(double x,double r)=>new(10+.6*x-.8*r,20+.8*x+.6*r);
            var tilted=DrawingPartialRevolution.Measure(Polygon(W(0,0),W(12,0),W(12,6),W(0,6)),Axis(new(10,20),new(13,24)),Math.PI/2);
            Point(tilted.Centroid,new(13.6-.8*8/Math.PI,24.8+.6*8/Math.PI,8/Math.PI));Point(tilted.Min,new(5.2,20,0));Point(tilted.Max,new(17.2,33.2,6));
            return Task.CompletedTask;
        });
        await test("partial revolution parser keeps legacy default and degree angle write isolation",()=>{
            var id=Guid.NewGuid().ToString();ModifyArguments Parse(object op)=>ModifyValidation.Parse(Wire.Element(new{bridge_instance_id=id,document_id=id,operation_id=id,expected_revision="1",units="Millimeters",operations=new[]{op}}));
            var old=Parse(new{operation="revolve_drawing_profile",handles=new[]{"A"},axis_handle="B",name="Full"});if(old.Operations[0] is not DrawingProfileEdit{AngleDegrees:null})throw new Exception("Legacy default changed");
            var angle=Parse(new{operation="revolve_drawing_profile",handles=new[]{"A"},axis_handle="B",name="Full",angle_degrees=360});if(angle.Fingerprint()==old.Fingerprint())throw new Exception("Explicit editable full extent omitted");
            var edit=Parse(new{operation="set_model_revolution",handles=new[]{"C"},expected_last_item_id=Guid.NewGuid().ToString("B"),expected_angle_degrees=90,angle_degrees=180});if(!WriteRecovery.NotGuaranteed(edit))throw new Exception("Unknown write policy absent");
            var schema=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_entities").InputSchema.GetProperty("properties").GetProperty("operations").GetProperty("items").GetProperty("oneOf").EnumerateArray().Single(x=>x.GetProperty("properties").GetProperty("operation").GetProperty("const").GetString()=="revolve_drawing_profile");
            if(schema.GetProperty("required").EnumerateArray().Any(x=>x.GetString()=="angle_degrees")||!schema.GetProperty("properties").TryGetProperty("angle_degrees",out _))throw new Exception("Optional degree field mismatch");
            return Task.CompletedTask;
        });
    }
}
