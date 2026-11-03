using NanoCAD.Contracts;

internal static class PolylineTopologyTests
{
    private static PolylineShapeVertex V(double x,double y,double b=0,double sw=0,double ew=0)=>new(new(x,y),b,sw,ew);
    private static void Near(double a,double b){if(Math.Abs(a-b)>1e-8)throw new Exception($"{a} differs from {b}");}
    private static void Deny(Action action,string? code=null){try{action();}catch(DomainException e){if(code!=null&&e.Error.code!=code)throw;return;}throw new Exception("Invalid contour accepted");}
    internal static async Task Run(Func<string,Func<Task>,Task> test)
    {
        await test("polyline topology fillets have exact radius tangent points and winding",()=>{
            foreach(int sign in new[]{1,-1})
            {
                var source=new PolylineShape(false,[V(0,0),V(20,0),V(20,sign*20)]);
                var result=PolylineTopologyGeometry.Corners(source,[new(1,"fillet",3,0,0)]);
                if(result.Vertices.Length!=4)throw new Exception("Corner topology");
                Near(result.Vertices[1].Position.X,17);Near(result.Vertices[1].Position.Y,0);
                Near(result.Vertices[2].Position.X,20);Near(result.Vertices[2].Position.Y,3*sign);
                double b=result.Vertices[1].Bulge;Near(b,sign*Math.Tan(Math.PI/8));
                double chord=Math.Sqrt(18);Near(chord*(1+b*b)/(4*Math.Abs(b)),3);
            }
            return Task.CompletedTask;
        });
        await test("polyline topology mixed corners use original indices and preserve unrelated arc",()=>{
            var source=new PolylineShape(true,[V(0,0),V(40,0),V(40,30),V(20,30),V(20,15),V(0,15,.3)]);
            var result=PolylineTopologyGeometry.Corners(source,[new(1,"chamfer",0,4,7),new(4,"fillet",2,0,0)]);
            if(result.Vertices.Length!=8||!result.Closed)throw new Exception("Combined corner count");
            Near(result.Vertices[1].Position.X,36);Near(result.Vertices[2].Position.Y,7);
            Near(result.Vertices[5].Bulge,-Math.Tan(Math.PI/8));
            if(result.Vertices[^1]!=source.Vertices[^1])throw new Exception("Unrelated closing arc lost");
            return Task.CompletedTask;
        });
        await test("polyline topology reversal keeps arc paths taper and terminal metadata",()=>{
            foreach(bool closed in new[]{false,true})
            {
                var source=new PolylineShape(closed,[V(0,0,.5,2,3),V(20,0,-.2,4,5),V(20,20,.1,6,7)]);
                var result=PolylineTopologyGeometry.Reverse(source);
                if(!PolylineTopologyGeometry.Equivalent(source,PolylineTopologyGeometry.Reverse(result)))throw new Exception("Reverse round trip");
                Near(result.Vertices[0].Bulge,.2);Near(result.Vertices[0].StartWidth,5);Near(result.Vertices[0].EndWidth,4);
                if(!closed&&result.Vertices[^1]!=(source.Vertices[^1] with{Position=source.Vertices[0].Position}))throw new Exception("Terminal data lost");
            }
            return Task.CompletedTask;
        });
        await test("contour result comparison accepts equivalent traversal but detects lost geometry",()=>{
            var source=new PolylineShape(true,[V(0,0,.25,2,3),V(20,0,0,4,5),V(20,10,0,6,7),V(0,10,0,8,9)]);
            var rotated=new PolylineShape(true,[source.Vertices[2],source.Vertices[3],source.Vertices[0],source.Vertices[1]]);
            if(PolylineTopologyGeometry.Equivalent(source,rotated)||!PolylineTopologyGeometry.SameLocus(source,rotated))throw new Exception("Closed start-vertex normalization was not accepted");
            if(!PolylineTopologyGeometry.SameLocus(source,PolylineTopologyGeometry.Reverse(source)))throw new Exception("Equivalent reversed traversal was rejected");
            var missing=rotated with{Vertices=rotated.Vertices.Take(3).ToArray()};
            if(PolylineTopologyGeometry.SameLocus(source,missing))throw new Exception("Lost contour segment was accepted");
            var widthChanged=rotated with{Vertices=rotated.Vertices.Select((v,i)=>i==0?v with{StartWidth=v.StartWidth+1}:v).ToArray()};
            if(PolylineTopologyGeometry.SameLocus(source,widthChanged))throw new Exception("Changed material width was accepted");
            return Task.CompletedTask;
        });
        await test("polyline topology refuses overlapping treatments and invalid corners",()=>{
            var rectangle=new PolylineShape(true,[V(0,0),V(20,0),V(20,10),V(0,10)]);
            Deny(()=>PolylineTopologyGeometry.Corners(rectangle,[new(1,"fillet",6,0,0),new(2,"fillet",6,0,0)]),"CORNER_OVERLAP");
            Deny(()=>PolylineTopologyGeometry.Corners(rectangle,[new(1,"chamfer",0,25,2)]),"CORNER_OVERLAP");
            Deny(()=>PolylineTopologyGeometry.Corners(rectangle with{Closed=false},[new(0,"fillet",1,0,0)]),"CORNER_NOT_FOUND");
            Deny(()=>PolylineTopologyGeometry.Corners(new(false,[V(0,0,.2),V(20,0),V(20,20)]),[new(1,"fillet",1,0,0)]),"UNSUPPORTED_CORNER");
            Deny(()=>PolylineTopologyGeometry.Validate(new(true,[V(0,0),V(20,0),V(0,0)])),"DEGENERATE_GEOMETRY");
            return Task.CompletedTask;
        });
        await test("polyline topology parser preserves complete geometry and budgets ordered operations",()=>{
            string id=Guid.NewGuid().ToString();
            object Shape(params double[][] points)=>new{closed=false,vertices=points.Select(p=>new{position=p,bulge=0d,start_width=0d,end_width=0d}).ToArray()};
            var old=Shape([0,0],[20,0],[20,20]);var next=Shape([0,0],[10,0],[20,0],[20,20]);
            object Change(object expected,object replacement)=>new{operation="set_polyline_geometry",handles=new[]{"A"},expected_geometry=expected,geometry=replacement};
            ModifyArguments Parse(params object[] ops)=>ModifyValidation.Parse(Wire.Element(new{bridge_instance_id=id,document_id=id,operation_id=id,expected_revision="1",units="Millimeters",operations=ops}));
            var a=Parse(Change(old,next),new{operation="reverse_polyline",handles=new[]{"A"},expected_geometry=next});
            if(a.Operations[0] is not PolylineGeometrySpec{Geometry:{Vertices.Length:4}}||a.Operations[1] is not PolylineReverseSpec)throw new Exception("Shape contract lost");
            if(a.Fingerprint()==Parse(Change(next,old)).Fingerprint())throw new Exception("Shape identity omitted");
            Deny(()=>Parse(Change(old,new{closed=true,vertices=new[]{new{position=new[]{0d,0},bulge=0d},new{position=new[]{20d,0},bulge=0d}}})));
            Deny(()=>Parse(new{operation="set_polyline_corners",handles=new[]{"A"},expected_geometry=old,corners=new[]{new{index="1",kind="fillet",radius=2d}}}));
            // Complete ordered edits are bounded by request bytes, not cumulative
            // duplicate vertex counts from repeated expectations/readback.
            var longShape=Shape(Enumerable.Range(0,256).Select(i=>new[]{(double)i,0d}).ToArray());
            if(Parse(Enumerable.Repeat(Change(longShape,longShape),5).ToArray()).Operations.Length!=5)throw new Exception("Complete ordered geometry was truncated");
            return Task.CompletedTask;
        });
    }
}
