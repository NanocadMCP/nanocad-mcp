using NanoCAD.Contracts;

internal static class WriteSpaceTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        await test("paper write intent is explicit preserved in identity and excludes model construction",()=>
        {
            var create=new CreateArguments("b","d","1","o","Millimeters",new EntitySpec[]{new TextSpec("Title",new(20,20),3,"Standard","0")});
            if(create.TargetSpace!="model"||create.Fingerprint()==(create with{TargetSpace="paper"}).Fingerprint())throw new Exception("Space identity lost");
            WriteSpace.Check(create with{TargetSpace="paper"});
            var edit=new ModifyArguments("b","d","1","o","Millimeters",new EditSpec[]{new MoveSpec(new[]{"AB"},new(2,3))});
            WriteSpace.Check(edit with{TargetSpace="paper"});
            if(edit.Fingerprint()==(edit with{TargetSpace="paper"}).Fingerprint())throw new Exception("Edit space identity lost");
            try{WriteSpace.Check(create with{TargetSpace="paper",Entities=new EntitySpec[]{new AssociativeHatchSpec("AB","0")}});throw new Exception("Unsupported sheet dependency accepted");}catch(DomainException e)when(e.Error.code=="OUT_OF_SCOPE"){}
            if(WriteSpace.Parse(Wire.Element(new{}))!="model"||WriteSpace.Parse(Wire.Element(new{target_space="paper"}))!="paper")throw new Exception("Space parse failed");
            return Task.CompletedTask;
        });
    }
}
