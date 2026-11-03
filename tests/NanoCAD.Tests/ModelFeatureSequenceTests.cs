using NanoCAD.Contracts;

internal static class ModelFeatureSequenceTests
{
    internal static async Task Run(Func<string,Func<Task>,Task> test)
    {
        void Deny(ModelSequenceLink[] nodes,string last="C"){try{ModelFeatureSequence.Order(nodes,"A",last);}catch(DomainException){return;}throw new Exception("Ambiguous history order accepted");}
        await test("projected feature chain follows explicit predecessors rather than inventory order",()=>{
            var result=ModelFeatureSequence.Order(new[]{new ModelSequenceLink("C","B"),new("A",null),new("B","A")},"A","C");
            if(!result.SequenceEqual(new[]{"A","B","C"}))throw new Exception("Feature order lost");
            if(ModelFeatureSequence.Order(new[]{new ModelSequenceLink("A",null)},"A","A").Length!=1)throw new Exception("Single base failed");return Task.CompletedTask;
        });
        await test("projected feature chain rejects branches cycles missing links and wrong last feature",()=>{
            Deny(new[]{new ModelSequenceLink("A",null),new("B","A"),new("C","A")});
            Deny(new[]{new ModelSequenceLink("A","C"),new("B","A"),new("C","B")});
            Deny(new[]{new ModelSequenceLink("A",null),new("B",null),new("C","B")});
            Deny(new[]{new ModelSequenceLink("A",null),new("B","A"),new("C","B")},"B");
            Deny(new[]{new ModelSequenceLink("A",null),new("A",null)},"A");return Task.CompletedTask;
        });
        await test("projected feature order follows normalized sketch and profile predecessors",()=>{
            var links=new[]{new ModelSequenceLink("B","SB"),new("C","PC"),new("SC","B"),new("A","SA"),new("SA",null),new("SB","A"),new("PC","SC")};
            var result=ModelFeatureSequence.FromHistory(links,new[]{"C","A","B"},"A","C");
            if(!result.SequenceEqual(new[]{"A","B","C"}))throw new Exception("Normalized predecessor chain lost");
            foreach(var invalid in new[]{new[]{new ModelSequenceLink("A",null),new("B","S"),new("S","B")},new[]{new ModelSequenceLink("A",null),new("B","missing")}})
            {try{ModelFeatureSequence.FromHistory(invalid,new[]{"A","B"},"A","B");}catch(DomainException){continue;}throw new Exception("Broken intermediate predecessor accepted");}
            return Task.CompletedTask;
        });
    }
}
