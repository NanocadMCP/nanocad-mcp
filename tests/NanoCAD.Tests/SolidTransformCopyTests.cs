using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class SolidTransformCopyTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        string bridge=Guid.NewGuid().ToString("D"),doc=Guid.NewGuid().ToString("D"),op=Guid.NewGuid().ToString("D");
        object Translation(double z=3)=>new{kind="translation",displacement=new[]{1d,2,z}};
        object Rotation(double angle=1)=>new{kind="rotation",axis_origin=new[]{0d,1,2},axis_direction=new[]{1d,1,1},angle};
        object Mirror(double normal=1)=>new{kind="mirror",plane_origin=new[]{5d,0,0},plane_normal=new[]{normal,normal,0}};
        object Copy(object transform,string handle="aB")=>new{type="Solid3d",kind="transform_copy",source_handle=handle,transform};
        CreateArguments Parse(params object[] entities)=>CreateValidation.Parse(Wire.Element(new{bridge_instance_id=bridge,document_id=doc,operation_id=op,expected_revision="1",units="Millimeters",entities}));
        void Reject(object entity){try{Parse(entity);}catch(DomainException){return;}throw new Exception("Invalid transformed copy accepted.");}
        await test("solid transformed copy retains source XYZ plane and axis intent",()=>
        {
            var a=Parse(Copy(Translation()));var spec=(SolidTransformCopySpec)a.Entities.Single();
            if(spec.SourceHandle!="AB" || spec.Transform.Displacement!=new Point3(1,2,3))throw new Exception("Copy intent lost.");
            foreach(var changed in new[]{Copy(Translation(4)),Copy(Translation(),"AC"),Copy(Rotation()),Copy(Mirror())})
                if(Parse(changed).Fingerprint()==a.Fingerprint())throw new Exception("Transformed-copy fingerprint collision.");
            if(((SolidTransformCopySpec)Parse(Copy(Mirror())).Entities.Single()).Transform.Origin!=new Point3(5,0,0))throw new Exception("Mirror plane lost.");
            return Task.CompletedTask;
        });
        await test("solid transformed copy rejects degenerate axes excessive displacement and unsupported scale",()=>
        {
            Reject(Copy(Mirror(0)));Reject(Copy(Rotation(Math.PI*2+0.1)));Reject(Copy(Translation(1e6+1)));
            Reject(Copy(new{kind="rotation",axis_origin=new[]{0d,0,0},axis_direction=new[]{0d,0,0},angle=1}));
            Reject(Copy(new{kind="scale",factor=2}));Reject(Copy(Mirror(),"xyz"));
            Reject(Copy(new{kind="mirror",plane_origin=new[]{0d,0,0},plane_normal=new[]{1d,0,0},erase_source=true}));
            return Task.CompletedTask;
        });
        await test("solid transformed copy shares native body budget and cannot enter block definitions",()=>
        {
            var copy=Copy(Translation());
            Parse(Enumerable.Repeat(copy,300).ToArray());
            try{CreateValidation.ParseEntities(Wire.Element(new[]{copy}));throw new Exception("Copy entered block definition.");}catch(DomainException){}
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!,"tools-list.json"),JsonSerializer.Serialize(ToolCatalog.Tools,Wire.Json));
            return Task.CompletedTask;
        });
    }
}
