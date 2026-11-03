using NanoCAD.Contracts;

internal static class PolylineEditing431Tests
{
    private static void Require(bool value,string message){if(!value)throw new Exception(message);}
    private static void Deny(Action action,string code)
    {try{action();}catch(DomainException error){Require(error.Error.code==code,error.Message);return;}throw new Exception("Invalid edit accepted.");}
    internal static async Task Run(Func<string,Func<Task>,Task> test)
    {
        await test("block431 sparse contour widths preserve untouched arcs and reject stale or terminal segments",()=>
        {
            var source=new PolylineShape(false,[new(new(0,0),.25,2,3),new(new(20,0),-.5,4,5),new(new(30,10),0,6,7)]);
            var change=new PolylineVertexEdit(0,new(0,0),new(0,0),ExpectedStartWidth:2,StartWidth:8,ExpectedEndWidth:3,EndWidth:9);
            var result=PolylineSparseGeometry431.Apply(source,[change]);
            Require(result.Vertices[0].StartWidth==8&&result.Vertices[0].EndWidth==9&&result.Vertices[0].Bulge==.25,"Outgoing taper change lost its arc.");
            Require(result.Vertices[1]==source.Vertices[1]&&result.Vertices[2]==source.Vertices[2]&&!result.Closed,"Untouched source segments changed.");
            Deny(()=>PolylineSparseGeometry431.Apply(source,[change with{ExpectedStartWidth=99}]),"GEOMETRY_CHANGED");
            Deny(()=>PolylineSparseGeometry431.Apply(source,[new(2,new(30,10),new(30,10),ExpectedStartWidth:6,StartWidth:8)]),"SEGMENT_NOT_FOUND");
            Deny(()=>PolylineSparseGeometry431.Apply(source,[new(1,new(20,0),new(0,0))]),"DEGENERATE_GEOMETRY");
            return Task.CompletedTask;
        });
        await test("block431 Polyline workplanes and sparse taper expectations survive public parsing",()=>
        {
            var frame=new{origin=new[]{100d,200,300},x_axis=new[]{1d,0,0},y_axis=new[]{0d,0,1}};
            string id=Guid.NewGuid().ToString();
            ModifyArguments Parse(params object[] operations)=>ModifyValidation.Parse(Wire.Element(new{bridge_instance_id=id,document_id=id,operation_id=id,expected_revision="1",units="Millimeters",operations}));
            object Shape()=>new{closed=false,vertices=new[]{new{position=new[]{0d,0},bulge=.25,start_width=2d,end_width=3d},new{position=new[]{20d,0},bulge=0d,start_width=0d,end_width=0d}}};
            var parsed=Parse(new{operation="set_polyline_vertices",handles=new[]{"A"},workplane=frame,vertices=new[]{new{index=0,expected_position=new[]{0d,0},position=new[]{0d,0},expected_start_width=2d,start_width=4d}}},
                new{operation="reverse_polyline",handles=new[]{"A"},expected_geometry=Shape(),workplane=frame});
            Require(parsed.Operations[0] is PolylineVerticesSpec{Workplane:not null,Vertices:[{ExpectedStartWidth:2,StartWidth:4}]},"Sparse workplane/taper was lost.");
            Require(PolylineEditFrames431.Frame(parsed.Operations,"A") is {} f&&f.Origin.Z==300&&DrawingWorkplane.Normal(f).Y==-1,"Oriented frame lost.");
            Deny(()=>Parse(new{operation="set_polyline_vertices",handles=new[]{"A"},vertices=new[]{new{index=0,expected_position=new[]{0d,0},position=new[]{0d,0},start_width=2d}}}),"INVALID_ARGUMENT");
            var other=Parse(new{operation="reverse_polyline",handles=new[]{"A"},expected_geometry=Shape(),workplane=frame},new{operation="move",handles=new[]{"A"},displacement=new[]{1d,0}});
            Deny(()=>PolylineEditFrames431.Frame(other.Operations,"A"),"INVALID_OPERATION_ORDER");
            return Task.CompletedTask;
        });
    }
}
