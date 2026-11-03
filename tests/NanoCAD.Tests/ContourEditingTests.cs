using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class ContourEditingTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        ContourCurve Line(double x1,double y1,double x2,double y2)=>new("Line",new(x1,y1),new(x2,y2));
        bool Near(double a,double b)=>Math.Abs(a-b)<1e-7;
        void Need(bool okay){if(!okay)throw new Exception("Unexpected contour result.");}
        void Reject(Action work,string code){try{work();}catch(DomainException e)when(e.Error.code==code){return;}throw new Exception("Expected "+code);}
        var id=Guid.NewGuid().ToString("D");
        ModifyArguments Parse(params object[] operations)=>ModifyValidation.Parse(Wire.Element(new{bridge_instance_id=id,document_id=id,operation_id=id,expected_revision="7",units="Millimeters",operations}));
        await test("contour trim removes picked line interval and respects finite boundaries",()=>{
            var line=Line(0,0,30,0);var left=Line(10,-5,10,5);var right=Line(20,-5,20,5);
            var result=ContourGeometry.Trim(line,new[]{left,right},new(15,0));
            Need(result.Length==2&&result[0].Start==new Point2(0,0)&&result[0].End==new Point2(10,0)&&result[1].Start==new Point2(20,0)&&result[1].End==new Point2(30,0));
            Reject(()=>ContourGeometry.Trim(line,new[]{Line(10,1,10,5)},new(15,0)),"NO_INTERSECTION");
            Reject(()=>ContourGeometry.Trim(line,new[]{left},new(10,0)),"AMBIGUOUS_PICK");
            Reject(()=>ContourGeometry.Trim(line,new[]{left},new(15,1)),"PICK_POINT_OFF_CURVE");
            return Task.CompletedTask;
        });
        await test("contour circular trim handles wraparound and tangent ambiguity",()=>{
            var circle=new ContourCurve("Circle",Center:new(0,0),Radius:10);
            var vertical=Line(0,-20,0,20);var right=ContourGeometry.Trim(circle,new[]{vertical},new(10,0)).Single();
            Need(right.Kind=="Arc"&&Near(right.StartAngle,Math.PI/2)&&Near(right.Sweep,Math.PI));
            var left=ContourGeometry.Trim(circle,new[]{vertical},new(-10,0)).Single();
            Need(Near(left.StartAngle,3*Math.PI/2)&&Near(left.Sweep,Math.PI));
            Reject(()=>ContourGeometry.Trim(circle,new[]{Line(-20,10,20,10)},new(0,-10)),"NO_INTERSECTION");
            return Task.CompletedTask;
        });
        await test("contour extension chooses nearest boundary and preserves arc support",()=>{
            var source=Line(0,0,10,0);
            var extended=ContourGeometry.Extend(source,new[]{Line(20,-5,20,5),Line(15,-5,15,5)},"end");
            Need(extended.Start==source.Start&&extended.End==new Point2(15,0));
            var start=ContourGeometry.Extend(source,new[]{new ContourCurve("Circle",Center:new(-10,0),Radius:3)},"start");Need(start.Start==new Point2(-7,0)&&start.End==source.End);
            var arc=new ContourCurve("Arc",Center:new(0,0),Radius:10,StartAngle:0,Sweep:Math.PI/4);
            var arcEnd=ContourGeometry.Extend(arc,new[]{Line(0,-20,0,20)},"end");Need(Near(arcEnd.Sweep,Math.PI/2)&&arcEnd.Center==arc.Center&&arcEnd.Radius==arc.Radius);
            var arcStart=ContourGeometry.Extend(arc,new[]{Line(0,-20,0,20)},"start");Need(Near(arcStart.StartAngle,3*Math.PI/2)&&Near(arcStart.Sweep,3*Math.PI/4));
            Reject(()=>ContourGeometry.Extend(source,new[]{Line(5,-5,5,5)},"end"),"NO_EXTENSION");
            return Task.CompletedTask;
        });
        await test("contour offset preserves sides winding and rejects collapsed polygons",()=>{
            var line=ContourGeometry.Offset(Line(10,0,0,0),2,"left");Need(line.Start==new Point2(10,-2)&&line.End==new Point2(0,-2));
            var circle=new ContourCurve("Circle",Center:new(5,4),Radius:10);Need(ContourGeometry.Offset(circle,3,"inward").Radius==7);
            var p=new[]{new Point2(0,0),new Point2(20,0),new Point2(20,10),new Point2(0,10)};
            var polygon=new ContourCurve("Polyline",Vertices:p,Closed:true);
            var outward=ContourGeometry.Offset(polygon,2,"outward").Vertices!;
            Need(outward.SequenceEqual(new[]{new Point2(-2,-2),new Point2(22,-2),new Point2(22,12),new Point2(-2,12)}));
            var reversed=ContourGeometry.Offset(polygon with{Vertices=p.Reverse().ToArray()},2,"outward").Vertices!;
            Need(outward.All(point=>reversed.Contains(point)));
            Reject(()=>ContourGeometry.Offset(polygon,6,"inward"),"INVALID_GEOMETRY");
            Reject(()=>ContourGeometry.Offset(polygon with{Vertices=new[]{new Point2(0,0),new Point2(20,0),new Point2(5,5),new Point2(0,10)}},1,"outward"),"OUT_OF_SCOPE");
            return Task.CompletedTask;
        });
        await test("contour contracts preserve provenance and reject erased boundary reuse",()=>{
            var trim=new{operation="trim_curve",handles=new[]{"a"},boundary_handles=new[]{"0b"},pick_point=new[]{5d,0}};
            var args=Parse(trim);Need(args.Operations.Single() is TrimCurveSpec spec&&spec.Handles[0]=="A"&&spec.BoundaryHandles[0]=="B");
            Reject(()=>Parse(trim,new{operation="extend_curve",handles=new[]{"C"},boundary_handles=new[]{"A"},end="end"}),"INVALID_OPERATION_ORDER");
            Reject(()=>Parse(new{operation="offset_curve",handles=new[]{"A"},side="inside",distance=2}),"INVALID_ARGUMENT");
            Need(Parse(trim).Fingerprint()!=Parse(new{operation="trim_curve",handles=new[]{"A"},boundary_handles=new[]{"C"},pick_point=new[]{5d,0}}).Fingerprint());
            var legacy=new ModifiedEntities(id,"committed",false,Array.Empty<EntityRef>(),Array.Empty<EntityRef>(),Array.Empty<EntityRef>(),Array.Empty<CopyMapping>(),"single_command");
            Need(!Wire.Element(legacy).TryGetProperty("contour_results",out _));
            var output=Wire.Element(legacy with{contour_results=new[]{new ContourMapping(0,"trim_curve",new(id,id,"A"),new[]{new EntityRef(id,id,"C")},true)}});
            Need(output.GetProperty("contour_results")[0].GetProperty("source_erased").GetBoolean());
            var tool=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_entities");
            Need(tool.InputSchema.GetRawText().Contains("boundary_handles")&&tool.OutputSchema!.Value.GetRawText().Contains("contour_results"));
            return Task.CompletedTask;
        });
    }
}
