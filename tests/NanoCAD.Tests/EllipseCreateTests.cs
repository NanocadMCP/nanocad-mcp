using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class EllipseCreateTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        string bridge=Guid.NewGuid().ToString("D"),doc=Guid.NewGuid().ToString("D"),operation=Guid.NewGuid().ToString("D");
        object Entity(double x=10,double ax=10,double ay=0,double ratio=.5,double start=.2,double sweep=1.1,string layer="0")=>
            new{type="Ellipse",center=new[]{x,20d},major_axis=new[]{ax,ay},radius_ratio=ratio,start_parameter=start,sweep_parameter=sweep,layer};
        CreateArguments Parse(params object[] entities)=>CreateValidation.Parse(Wire.Element(new{bridge_instance_id=bridge,document_id=doc,operation_id=operation,expected_revision="12",units="Millimeters",entities}));
        void Deny(Action action){try{action();}catch(DomainException){return;}throw new Exception("Invalid ellipse creation accepted");}
        await test("ellipse creation preserves parametric axes and every replay geometry field",()=>{
            var args=Parse(Entity());var spec=(EllipseSpec)args.Entities.Single();
            if(spec.Center!=new Point2(10,20)||spec.MajorAxis!=new Point2(10,0)||spec.RadiusRatio!=.5||spec.StartParameter!=.2||spec.SweepParameter!=1.1)throw new Exception("Ellipse intent lost");
            foreach(var changed in new[]{Entity(x:11),Entity(ax:11),Entity(ay:1),Entity(ratio:.6),Entity(start:.3),Entity(sweep:1.2),Entity(layer:"Other")})
                if(args.Fingerprint()==Parse(changed).Fingerprint())throw new Exception("Ellipse parameter missing from fingerprint");
            var wrapped=(EllipseSpec)Parse(Entity(start:5.5,sweep:1.4)).Entities.Single();EllipseCreation.RequireMatch(wrapped,EllipseCreation.Expected(wrapped));
            var full=(EllipseSpec)Parse(Entity(start:0,sweep:2*Math.PI)).Entities.Single();EllipseCreation.RequireMatch(full,EllipseCreation.Expected(full));
            return Task.CompletedTask;
        });
        await test("ellipse creation rejects degenerate axes ambiguous seams and extents beyond bounds",()=>{
            foreach(var entity in new[]{Entity(ax:0),Entity(ax:1000001),Entity(ratio:.009),Entity(ratio:1.01),Entity(ax:.01,ratio:.01),Entity(x:1e9),
                Entity(start:2*Math.PI),Entity(start:-1),Entity(sweep:0),Entity(sweep:2*Math.PI+1e-6),Entity(sweep:2*Math.PI),Entity(start:0,sweep:2*Math.PI-1e-8)})Deny(()=>Parse(entity));
            Deny(()=>Parse(new{type="Ellipse",center=new[]{0,0},major_axis=new[]{10,0,0},radius_ratio=.5,start_parameter=0,sweep_parameter=1}));
            Deny(()=>Parse(new{type="Ellipse",center=new[]{0,0},major_axis=new[]{10,0},radius_ratio=.5,start_angle=0,sweep_angle=1}));
            Parse(Entity(ax:.001,ratio:1,start:0,sweep:2*Math.PI));return Task.CompletedTask;
        });
        await test("ellipse creation guard rejects valid native fallback or wrong parameter semantics",()=>{
            var spec=(EllipseSpec)Parse(Entity(ax:6,ay:8)).Entities.Single();var expected=EllipseCreation.Expected(spec);EllipseCreation.RequireMatch(spec,expected);
            foreach(var wrong in new[]{spec with{Center=new Point2(0,0)},spec with{MajorAxis=new Point2(10,0)},spec with{RadiusRatio=.4},spec with{StartParameter=.3},spec with{SweepParameter=1.2},spec with{StartParameter=0,SweepParameter=2*Math.PI}})
                Deny(()=>EllipseCreation.RequireMatch(spec,EllipseCreation.Expected(wrong)));
            return Task.CompletedTask;
        });
        await test("ellipse creation schema retains explicit scope opt-in and read description",()=>{
            Deny(()=>CreateValidation.ParseEntities(Wire.Element(new[]{Entity()})));
            var blocks=BlockWriteSchema.Define().GetProperty("properties").GetProperty("entities").GetProperty("items").GetProperty("oneOf");
            if(blocks.GetArrayLength()!=7||!blocks.EnumerateArray().Any(v=>v.GetProperty("properties").GetProperty("type").GetProperty("const").GetString()=="Ellipse"))throw new Exception("Block curve schema missing");
            var tool=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.create_entities");
            var variant=tool.InputSchema.GetProperty("properties").GetProperty("entities").GetProperty("items").GetProperty("oneOf").EnumerateArray().Single(v=>v.GetProperty("properties").GetProperty("type").GetProperty("const").GetString()=="Ellipse");
            if(variant.GetProperty("properties").GetProperty("radius_ratio").GetProperty("minimum").GetDouble()!=.01||tool.Annotations?.ReadOnlyHint!=false||ToolCatalog.Tools.Count!=ToolCatalog.OperationNames.Length)throw new Exception("Schema scope mismatch");
            var description=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.inspect_objects").Description!;
            if(!description.Contains("Ellipse")||!description.Contains("Spline"))throw new Exception("Read description remains stale");
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!,"tools-list.json"),JsonSerializer.Serialize(ToolCatalog.Tools,Wire.Json));return Task.CompletedTask;
        });
    }
}
