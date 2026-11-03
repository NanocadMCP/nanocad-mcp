using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class DrilledSolidCopyTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        string bridge=Guid.NewGuid().ToString("D"),doc=Guid.NewGuid().ToString("D"),op=Guid.NewGuid().ToString("D");
        Dictionary<string,object> Drill()=>new(){["type"]="Solid3d",["kind"]="drilled_copy",["source_handle"]="00aB",["start"]=new[]{1d,2,3},["direction"]=new[]{1d,1,-1},["radius"]=0.5,["depth"]=10d};
        CreateArguments Parse(params object[] entities)=>CreateValidation.Parse(Wire.Element(new{bridge_instance_id=bridge,document_id=doc,operation_id=op,expected_revision="1",units="Millimeters",entities}));
        void Reject(object entity){try{Parse(entity);}catch(DomainException){return;}throw new Exception("Invalid drilled copy accepted.");}
        await test("solid drilled copy retains source and every cutter dimension in request fingerprint",()=>
        {
            var baseline=Parse(Drill());var spec=(DrilledSolidCopySpec)baseline.Entities.Single();
            if(spec.SourceHandle!="AB" || spec.Start!=new Point3(1,2,3) || spec.Direction!=new Point3(1,1,-1) || spec.Radius!=0.5 || spec.Depth!=10)throw new Exception("Cutter intent changed.");
            foreach(var change in new Dictionary<string,object>{["source_handle"]="AC",["start"]=new[]{0d,0,0},["direction"]=new[]{0d,0,1},["radius"]=1d,["depth"]=11d})
            {var item=Drill();item[change.Key]=change.Value;if(Parse(item).Fingerprint()==baseline.Fingerprint())throw new Exception("Cutter fingerprint collision: "+change.Key);}
            return Task.CompletedTask;
        });
        await test("solid drilled copy rejects invalid cutter geometry and source mutation controls",()=>
        {
            foreach(var change in new Dictionary<string,object>{["direction"]=new[]{0d,0,0},["radius"]=0d,["depth"]=1e6+1,["source_handle"]="0",["erase_source"]=true})
            {var item=Drill();item[change.Key]=change.Value;Reject(item);}
            var missing=Drill();missing.Remove("start");Reject(missing);
            return Task.CompletedTask;
        });
        await test("solid drilled copy shares body budget and stays out of block definitions",()=>
        {
            Parse(Enumerable.Repeat<object>(Drill(),300).ToArray());
            try{CreateValidation.ParseEntities(Wire.Element(new[]{Drill()}));throw new Exception("Drill entered block definition.");}catch(DomainException){}
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!,"tools-list.json"),JsonSerializer.Serialize(ToolCatalog.Tools,Wire.Json));
            return Task.CompletedTask;
        });
    }
}
