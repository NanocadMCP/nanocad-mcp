using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class DrawingRevolutionTests
{
    internal static async Task Run(Func<string,Func<Task>,Task> test)
    {
        SketchConstraintCurve Axis(Point2? a=null,Point2? b=null)=>new("F","line",a??new(0,-5),b??new(0,20));
        SketchConstraintCurve[] Polygon(params Point2[] p)=>p.Select((v,i)=>new SketchConstraintCurve((i+1).ToString("X"),"line",v,p[(i+1)%p.Length])).ToArray();
        void Near(double a,double b){if(Math.Abs(a-b)>1e-7*Math.Max(1,Math.Abs(b)))throw new Exception($"{a} != {b}");}
        void Point(Point3 a,Point3 b){Near(a.X,b.X);Near(a.Y,b.Y);Near(a.Z,b.Z);}
        void Deny(Action a){try{a();}catch(DomainException){return;}throw new Exception("Invalid revolution accepted");}
        await test("drawing revolution analytic moments distinguish cylinders annuli cones and tori",()=>{
            var cylinder=DrawingRevolutionGeometry.Measure(Polygon(new(0,0),new(4,0),new(4,10),new(0,10)),Axis());Near(cylinder.Volume,160*Math.PI);Point(cylinder.Centroid,new(0,5,0));Point(cylinder.Min,new(-4,0,-4));Point(cylinder.Max,new(4,10,4));
            var annulus=DrawingRevolutionGeometry.Measure(Polygon(new(2,0),new(4,0),new(4,10),new(2,10)),Axis());Near(annulus.Volume,120*Math.PI);
            var cone=DrawingRevolutionGeometry.Measure(Polygon(new(0,0),new(4,0),new(0,12)),Axis());Near(cone.Volume,64*Math.PI);Point(cone.Centroid,new(0,3,0));
            var torus=DrawingRevolutionGeometry.Measure(new[]{new SketchConstraintCurve("A","circle",Center:new(10,2),Radius:3)},Axis());Near(torus.Volume,180*Math.PI*Math.PI);Point(torus.Centroid,new(0,2,0));Point(torus.Min,new(-13,-1,-13));Point(torus.Max,new(13,5,13));return Task.CompletedTask;
        });
        await test("drawing revolution follows arbitrary coplanar axis and reversed source orientations",()=>{
            Point2 W(double u,double r)=>new(10+.6*u-.8*r,20+.8*u+.6*r);var points=new[]{W(0,2),W(10,2),W(10,4),W(0,4)};
            var result=DrawingRevolutionGeometry.Measure(Polygon(points),Axis(new(10,20),new(13,24)));Near(result.Volume,120*Math.PI);Point(result.Centroid,new(13,24,0));Point(result.Min,new(6.8,17.6,-4));Point(result.Max,new(19.2,30.4,4));
            var reversed=DrawingRevolutionGeometry.Measure(Polygon(points.Reverse().ToArray()),Axis(new(13,24),new(10,20)));Near(reversed.Volume,result.Volume);Point(reversed.Centroid,result.Centroid);Point(reversed.Min,result.Min);Point(reversed.Max,result.Max);return Task.CompletedTask;
        });
        await test("drawing revolution rejects crossing profiles touching circles degenerate axes and duplicate axis sources",()=>{
            Deny(()=>DrawingRevolutionGeometry.Measure(Polygon(new(-1,0),new(1,0),new(1,3),new(-1,3)),Axis()));
            Deny(()=>DrawingRevolutionGeometry.Measure(new[]{new SketchConstraintCurve("A","circle",Center:new(2,2),Radius:2)},Axis()));
            Deny(()=>DrawingRevolutionGeometry.Measure(Polygon(new(0,0),new(4,0),new(0,12)),Axis(new(1,1),new(1,1))));
            var id=Guid.NewGuid().ToString();object Input(string axis)=>new{bridge_instance_id=id,document_id=id,expected_revision="1",operation_id=id,units="Millimeters",operations=new[]{new{operation="revolve_drawing_profile",handles=new[]{"A"},axis_handle=axis,name="Turned part"}}};
            var parsed=ModifyValidation.Parse(Wire.Element(Input("00F"))).Operations.Single();if(parsed is not DrawingProfileEdit{AxisHandle:"F"})throw new Exception("Axis handle binding lost");Deny(()=>ModifyValidation.Parse(Wire.Element(Input("00A"))));
            var tool=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_entities");if(!tool.InputSchema.GetRawText().Contains("revolve_drawing_profile"))throw new Exception("Revolution not advertised");return Task.CompletedTask;
        });
    }
}
