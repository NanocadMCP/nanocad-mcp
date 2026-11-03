using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class DrawingWorkplaneTests
{
    internal static async Task Run(Func<string,Func<Task>,Task> test)
    {
        string id=Guid.NewGuid().ToString("D");double q=1/Math.Sqrt(2);
        object frame=new{origin=new[]{10d,20,30},x_axis=new[]{q,q,0},y_axis=new[]{0d,0,1}};
        object Context(object[] entities)=>new{bridge_instance_id=id,document_id=id,operation_id=id,expected_revision="1",units="Millimeters",entities};
        void Need(bool value){if(!value)throw new Exception("Workplane assertion failed");}
        void Deny(Action f){try{f();}catch(DomainException){return;}throw new Exception("Invalid workplane accepted");}
        await test("drawing workplane preserves explicit oblique coordinates and handedness",()=>
        {
            var p=DrawingWorkplane.Parse(Wire.Element(frame));var world=DrawingWorkplane.World(p,new(4,5));
            Need(Math.Abs(world.X-(10+4*q))<1e-10&&Math.Abs(world.Y-(20+4*q))<1e-10&&world.Z==35);
            var n=DrawingWorkplane.Normal(p);Need(Math.Abs(n.X-q)<1e-10&&Math.Abs(n.Y+q)<1e-10&&n.Z==0);
            var local=DrawingWorkplane.Local(p,world);Need(Math.Abs(local.X-4)<1e-10&&Math.Abs(local.Y-5)<1e-10);
            Deny(()=>DrawingWorkplane.Local(p,new(world.X+q,world.Y-q,world.Z)));return Task.CompletedTask;
        });
        await test("drawing workplane rejects skew scale duplicate fields and invalid coordinates",()=>
        {
            Deny(()=>DrawingWorkplane.Parse(Wire.Element(new{origin=new[]{0,0,0},x_axis=new[]{2,0,0},y_axis=new[]{0,1,0}})));
            Deny(()=>DrawingWorkplane.Parse(Wire.Element(new{origin=new[]{0,0,0},x_axis=new[]{1,0,0},y_axis=new[]{1,0,0}})));
            using var bad=JsonDocument.Parse("{\"origin\":[0,0,0],\"x_axis\":[1,0,0],\"x_axis\":[0,1,0]}");Deny(()=>DrawingWorkplane.Parse(bad.RootElement));
            using var duplicate=JsonDocument.Parse("{\"type\":\"Line\",\"type\":\"Circle\",\"workplane\":null}");Deny(()=>DrawingWorkplane.WithoutFrame(duplicate.RootElement));return Task.CompletedTask;
        });
        await test("drawing workplane creation fingerprints frame and preserves legacy block boundary",()=>
        {
            var entities=new[]{new{type="Line",start=new[]{0,0},end=new[]{3,4},workplane=frame}};
            var parsed=CreateValidation.Parse(Wire.Element(Context(entities)));Need(parsed.Entities[0].Workplane!=null);
            var ordinary=CreateValidation.Parse(Wire.Element(Context(new[]{new{type="Line",start=new[]{0,0},end=new[]{3,4}}})));Need(ordinary.Entities[0].Workplane==null&&parsed.Fingerprint()!=ordinary.Fingerprint());
            Deny(()=>CreateValidation.ParseEntities(Wire.Element(entities)));return Task.CompletedTask;
        });
        await test("drawing workplane propagates through constraint read and model edit contracts",()=>
        {
            var args=DocumentConstraintsValidation.Parse(Wire.Element(new{bridge_instance_id=id,document_id=id,expected_revision="1",handles=new[]{"A"},workplane=frame,include_variables=true}));Need(args.Workplane!=null&&args.IncludeVariables);
            var edit=ModifyValidation.Parse(Wire.Element(new{bridge_instance_id=id,document_id=id,operation_id=id,expected_revision="1",units="Millimeters",operations=new[]{new{operation="extrude_drawing_profile",handles=new[]{"A"},distance=5,direction="negative",name="Part",workplane=frame}}}));Need(((DrawingProfileEdit)edit.Operations[0]).Workplane==args.Workplane);
            foreach(string tool in new[]{"nanocad.create_entities","nanocad.modify_entities","nanocad.inspect_constraints"})Need(ToolCatalog.Tools.Single(t=>t.Name==tool).InputSchema.GetRawText().Contains("workplane"));return Task.CompletedTask;
        });
    }
}
