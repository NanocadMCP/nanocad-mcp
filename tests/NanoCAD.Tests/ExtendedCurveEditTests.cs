using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class ExtendedCurveEditTests
{
    internal static async Task Run(Func<string,Func<Task>,Task> test)
    {
        string id=Guid.NewGuid().ToString();
        ModifyArguments Parse(params object[] ops)=>ModifyValidation.Parse(Wire.Element(new{bridge_instance_id=id,document_id=id,operation_id=id,expected_revision="1",units="Millimeters",operations=ops}));
        object Edit(object a,object b)=>new{operation="set_curve_geometry",handles=new[]{"A"},expected_geometry=a,geometry=b};
        object Ellipse(double start,double sweep,double ratio=.5)=>new{type="Ellipse",center=new[]{0d,0},major_axis=new[]{20d,10},radius_ratio=ratio,start_parameter=start,sweep_parameter=sweep};
        object Spline(bool closed=false,int degree=2,double[]? weights=null)=>new{type="Spline",degree,closed,
            control_points=closed?new[]{new[]{0d,0},new[]{10d,0},new[]{10d,10},new[]{0d,0}}:new[]{new[]{0d,0},new[]{10d,20},new[]{20d,0}},
            knots=closed?new[]{0d,0,0,.5,1,1,1}:new[]{0d,0,0,1,1,1},weights=weights??Array.Empty<double>()};
        void Deny(Action action){try{action();}catch(DomainException){return;}throw new Exception("Invalid curve edit accepted");}
        await test("extended curve edits retain ellipse frame closure span and strict same-type identity",()=>{
            var a=Parse(Edit(Ellipse(0,2*Math.PI),Ellipse(1.5*Math.PI,Math.PI,.4)));
            if(a.Operations[0] is not CurveGeometrySpec{Geometry:{Ellipse:{RadiusRatio:.4,StartParameter:var start,SweepParameter:var sweep}}}||start!=1.5*Math.PI||sweep!=Math.PI)throw new Exception("Ellipse definition lost");
            if(a.Fingerprint()==Parse(Edit(Ellipse(0,2*Math.PI),Ellipse(1.5*Math.PI,Math.PI,.5))).Fingerprint())throw new Exception("Ratio omitted from identity");
            Deny(()=>Parse(Edit(Ellipse(0,2*Math.PI),Ellipse(1,2*Math.PI))));Deny(()=>Parse(Edit(Ellipse(0,2*Math.PI),Spline())));
            Deny(()=>Parse(Edit(Ellipse(0,2*Math.PI),new{type="Ellipse",center=new[]{0d,0},major_axis=new[]{20d,10},radius_ratio=.5,start_parameter=0d,sweep_parameter=1d,layer="X"})));
            return Task.CompletedTask;
        });
        await test("extended curve edits carry NURBS rational closure and full expected definitions",()=>{
            var a=Parse(Edit(Spline(),Spline(weights:[1,.5,1])),Edit(Spline(weights:[1,.5,1]),Spline(closed:true)));
            if(a.Operations[1] is not CurveGeometrySpec{Geometry:{Spline:{Closed:true,ControlPoints.Length:4}}})throw new Exception("Closed NURBS definition lost");
            if(a.Fingerprint()==Parse(Edit(Spline(),Spline(weights:[1,.6,1])),Edit(Spline(weights:[1,.5,1]),Spline(closed:true))).Fingerprint())throw new Exception("NURBS weights omitted");
            Deny(()=>Parse(Edit(Spline(),Spline(weights:[1,0,1]))));Deny(()=>Parse(Edit(Spline(),Spline(degree:3))));
            Deny(()=>Parse(Edit(Spline(),new{type="Spline",degree=2,control_points=new[]{new[]{0d,0},new[]{10d,20},new[]{20d,0}},knots=new[]{0d,0,0,1,1,1},weights=Array.Empty<double>()})));
            return Task.CompletedTask;
        });
        await test("extended curve edits share bounded batch budget and preserve old sparse operations",()=>{
            var old=Parse(new{operation="set_polyline_vertices",handles=new[]{"A"},vertices=new[]{new{index=0,expected_position=new[]{0d,0},position=new[]{1d,0}}}});
            if(old.Operations[0] is not PolylineVerticesSpec{Vertices:[{Bulge:null}]})throw new Exception("Sparse compatibility lost");
            return Task.CompletedTask;
        });
        await test("extended curve edit schema exposes full definitions without layer inference",()=>{
            var input=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_entities").InputSchema;
            var operation=input.GetProperty("properties").GetProperty("operations").GetProperty("items").GetProperty("oneOf").EnumerateArray().Single(v=>v.GetProperty("properties").GetProperty("operation").GetProperty("const").GetString()=="set_curve_geometry");
            var choices=operation.GetProperty("properties").GetProperty("geometry").GetProperty("oneOf").EnumerateArray().ToArray();
            foreach(string type in new[]{"Ellipse","Spline"})
            {
                var shape=choices.Single(v=>v.GetProperty("properties").GetProperty("type").GetProperty("const").GetString()==type);
                if(shape.GetProperty("properties").TryGetProperty("layer",out _))throw new Exception("Geometry includes layer");
                if(type=="Spline"&&!shape.GetProperty("required").EnumerateArray().Any(v=>v.GetString()=="closed"))throw new Exception("Implicit reopening allowed");
            }
            return Task.CompletedTask;
        });
    }
}
