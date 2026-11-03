using NanoCAD.Contracts;
using NanoCAD.McpServer;
internal static class ModelSketchEditTests
{
    internal static async Task Run(Func<string,Func<Task>,Task> test)
    {
        string sketch=Guid.NewGuid().ToString("B"),last=Guid.NewGuid().ToString("B"),element=Guid.NewGuid().ToString("B");
        object Arc(double r,string unit="radians",double[]? normal=null)=>new{kind="circular_arc",center=new[]{0d,0,0},radius=r,normal=normal??new[]{0d,0,1},reference_direction=new[]{1d,0,0},start_angle=0d,end_angle=2*Math.PI,angle_unit=unit};
        object Edit(object geometry)=>new{operation="set_model_sketch_geometry",handles=new[]{"A"},expected_last_item_id=last,sketch_id=sketch,elements=new[]{new{element_id=element,expected_geometry=Arc(10),geometry}}};
        ModifyArguments Parse(params object[] edits)=>ModifyValidation.Parse(Wire.Element(new{bridge_instance_id=Guid.NewGuid().ToString("D"),document_id=Guid.NewGuid().ToString("D"),expected_revision="1",operation_id=Guid.NewGuid().ToString("D"),units="Millimeters",operations=edits}));
        await test("model-sketch-preserves-edit-identities-and-recovery",()=>{var a=Parse(Edit(Arc(12)));var e=(ModelSketchEdit)a.Operations.Single();if(!WriteRecovery.NotGuaranteed(a)||e.Elements.Single().Geometry.GetProperty("radius").GetDouble()!=12||!e.SketchId.Equals(sketch,StringComparison.OrdinalIgnoreCase))throw new Exception("Lost sketch edit fields or recovery policy");return Task.CompletedTask;});
        foreach(var pair in new[]{("units",Arc(12,"degrees")),("normal",Arc(12,normal:new[]{0d,0,2})),("radius",Arc(0))})
            await test("model-sketch-rejects-"+pair.Item1,()=>{try{Parse(Edit(pair.Item2));throw new Exception("Accepted invalid sketch geometry");}catch(DomainException){}return Task.CompletedTask;});
        await test("model-sketch-rejects-mixed-write",()=>{try{Parse(Edit(Arc(12)),new{operation="move",handles=new[]{"B"},displacement=new[]{1d,2}});throw new Exception("Accepted mixed write");}catch(DomainException){}return Task.CompletedTask;});
        await test("model-sketch-schema-input-output",()=>{var c=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_entities");if(!c.InputSchema.GetRawText().Contains("set_model_sketch_geometry")||!c.OutputSchema!.Value.GetRawText().Contains("model_sketch_changes"))throw new Exception("Missing sketch contract");return Task.CompletedTask;});
        await test("model-sketch-retained-profile-option-roundtrip",()=>{
            var a=CreateValidation.Parse(Wire.Element(new{bridge_instance_id=Guid.NewGuid().ToString("D"),document_id=Guid.NewGuid().ToString("D"),operation_id=Guid.NewGuid().ToString("D"),expected_revision="1",units="Millimeters",entities=new[]{new{type="Solid3d",kind="model_extrusion",plane=new{origin=new[]{0,0,0},x_axis=new[]{1,0,0},y_axis=new[]{0,1,0}},profile=new{kind="circle",center=new[]{0,0},radius=10},distance=7,direction="positive",editable_profile=true}}}));
            var p=(ModelExtrusionSpec)a.Entities.Single();if(!p.EditableProfile||Math.Abs(ModelExtrusionGeometry.Measure(p).Volume-700*Math.PI)>1e-9)throw new Exception("Retained profile changed requested geometry");return Task.CompletedTask;
        });
        await test("model-sketch-retained-profile-bound-before-native-write",()=>{
            var vertices=Enumerable.Range(0,40).Select(i=>new[]{10*Math.Cos(i*Math.PI/20),10*Math.Sin(i*Math.PI/20)}).ToArray();
            try{CreateValidation.Parse(Wire.Element(new{bridge_instance_id=Guid.NewGuid().ToString("D"),document_id=Guid.NewGuid().ToString("D"),operation_id=Guid.NewGuid().ToString("D"),expected_revision="1",units="Millimeters",entities=new[]{new{type="Solid3d",kind="model_extrusion",plane=new{origin=new[]{0,0,0},x_axis=new[]{1,0,0},y_axis=new[]{0,1,0}},profile=new{kind="polygon",vertices},distance=7,direction="positive",editable_profile=true}}}));throw new Exception("Oversized retained profile accepted");}catch(DomainException){}return Task.CompletedTask;
        });
    }
}
