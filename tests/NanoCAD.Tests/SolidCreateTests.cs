using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class SolidCreateTests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        var bridge=Guid.NewGuid().ToString("D");var doc=Guid.NewGuid().ToString("D");var op=Guid.NewGuid().ToString("D");
        object Request(object[] entities)=>new{bridge_instance_id=bridge,document_id=doc,operation_id=op,expected_revision="9",units="Millimeters",entities};
        object Box(double z=3)=>new{type="Solid3d",kind="box",center=new[]{1d,2,z},size=new[]{10,20,30}};
        void Reject(object[] entities)
        { try { CreateValidation.Parse(Wire.Element(Request(entities))); } catch(DomainException){return;} throw new Exception("Invalid solid accepted."); }
        await test("solid create validates 3D primitives and retains XYZ in operation identity",()=>
        {
            var entities=new object[]{Box(),new{type="Solid3d",kind="sphere",center=new[]{20,0,0},radius=5},new{type="Solid3d",kind="cylinder",center=new[]{40,0,10},radius=3,height=8}};
            var args=CreateValidation.Parse(Wire.Element(Request(entities)));
            if(args.Entities.Length!=3 || args.Entities.Any(x=>x is not SolidSpec) || ((SolidSpec)args.Entities[0]).Center.Z!=3) throw new Exception("Primitive intent lost.");
            if(CreateValidation.Parse(Wire.Element(Request(new[]{Box()}))).Fingerprint()==CreateValidation.Parse(Wire.Element(Request(new[]{Box(4)}))).Fingerprint()) throw new Exception("Z missing from replay identity.");
            if(CreateValidation.Parse(Wire.Element(Request(Enumerable.Repeat(Box(),300).ToArray()))).Entities.Length!=300)throw new Exception("Body count restriction remains.");
            Reject(new object[]{Box(),new{type="Solid3d",kind="sphere",center=new[]{0,0,0},radius=0}});
            Reject(new object[]{new{type="Solid3d",kind="box",center=new[]{0,0},size=new[]{1,2,3}}});
            Reject(new object[]{new{type="Solid3d",kind="box",center=new[]{0,0,0},size=new[]{1d,2,1e-5}}});
            Reject(new object[]{new{type="Solid3d",kind="sphere",center=new[]{0,0,0},radius=3,history=true}});
            return Task.CompletedTask;
        });
        await test("solid create stays outside block definitions and exposes precise native variants",()=>
        {
            try {CreateValidation.ParseEntities(Wire.Element(new[]{Box()}),"$.entities");throw new Exception("3D leaked into block scope.");}catch(DomainException){}
            var tool=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.create_entities");
            int count=tool.InputSchema.GetProperty("properties").GetProperty("entities").GetProperty("items").GetProperty("oneOf").EnumerateArray().Count(v=>v.GetProperty("properties").GetProperty("type").GetProperty("const").GetString()=="Solid3d");
            // The native-history model_extrusion adds one variant to the13
            // direct/derived body variants; none belongs in block definitions.
            if(count!=14 || BlockWriteSchema.Define().GetProperty("properties").GetProperty("entities").GetProperty("items").GetProperty("oneOf").EnumerateArray().Any(v=>v.GetProperty("properties").GetProperty("type").GetProperty("const").GetString()=="Solid3d")) throw new Exception("Solid/block schema scope mismatch.");
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!,"tools-list.json"),JsonSerializer.Serialize(ToolCatalog.Tools,Wire.Json));
            return Task.CompletedTask;
        });
    }
}

