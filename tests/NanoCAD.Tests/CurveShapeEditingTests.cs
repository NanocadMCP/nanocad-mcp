using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class CurveShapeEditingTests
{
    internal static async Task Run(Func<string,Func<Task>,Task> test)
    {
        string id=Guid.NewGuid().ToString();
        ModifyArguments Parse(object op)=>ModifyValidation.Parse(Wire.Element(new{bridge_instance_id=id,document_id=id,operation_id=id,expected_revision="1",units="Millimeters",operations=new[]{op}}));
        void Deny(object op){try{Parse(op);}catch(DomainException){return;}throw new Exception("Unsafe curvature input accepted");}
        object Arc(double start,double sweep,double radius=10)=>new{type="Arc",center=new[]{0d,0},radius,start_angle=start,sweep_angle=sweep};
        object Edit(object before,object after)=>new{operation="set_curve_geometry",handles=new[]{"A"},expected_geometry=before,geometry=after};
        object Vertex(params object[] vertices)=>new{operation="set_polyline_vertices",handles=new[]{"B"},vertices};
        await test("curve shape edits retain arc sweep and canonical angular expectations",()=>{
            var a=Parse(Edit(Arc(0,Math.PI),Arc(-Math.PI/2,1.5*Math.PI,12)));
            var b=Parse(Edit(Arc(2*Math.PI,Math.PI),Arc(1.5*Math.PI,1.5*Math.PI,12)));
            if(a.Fingerprint()!=b.Fingerprint()||a.Operations[0] is not CurveGeometrySpec{Geometry:{Radius:12,SweepAngle:var sweep}}||Math.Abs(sweep-1.5*Math.PI)>1e-12)throw new Exception("Equivalent angles or full sweep lost");
            if(a.Fingerprint()==Parse(Edit(Arc(0,Math.PI,11),Arc(-Math.PI/2,1.5*Math.PI,12))).Fingerprint())throw new Exception("Expected geometry omitted from identity");return Task.CompletedTask;
        });
        await test("curve shape edits reject circle conversion zero negative and full-turn arcs",()=>{
            foreach(double sweep in new[]{0d,-1,2*Math.PI})Deny(Edit(Arc(0,Math.PI),Arc(0,sweep)));
            Deny(Edit(Arc(0,Math.PI),new{type="Circle",center=new[]{0d,0},radius=10d}));
            Deny(Edit(Arc(0,Math.PI),new{type="Arc",center=new[]{0d,0},radius=10d,start_angle=0d}));return Task.CompletedTask;
        });
        await test("curve shape edits require paired bulge expectations and preserve position-only contracts",()=>{
            object V(double old,double value)=>new{index=0,expected_position=new[]{0d,0},position=new[]{0d,0},expected_bulge=old,bulge=value};
            var a=Parse(Vertex(V(0,1)));if(a.Operations[0] is not PolylineVerticesSpec{Vertices:[{ExpectedBulge:0,Bulge:1}]})throw new Exception("Curvature-only edit lost");
            if(a.Fingerprint()==Parse(Vertex(V(.5,1))).Fingerprint()||a.Fingerprint()==Parse(Vertex(V(0,-1))).Fingerprint())throw new Exception("Curvature expectations omitted from identity");
            var legacy=Parse(Vertex(new{index=0,expected_position=new[]{0d,0},position=new[]{2d,0}}));if(((PolylineVerticesSpec)legacy.Operations[0]).Vertices[0].Bulge!=null)throw new Exception("Legacy vertex edit changes curvature");
            Deny(Vertex(new{index=0,expected_position=new[]{0d,0},position=new[]{0d,0},bulge=1d}));Deny(Vertex(new{index=0,expected_position=new[]{0d,0},position=new[]{0d,0},expected_bulge=0d}));Deny(Vertex(V(0,1e7)));return Task.CompletedTask;
        });
        await test("curve shape edit schemas disclose curvature fields and owning-adapter limits",()=>{
            var tool=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_entities");var text=tool.InputSchema.GetRawText();
            foreach(string field in new[]{"expected_bulge","dependentRequired","sweep_angle","source dimensions"})if(!text.Contains(field))throw new Exception("Missing curvature contract: "+field);return Task.CompletedTask;
        });
    }
}
