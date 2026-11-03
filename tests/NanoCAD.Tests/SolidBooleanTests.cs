using NanoCAD.Contracts;

internal static class SolidBooleanTests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        var bridge=Guid.NewGuid().ToString("D");var doc=Guid.NewGuid().ToString("D");var op=Guid.NewGuid().ToString("D");
        object Intent(string action,string[] handles)=>new{type="Solid3d",kind="boolean",@operator=action,source_handles=handles};
        CreateArguments Parse(object value)=>CreateValidation.Parse(Wire.Element(new{bridge_instance_id=bridge,document_id=doc,operation_id=op,expected_revision="7",units="Millimeters",entities=new[]{value}}));
        await test("solid boolean retains source order and operation in exact replay identity",()=>
        {
            var a=Parse(Intent("subtract",new[]{"0a","B"}));
            if(a.Entities[0] is not SolidBooleanSpec b || !b.SourceHandles.SequenceEqual(new[]{"A","B"}))throw new Exception("Source identities changed.");
            if(a.Fingerprint()==Parse(Intent("subtract",new[]{"B","A"})).Fingerprint() || a.Fingerprint()==Parse(Intent("union",new[]{"A","B"})).Fingerprint())throw new Exception("Distinct boolean intents collided.");
            return Task.CompletedTask;
        });
        await test("solid boolean rejects duplicate missing or foreign-shaped operands before native calls",()=>
        {
            foreach(var value in new[]{Intent("union",new[]{"A","0a"}),Intent("subtract",new[]{"A"}),Intent("union",new[]{"A","B","C"}),Intent("xor",new[]{"A","B"}),Intent("union",new[]{"0","B"})})
            {try{Parse(value);throw new Exception("Invalid boolean accepted.");}catch(DomainException){}}
            try{CreateValidation.ParseEntities(Wire.Element(new[]{Intent("union",new[]{"A","B"})}),"$.entities");throw new Exception("Boolean leaked into block scope.");}catch(DomainException){}
            return Task.CompletedTask;
        });
    }
}
