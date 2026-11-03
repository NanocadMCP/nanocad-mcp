using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class CurvedEditTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        void Deny(Action action){try{action();}catch(DomainException){return;}throw new Exception("Unsupported curved edit geometry accepted");}
        await test("curved edit scope requires exact XY positive normal and finite ellipse size",()=>{
            var e=EllipseCreation.Expected(new EllipseSpec(new(0,0),new(10,0),.5,0,2*Math.PI,"0"));CurvedEditScope.Require(e);
            // Native rotated ellipse reports +Z as 1.0000000000000002.
            // EllipseVerification already validates unit length; direction stays exact XY.
            CurvedEditScope.Require(e with{normal=new[]{0d,0d,Math.BitIncrement(1d)}});
            Deny(()=>CurvedEditScope.Require(e with{minor_axis=new[]{0d,0d,5d},normal=new[]{0d,-1d,0d}}));
            Deny(()=>CurvedEditScope.Require(e with{minor_axis=new[]{0d,-5d,0d},normal=new[]{0d,0d,-1d}}));
            Deny(()=>CurvedEditScope.Require(e with{center=new[]{0d,0d,1e-12},start_point=new[]{10d,0d,1e-12},end_point=new[]{10d,0d,1e-12}}));
            Deny(()=>CurvedEditScope.Require(EllipseCreation.Expected(new EllipseSpec(new(0,0),new(1e-10,0),1,0,2*Math.PI,"0"))));
            return Task.CompletedTask;
        });
        await test("curved edit scope retains open nonperiodic bounded spline without projection",()=>{
            var s=new SplineGeometry(1,false,false,false,true,false,new[]{new[]{0d,0d,0d},new[]{3d,4d,0d}},new[]{0d,0d,1d,1d},Array.Empty<double>(),1e-7,1e-7,0,1,new[]{0d,0d,0d},new[]{3d,4d,0d});
            CurvedEditScope.Require(s);
            Deny(()=>CurvedEditScope.Require(s with{control_points=new[]{new[]{0d,0d,1e-12},new[]{3d,4d,0d}},start_point=new[]{0d,0d,1e-12}}));
            Deny(()=>CurvedEditScope.Require(s with{periodic=true}));Deny(()=>CurvedEditScope.Require(s with{closed=true}));Deny(()=>CurvedEditScope.Require(s with{has_fit_data=true}));
            return Task.CompletedTask;
        });
        await test("curved edit catalog retains standard semantic tool and scope",()=>{
            var modify=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_entities");
            if(!modify.Description!.Contains("Ellipse/clamped Spline")||modify.Description.Contains("10000 polyline vertices plus spline controls")||modify.Annotations?.ReadOnlyHint!=false||ToolCatalog.Tools.Count!=ToolCatalog.OperationNames.Length)throw new Exception("Modify scope metadata missing");
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!,"tools-list.json"),JsonSerializer.Serialize(ToolCatalog.Tools,Wire.Json));return Task.CompletedTask;
        });
    }
}
