using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class ContourSize344Tests
{
    private static readonly string Id=Guid.NewGuid().ToString("D");
    private static ModifyArguments Parse(object operation)=>ModifyValidation.Parse(Wire.Element(new
    {bridge_instance_id=Id,document_id=Id,operation_id=Id,expected_revision="1",units="Millimeters",operations=new[]{operation}}));
    private static void Need(bool value){if(!value)throw new Exception("Contour data or contract was lost.");}
    private static void Reject(Action action,string code)
    {try{action();}catch(DomainException e)when(e.Error.code==code){return;}throw new Exception("Expected "+code);}
    private static void Cancelled(Action action)
    {try{action();}catch(OperationCanceledException){return;}throw new Exception("Cancellation was not retained.");}
    private static object ShapeInput(PolylineShape shape)=>new{closed=shape.Closed,vertices=shape.Vertices.Select(v=>new
    {position=new[]{v.Position.X,v.Position.Y},bulge=v.Bulge,start_width=v.StartWidth,end_width=v.EndWidth}).ToArray()};
    internal static async Task Run(Func<string,Func<Task>,Task> test)
    {
        await test("contour344 ordered join split and explode retain every segment beyond old inventory ceilings",()=>
        {
            var lines=Enumerable.Range(0,300).Select(i=>new{type="Line",start=new[]{(double)i,0},end=new[]{(double)i+1,0}}).ToArray();
            var handles=Enumerable.Range(1,lines.Length).Select(i=>(0x1000+i).ToString("X")).ToArray();
            var join=(ContourAssemblySpec)Parse(new{operation="join_curves",handles,expected_sources=lines,
                erase_sources=false,closed=false,reverse=new bool[lines.Length]}).Operations.Single();
            var joined=ContourAssemblyGeometry.Plan(join).Single();
            Need(joined.Shape.Vertices.Length==301&&joined.Shape.Vertices[0].Position==new Point2(0,0)&&joined.Shape.Vertices[^1].Position==new Point2(300,0));
            var expected=new[]{new{type="Polyline",closed=false,vertices=joined.Shape.Vertices.Select(v=>new
                {position=new[]{v.Position.X,v.Position.Y},bulge=v.Bulge,start_width=v.StartWidth,end_width=v.EndWidth}).ToArray()}};
            var split=(ContourAssemblySpec)Parse(new{operation="split_curve",handles=new[]{"A"},expected_sources=expected,
                erase_sources=false,distances=Enumerable.Range(1,40).Select(i=>i*5d).ToArray()}).Operations.Single();
            var pieces=ContourAssemblyGeometry.Plan(split);Need(pieces.Length==41);
            Need(pieces[0].Shape.Vertices[0].Position==joined.Shape.Vertices[0].Position&&pieces[^1].Shape.Vertices[^1].Position==joined.Shape.Vertices[^1].Position);
            for(int i=1;i<pieces.Length;i++)Need(pieces[i-1].Shape.Vertices[^1].Position==pieces[i].Shape.Vertices[0].Position);
            var explode=(ContourAssemblySpec)Parse(new{operation="explode_polyline",handles=new[]{"A"},expected_sources=expected,erase_sources=false}).Operations.Single();
            var segments=ContourAssemblyGeometry.Plan(explode);Need(segments.Length==300);
            for(int i=0;i<segments.Length;i++)Need(segments[i].Type=="Line"&&segments[i].Shape.Vertices[0].Position==new Point2(i,0)&&segments[i].Shape.Vertices[^1].Position==new Point2(i+1,0));
            Reject(()=>Parse(new{operation="join_curves",handles=new[]{"A"},expected_sources=lines.Take(1).ToArray(),erase_sources=false,closed=false,reverse=new[]{false}}),"INVALID_ARGUMENT");
            Reject(()=>ContourAssemblyGeometry.Plan(explode with{Sources=[joined with{Shape=joined.Shape with{Vertices=joined.Shape.Vertices.Select((v,i)=>i==0?v with{StartWidth=1}:v).ToArray()}}]}),"UNSUPPORTED_CURVE_WIDTH");
            return Task.CompletedTask;
        });
        await test("contour344 finite trim and indexed topology remain usable without count truncation",()=>
        {
            var source=new ContourCurve("Line",new(0,0),new(600,0));
            var outline=new ContourCurve("Polyline",Vertices:Enumerable.Range(0,513).Select(i=>new Point2(i,i%2==0?-1:1)).ToArray());
            var trimmed=ContourGeometry.Trim(source,[outline],new(250.75,0));
            Need(trimmed.Length==2&&trimmed[0].End==new Point2(250.5,0)&&trimmed[1].Start==new Point2(251.5,0));
            var boundaries=Enumerable.Range(0,300).Select(i=>(0x1000+i).ToString("X")).ToArray();
            Need(((TrimCurveSpec)Parse(new{operation="trim_curve",handles=new[]{"A"},boundary_handles=boundaries,pick_point=new[]{250.75,0}}).Operations.Single()).BoundaryHandles.SequenceEqual(boundaries));
            Reject(()=>Parse(new{operation="trim_curve",handles=new[]{"A"},boundary_handles=new[]{"B","B"},pick_point=new[]{1d,0}}),"INVALID_ARGUMENT");
            var vertices=Enumerable.Range(0,1201).Select(i=>new PolylineShapeVertex(new(i,0),0,0,0)).ToArray();var shape=new PolylineShape(false,vertices);
            var reversed=PolylineTopologyGeometry.Reverse(shape);Need(PolylineTopologyGeometry.Equivalent(shape,PolylineTopologyGeometry.Reverse(reversed)));
            Need(Parse(new{operation="reverse_polyline",handles=new[]{"A"},expected_geometry=ShapeInput(shape)}).Operations.Single() is PolylineReverseSpec);
            var sparse=Enumerable.Range(1001,150).Select(i=>new{index=i,expected_position=new[]{(double)i,0},position=new[]{(double)i,1}}).ToArray();
            Need(((PolylineVerticesSpec)Parse(new{operation="set_polyline_vertices",handles=new[]{"A"},vertices=sparse}).Operations.Single()).Vertices.Length==150);
            var zigzag=new PolylineShape(false,Enumerable.Range(0,301).Select(i=>new PolylineShapeVertex(new(i*10,i%2==0?0:10),0,0,0)).ToArray());
            var cornerEdits=Enumerable.Range(1,299).Select(i=>new{index=i,kind="chamfer",incoming_distance=1d,outgoing_distance=1d}).ToArray();
            var cornerSpec=(PolylineCornersSpec)Parse(new{operation="set_polyline_corners",handles=new[]{"A"},expected_geometry=ShapeInput(zigzag),corners=cornerEdits}).Operations.Single();
            var finished=PolylineTopologyGeometry.Corners(cornerSpec.ExpectedGeometry,cornerSpec.Corners);
            Need(finished.Vertices.Length==600&&finished.Vertices[0]==zigzag.Vertices[0]&&finished.Vertices[^1]==zigzag.Vertices[^1]);
            Reject(()=>Parse(new{operation="set_polyline_vertices",handles=new[]{"A"},vertices=new[]{sparse[0],sparse[0]}}),"INVALID_ARGUMENT");
            Reject(()=>Parse(new{operation="set_polyline_vertices",handles=new[]{"A"},vertices=new[]{new{index=2147483648L,expected_position=new[]{0d,0},position=new[]{0d,1}}}}),"INVALID_ARGUMENT");
            return Task.CompletedTask;
        });
        await test("contour344 actual bytes and cancellation bound complete plans without changing geometry refusals",()=>
        {
            var vertices=Enumerable.Range(0,140000).Select(i=>new PolylineShapeVertex(new(i,0),0,0,0)).ToArray();var large=new PolylineShape(false,vertices);
            Need(JsonSerializer.SerializeToUtf8Bytes(large,Wire.Json).Length>NativeCurveRead342.SnapshotByteBudget);
            Reject(()=>PolylineTopologyGeometry.Validate(large),"RESPONSE_LIMIT_EXCEEDED");
            int calls=0;Cancelled(()=>PolylineTopologyGeometry.Reverse(large,()=>{if(++calls==20)throw new OperationCanceledException();}));
            var finite=new ContourCurve("Line",new(0,0),new(20,0));
            calls=0;Cancelled(()=>ContourGeometry.Trim(finite,[new("Line",new(5,-1),new(5,1))],new(10,0),()=>{if(++calls==12)throw new OperationCanceledException();}));
            var item=Wire.Element(new{bridge_instance_id=Id,document_id=Id,operation_id=Id,expected_revision="1",units="Millimeters",
                operations=new[]{new{operation="offset_curve",handles=new[]{"A"},distance=1d,side="left"}}}).GetRawText();
            using var oversized=JsonDocument.Parse(item.Insert(item.LastIndexOf('}'),new string(' ',Wire.MaxRequestBytes)));
            Reject(()=>ModifyValidation.Parse(oversized.RootElement),"INVALID_ARGUMENT");
            Reject(()=>ContourGeometry.Trim(finite,[new("Line",new(5,-1),new(5,1))],new(5,0)),"AMBIGUOUS_PICK");
            Reject(()=>PolylineTopologyGeometry.Validate(new(false,[new(new(0,0),0,0,0),new(new(0,0),0,0,0)])),"DEGENERATE_GEOMETRY");
            var schemas=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_entities").InputSchema.GetProperty("properties").GetProperty("operations").GetProperty("items").GetProperty("oneOf");
            foreach(string name in new[]{"join_curves","explode_polyline","set_polyline_geometry","set_polyline_corners","reverse_polyline","set_polyline_vertices"})
            {
                var schema=schemas.EnumerateArray().Single(x=>x.GetProperty("properties").GetProperty("operation").GetProperty("const").GetString()==name);
                var properties=schema.GetProperty("properties");
                if(name=="join_curves")Need(!properties.GetProperty("handles").TryGetProperty("maxItems",out _)&&!properties.GetProperty("reverse").TryGetProperty("maxItems",out _));
                if(name=="set_polyline_vertices")Need(!properties.GetProperty("vertices").TryGetProperty("maxItems",out _));
                if(name=="set_polyline_geometry")Need(!properties.GetProperty("geometry").GetProperty("properties").GetProperty("vertices").TryGetProperty("maxItems",out _));
            }
            return Task.CompletedTask;
        });
    }
}
