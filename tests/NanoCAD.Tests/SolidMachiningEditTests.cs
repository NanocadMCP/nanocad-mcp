using NanoCAD.Contracts;

internal static class SolidMachiningEditTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        string bridge=Guid.NewGuid().ToString("D"),doc=Guid.NewGuid().ToString("D"),operation=Guid.NewGuid().ToString("D");
        ModifyArguments Parse(params object[] operations)=>ModifyValidation.Parse(Wire.Element(new
        {bridge_instance_id=bridge,document_id=doc,operation_id=operation,expected_revision="1",units="Millimeters",operations}));
        Dictionary<string,object> Finish(string kind,string handle)
        {
            var item=new Dictionary<string,object>{{"operation",kind},{"handles",new[]{handle}},{"topology_token",new string('A',64)},
                {kind=="shell_solid"?"remove_faces":"edges",new[]{"1"}}};
            if(kind=="chamfer_solid"){item["base_face"]="2";item["base_distance"]=1d;item["other_distance"]=2d;}
            else item[kind=="shell_solid"?"thickness":"radius"]=1d;
            return item;
        }
        Dictionary<string,object> Drill(string handle)=>new(){{"operation","drill_solid"},{"handles",new[]{handle}},
            {"start",new[]{1d,2d,3d}},{"direction",new[]{0d,0d,1d}},{"radius",.5d},{"depth",10d}};
        void Reject(params object[] operations)
        {try{Parse(operations);throw new Exception("Unsafe direct solid machining accepted.");}catch(DomainException){}}
        await test("direct solid machining validates four independent operations and uncertain recovery",()=>
        {
            var args=Parse(Finish("fillet_solid","A1"),Finish("chamfer_solid","A2"),Finish("shell_solid","A3"),Drill("A4"));
            if(args.Operations.Length!=4||args.Operations.Count(x=>x is SolidFinishingEditSpec)!=3||args.Operations.Single(x=>x is SolidDrillEditSpec) is not SolidDrillEditSpec drill||drill.Depth!=10)
                throw new Exception("Direct machining intent lost.");
            if(!WriteRecovery.NotGuaranteed(args)||!WriteRecovery.UnknownFailure(true,false,false,WriteRecovery.NotGuaranteed(args)))
                throw new Exception("Started native machining was treated as rollback-safe.");
            var changed=Drill("A4");changed["depth"]=11d;
            if(Parse(Finish("fillet_solid","A1"),Finish("chamfer_solid","A2"),Finish("shell_solid","A3"),changed).Fingerprint()==args.Fingerprint())
                throw new Exception("Machining fingerprint omitted cutter dimensions.");
            return Task.CompletedTask;
        });
        await test("direct solid machining rejects stale identity, degenerate cutters and same-handle batching",()=>
        {
            var stale=Finish("fillet_solid","A1");stale["topology_token"]="old";Reject(stale);
            var duplicate=Finish("shell_solid","A1");duplicate["remove_faces"]=new[]{"1","1"};Reject(duplicate);
            var zero=Drill("A1");zero["direction"]=new[]{0d,0d,0d};Reject(zero);
            Reject(Finish("chamfer_solid","A1"),Drill("A1"));
            Reject(Finish("fillet_solid","A1"),new{operation="erase",handles=new[]{"A1"}});
            return Task.CompletedTask;
        });
    }
}
