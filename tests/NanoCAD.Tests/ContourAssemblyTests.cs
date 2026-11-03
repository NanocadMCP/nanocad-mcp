using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class ContourAssemblyTests
{
    static PolylineShapeVertex V(double x,double y,double b=0,double sw=0,double ew=0)=>new(new(x,y),b,sw,ew);
    static ContourSource S(string type,bool closed,params PolylineShapeVertex[] v)=>new(type,new(closed,v));
    static ContourAssemblySpec E(string kind,ContourSource[] sources,bool closed=false,bool[]? reverse=null,double[]? cuts=null)=>new(kind,Enumerable.Range(1,sources.Length).Select(i=>i.ToString("X")).ToArray(),sources,true,closed,reverse??new bool[sources.Length],cuts??[]);
    static void Near(double a,double b){if(Math.Abs(a-b)>1e-7)throw new Exception($"{a} != {b}");}
    static void Deny(Action action,string code){try{action();}catch(DomainException e) when(e.Error.code==code){return;}throw new Exception("Expected "+code);}
    internal static async Task Run(Func<string,Func<Task>,Task> test)
    {
        await test("contour assembly workplane contract preserves identity and optional legacy schema",()=>{
            string id=Guid.NewGuid().ToString("D");
            var plain=System.Text.Json.Nodes.JsonNode.Parse("{\"operation\":\"split_curve\",\"handles\":[\"A\"],\"expected_sources\":[{\"type\":\"Line\",\"start\":[0,0],\"end\":[10,0]}],\"erase_sources\":false,\"distances\":[3]}")!;
            ModifyArguments Parse(System.Text.Json.Nodes.JsonNode operation)=>ModifyValidation.Parse(Wire.Element(new{bridge_instance_id=id,document_id=id,operation_id=id,expected_revision="1",units="Millimeters",operations=new[]{operation}}));
            var before=Parse(plain);var framed=plain.DeepClone();framed["workplane"]=System.Text.Json.Nodes.JsonNode.Parse("{\"origin\":[10,20,30],\"x_axis\":[0,1,0],\"y_axis\":[0,0,1]}");
            var after=Parse(framed);var edit=(ContourAssemblySpec)after.Operations[0];
            if(edit.Workplane==null||((ContourAssemblySpec)before.Operations[0]).Workplane!=null||before.Fingerprint()==after.Fingerprint())throw new Exception("Frame missing from operation identity");
            var pieces=ContourAssemblyGeometry.Plan(edit);Near(ContourAssemblyGeometry.Segments(pieces[0].Shape).Single().Length,3);
            var p=DrawingWorkplane.World(edit.Workplane,pieces[0].Shape.Vertices[^1].Position);Near(p.X,10);Near(p.Y,23);Near(p.Z,30);
            var schema=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_entities").InputSchema;
            var variant=schema.GetProperty("properties").GetProperty("operations").GetProperty("items").GetProperty("oneOf").EnumerateArray().Single(v=>v.GetProperty("properties").GetProperty("operation").TryGetProperty("const",out var k)&&k.GetString()=="split_curve");
            if(!variant.GetProperty("properties").TryGetProperty("workplane",out _)||variant.GetProperty("required").EnumerateArray().Any(v=>v.GetString()=="workplane"))throw new Exception("Workplane must remain optional");
            return Task.CompletedTask;
        });
        await test("contour assembly ordered join retains reversed arcs and taper widths",()=>{
            var a=S("Line",false,V(0,0),V(10,0));var b=S("Polyline",false,V(10,10,1,2,4),V(10,0));
            var result=ContourAssemblyGeometry.Plan(E("join_curves",[a,b],reverse:[false,true])).Single();
            if(result.Type!="Polyline"||result.Shape.Vertices.Length!=3)throw new Exception("Wrong topology");
            var arc=ContourAssemblyGeometry.Segments(result.Shape)[1];Near(arc.Bulge,-1);Near(arc.StartWidth,4);Near(arc.EndWidth,2);Near(arc.Length,5*Math.PI);
            Near(arc.Center.X,10);Near(arc.Center.Y,5);
            Deny(()=>ContourAssemblyGeometry.Plan(E("join_curves",[a,b])),"CONTOUR_GAP");
            return Task.CompletedTask;
        });
        await test("contour assembly split closed path wraps seam and conserves curved length widths",()=>{
            var source=S("Polyline",true,V(0,0,1,2,6),V(10,0),V(10,10),V(0,10));
            double perimeter=5*Math.PI+30,cut=2.5*Math.PI;
            var result=ContourAssemblyGeometry.Plan(E("split_curve",[source],cuts:[cut,cut+10]));
            if(result.Length!=2||result.Any(x=>x.Shape.Closed))throw new Exception("Closed split topology");
            Near(result.Sum(x=>ContourAssemblyGeometry.Segments(x.Shape).Sum(s=>s.Length)),perimeter);
            Near(result[0].Shape.Vertices[0].StartWidth,4);Near(result[1].Shape.Vertices[^2].EndWidth,4);
            Near(result[0].Shape.Vertices[0].Position.X,5);Near(result[0].Shape.Vertices[0].Position.Y,-5);
            var opened=ContourAssemblyGeometry.Plan(E("split_curve",[source],cuts:[0])).Single();
            if(opened.Shape.Closed||opened.Shape.Vertices[0].Position!=opened.Shape.Vertices[^1].Position)throw new Exception("Single seam must open loop");
            return Task.CompletedTask;
        });
        await test("contour assembly line and major arc division preserves exact metric and endpoints",()=>{
            var line=S("Line",false,V(0,0),V(30,40));
            var parts=ContourAssemblyGeometry.Plan(E("split_curve",[line],cuts:[10,35]));
            Near(parts[0].Shape.Vertices[^1].Position.X,6);Near(parts[1].Shape.Vertices[^1].Position.Y,28);
            var arc=S("Arc",false,V(10,0,Math.Tan(3*Math.PI/8)),V(0,-10));
            var arcParts=ContourAssemblyGeometry.Plan(E("split_curve",[arc],cuts:[Math.PI*5,Math.PI*10]));
            foreach(var p in arcParts){var s=ContourAssemblyGeometry.Segments(p.Shape).Single();Near(s.Radius,10);Near(s.Length,Math.PI*5);}
            Near(arcParts[0].Shape.Vertices[^1].Position.Y,10);
            return Task.CompletedTask;
        });
        await test("contour assembly explode and split refuse lossy or ambiguous requests",()=>{
            var p=S("Polyline",false,V(0,0,-1),V(10,0),V(20,0));
            var parts=ContourAssemblyGeometry.Plan(E("explode_polyline",[p]));
            if(parts.Length!=2||parts[0].Type!="Arc"||parts[1].Type!="Line")throw new Exception("Primitive types lost");
            Deny(()=>ContourAssemblyGeometry.Plan(E("explode_polyline",[S("Polyline",false,V(0,0,0,1,1),V(10,0))])),"UNSUPPORTED_CURVE_WIDTH");
            foreach(double[] cuts in new[]{new[]{0d},new[]{5d,5d},new[]{15d,5d},new[]{30d}})
                Deny(()=>ContourAssemblyGeometry.Plan(E("split_curve",[p],cuts:cuts)),"INVALID_SPLIT_DISTANCE");
            return Task.CompletedTask;
        });
        await test("contour assembly contract fingerprints source order options and distances with honest schemas",()=>{
            string id=Guid.NewGuid().ToString();
            object Op(double cut,bool erase)=>new{operation="split_curve",handles=new[]{"A"},expected_sources=new[]{new{type="Line",start=new[]{0d,0d},end=new[]{20d,0d}}},erase_sources=erase,distances=new[]{cut}};
            ModifyArguments Parse(params object[] ops)=>ModifyValidation.Parse(Wire.Element(new{bridge_instance_id=id,document_id=id,operation_id=id,expected_revision="1",units="Millimeters",operations=ops}));
            var a=Parse(Op(5,true));if(a.Fingerprint()==Parse(Op(6,true)).Fingerprint()||a.Fingerprint()==Parse(Op(5,false)).Fingerprint())throw new Exception("Fingerprint lost options");
            Deny(()=>Parse(Op(5,true),Op(6,true)),"INVALID_ARGUMENT");
            var tool=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_entities");
            if(!tool.OutputSchema!.Value.GetProperty("properties").GetProperty("data").GetProperty("anyOf")[0].GetProperty("properties").TryGetProperty("contour_assembly_results",out _))throw new Exception("Output contract absent");
            foreach(string kind in new[]{"join_curves","split_curve","explode_polyline"})
            {
                var schema=tool.InputSchema.GetProperty("properties").GetProperty("operations").GetProperty("items").GetProperty("oneOf").EnumerateArray().Single(x=>x.GetProperty("properties").GetProperty("operation").GetProperty("const").GetString()==kind);
                if(!schema.GetProperty("description").GetString()!.Contains("expected_sources"))throw new Exception("Source geometry semantics absent");
            }
            return Task.CompletedTask;
        });
    }
}
